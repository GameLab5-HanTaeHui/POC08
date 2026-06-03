// ============================================================
// ObjectDirectionController.cs  v1.1
// 탑뷰 자식 오브젝트 방향 동기화 컴포넌트
//
// [v1.1 변경 — 이동 방향 → WeaponPivot 실시간 동기화 추가]
//   HandleFacingChanged() 에서 스윙 중이 아닐 때
//   PlayerWeaponSwingController.UpdatePivotToFacing(dir) 를 호출.
//
//   [변경 이유]
//     기존: WeaponPivot 은 공격 시에만 RotatePivotToAttackDir() 로 회전.
//           이동 방향이 바뀌어도 WeaponPivot 은 마지막 공격 방향을 유지.
//           → 비공격 상태에서 무기가 엉뚱한 방향을 향함.
//
//     변경: 이동 방향 변경 시 스윙 중이 아니면 즉시 WeaponPivot 회전.
//           스윙 중이면 공격 방향(WeaponSwingController 제어) 유지.
//           → 이동 방향 = 무기 방향 항상 일치 (비공격 상태)
//           → 공격 중에는 공격 방향 고정 유지
//
// [POC07 참고 스크립트]
//   ObjectFlipController.cs (v1.5)
//   → 횡스크롤 좌/우 flipX + localPosition.x 반전 방식
//   → 탑뷰에서 전면 재설계
//
// [POC07 → POC08 변환 이유]
//   POC07: facing = +1/-1 (좌/우 2방향)
//          → localPosition.x 부호 반전으로 자식 위치 처리
//          → flipX 로 스프라이트 반전
//
//   POC08: facing = Vector2 (8방향)
//          → localPosition.x 반전 개념 불필요
//          → 두 가지 역할 수행:
//            ① WeaponPivot 방향 동기화 (비공격 상태에서 이동방향 추적)
//            ② SpriteRenderer flipX (좌/우 방향만 적용)
//
// [이벤트 소스]
//   PlayerMoveController.OnFacingChanged(Vector2)
//   EnemyAI.OnFlipped(float)  — 적 전용 (좌/우만 필요)
//
// [역할]
//   ① SpriteRenderer 좌우 반전 (X 방향 기준 flipX)
//   ② WeaponPivot 방향 동기화 — 비공격 상태에서 이동 방향으로 실시간 회전
//   ③ 자식 Transform 위치 동기화 (선택 사항)
//
// [네임스페이스]
//   namespace : SEAL
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 탑뷰 자식 오브젝트 방향 동기화 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [부착 위치]
    ///   Player 또는 Enemy 루트 오브젝트에 부착.
    ///
    /// [PlayerMoveController.OnFacingChanged 수신 흐름]
    ///   PlayerMoveController.UpdateFacingDirection()
    ///     → OnFacingChanged(Vector2 newFacing) 발행
    ///     → ObjectDirectionController.HandleFacingChanged(dir)
    ///       → SpriteRenderer flipX (X < 0 이면 반전)
    ///       → PlayerWeaponSwingController.NotifyFacingChanged(dir)
    ///
    /// [적(EnemyAI) 소스 수신 흐름]
    ///   EnemyAI.OnFlipped(float dir) 발행 (+1/-1)
    ///     → ObjectDirectionController.HandleFlippedFloat(dir)
    ///       → SpriteRenderer flipX (dir < 0 이면 반전)
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class ObjectDirectionController : MonoBehaviour
    {
        // ──────────────────────────────────────────
        // 이벤트 소스 열거형
        // ──────────────────────────────────────────

        /// <summary>
        /// OnFacingChanged 이벤트 소스 타입.
        /// 부착 대상에 따라 선택.
        /// </summary>
        public enum DirectionSourceType
        {
            /// <summary>
            /// PlayerMoveController.OnFacingChanged(Vector2) 구독.
            /// Player 오브젝트에 사용.
            /// </summary>
            PlayerMoveController,

            /// <summary>
            /// EnemyAI.OnFlipped(float) 구독.
            /// 일반 Enemy 오브젝트에 사용.
            /// POC07 EnemyAI 소스와 동일.
            /// </summary>
            EnemyAI,
        }

        // ──────────────────────────────────────────
        // Inspector — 소스 설정
        // ──────────────────────────────────────────

        [Header("── 이벤트 소스 ──────────────────────")]

        /// <summary>
        /// 방향 변경 이벤트 소스 타입.
        /// Player = PlayerMoveController / Enemy = EnemyAI.
        /// </summary>
        [Tooltip("방향 이벤트 소스. Player=PlayerMoveController / Enemy=EnemyAI.")]
        [SerializeField] private DirectionSourceType _sourceType = DirectionSourceType.PlayerMoveController;

        // ──────────────────────────────────────────
        // Inspector — SpriteRenderer flipX 대상
        // ──────────────────────────────────────────

        [Header("── SpriteRenderer flipX 대상 ──────────────────────")]

        /// <summary>
        /// flipX 를 반전할 SpriteRenderer 목록.
        /// 방향 전환 시 X 방향 기준으로 flipX 를 설정.
        /// 탑뷰에서는 좌/우만 스프라이트 반전 (상/하는 별도 처리).
        /// </summary>
        [Tooltip("flipX 반전 대상 SpriteRenderer 목록. X 방향 기준으로 반전.")]
        [SerializeField] private List<SpriteRenderer> _spriteRenderers = new();

        /// <summary>
        /// true: 방향이 오른쪽(X > 0)일 때 flipX = true (반전).
        /// false: 방향이 왼쪽(X < 0)일 때 flipX = true (기본 동작).
        /// 스프라이트 원본이 오른쪽/왼쪽 중 어느 방향을 바라보는지에 따라 설정.
        /// </summary>
        [Tooltip("flipX 반전 기준. true=오른쪽일 때 반전 / false=왼쪽일 때 반전 (기본).")]
        [SerializeField] private bool _invertFlipX = false;

        // ──────────────────────────────────────────
        // Inspector — WeaponSwingController 동기화
        // ──────────────────────────────────────────

        [Header("── WeaponSwingController 동기화 ──────────────────────")]

        /// <summary>
        /// 방향 전환 시 원점 재동기화할 PlayerWeaponSwingController.
        /// Player 오브젝트에서만 사용. null 이면 동기화 생략.
        ///
        /// [동기화 이유]
        ///   WeaponPivot 이 공격 방향으로 회전하는 구조이므로
        ///   방향 전환 시 별도 원점 재계산 없이 다음 PlaySwing 호출 시 자동 처리됨.
        ///   단, 스윙 중 방향이 바뀌면 현재 스윙을 취소하는 용도로 사용.
        /// </summary>
        [Tooltip("방향 전환 시 스윙 취소 대상 PlayerWeaponSwingController. null=생략.")]
        [SerializeField] private PlayerWeaponSwingController _swingController;

        /// <summary>
        /// 방향 전환 시 스윙 중이면 자동 취소 여부.
        /// true: 이동 방향 전환 시 현재 스윙 강제 종료.
        /// false: 스윙 중 방향 전환 허용 (단, WeaponPivot 회전은 다음 스윙 시 반영).
        /// </summary>
        [Tooltip("방향 전환 시 스윙 중이면 자동 취소. true = 전환 시 스윙 취소.")]
        [SerializeField] private bool _cancelSwingOnDirectionChange = false;

        // ──────────────────────────────────────────
        // Inspector — 자식 Transform 동기화 (선택)
        // ──────────────────────────────────────────

        [Header("── 자식 Transform 위치 동기화 (선택) ──────────────────────")]

        /// <summary>
        /// 방향 전환 시 localPosition.x 를 절댓값 기준으로 동기화할 Transform 목록.
        /// POC07 의 _flipTargets 개념.
        ///
        /// [탑뷰에서의 사용 케이스]
        ///   주로 HitBox, HurtBox 처럼 플레이어 앞에 항상 위치해야 하는
        ///   오브젝트 중 X축 방향만 동기화가 필요한 경우에 사용.
        ///   WeaponPivot 은 PlayerWeaponSwingController 가 제어하므로 여기에 연결 불필요.
        /// </summary>
        [Tooltip("X 위치 동기화 대상 Transform. POC07의 _flipTargets 개념. 선택 사항.")]
        [SerializeField] private List<Transform> _syncTargets = new();

        /// <summary>
        /// _syncTargets 각각의 반전 여부.
        /// false = 이동 방향 X 부호 그대로 / true = 반전 (후방 위치용).
        /// _syncTargets 와 1:1 대응.
        /// </summary>
        [Tooltip("_syncTargets 반전 여부. false=정면 / true=후방. _syncTargets 와 1:1 대응.")]
        [SerializeField] private List<bool> _syncInvertList = new();

        // ──────────────────────────────────────────
        // 캐시
        // ──────────────────────────────────────────

        /// <summary>
        /// _syncTargets 각각의 localPosition.x 원본값 (부호 포함).
        /// Awake 에서 캐싱. 방향 전환 시 이 값을 기준으로 X 재계산.
        /// POC07 _originalAbsX 와 동일 역할 (v1.5 부호 포함 방식 계승).
        /// </summary>
        private float[] _originalLocalX;

        // ──────────────────────────────────────────
        // 현재 방향
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 바라보는 방향 벡터.
        /// PlayerMoveController.OnFacingChanged 수신 시 갱신.
        /// 외부에서 읽기 가능.
        /// </summary>
        private Vector2 _currentFacing = Vector2.right;

        // ──────────────────────────────────────────
        // 컴포넌트 참조
        // ──────────────────────────────────────────

        /// <summary> PlayerMoveController 참조. Player 소스일 때 사용. </summary>
        private PlayerMoveController _moveController;

        // ──────────────────────────────────────────
        // 프로퍼티
        // ──────────────────────────────────────────

        /// <summary> 현재 바라보는 방향 벡터. </summary>
        public Vector2 CurrentFacing => _currentFacing;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // _syncTargets 원본 X 좌표 캐싱 (부호 포함 — POC07 v1.5 방식 계승)
            CacheOriginalLocalX();
        }

        private void Start()
        {
            SubscribeEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독 / 해제
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 소스 타입에 따라 방향 이벤트 구독.
        /// Start 에서 호출 (Awake 실행 순서 보장).
        /// </summary>
        private void SubscribeEvents()
        {
            switch (_sourceType)
            {
                case DirectionSourceType.PlayerMoveController:
                    _moveController = GetComponentInParent<PlayerMoveController>();
                    if (_moveController == null)
                        _moveController = GetComponent<PlayerMoveController>();

                    if (_moveController != null)
                        _moveController.OnFacingChanged += HandleFacingChanged;
                    else
                        Debug.LogWarning("[ObjectDirectionController] PlayerMoveController 를 찾을 수 없습니다.");
                    break;

                case DirectionSourceType.EnemyAI:
                    // EnemyAI 는 POC07 방식 (float +1/-1) 유지
                    // EnemyAI 구현 시 OnFlipped(float) 이벤트 연결 필요
                    Debug.Log("[ObjectDirectionController] EnemyAI 소스 — EnemyAI 구현 후 연결 필요.");
                    break;
            }
        }

        /// <summary>
        /// 이벤트 구독 해제. OnDestroy 에서 호출.
        /// </summary>
        private void UnsubscribeEvents()
        {
            if (_moveController != null)
                _moveController.OnFacingChanged -= HandleFacingChanged;
        }

        // ══════════════════════════════════════════════════════
        // 방향 처리 — Player 소스 (Vector2)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerMoveController.OnFacingChanged(Vector2) 수신.
        /// 탑뷰 8방향 → flipX + WeaponPivot 방향 동기화 처리.
        ///
        /// [v1.1 추가 — WeaponPivot 이동방향 추적]
        ///   스윙 중이 아닐 때: _swingController.UpdatePivotToFacing(dir) 호출
        ///     → 이동 방향과 무기 방향 항상 일치
        ///   스윙 중일 때: WeaponPivot 변경 없음
        ///     → 공격 방향 고정 유지 (RotatePivotToAttackDir 가 제어 중)
        ///
        /// [POC07 HandleFlipped(float dir) 와의 차이]
        ///   POC07: dir = +1/-1 (좌/우 2방향) → localPosition.x 부호 반전
        ///   POC08: dir = Vector2 (8방향) → flipX X성분 참조 + WeaponPivot Z회전
        /// </summary>
        private void HandleFacingChanged(Vector2 newFacing)
        {
            _currentFacing = newFacing;

            // ① flipX 처리 — X 방향 성분만 참조
            ApplyFlipX(newFacing.x);

            // ② WeaponPivot 이동 방향 동기화 — 비공격 상태에서만
            //    스윙 중이면 공격 방향 고정 유지 (SwingController 가 제어)
            if (_swingController != null && !_swingController.IsSwinging)
                _swingController.UpdatePivotToFacing(newFacing);

            // ③ _syncTargets X 동기화 (선택)
            SyncTargetPositions(newFacing.x);

            // ④ 스윙 취소 (설정된 경우)
            if (_cancelSwingOnDirectionChange && _swingController != null
                && _swingController.IsSwinging)
            {
                _swingController.CancelSwing();
            }
        }

        /// <summary>
        /// EnemyAI.OnFlipped(float dir) 수신용 핸들러.
        /// EnemyAI 구현 완료 후 이벤트에 연결.
        /// POC07 HandleFlipped(float) 와 동일한 시그니처.
        /// </summary>
        /// <param name="dir">+1 = 오른쪽 / -1 = 왼쪽.</param>
        public void HandleFlippedFloat(float dir)
        {
            _currentFacing = new Vector2(dir, 0f);

            ApplyFlipX(dir);
            SyncTargetPositions(dir);
        }

        // ══════════════════════════════════════════════════════
        // 방향 처리 세부
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SpriteRenderer flipX 일괄 처리.
        /// 탑뷰에서는 X 성분만 참조 (좌/우).
        ///
        /// [flipX 결정 규칙]
        ///   _invertFlipX = false (기본): X < 0 → flipX = true (왼쪽을 바라볼 때 반전)
        ///   _invertFlipX = true        : X > 0 → flipX = true (오른쪽을 바라볼 때 반전)
        ///   |X| < 0.01 (상/하): 이전 flipX 유지 (변경 없음)
        /// </summary>
        private void ApplyFlipX(float xDir)
        {
            // 좌우 방향이 없을 때는 변경하지 않음
            if (Mathf.Abs(xDir) < 0.01f) return;

            bool shouldFlip = _invertFlipX ? xDir > 0f : xDir < 0f;

            foreach (var sr in _spriteRenderers)
            {
                if (sr != null)
                    sr.flipX = shouldFlip;
            }
        }

        /// <summary>
        /// _syncTargets localPosition.x 동기화.
        /// POC07 HandleFlipped 의 ① 번 처리와 동일한 역할.
        ///
        /// [탑뷰 사용 케이스]
        ///   주로 HitBox 처럼 플레이어 앞에 고정된 오브젝트만 연결.
        ///   WeaponPivot 은 여기에 연결하지 않음
        ///   (PlayerWeaponSwingController 가 직접 Z회전으로 처리).
        ///
        /// [좌표 재계산 규칙 — POC07 v1.5 방식 계승]
        ///   originalX * xSign * sign(invert)
        ///   xSign  : xDir > 0 → +1 / xDir < 0 → -1
        ///   invert : false → +1 / true → -1
        /// </summary>
        private void SyncTargetPositions(float xDir)
        {
            if (_syncTargets.Count == 0) return;
            if (Mathf.Abs(xDir) < 0.01f) return; // 상/하 방향은 X 동기화 없음

            float xSign = xDir > 0f ? 1f : -1f;

            for (int i = 0; i < _syncTargets.Count; i++)
            {
                if (_syncTargets[i] == null) continue;

                bool invert = i < _syncInvertList.Count && _syncInvertList[i];
                float sign = invert ? -1f : 1f;

                Vector3 pos = _syncTargets[i].localPosition;
                _syncTargets[i].localPosition = new Vector3(
                    _originalLocalX[i] * xSign * sign,
                    pos.y,
                    pos.z);
            }
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// _syncTargets localPosition.x 원본값 캐싱 (부호 포함).
        /// POC07 CacheOriginalPositions() v1.5 방식 계승.
        ///
        /// [v1.5 방식 채택 이유]
        ///   Mathf.Abs 로 절댓값만 저장하면 음수 위치 오브젝트가
        ///   반전 시 반대편이 아닌 같은 쪽으로 이동하는 버그 발생.
        ///   부호 포함으로 저장하면 정확한 대칭 복원 가능.
        /// </summary>
        private void CacheOriginalLocalX()
        {
            _originalLocalX = new float[_syncTargets.Count];
            for (int i = 0; i < _syncTargets.Count; i++)
            {
                if (_syncTargets[i] != null)
                    _originalLocalX[i] = _syncTargets[i].localPosition.x; // 부호 포함
            }
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 런타임에 SpriteRenderer 반전 대상 추가.
        /// </summary>
        public void AddSpriteRenderer(SpriteRenderer sr)
        {
            if (sr != null && !_spriteRenderers.Contains(sr))
                _spriteRenderers.Add(sr);
        }

        /// <summary>
        /// 런타임에 SpriteRenderer 반전 대상 제거.
        /// </summary>
        public void RemoveSpriteRenderer(SpriteRenderer sr)
            => _spriteRenderers.Remove(sr);

        /// <summary>
        /// 런타임에 localPosition.x 동기화 대상 추가.
        /// </summary>
        public void AddSyncTarget(Transform target, bool invert = false)
        {
            if (target == null) return;

            Array.Resize(ref _originalLocalX, _originalLocalX.Length + 1);
            _originalLocalX[^1] = target.localPosition.x;
            _syncTargets.Add(target);
            _syncInvertList.Add(invert);
        }

        /// <summary>
        /// 런타임에 localPosition.x 동기화 대상 제거.
        /// </summary>
        public void RemoveSyncTarget(Transform target)
        {
            int idx = _syncTargets.IndexOf(target);
            if (idx < 0) return;

            _syncTargets.RemoveAt(idx);
            if (idx < _syncInvertList.Count) _syncInvertList.RemoveAt(idx);

            var newArr = new float[_originalLocalX.Length - 1];
            for (int i = 0, j = 0; i < _originalLocalX.Length; i++)
            {
                if (i == idx) continue;
                newArr[j++] = _originalLocalX[i];
            }
            _originalLocalX = newArr;
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 디버그
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 현재 방향 표시
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, (Vector3)_currentFacing * 1.5f);

            // _syncTargets 위치 표시
            for (int i = 0; i < _syncTargets.Count; i++)
            {
                if (_syncTargets[i] == null) continue;

                bool invert = i < _syncInvertList.Count && _syncInvertList[i];
                Gizmos.color = invert
                    ? new Color(1f, 0.4f, 0f, 0.7f)   // 후방 = 주황
                    : new Color(0f, 0.7f, 1f, 0.7f);   // 정면 = 파랑

                Gizmos.DrawWireSphere(_syncTargets[i].position, 0.15f);
                UnityEditor.Handles.Label(
                    _syncTargets[i].position + Vector3.up * 0.25f,
                    $"[Sync] {_syncTargets[i].name}" + (invert ? " [후방]" : " [정면]"));
            }
        }
#endif
    }
}