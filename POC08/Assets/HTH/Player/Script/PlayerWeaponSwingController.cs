// ============================================================
// PlayerWeaponSwingController.cs  v1.3
// 탑뷰 무기 스윙 연출 전담 컴포넌트
//
// [v1.3 변경 — DOLocalPath 곡선 이동 도입]
//   타격 구간 이동: DOLocalMove (직선) → DOLocalPath (호 궤적)
//   PlayerAttackDataSO 의 ArcHeight 값으로 호의 볼록함 제어.
//
//   [ArcPath 계산 원리]
//     BackPos 와 AttackPos 의 중점(mid)에서
//     두 점을 잇는 벡터의 수직 방향으로 ArcHeight 만큼 오프셋.
//     → 3점 배열 [backPos, controlPoint, attackPos] 을 DOLocalPath 에 전달.
//     → PathType.CatmullRom 으로 부드러운 호 보간.
//
//   [ArcHeight = 0 처리]
//     제어점이 중점과 동일 → 직선과 동일.
//     DOLocalMove 를 별도 호출할 필요 없이 DOLocalPath 로 통일.
//
//   [Combo3 (찌르기) 예외]
//     직선이 컨셉 → ArcHeight 없음 → DOLocalMove 유지.
//
// [v1.2 변경 — UpdatePivotToFacing() + HitboxManager 직접 제어]
// [v1.1 변경 — CalcSwingDelta + RotateWeaponDelta]
//
//   [추가 이유]
//     기존: WeaponPivot 은 공격 시 RotatePivotToAttackDir() 에서만 회전.
//           비공격 상태에서 이동 방향이 바뀌어도 WeaponPivot 은 마지막
//           공격 방향을 유지 → 무기가 엉뚱한 방향을 향하는 문제.
//
//     추가: ObjectDirectionController.HandleFacingChanged() 에서
//           스윙 중이 아닐 때 UpdatePivotToFacing(dir) 을 호출.
//           → 비공격 상태: 이동 방향 = 무기 방향 (항상 일치)
//           → 공격 중    : RotatePivotToAttackDir 가 제어 (변경 없음)
//
//   [함수]
//     UpdatePivotToFacing(Vector2 facingDir)  ← 신규 public
//       → 스윙 중이 아닐 때 ObjectDirectionController 에서 호출
//       → 내부적으로 RotatePivotToAttackDir() 와 동일 로직
//       → 차이: 부드러운 DOTween 보간 없음 (즉시 회전 — 이동 방향 추적)
//
// [v1.1 변경 — 회전 방향 bool 연동]
//   PlayerAttackDataSO v2.1 의 Combo1/2/Charge Clockwise bool 을 읽어
//   RotateWeapon() 에서 RotateMode.LocalAxisAdd + CalcSwingDelta 방식으로
//   시계/반시계 방향을 명확하게 제어.
//
//   [변경 전 — RotateMode.Fast]
//     DOTween 이 최단 경로를 자동 선택 → 방향 예측 불가
//     같은 각도 설정이어도 실행마다 다른 방향으로 회전 가능
//
//   [변경 후 — RotateMode.LocalAxisAdd + CalcSwingDelta]
//     백스윙 회전 : 원점(0°) → BackRot (절대 목표값, RotateMode.Fast)
//     타격 회전  : BackRot → AttackRot 사이의 delta 를 LocalAxisAdd 로 누적
//                 PlayerAttackDataSO.CalcSwingDelta(rotBack, rotAtk, clockwise)
//                 → CW: delta 음수 보정 / CCW: delta 양수 보정
//     복귀 회전  : 현재 각도에서 0° 로 RotateMode.Fast (최단 경로 허용)
//
//   [함수 변경]
//     RotateWeapon(float zAngle, float duration, Ease ease)
//       → 절대 각도 회전. 백스윙 / 복귀에 사용. RotateMode.Fast.
//     RotateWeaponDelta(float delta, float duration, Ease ease)  ← 신규
//       → 누적 각도 회전. 타격 단계에만 사용. RotateMode.LocalAxisAdd.
//       → CalcSwingDelta 가 반환한 delta 를 그대로 전달.
//
// [POC07 참고 스크립트]
//   PlayerWeaponMover.cs (v1.5) — Weapon DOTween 스윙 연출
//   KeyDataSO.cs (v1.6)         — 콤보별 위치/회전 수치 정의
//
// [POC07과의 구조 차이]
//   POC07: 횡스크롤 고정 → facing=+1/-1 로 X만 반전
//   POC08: 탑뷰 8방향 → WeaponPivot 을 공격 방향으로 Z회전
//          Weapon 은 WeaponPivot 의 자식, 로컬 좌표계 기준 이동
//
// [무기 피벗 구조]
//   Player
//   └─ WeaponPivot  ← 공격 방향으로 Z회전 (이 컴포넌트가 제어)
//      └─ Weapon    ← DOLocalMove + DOLocalRotate (이 컴포넌트가 제어)
//
// [스윙 흐름]
//   1. WeaponPivot 을 공격 방향 각도로 즉시 회전
//   2. Weapon 을 원점으로 스냅
//   3. Sequence:
//      Append : 백스윙 위치 + RotateWeapon(rotBack)     ← Fast (절대)
//      Append : 타격 위치  + RotateWeaponDelta(delta)   ← LocalAxisAdd (방향 제어)
//      Append : 복귀 위치  + RotateWeapon(0f)           ← Fast (원점)
//   4. 복귀 후 IsSwinging = false
//
// [콤보별 이즈 설계]
//   Combo1 횡베기  : 백스윙 OutQuart / 타격 InOutCubic / 복귀 OutQuart
//   Combo2 내리찍기: 백스윙 OutQuart / 타격 InCubic    / 복귀 OutBounce
//   Combo3 찌르기  : 백스윙 OutQuart / 타격 OutExpo    / 복귀 OutBack
//   Charge 강타    : 백스윙 OutBack  / 타격 InOutQuart / 복귀 OutElastic
//
// [네임스페이스]
//   namespace : SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;
using System;

namespace SEAL
{
    /// <summary>
    /// 탑뷰 무기 스윙 연출 전담 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [부착 위치]
    ///   Player 오브젝트 또는 WeaponPivot 오브젝트에 부착.
    ///
    /// [외부 호출 예시 — PlayerAttackController]
    ///   _swingCtrl.PlaySwing(ComboIndex.Combo1, attackDir, OnHitCallback);
    ///   _swingCtrl.PlayChargeSwing(attackDir, OnHitCallback);
    ///   _swingCtrl.CancelSwing();
    ///
    /// [좌표 기준]
    ///   모든 Vector2 위치값 = WeaponPivot 로컬 기준 (공격 방향=+X).
    ///   Z회전 = Weapon 오브젝트 자체 회전 (무기 날 방향 표현).
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class PlayerWeaponSwingController : MonoBehaviour
    {
        // ──────────────────────────────────────────
        // 콤보 인덱스 열거형
        // ──────────────────────────────────────────

        /// <summary>
        /// 콤보 단계 식별 열거형.
        /// PlayerAttackController 에서 현재 콤보 단계 전달에 사용.
        /// </summary>
        public enum ComboIndex
        {
            /// <summary> 1콤보 — 횡베기. </summary>
            Combo1 = 0,
            /// <summary> 2콤보 — 내리찍기. </summary>
            Combo2 = 1,
            /// <summary> 3콤보 — 찌르기 피니셔. </summary>
            Combo3 = 2,
            /// <summary> 강공격 — 회전 강타. </summary>
            Charge = 3,
        }

        // ──────────────────────────────────────────
        // Inspector — 오브젝트 연결
        // ──────────────────────────────────────────

        [Header("── 오브젝트 연결 ──────────────────────")]

        /// <summary>
        /// 무기 피벗 Transform.
        /// 공격 방향으로 Z회전하는 부모 오브젝트.
        /// Player 하위 WeaponPivot 연결.
        /// </summary>
        [Tooltip("무기 피벗 Transform. 공격 방향으로 Z회전. WeaponPivot 연결.")]
        [SerializeField] private Transform _weaponPivot;

        /// <summary>
        /// 무기 오브젝트 Transform.
        /// DOLocalMove + DOLocalRotate 연출 대상.
        /// WeaponPivot 의 자식 Weapon 연결.
        /// </summary>
        [Tooltip("무기 오브젝트 Transform. DOLocalMove/DOLocalRotate 대상. Weapon 연결.")]
        [SerializeField] private Transform _weapon;

        /// <summary>
        /// 플레이어 Visual Transform.
        /// 공격 전진(Lunge) 연출 대상.
        /// null 이면 자신 transform 사용.
        /// </summary>
        [Tooltip("공격 전진 대상 Visual Transform. null=자신 transform.")]
        [SerializeField] private Transform _visualTransform;

        // ──────────────────────────────────────────
        // Inspector — 데이터 SO
        // ──────────────────────────────────────────

        [Header("── 데이터 SO ──────────────────────")]

        /// <summary>
        /// 공격 수치 ScriptableObject.
        /// 콤보별 위치, 회전, 히트스톱, 봉인도 포함.
        /// </summary>
        [Tooltip("공격 수치 SO. PlayerAttackDataSO 연결 필수.")]
        [SerializeField] private PlayerAttackDataSO _data;

        [Header("── 히트박스 매니저 ──────────────────────")]

        /// <summary>
        /// 히트박스 활성/비활성 담당 컴포넌트.
        /// InsertCallback 타이밍에 Enable/Disable 직접 호출.
        ///
        /// [연결 방식]
        ///   Player 오브젝트의 PlayerAttackHitboxManager 연결.
        ///   null 이면 Awake 에서 GetComponent 로 자동 탐색.
        ///
        /// [히트박스 활성 흐름]
        ///   백스윙 완료 → EnableHitbox(comboIndex, sealAmount)  ← 콜라이더 활성
        ///   타격 완료   → DisableAllHitboxes()                  ← 콜라이더 비활성
        ///   복귀 구간   → 비활성 유지
        /// </summary>
        [Tooltip("히트박스 관리 컴포넌트. null=Awake 자동 탐색.")]
        [SerializeField] private PlayerAttackHitboxManager _hitboxManager;

        // ──────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 스윙 진행 중 여부.
        /// true 동안 새 PlaySwing 호출 시 기존 시퀀스 즉시 종료 후 시작.
        /// </summary>
        private bool _isSwinging;

        /// <summary>
        /// 현재 실행 중인 DOTween Sequence.
        /// 취소/중단 시 Kill 에 사용.
        /// </summary>
        private Sequence _swingSequence;

        /// <summary>
        /// 스윙 코루틴 참조.
        /// 히트스톱 대기 + Sequence 완료 대기에 사용.
        /// </summary>
        private Coroutine _swingCoroutine;

        /// <summary>
        /// 강공격 홀드 맥동 Tween 참조.
        /// 홀드 해제 시 Kill.
        /// </summary>
        private Tween _chargePulseTween;

        /// <summary>
        /// Weapon 오브젝트의 로컬 원점 위치.
        /// Awake 에서 캐싱. 스윙 시작 전 스냅 및 복귀에 사용.
        /// </summary>
        private Vector3 _weaponOriginLocalPos;

        /// <summary>
        /// Weapon 오브젝트의 원점 스케일.
        /// 강공격 맥동 복귀에 사용.
        /// </summary>
        private Vector3 _weaponOriginScale;

        /// <summary>
        /// 히트스톱 코루틴 참조.
        /// 중복 실행 방지.
        /// </summary>
        private Coroutine _hitStopCoroutine;

        /// <summary>
        /// 복귀 구간 시작 시 발행.
        /// PlayerAttackController 에서 구독 → _canChangeDir = true 설정.
        /// </summary>
        public event Action OnReturnStart;

        // ──────────────────────────────────────────
        // 프로퍼티
        // ──────────────────────────────────────────

        /// <summary> 현재 스윙 중 여부. </summary>
        public bool IsSwinging => _isSwinging;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_visualTransform == null)
                _visualTransform = transform;

            if (_weapon != null)
            {
                _weaponOriginLocalPos = _weapon.localPosition;
                _weaponOriginScale = _weapon.localScale;
            }

            // _hitboxManager 자동 탐색 (Inspector 미연결 시)
            if (_hitboxManager == null)
                _hitboxManager = GetComponent<PlayerAttackHitboxManager>();
            if (_hitboxManager == null)
                Debug.LogWarning("[PlayerWeaponSwingController] PlayerAttackHitboxManager 미연결.");

            if (_data == null)
                Debug.LogError("[PlayerWeaponSwingController] PlayerAttackDataSO 미연결.");
            if (_weaponPivot == null)
                Debug.LogError("[PlayerWeaponSwingController] WeaponPivot 미연결.");
            if (_weapon == null)
                Debug.LogError("[PlayerWeaponSwingController] Weapon 미연결.");
        }

        private void OnDestroy()
        {
            _swingSequence?.Kill();
            _chargePulseTween?.Kill();
            if (_hitStopCoroutine != null) Time.timeScale = 1f;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 콤보 스윙 실행.
        /// PlayerAttackController 에서 콤보 단계에 맞춰 호출.
        ///
        /// [v1.2 변경 — onHit 콜백 제거]
        ///   기존: onHit(float radius) 콜백 파라미터
        ///   변경: sealAmount(float) 직접 전달
        ///         → BuildComboSequence 내부에서 _hitboxManager 직접 제어
        ///         → 공격 타격 구간에만 Collider2D 활성, 나머지 구간 비활성
        /// </summary>
        /// <param name="combo">콤보 단계.</param>
        /// <param name="attackDir">공격 방향 (정규화 Vector2).</param>
        /// <param name="sealAmount">이 콤보의 봉인도 누적량.</param>
        public void PlaySwing(ComboIndex combo, Vector2 attackDir, float sealAmount)
        {
            if (_data == null || _weapon == null || _weaponPivot == null) return;

            CancelSwing();
            _swingCoroutine = StartCoroutine(SwingRoutine(combo, attackDir, sealAmount));
        }

        /// <summary>
        /// 강공격 스윙 실행.
        ///
        /// [v1.2 변경 — onHit 콜백 제거]
        ///   sealAmount 직접 전달 → BuildChargeSequence 내부에서 _hitboxManager 제어.
        /// </summary>
        /// <param name="attackDir">공격 방향 (정규화 Vector2).</param>
        /// <param name="sealAmount">강공격 봉인도 누적량.</param>
        public void PlayChargeSwing(Vector2 attackDir, float sealAmount)
        {
            if (_data == null || _weapon == null || _weaponPivot == null) return;

            CancelSwing();
            _swingCoroutine = StartCoroutine(ChargeSwingRoutine(attackDir, sealAmount));
        }

        /// <summary>
        /// 강공격 홀드 중 무기 맥동(충전) 연출 시작.
        /// PlayerAttackController 에서 A키 홀드 감지 시 호출.
        /// </summary>
        public void StartChargePulse()
        {
            if (_weapon == null || _data.ChargePulseScale <= 0f) return;

            StopChargePulse();

            _chargePulseTween = _weapon
                .DOScale(_weaponOriginScale * (1f + _data.ChargePulseScale), _data.ChargePulsePeriod)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        /// <summary>
        /// 강공격 홀드 맥동 연출 종료.
        /// 릴리즈 또는 취소 시 호출.
        /// </summary>
        public void StopChargePulse()
        {
            _chargePulseTween?.Kill();
            _chargePulseTween = null;

            if (_weapon != null)
                _weapon.localScale = _weaponOriginScale;
        }

        /// <summary>
        /// 스윙 즉시 중단 + 무기 원점 복귀.
        /// 피격 / 사망 / 봉인 집행 등 상태 전환 시 호출.
        /// </summary>
        public void CancelSwing()
        {
            _swingSequence?.Kill();
            if (_swingCoroutine != null)
            {
                StopCoroutine(_swingCoroutine);
                _swingCoroutine = null;
            }

            _isSwinging = false;
            SnapWeaponToOrigin();
        }

        /// <summary>
        /// 이동 방향으로 WeaponPivot 즉시 회전.
        /// 비공격 상태에서 ObjectDirectionController 가 매 방향 변경 시 호출.
        ///
        /// [호출 조건 — ObjectDirectionController.HandleFacingChanged()]
        ///   IsSwinging == false 일 때만 호출.
        ///   IsSwinging == true  일 때는 RotatePivotToAttackDir 가 제어 중이므로 호출 안 함.
        ///
        /// [RotatePivotToAttackDir 와의 차이]
        ///   RotatePivotToAttackDir : 공격 시작 시 1회 즉시 회전 (내부 private)
        ///   UpdatePivotToFacing    : 이동 방향 실시간 추적용 (외부 public)
        ///   둘 다 동일한 각도 계산 (Atan2 → Quaternion.Euler Z).
        ///
        /// [즉시 회전 이유]
        ///   이동 방향은 입력마다 즉시 변경됨.
        ///   DOTween 보간을 넣으면 이동 방향보다 무기가 늦게 따라가는 어색함 발생.
        ///   8방향 방향키 입력 기준 → 즉시 스냅이 더 자연스러움.
        /// </summary>
        /// <param name="facingDir">이동 방향 벡터 (정규화됨).</param>
        public void UpdatePivotToFacing(Vector2 facingDir)
        {
            if (_weaponPivot == null) return;
            if (facingDir.sqrMagnitude < 0.001f) return; // 입력 없음 → 현재 방향 유지

            float angle = Mathf.Atan2(facingDir.y, facingDir.x) * Mathf.Rad2Deg;
            _weaponPivot.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // ══════════════════════════════════════════════════════
        // 스윙 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기본 공격 스윙 코루틴.
        /// Combo3 피니셔일 때 히트스톱을 Sequence 완료 후 적용.
        /// </summary>
        /// <summary>
        /// 기본 공격 스윙 코루틴.
        /// v1.2: onHit 콜백 제거 → sealAmount 직접 전달 → BuildComboSequence 가 _hitboxManager 제어.
        /// </summary>
        private IEnumerator SwingRoutine(ComboIndex combo, Vector2 attackDir, float sealAmount)
        {
            _isSwinging = true;

            // 스윙 시작 시 히트박스 확실히 비활성화 (이전 스윙 잔재 방지)
            _hitboxManager?.DisableAllHitboxes();

            RotatePivotToAttackDir(attackDir);
            SnapWeaponToOrigin();

            bool done = false;
            _swingSequence = BuildComboSequence(combo, sealAmount);
            _swingSequence.OnComplete(() => done = true);
            _swingSequence.Play();

            yield return new WaitUntil(() => done);

            // 스윙 종료 시 히트박스 비활성화 보장
            _hitboxManager?.DisableAllHitboxes();

            if (combo == ComboIndex.Combo3 && _data.Combo3HitStopDuration > 0f)
            {
                if (_hitStopCoroutine != null) StopCoroutine(_hitStopCoroutine);
                _hitStopCoroutine = StartCoroutine(
                    HitStopRoutine(_data.Combo3HitStopDuration));
                yield return _hitStopCoroutine;
            }

            _isSwinging = false;
            _swingCoroutine = null;
        }

        /// <summary>
        /// 강공격 스윙 코루틴.
        /// v1.2: onHit 콜백 제거 → sealAmount 직접 전달.
        /// </summary>
        private IEnumerator ChargeSwingRoutine(Vector2 attackDir, float sealAmount)
        {
            _isSwinging = true;

            _hitboxManager?.DisableAllHitboxes();

            StopChargePulse();
            RotatePivotToAttackDir(attackDir);
            SnapWeaponToOrigin();

            bool done = false;
            _swingSequence = BuildChargeSequence(sealAmount);
            _swingSequence.OnComplete(() => done = true);
            _swingSequence.Play();

            yield return new WaitUntil(() => done);

            _hitboxManager?.DisableAllHitboxes();

            if (_data.ChargeHitStopDuration > 0f)
            {
                if (_hitStopCoroutine != null) StopCoroutine(_hitStopCoroutine);
                _hitStopCoroutine = StartCoroutine(
                    HitStopRoutine(_data.ChargeHitStopDuration));
                yield return _hitStopCoroutine;
            }

            _isSwinging = false;
            _swingCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // Sequence 빌더 — POC07 BuildSwingSequence 구조 계승
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 콤보별 DOTween Sequence 생성.
        ///
        /// [구조]
        ///   Append     : 백스윙 (위치 + Z회전) → OutQuart
        ///   AppendCallback: _hitboxManager.EnableHitbox  ← 백스윙 완료 직후 활성
        ///   Append     : 타격 (위치 + Z회전) → 콤보별 이즈
        ///   AppendCallback: _hitboxManager.DisableAllHitboxes ← 타격 완료 직후 비활성
        ///   Append     : 복귀 (원점 위치 + Z=0) → 콤보별 이즈
        ///
        /// [InsertCallback 대신 AppendCallback 을 사용하는 이유]
        ///   InsertCallback(time) 은 Sequence 전체 기준 절대 시각에 발화.
        ///   Append 와 같은 시각(bD)이면 DOTween 내부 순서에 따라
        ///   Append 보다 InsertCallback 이 먼저 실행되는 경우 발생.
        ///   → 백스윙 시작 시점에 EnableHitbox 가 발화하는 역전 버그.
        ///   AppendCallback 은 이전 Append 가 완전히 완료된 직후 실행 보장.
        ///   → 백스윙 끝 → EnableHitbox → 타격 → DisableHitbox → 복귀 순서 확실.
        ///
        /// [v1.2 변경 — onHit 콜백 제거, _hitboxManager 직접 제어]
        /// </summary>
        /// <param name="combo">콤보 단계.</param>
        /// <param name="sealAmount">이 콤보의 봉인도 누적량.</param>
        private Sequence BuildComboSequence(ComboIndex combo, float sealAmount)
        {
            Sequence seq = DOTween.Sequence();
            seq.SetUpdate(true);

            float bD = _data.BackswingDuration;
            float aD = _data.AttackDuration;
            float rD = _data.ReturnDuration;

            // 히트박스 인덱스 = 콤보 인덱스와 동일
            int hitboxIndex = (int)combo;

            Vector3 origin = _weaponOriginLocalPos;

            // 콤보별 위치/회전 값 선택
            Vector3 backPos;
            Vector3 atkPos;
            float rotBack;
            float rotAtk;

            switch (combo)
            {
                // ── Combo1 — 횡베기 ──────────────────────────────
                case ComboIndex.Combo1:
                    backPos = ToV3(_data.Combo1BackPos);
                    atkPos = ToV3(_data.Combo1AttackPos);
                    rotBack = _data.Combo1RotBack;
                    rotAtk = _data.Combo1RotAtk;

                    // ① 백스윙
                    seq.Append(_weapon.DOLocalMove(backPos, bD).SetEase(Ease.OutQuart).SetUpdate(true));
                    seq.Join(RotateWeapon(rotBack, bD, Ease.OutQuart));

                    // ② 백스윙 완료 직후 → 히트박스 활성 (타격 Append 시작 전 보장)
                    seq.AppendCallback(() => _hitboxManager?.EnableHitbox(hitboxIndex, sealAmount));

                    // ③ 타격 — 호(arc) 궤적 이동 + CalcSwingDelta 회전
                    {
                        float delta1 = PlayerAttackDataSO.CalcSwingDelta(
                            rotBack, rotAtk, _data.Combo1Clockwise);
                        seq.Append(ArcPath(backPos, atkPos, _data.Combo1ArcHeight, aD, Ease.InOutCubic));
                        seq.Join(RotateWeaponDelta(delta1, aD, Ease.InOutCubic));
                    }

                    // ④ 타격 완료 직후 → 히트박스 비활성 (복귀 시작 전 보장)
                    seq.AppendCallback(() => _hitboxManager?.DisableAllHitboxes());

                    // ⑤ 복귀
                    seq.AppendCallback(() => OnReturnStart?.Invoke());
                    seq.Append(_weapon.DOLocalMove(origin, rD).SetEase(Ease.OutQuart).SetUpdate(true));
                    seq.Join(RotateWeapon(0f, rD, Ease.OutQuart));
                    break;

                // ── Combo2 — 내리찍기 ────────────────────────────
                case ComboIndex.Combo2:
                    backPos = ToV3(_data.Combo2BackPos);
                    atkPos = ToV3(_data.Combo2AttackPos);
                    rotBack = _data.Combo2RotBack;
                    rotAtk = _data.Combo2RotAtk;

                    // ① 백스윙
                    seq.Append(_weapon.DOLocalMove(backPos, bD).SetEase(Ease.OutQuart).SetUpdate(true));
                    seq.Join(RotateWeapon(rotBack, bD, Ease.OutQuart));

                    // ② 백스윙 완료 직후 → 히트박스 활성
                    seq.AppendCallback(() => _hitboxManager?.EnableHitbox(hitboxIndex, sealAmount));

                    // ③ 타격 — 호(arc) 궤적 이동
                    {
                        float delta2 = PlayerAttackDataSO.CalcSwingDelta(
                            rotBack, rotAtk, _data.Combo2Clockwise);
                        seq.Append(ArcPath(backPos, atkPos, _data.Combo2ArcHeight, aD, Ease.InCubic));
                        seq.Join(RotateWeaponDelta(delta2, aD, Ease.InCubic));
                    }

                    // ④ 타격 완료 직후 → 히트박스 비활성
                    seq.AppendCallback(() => _hitboxManager?.DisableAllHitboxes());

                    // ⑤ 복귀 (OutBounce — 내리찍힌 반동감)
                    seq.AppendCallback(() => OnReturnStart?.Invoke());
                    seq.Append(_weapon.DOLocalMove(origin, rD).SetEase(Ease.OutBounce).SetUpdate(true));
                    seq.Join(RotateWeapon(0f, rD, Ease.OutQuart));
                    break;

                // ── Combo3 — 찌르기 피니셔 ───────────────────────
                default: // Combo3 — 찌르기 피니셔
                    backPos = ToV3(_data.Combo3BackPos);
                    atkPos = ToV3(_data.Combo3AttackPos);
                    rotBack = 0f;
                    rotAtk = 0f;

                    // ① 백스윙
                    seq.Append(_weapon.DOLocalMove(backPos, bD).SetEase(Ease.OutQuart).SetUpdate(true));
                    seq.Join(RotateWeapon(rotBack, bD, Ease.OutQuart));

                    // ② 찌르기 직전 예비동작 딜레이 (0.03초)
                    seq.AppendInterval(0.03f);

                    // ③ 백스윙 + 딜레이 완료 직후 → 히트박스 활성
                    seq.AppendCallback(() => _hitboxManager?.EnableHitbox(hitboxIndex, sealAmount));

                    // ④ 타격 (찌르기)
                    seq.Append(_weapon.DOLocalMove(atkPos, aD).SetEase(Ease.OutExpo));
                    seq.AppendCallback(() =>
                        _weapon.DOPunchPosition(
                            Vector3.right * 0.1f, 0.12f, vibrato: 8, elasticity: 0.5f));

                    // ⑤ 타격 완료 직후 → 히트박스 비활성
                    seq.AppendCallback(() => _hitboxManager?.DisableAllHitboxes());

                    // ⑥ 복귀
                    seq.AppendCallback(() => OnReturnStart?.Invoke());
                    seq.Append(_weapon.DOLocalMove(origin, rD).SetEase(Ease.OutBack).SetUpdate(true));
                    seq.Join(RotateWeapon(0f, rD, Ease.OutBack));
                    break;
            }

            return seq;
        }

        /// <summary>
        /// 강공격 DOTween Sequence 생성.
        ///
        /// [v1.2 변경 — onHit 콜백 제거, _hitboxManager 직접 호출]
        ///   InsertCallback → _hitboxManager.EnableHitbox(HitboxCharge, sealAmount)
        ///                  → _hitboxManager.DisableAllHitboxes()
        /// </summary>
        /// <param name="sealAmount">강공격 봉인도 누적량.</param>
        private Sequence BuildChargeSequence(float sealAmount)
        {
            Sequence seq = DOTween.Sequence();
            seq.SetUpdate(true);

            float bD = _data.BackswingDuration * 1.3f;
            float aD = _data.AttackDuration;
            float rD = _data.ReturnDuration;

            Vector3 origin = _weaponOriginLocalPos;
            Vector3 backPos = ToV3(_data.ChargeBackPos);
            Vector3 atkPos = ToV3(_data.ChargeAttackPos);
            float rotBack = _data.ChargeRotBack;
            float rotAtk = _data.ChargeRotAtk;

            // ① 백스윙
            seq.Append(_weapon.DOLocalMove(backPos, bD).SetEase(Ease.OutBack).SetUpdate(true));
            seq.Join(RotateWeapon(rotBack, bD, Ease.OutBack));

            // ② 백스윙 완료 직후 → 히트박스 활성 (강공격 전용 인덱스)
            seq.AppendCallback(() =>
                _hitboxManager?.EnableHitbox(PlayerAttackHitboxManager.HitboxCharge, sealAmount));

            // ③ 타격 — 호(arc) 궤적 이동 (원호 스윙 느낌)
            {
                float chargeDelta = PlayerAttackDataSO.CalcSwingDelta(
                    rotBack, rotAtk, _data.ChargeClockwise);
                seq.Append(ArcPath(backPos, atkPos, _data.ChargeArcHeight, aD, Ease.InOutQuart));
                seq.Join(RotateWeaponDelta(chargeDelta, aD, Ease.InOutQuart));
            }

            // ④ 타격 완료 직후 → 히트박스 비활성
            seq.AppendCallback(() => _hitboxManager?.DisableAllHitboxes());

            // ⑤ 복귀
            seq.AppendCallback(() => OnReturnStart?.Invoke());
            seq.Append(_weapon.DOLocalMove(origin, rD * 1.2f).SetEase(Ease.OutElastic).SetUpdate(true));
            seq.Join(RotateWeapon(0f, rD * 1.2f, Ease.OutElastic));

            return seq;
        }

        // ══════════════════════════════════════════════════════
        // 히트스톱
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 히트스톱 코루틴.
        /// Time.timeScale 을 낮춰 전체 일시 정지 효과.
        /// WaitForSecondsRealtime → timeScale 영향 없이 실시간 대기.
        /// POC07 HitStopRoutine 과 동일한 방식.
        /// </summary>
        private IEnumerator HitStopRoutine(float duration)
        {
            Time.timeScale = _data.HitStopTimeScale;
            yield return new WaitForSecondsRealtime(duration);

            Time.timeScale = 1f;
            _hitStopCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 보조 — 탑뷰 8방향 변환
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// WeaponPivot 을 공격 방향으로 Z축 회전.
        /// 탑뷰에서 8방향 공격 방향을 각도로 변환.
        ///
        /// [POC07과의 차이]
        ///   POC07: facing=+1/-1 로 X좌표만 반전 (횡스크롤 고정)
        ///   POC08: attackDir 를 각도로 변환 → WeaponPivot 회전 (8방향 대응)
        ///
        /// [각도 계산]
        ///   오른쪽(1,0) = 0° / 위(0,1) = 90° / 왼쪽(-1,0) = 180° / 아래(0,-1) = -90°
        ///   Weapon 스프라이트가 기본적으로 오른쪽을 향한다고 가정.
        /// </summary>
        /// <param name="attackDir">정규화된 공격 방향 벡터.</param>
        private void RotatePivotToAttackDir(Vector2 attackDir)
        {
            if (_weaponPivot == null) return;

            float angle = Mathf.Atan2(attackDir.y, attackDir.x) * Mathf.Rad2Deg;
            _weaponPivot.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// Weapon 을 로컬 원점으로 즉시 스냅.
        /// 스윙 시작 전 항상 호출하여 이전 위치 잔상 방지.
        /// </summary>
        private void SnapWeaponToOrigin()
        {
            if (_weapon == null) return;

            DOTween.Kill(_weapon, complete: false);
            _weapon.localPosition = _weaponOriginLocalPos;
            _weapon.localRotation = Quaternion.identity;
            _weapon.localScale = _weaponOriginScale;
        }

        /// <summary>
        /// Weapon 오브젝트 Z축 로컬 회전 Tween 생성 — 절대 목표 각도.
        /// RotateMode.Fast 사용: DOTween 이 현재 각도에서 목표 각도까지 최단 경로로 회전.
        ///
        /// [사용 단계]
        ///   백스윙 : 원점(0°) → RotBack  (최단 경로 허용)
        ///   복귀   : 현재 각도 → 0°       (최단 경로 허용)
        ///
        /// [타격 단계에는 사용 금지]
        ///   타격 단계는 RotateWeaponDelta 를 사용해야 방향이 보장됨.
        /// </summary>
        /// <param name="zAngle">목표 Z각도 (도). 절대값.</param>
        /// <param name="duration">회전 시간 (초).</param>
        /// <param name="ease">이즈 타입.</param>
        private Tweener RotateWeapon(float zAngle, float duration, Ease ease)
        {
            return _weapon
                .DOLocalRotate(new Vector3(0f, 0f, zAngle), duration, RotateMode.Fast)
                .SetEase(ease)
                .SetUpdate(true);
        }

        /// <summary>
        /// Weapon 오브젝트 Z축 로컬 회전 Tween 생성 — 누적 회전량(delta).
        /// RotateMode.LocalAxisAdd 사용: 현재 각도에서 delta 만큼 추가 회전.
        ///
        /// [사용 단계]
        ///   타격: BackRot → AttackRot 사이를 Clockwise bool 기준으로 방향 고정.
        ///
        /// [방향 규칙 — LocalAxisAdd]
        ///   delta 양수 = 반시계(CCW) / delta 음수 = 시계(CW)
        ///   PlayerAttackDataSO.CalcSwingDelta() 가 bool 에 맞는 부호로 보정.
        ///
        /// [예시]
        ///   Combo1 Clockwise=true, rotBack=-150, rotAtk=25
        ///   raw delta = 25 - (-150) = 175 → CW 이므로 175 - 360 = -185
        ///   → _weapon 이 시계 방향으로 185° 회전
        /// </summary>
        /// <param name="delta">누적 Z회전량 (도). 음수=시계 / 양수=반시계.</param>
        /// <param name="duration">회전 시간 (초).</param>
        /// <param name="ease">이즈 타입.</param>
        private Tweener RotateWeaponDelta(float delta, float duration, Ease ease)
        {
            return _weapon
                .DOLocalRotate(new Vector3(0f, 0f, delta), duration, RotateMode.LocalAxisAdd)
                .SetEase(ease)
                .SetUpdate(true);
        }

        /// <summary>
        /// Vector2 → Vector3 변환 (z=0 고정).
        /// DataSO 의 Vector2 위치값을 DOLocalMove/DOLocalPath 에 전달할 때 사용.
        /// </summary>
        private static Vector3 ToV3(Vector2 v) => new Vector3(v.x, v.y, 0f);

        /// <summary>
        /// 타격 구간 호(arc) 궤적 Tween 생성.
        /// DOLocalPath + CatmullRom 으로 backPos → 제어점 → attackPos 곡선 이동.
        ///
        /// [제어점 계산]
        ///   mid         = (backPos + attackPos) / 2
        ///   dir         = (attackPos - backPos).normalized
        ///   perp        = Vector3.Cross(dir, Vector3.forward) → 수직 벡터
        ///   controlPoint = mid + perp * arcHeight
        ///
        /// [arcHeight = 0]
        ///   controlPoint = mid → 직선과 동일한 궤적 (DOLocalPath 로 통일)
        ///
        /// [권장 arcHeight]
        ///   Combo1 횡베기  : +0.8 (위볼록 — 호를 그리며 휩쓸기)
        ///   Combo2 내리찍기: -0.6 (아래볼록 — 포물선 내리찍기)
        ///   Charge 강타    : +1.5 (크게 볼록 — 원호 스윙)
        ///   Combo3 찌르기  : 직선 컨셉 → ArcPath 미사용 (DOLocalMove 유지)
        /// </summary>
        private Tweener ArcPath(Vector3 backPos, Vector3 attackPos, float arcHeight, float duration, Ease ease)
        {
            // ──────────────────────────────────────────────────────
            // [8방향 대응 원리]
            //   backPos / attackPos 는 WeaponPivot 로컬 좌표 (+X = 공격 방향).
            //   WeaponPivot 이 공격 방향으로 Z회전한 상태에서
            //   자식인 Weapon 의 로컬 좌표계도 함께 회전.
            //   → backPos/attackPos 그대로 사용하면 8방향 자동 대응.
            //
            //   controlPoint 도 같은 로컬 좌표계에서 계산.
            //   수직 벡터(perp) = Vector3.Cross(dir, Vector3.forward)
            //     → 로컬 +X 방향의 수직 = 로컬 +Y/-Y 방향
            //     → WeaponPivot 회전과 함께 월드에서도 올바른 방향으로 변환됨.
            //
            //   DOLocalPath 는 반드시 PathMode.Ignore 로 설정해야
            //   경유점을 로컬 좌표로 해석. 기본값은 월드 경유점.
            // ──────────────────────────────────────────────────────

            // 로컬 좌표계 기준 중점 + 수직 오프셋으로 제어점 계산
            Vector3 mid = (backPos + attackPos) * 0.5f;
            Vector3 dir = (attackPos - backPos);
            // 로컬 이동방향의 수직 벡터 (Z=forward 기준 2D 평면)
            Vector3 perp = Vector3.Cross(dir.normalized, Vector3.forward);
            Vector3 controlPoint = mid + perp * arcHeight;

            // [backPos, controlPoint, attackPos] 로컬 3점 곡선
            Vector3[] path = { backPos, controlPoint, attackPos };

            return _weapon
                .DOLocalPath(path, duration, PathType.CatmullRom, PathMode.Ignore)
                .SetEase(ease)
                .SetUpdate(true);
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 디버그
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_data == null || _weapon == null) return;

            // 히트박스 위치 표시 (씬 뷰)
            // WeaponPivot 의 월드 오른쪽 방향 = 현재 공격 방향
            Vector3 pivotRight = _weaponPivot != null
                ? _weaponPivot.right
                : Vector3.right;

            Vector3 hitCenter = transform.position + pivotRight * _data.HitboxOffset;

            Gizmos.color = _isSwinging
                ? new Color(1f, 0.3f, 0.3f, 0.5f)
                : new Color(1f, 1f, 1f, 0.3f);

            Gizmos.DrawWireSphere(hitCenter, _data.HitboxRadius);

            // 강공격 히트박스
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.2f);
            Gizmos.DrawWireSphere(hitCenter, _data.HitboxRadius * _data.ChargeHitboxScale);

            // 공격 방향
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, pivotRight * 1.5f);

            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.8f,
                $"스윙중: {_isSwinging}");
        }
#endif
    }
}