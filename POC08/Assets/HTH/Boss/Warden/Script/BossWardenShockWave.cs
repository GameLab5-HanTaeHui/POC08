// ============================================================
// BossWardenShockwave.cs  v1.0
// Boss_Warden 충격파 컴포넌트 (탑뷰)
//
// [POC07 참고]
//   TestBossShockwave.cs (v1.3) 의 코루틴 + WaitForFixedUpdate 구조 계승.
//   BossShockwave.cs (v1.0) 의 AddForce 방식 참고.
//
// [POC07 TestBossShockwave v1.3 의 핵심 교훈]
//   velocity 를 동기 설정하면 같은 프레임 PlayerMover.FixedUpdate 가
//   velocity 를 덮어써서 넉백이 적용되지 않는 버그 발생.
//   → WaitForFixedUpdate() 후 velocity 설정으로 해결.
//
// [탑뷰 변환 포인트]
//   POC07: 수평(X) + 수직(Y) 힘으로 대각선 날아가는 느낌
//   POC08: 탑뷰이므로 수직 힘 없음 — X/Y 평면 내 방향으로만 날아감
//          보스 → 플레이어 방향 * knockbackForce
//          WaitForFixedUpdate 후 linearVelocity 직접 설정
//
// [시각 연출 — 프리미티브]
//   Particle / Sprite 없음.
//   SpriteRenderer 원형 디스크 Scale 0 → 충격파 반경으로 빠르게 확장 (DOScale OutQuart)
//   DOColor 투명 페이드 아웃.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 충격파 컴포넌트. 탑뷰. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [호출처]
    ///   BossWardenCore.ExitDilPhase(false) — 딜 페이즈 일반 종료 시
    ///
    /// [넉백 흐름 — POC07 v1.3 교훈 적용]
    ///   Trigger() 호출
    ///     → OverlapCircleNonAlloc 으로 Player 레이어 감지
    ///     → BlockPlayerInput 즉시
    ///     → WaitForFixedUpdate() — PlayerMoveController.FixedUpdate 완료 대기
    ///     → linearVelocity = dir * knockbackForce 설정
    ///       (Block 상태이므로 PlayerMoveController 덮어쓰기 없음)
    ///     → WaitForSecondsRealtime(blockDuration)
    ///     → UnblockPlayerInput
    ///
    /// [DOTween 디스크 연출]
    ///   _discRenderer.transform.localScale → 충격파 반경 크기로 DOScale (OutQuart)
    ///   _discRenderer.color DOColor 투명 페이드
    ///   완료 후 SetActive(false)
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossWardenShockwave : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO ──────────────────────")]

        [Tooltip("BossWardenDataSO. BossWardenCore.Initialize() 에서 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 레이어 ──────────────────────")]

        /// <summary>
        /// 플레이어 감지 레이어 마스크.
        /// Player 레이어 선택.
        /// </summary>
        [Tooltip("Player 레이어 마스크.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 시각 연출 (프리미티브) ──────────────────────")]

        /// <summary>
        /// 충격파 확장 디스크 SpriteRenderer.
        /// 기본 SetActive = false.
        /// Trigger() 시 Scale 0 → 반경 크기로 DOScale.
        /// SortingLayer = Ground 권장 (바닥에 표시).
        /// </summary>
        [Tooltip("충격파 확장 디스크. 기본 SetActive=false. SortingLayer=Ground 권장.")]
        [SerializeField] private SpriteRenderer _discRenderer;

        [Header("── 카메라 셰이크 (선택) ──────────────────────")]

        [Tooltip("카메라 Transform. 미연결 시 셰이크 없음.")]
        [SerializeField] private Transform _cameraTransform;

        [Tooltip("카메라 셰이크 강도.")]
        [Min(0f)]
        [SerializeField] private float _cameraShakeStrength = 0.25f;

        [Tooltip("카메라 셰이크 지속 시간 (초).")]
        [Min(0f)]
        [SerializeField] private float _cameraShakeDuration = 0.3f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// OverlapCircle 버퍼.
        /// GC Alloc 없이 플레이어 감지.
        /// </summary>
        private readonly Collider2D[] _overlapBuffer = new Collider2D[8];

        private PlayerInputHandler _input;
        private Coroutine _knockbackCoroutine;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Start()
        {
            _input = PlayerInputHandler.Instance;

            // 디스크 초기 비활성
            if (_discRenderer != null)
                _discRenderer.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenCore 에서 DataSO 주입.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
        }

        // ══════════════════════════════════════════════════════
        // 충격파 발동
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 충격파 발동.
        /// BossWardenCore.ExitDilPhase(false) 에서 호출.
        ///
        /// [처리 순서]
        ///   1. 디스크 연출 시작
        ///   2. 카메라 셰이크
        ///   3. 플레이어 감지 → 넉백 코루틴 시작
        /// </summary>
        /// <param name="origin">충격파 발원 월드 위치 (보스 본체 위치).</param>
        public void Trigger(Vector3 origin)
        {
            if (_data == null) return;

            // 1. 디스크 연출
            PlayDiscEffect(origin);

            // 2. 카메라 셰이크
            if (_cameraTransform != null && _cameraShakeStrength > 0f)
            {
                _cameraTransform.DOKill();
                _cameraTransform.DOShakePosition(
                    _cameraShakeDuration,
                    strength: new Vector3(_cameraShakeStrength, _cameraShakeStrength, 0f),
                    vibrato: 20,
                    randomness: 90f)
                    .SetUpdate(true); // TimeScale 무관
            }

            // 3. 플레이어 감지 → 넉백
            int count = Physics2D.OverlapCircleNonAlloc(
                origin,
                _data.shockwaveRadius,
                _overlapBuffer,
                _playerLayer);

            for (int i = 0; i < count; i++)
            {
                Collider2D col = _overlapBuffer[i];
                if (col == null) continue;

                Rigidbody2D rb = col.GetComponentInParent<Rigidbody2D>();
                if (rb == null) continue;

                // 넉백 방향 = 보스 → 플레이어 (탑뷰 X/Y 평면)
                Vector2 dir = ((Vector2)col.transform.position - (Vector2)origin);
                if (dir.sqrMagnitude < 0.001f)
                    dir = Vector2.right; // 동일 위치면 기본 방향

                dir = dir.normalized;

                // 기존 넉백 코루틴 중단 후 재시작
                if (_knockbackCoroutine != null)
                    StopCoroutine(_knockbackCoroutine);

                _knockbackCoroutine = StartCoroutine(
                    ApplyKnockbackRoutine(rb, dir));
            }

            Debug.Log($"[BossWardenShockwave] 충격파 발동 — 반경:{_data.shockwaveRadius}");
        }

        // ══════════════════════════════════════════════════════
        // 넉백 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 탑뷰 넉백 코루틴.
        ///
        /// [POC07 v1.3 교훈 적용]
        ///   BlockAll 즉시 → WaitForFixedUpdate → velocity 설정 순서.
        ///   PlayerMoveController.FixedUpdate 가 velocity 를 덮어쓰기 전에
        ///   Block 을 설정하여 다음 FixedUpdate 에서 velocity 보호.
        ///
        /// [탑뷰 넉백 방식]
        ///   POC07: 수평(X) + 수직(Y) 힘 → 대각선 날아가는 느낌
        ///   POC08: X/Y 평면 방향 × knockbackForce → 탑뷰 밀침
        ///          linearVelocity 직접 설정 (AddForce 아님 — 즉각 반응 보장)
        /// </summary>
        private IEnumerator ApplyKnockbackRoutine(Rigidbody2D rb, Vector2 direction)
        {
            // ① 즉시 입력 차단 (이동 / 대시 / 공격)
            _input?.BlockAll();

            // ② 다음 FixedUpdate 완료까지 대기
            //    → PlayerMoveController 의 이번 프레임 velocity 덮어쓰기 완료 후
            //    → 다음 프레임에서 Block 상태이므로 덮어쓰기 없음
            yield return new WaitForFixedUpdate();

            // ③ linearVelocity 직접 설정
            if (rb != null)
            {
                rb.linearVelocity = direction * _data.shockwaveKnockbackForce;
                Debug.Log($"[BossWardenShockwave] 넉백 velocity 설정 → {rb.linearVelocity}");
            }

            // ④ 넉백 지속 동안 차단 유지 (실시간 — TimeScale 무관)
            yield return new WaitForSecondsRealtime(_data.shockwaveKnockbackDuration);

            // ⑤ 차단 해제
            _input?.UnblockAll();

            _knockbackCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 디스크 연출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 충격파 확장 디스크 DOTween 연출.
        ///
        /// [연출 순서]
        ///   Scale 0 → 충격파 직경 (OutQuart 빠르게 확장)
        ///   Color 반투명 → 투명 페이드 (동시)
        ///   완료 → SetActive(false)
        ///
        /// [SortingLayer]
        ///   Ground 레이어에 표시하여 캐릭터 아래에 위치.
        /// </summary>
        private void PlayDiscEffect(Vector3 origin)
        {
            if (_discRenderer == null) return;
            if (_data == null) return;

            // 위치 이동 + 초기 크기 0
            _discRenderer.transform.position = new Vector3(origin.x, origin.y, 0f);
            _discRenderer.transform.localScale = Vector3.zero;

            // 색상 초기화 (붉은 반투명)
            _discRenderer.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            _discRenderer.gameObject.SetActive(true);

            // 충격파 직경 = radius * 2
            float diameter = _data.shockwaveRadius * 2f;

            // Sequence: 확장 + 페이드 동시 진행
            var seq = DOTween.Sequence();
            seq.Append(
                _discRenderer.transform
                    .DOScale(new Vector3(diameter, diameter, 1f), 0.35f)
                    .SetEase(Ease.OutQuart));
            seq.Join(
                _discRenderer
                    .DOColor(new Color(1f, 0.2f, 0.2f, 0f), 0.35f)
                    .SetEase(Ease.InCubic));
            seq.OnComplete(() =>
            {
                _discRenderer.gameObject.SetActive(false);
                _discRenderer.transform.localScale = Vector3.zero;
            });
            seq.SetUpdate(true); // TimeScale 무관
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            if (_data == null) return;

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _data.shockwaveRadius);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                transform.position + Vector3.down * 0.8f,
                $"Shockwave R:{_data.shockwaveRadius} F:{_data.shockwaveKnockbackForce}");
#endif
        }
    }
}