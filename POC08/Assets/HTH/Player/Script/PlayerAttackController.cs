// ============================================================
// PlayerAttackController.cs  v3.1
// 플레이어 공격 컨트롤러 — 콤보 / 이동 독립 확정판
//
// [v3.1 — 버그 수정 핵심 변경]
//
//   [수정 1] OnAttackEnded 이중 발행 제거
//     기존: HandleReturnStart()에서 OnAttackEnded 발행
//           + ComboRoutine 끝에서도 OnAttackEnded 발행
//           → PlayerController 상태 두 번 복귀 → 상태 꼬임
//     변경: OnAttackEnded 는 ComboRoutine 마지막 1곳에서만 발행
//           HandleReturnStart 는 _comboWindowOpen 열기 전담
//
//   [수정 2] 중복 ComboRoutine 코루틴 제거
//     기존: HandleReturnStart → _isAttacking = false
//           → HandleRelease → ExecuteCombo → 새 ComboRoutine 시작
//           → 기존 ComboRoutine 도 while(_swingController.IsSwinging) 루프 중
//           → 두 코루틴 동시 실행
//     변경: _isAttacking = false 시점을 ComboRoutine 내부로 이동
//           HandleReturnStart 는 _comboWindowOpen 만 열고 _isAttacking 건드리지 않음
//           ExecuteCombo 진입 시 기존 _attackCoroutine StopCoroutine 후 재시작
//
//   [수정 3] _comboInputQueued 제거
//     기존: _comboInputQueued 는 어디서도 true 설정 안 됨
//           → 항상 false → OnAttackEnded 항상 중복 발행
//     변경: _comboInputQueued 완전 제거
//           대신 _comboWindowOpen 플래그만 사용
//
//   [수정 4] 콤보 윈도우 구간 입력만 다음 콤보 예약
//     기존: HandleRelease 에서 _isAttacking = false 이면 즉시 ExecuteCombo
//           → OnReturnStart 이전에도 클릭하면 진입 가능
//     변경: HandleRelease 에서 _comboWindowOpen = true 일 때만 ExecuteCombo 허용
//           그 외 (_isAttacking = true) 는 완전 무시
//
//   [v3.0 원칙 유지]
//     SetMoveLocked 호출 없음
//     이동은 PlayerMoveController 독립 처리
//     공격은 이동과 완전 독립 실행
//     대시 최우선 (CancelAttack)
//
// [콤보 정확한 흐름 — v3.1]
//   클릭 → HandleRelease
//     → _isAttacking = false && _comboWindowOpen = false → ExecuteCombo(콤보 0)
//
//   ComboRoutine(0) 실행
//     → _isAttacking = true
//     → SwingRoutine 시작
//     → OnReturnStart 수신 → HandleReturnStart
//        → _comboWindowOpen = true   (이 구간에서만 클릭 허용)
//        → _isAttacking 은 그대로 true
//
//     [윈도우 구간에서 클릭]
//       HandleRelease → _comboWindowOpen = true → ExecuteCombo(콤보 1)
//         → StopCoroutine(_attackCoroutine)   ← 기존 ComboRoutine 중단
//         → _currentCombo = 1
//         → 새 ComboRoutine(1) 시작
//
//     [윈도우 구간 지남 / 클릭 없음]
//       ComboRoutine while 루프 IsSwinging = false 로 탈출
//         → _isAttacking = false
//         → OnAttackEnded 발행   ← 단 1번
//         → StartResetTimer
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 플레이어 공격 컨트롤러. (v3.1)
    ///
    /// ────────────────────────────────────────────────────
    /// [행동 우선순위]
    ///   1. 봉인 집행 (BossWardenSealExecutor 전담)
    ///   2. 회피 대시 (공격 캔슬 후 즉시 실행)
    ///   3. 공격 (이동과 완전 독립)
    ///   4. 이동 (PlayerMoveController 전담)
    ///
    /// [콤보 진입 조건]
    ///   !_isAttacking && !_comboWindowOpen → 1콤보 시작
    ///   !_isAttacking &&  _comboWindowOpen → 다음 콤보 순환
    ///   _isAttacking  && !_comboWindowOpen → 완전 무시
    ///
    /// [외부 이벤트 구독]
    ///   OnHitTarget    += (pos, sealAmount) => ...
    ///   OnAttackStarted += () => ...
    ///   OnAttackEnded   += () => ...   ← ComboRoutine 끝 1곳에서만 발행
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(PlayerMoveController))]
    [RequireComponent(typeof(PlayerWeaponSwingController))]
    public class PlayerAttackController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 데이터 SO ──────────────────────")]

        /// <summary>
        /// 공격 수치 ScriptableObject.
        /// ComboResetTime / MaxComboCount / 봉인도 등 포함. 필수 연결.
        /// </summary>
        [Tooltip("공격 수치 SO. PlayerAttackDataSO 연결 필수.")]
        [SerializeField] private PlayerAttackDataSO _data;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 이동 컨트롤러.
        /// FacingDirection 읽기 전용 참조.
        /// SetMoveLocked 호출 금지 — 이 컴포넌트는 이동 잠금 하지 않음.
        /// </summary>
        private PlayerMoveController _moveController;

        /// <summary>
        /// 무기 스윙 DOTween 연출 컨트롤러.
        /// PlaySwing / CancelSwing / IsSwinging 참조.
        /// </summary>
        private PlayerWeaponSwingController _swingController;

        /// <summary>
        /// 히트박스 활성/비활성 전담 컴포넌트.
        /// null 허용 — 경고만 출력.
        /// </summary>
        private PlayerAttackHitboxManager _hitboxManager;

        // ══════════════════════════════════════════════════════
        // 공격 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 공격 진행 중 여부.
        ///
        /// [true 구간]
        ///   ComboRoutine 시작 → SwingRoutine 완전 종료까지
        ///
        /// [false 구간]
        ///   ComboRoutine 종료 후 / CancelAttack 후
        ///
        /// [v3.1 변경]
        ///   HandleReturnStart 에서 더 이상 false 로 바꾸지 않음.
        ///   ComboRoutine while 루프 탈출 시점에만 false.
        ///   → 중복 ComboRoutine 생성 방지.
        /// </summary>
        private bool _isAttacking;

        /// <summary>
        /// 현재 콤보 단계. 0 = 1콤보, 1 = 2콤보, 2 = 3콤보.
        /// ExecuteCombo 에서 갱신.
        /// </summary>
        private int _currentCombo;

        /// <summary>
        /// 콤보 윈도우 열림 여부.
        ///
        /// [true 구간]
        ///   OnReturnStart(HandleReturnStart) 수신 시점 ~
        ///   ExecuteCombo 진입 즉시 false / ComboRoutine 종료 시 false
        ///
        /// [역할]
        ///   HandleRelease 에서 다음 콤보 진입 허용 여부 판단.
        ///   true = 다음 콤보 순환 허용.
        ///   false = 공격 중이면 완전 무시.
        /// </summary>
        private bool _comboWindowOpen;

        /// <summary>
        /// 현재 공격 방향. 콤보 시작 시점 마우스 방향으로 확정.
        /// 복귀 구간(OnReturnStart) 이후 다음 콤보 방향 업데이트 허용.
        /// </summary>
        private Vector2 _currentAttackDir;

        /// <summary>
        /// 다음 콤보 방향 스냅샷.
        /// 콤보 윈도우 구간에서 클릭 시 그 시점 FacingDirection 저장.
        /// ComboRoutine 시작 시 적용 후 zero 초기화.
        /// </summary>
        private Vector2 _nextComboDir;

        // ══════════════════════════════════════════════════════
        // 강공격 홀드 상태 (API 유지용)
        // ══════════════════════════════════════════════════════

        /// <summary>마우스 좌클릭 누른 시각. 홀드 시간 계산용.</summary>
        private float _pressTime;

        /// <summary>마우스 좌클릭 홀드 여부. Release 시 처리 판단.</summary>
        private bool _isHeld;

        // ══════════════════════════════════════════════════════
        // 코루틴 참조
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 실행 중인 ComboRoutine 코루틴 참조.
        /// ExecuteCombo 진입 시 기존 코루틴 StopCoroutine 후 재시작.
        /// → 중복 코루틴 방지.
        /// </summary>
        private Coroutine _attackCoroutine;

        /// <summary>
        /// 콤보 리셋 타이머 코루틴 참조.
        /// 새 공격 입력 시 StopCoroutine 후 재시작.
        /// </summary>
        private Coroutine _comboResetCoroutine;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 적 적중 시 발행.
        /// 파라미터: (적중 위치 Vector2, 봉인도 float).
        /// SealGauge, HitFeedback 등에서 구독.
        /// </summary>
        public event Action<Vector2, float> OnHitTarget;

        /// <summary>
        /// 공격 시작 시 1회 발행.
        /// PlayerController 에서 구독 → Attack 상태 진입.
        /// ComboRoutine 시작 시 발행.
        /// </summary>
        public event Action OnAttackStarted;

        /// <summary>
        /// 공격 완전 종료 시 1회 발행.
        ///
        /// [v3.1 — 단 1곳에서만 발행]
        ///   ComboRoutine while 루프 탈출 후 발행.
        ///   HandleReturnStart 에서는 발행하지 않음.
        ///   CancelAttack 에서 별도 발행 (캔슬 경로).
        ///
        /// PlayerController 에서 구독 → Idle/Move 상태 복귀.
        /// </summary>
        public event Action OnAttackEnded;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 공격 진행 중 여부.</summary>
        public bool IsAttacking => _isAttacking;

        /// <summary>현재 콤보 단계 (0부터).</summary>
        public int CurrentCombo => _currentCombo;

        /// <summary>
        /// 콤보 윈도우 열림 여부.
        /// UI 콤보 표시 등 외부 참조용.
        /// </summary>
        public bool IsComboWindowOpen => _comboWindowOpen;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _moveController = GetComponent<PlayerMoveController>();
            _swingController = GetComponent<PlayerWeaponSwingController>();
            _hitboxManager = GetComponent<PlayerAttackHitboxManager>();

            if (_hitboxManager == null)
                Debug.LogWarning("[PlayerAttackController] PlayerAttackHitboxManager 미연결.");

            if (_data == null)
            {
                Debug.LogError("[PlayerAttackController] PlayerAttackDataSO 미연결.");
                enabled = false;
            }
        }

        private void Start()
        {
            if (PlayerInputHandler.Instance == null)
            {
                Debug.LogError("[PlayerAttackController] PlayerInputHandler 없음.");
                enabled = false;
                return;
            }

            // ── 입력 구독 ──────────────────────
            PlayerInputHandler.Instance.OnAttack -= HandlePress;
            PlayerInputHandler.Instance.OnAttack += HandlePress;
            PlayerInputHandler.Instance.OnAttackReleased -= HandleRelease;
            PlayerInputHandler.Instance.OnAttackReleased += HandleRelease;

            // ── 히트박스 구독 ──────────────────────
            if (_hitboxManager != null)
            {
                _hitboxManager.OnHit -= HandleHit;
                _hitboxManager.OnHit += HandleHit;
            }

            // ── 대시 캔슬 구독 ──────────────────────
            if (_moveController != null)
            {
                _moveController.OnDashStarted -= HandleDashStarted;
                _moveController.OnDashStarted += HandleDashStarted;
            }

            // ── 복귀 구간 알림 구독 ──────────────────────
            if (_swingController != null)
            {
                _swingController.OnReturnStart -= HandleReturnStart;
                _swingController.OnReturnStart += HandleReturnStart;
            }
        }

        private void OnDestroy()
        {
            if (PlayerInputHandler.Instance != null)
            {
                PlayerInputHandler.Instance.OnAttack -= HandlePress;
                PlayerInputHandler.Instance.OnAttackReleased -= HandleRelease;
            }

            if (_hitboxManager != null) _hitboxManager.OnHit -= HandleHit;
            if (_moveController != null) _moveController.OnDashStarted -= HandleDashStarted;
            if (_swingController != null) _swingController.OnReturnStart -= HandleReturnStart;
        }

        // ══════════════════════════════════════════════════════
        // 입력 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 마우스 좌클릭 pressed 콜백.
        /// 홀드 타이머 기록 + 차지 맥동 시작.
        /// 차단 여부와 무관하게 타이머는 기록 (Release 에서 차단 체크).
        /// </summary>
        private void HandlePress()
        {
            _pressTime = Time.time;
            _isHeld = true;

            // 차단 아닐 때만 맥동 시작
            if (PlayerInputHandler.Instance != null &&
                !PlayerInputHandler.Instance.IsActionBlocked)
                _swingController?.StartChargePulse();
        }

        /// <summary>
        /// 마우스 좌클릭 released 콜백.
        ///
        /// [v3.1 진입 조건 — 명확하게 3가지만]
        ///   A. 차단 상태 (_actionBlocked) → 완전 무시
        ///   B. _isAttacking = true && _comboWindowOpen = false
        ///      → 공격 최중간 구간 → 완전 무시
        ///   C. _isAttacking = false
        ///      → 유휴/이동 상태 → 1콤보 시작
        ///   D. _isAttacking = true && _comboWindowOpen = true
        ///      → 복귀 구간 콤보 윈도우 → 다음 콤보 예약 (ExecuteCombo)
        ///
        /// [핵심 원칙]
        ///   B 구간에서 클릭 → 아무것도 하지 않음 (이전 콤보 진행 중)
        ///   D 구간에서 클릭 → 기존 ComboRoutine 중단 + 다음 콤보 시작
        /// </summary>
        private void HandleRelease()
        {
            if (!_isHeld) return;

            _isHeld = false;
            _swingController?.StopChargePulse();

            // A: 차단 상태 → 완전 무시
            if (PlayerInputHandler.Instance != null &&
                PlayerInputHandler.Instance.IsActionBlocked)
                return;

            // B: 공격 중이고 콤보 윈도우 닫힘 → 완전 무시
            //    (백스윙 / 타격 구간에서는 어떤 입력도 받지 않음)
            if (_isAttacking && !_comboWindowOpen)
                return;

            // C + D: 공격 중 아님 OR 콤보 윈도우 열림 → 실행
            ExecuteCombo();
        }

        /// <summary>
        /// 대시 시작 이벤트 수신 → 공격 즉시 캔슬.
        /// 회피 최우선 원칙.
        /// </summary>
        private void HandleDashStarted()
        {
            if (!_isAttacking) return;
            CancelAttack();
            Debug.Log("[PlayerAttackController] 대시 → 공격 캔슬");
        }

        /// <summary>
        /// SwingController.OnReturnStart 수신.
        /// 복귀 모션 시작 직전 시점 = 콤보 윈도우 구간 진입.
        ///
        /// [v3.1 변경 — _isAttacking 건드리지 않음]
        ///   기존: _isAttacking = false → HandleRelease 에서 즉시 ExecuteCombo 진입
        ///         → 기존 ComboRoutine 과 새 ComboRoutine 동시 실행
        ///   변경: _isAttacking 은 그대로 true 유지
        ///         _comboWindowOpen = true 만 설정
        ///         HandleRelease 에서 _comboWindowOpen 체크 후 ExecuteCombo 진입
        ///         ExecuteCombo 에서 기존 _attackCoroutine StopCoroutine 처리
        ///
        /// [이 시점에 처리하는 것]
        ///   1. _comboWindowOpen = true  (다음 클릭 허용)
        ///   2. 방향 변경 허용 (다음 콤보 방향 업데이트)
        ///   3. WeaponPivot 방향 현재 FacingDirection 으로 갱신
        /// </summary>
        private void HandleReturnStart()
        {
            // 콤보 윈도우 열기
            _comboWindowOpen = true;

            // 다음 콤보 방향 업데이트 (복귀 구간부터 방향 자유)
            Vector2 newDir = GetAttackDir();
            if (newDir.sqrMagnitude > 0.01f)
            {
                _nextComboDir = newDir;
                _swingController?.UpdatePivotToFacing(newDir);
            }

            Debug.Log("[PlayerAttackController] 콤보 윈도우 열림");
        }

        // ══════════════════════════════════════════════════════
        // 공격 실행
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 공격 실행 진입점.
        ///
        /// [v3.1 진입 조건]
        ///   HandleRelease 에서만 호출.
        ///   _isAttacking = false  → _currentCombo = 0 (새 콤보 시작)
        ///   _comboWindowOpen=true → _currentCombo 순환 (다음 콤보)
        ///
        /// [코루틴 중복 방지]
        ///   진입 즉시 기존 _attackCoroutine StopCoroutine.
        ///   → 이전 ComboRoutine while 루프 강제 종료.
        ///   → OnAttackEnded 중복 발행 없음.
        ///
        /// [콤보 윈도우 닫기]
        ///   진입 즉시 _comboWindowOpen = false.
        ///   → 단 1번 클릭만 허용 보장.
        /// </summary>
        private void ExecuteCombo()
        {
            StopResetTimer();

            // ── 콤보 단계 결정 ──────────────────────
            if (_comboWindowOpen)
            {
                // 복귀 구간 클릭 → 다음 콤보 순환
                _currentCombo = (_currentCombo + 1) % _data.MaxComboCount;
                _comboWindowOpen = false;   // 즉시 닫기 — 단 1회만
            }
            else
            {
                // 유휴/이동 중 클릭 → 1콤보 시작
                _currentCombo = 0;
                _comboWindowOpen = false;
            }

            // ── 기존 코루틴 중단 ──────────────────────
            // v3.1 핵심: 이전 ComboRoutine 이 살아있으면 강제 종료
            // → while(_swingController.IsSwinging) 루프 즉시 탈출
            // → 해당 루틴의 OnAttackEnded 발행 없이 종료됨
            if (_attackCoroutine != null)
            {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
            }

            // ── 새 콤보 코루틴 시작 ──────────────────────
            _attackCoroutine = StartCoroutine(ComboRoutine());
        }

        // ══════════════════════════════════════════════════════
        // 공격 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기본 공격 코루틴. 콤보 1회 전체 흐름 담당.
        ///
        /// [v3.1 흐름]
        ///   1. _isAttacking = true, _comboWindowOpen = false, _nextComboDir 초기화
        ///   2. OnAttackStarted 발행 (PlayerController Attack 상태 진입)
        ///   3. 공격 방향 결정 (FacingDirection 또는 _nextComboDir)
        ///   4. SwingController.PlaySwing 호출
        ///   5. 1프레임 대기 (IsSwinging = true 전파 보장)
        ///   6. while(IsSwinging) 루프 대기
        ///      → OnReturnStart 수신 → HandleReturnStart → _comboWindowOpen = true
        ///      → 플레이어 클릭 → HandleRelease → ExecuteCombo
        ///         → StopCoroutine(이 코루틴) → 새 ComboRoutine 시작
        ///      → 클릭 없으면 IsSwinging = false → while 탈출
        ///   7. _isAttacking = false
        ///   8. OnAttackEnded 발행   ← 단 1곳
        ///   9. StartResetTimer
        ///
        /// [이동]
        ///   SetMoveLocked 호출 없음.
        ///   PlayerMoveController 가 WASD 이동 독립 처리.
        /// </summary>
        private IEnumerator ComboRoutine()
        {
            // ── 상태 초기화 ──────────────────────
            _isAttacking = true;
            _comboWindowOpen = false;

            // _nextComboDir: ExecuteCombo 진입 전 HandleReturnStart 에서 저장한 값 유지
            // 0콤보 시작이면 zero → GetAttackDir 사용
            // 콤보 연결이면 HandleReturnStart 에서 저장한 방향 사용

            OnAttackStarted?.Invoke();

            // ── 공격 방향 결정 ──────────────────────
            if (_currentCombo == 0 || _nextComboDir.sqrMagnitude < 0.01f)
            {
                // 1콤보 시작 또는 다음 방향 없음 → 현재 마우스 방향
                _currentAttackDir = GetAttackDir();
            }
            else
            {
                // 콤보 연결 → HandleReturnStart 에서 저장한 방향
                _currentAttackDir = _nextComboDir.normalized;
            }

            _nextComboDir = Vector2.zero; // 소비 후 초기화

            // ── 스윙 실행 ──────────────────────
            var comboIndex = (PlayerWeaponSwingController.ComboIndex)
                Mathf.Clamp(_currentCombo, 0, 2);
            float sealAmount = GetComboSealAmount();

            _swingController.PlaySwing(comboIndex, _currentAttackDir, sealAmount);

            // 1프레임 대기: SwingRoutine 이 시작되어 IsSwinging = true 가 될 때까지 보장
            yield return null;

            // ── 스윙 완료 대기 ──────────────────────
            // IsSwinging = false 가 되는 시점 = SwingRoutine 완전 종료
            // 이 while 루프 중 ExecuteCombo 가 StopCoroutine 하면 즉시 탈출
            // → 아래 _isAttacking = false / OnAttackEnded 는 실행 안 됨 (정상)
            float _maxWait =
                (_data.BackswingDuration + _data.AttackDuration + _data.ReturnDuration) * 2f;
            float _elapsed = 0f;

            while (_swingController.IsSwinging && _elapsed < _maxWait)
            {
                _elapsed += Time.deltaTime;
                yield return null;
            }

            // ── 공격 완전 종료 ──────────────────────
            // 여기까지 도달 = 클릭 없이 모든 스윙 완료
            _comboWindowOpen = false;
            _isAttacking = false;

            // [v3.1] OnAttackEnded 는 이 1곳에서만 발행
            // CancelAttack 경로는 그쪽에서 별도 발행
            OnAttackEnded?.Invoke();

            StartResetTimer();

            _attackCoroutine = null;

            Debug.Log($"[PlayerAttackController] 콤보 {_currentCombo + 1} 완전 종료");
        }

        // ══════════════════════════════════════════════════════
        // 공격 캔슬
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 공격 즉시 캔슬.
        /// 대시 / 봉인 집행 / 피격 시 호출.
        ///
        /// [v3.1]
        ///   _attackCoroutine StopCoroutine → while 루프 강제 종료.
        ///   SwingController CancelSwing → DOTween 정리 + 무기 원점 복귀.
        ///   OnAttackEnded 발행 (캔슬 경로).
        ///   SetMoveLocked 호출 없음.
        /// </summary>
        public void CancelAttack()
        {
            // 코루틴 중단
            if (_attackCoroutine != null)
            {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
            }

            StopResetTimer();

            // DOTween + 히트박스 정리
            _swingController?.CancelSwing();
            _hitboxManager?.DisableAllHitboxes();
            _swingController?.StopChargePulse();

            // 상태 초기화
            _isAttacking = false;
            _comboWindowOpen = false;
            _currentCombo = 0;
            _nextComboDir = Vector2.zero;
            _isHeld = false;

            // 캔슬 경로 OnAttackEnded 발행
            OnAttackEnded?.Invoke();

            Debug.Log("[PlayerAttackController] 공격 캔슬");
        }

        // ══════════════════════════════════════════════════════
        // 히트박스 적중
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신.
        /// 적중 위치 계산 → OnHitTarget 발행 → HitFeedback 재생.
        /// </summary>
        /// <param name="hitCol">적중한 Collider2D.</param>
        /// <param name="sealAmount">이 히트의 봉인도 누적량.</param>
        private void HandleHit(Collider2D hitCol, float sealAmount)
        {
            Vector2 hitPos = hitCol.bounds.size.sqrMagnitude > 0.001f
                ? (Vector2)hitCol.bounds.center
                : (Vector2)hitCol.transform.position;

            OnHitTarget?.Invoke(hitPos, sealAmount);
            HitFeedbackController.Instance?.PlayEnemyHit(hitPos);

            Debug.Log($"[PlayerAttackController] 적중: {hitCol.name} | " +
                      $"봉인도 +{sealAmount:F1} | 콤보: {_currentCombo + 1}");
        }

        // ══════════════════════════════════════════════════════
        // 콤보 리셋 타이머
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 콤보 리셋 타이머 시작.
        /// ComboRoutine 정상 종료 후 호출.
        /// ComboResetTime 내 새 입력 없으면 _currentCombo = 0.
        /// </summary>
        private void StartResetTimer()
        {
            StopResetTimer();
            _comboResetCoroutine = StartCoroutine(ComboResetRoutine());
        }

        /// <summary>
        /// 콤보 리셋 타이머 중단.
        /// 새 공격 입력 / CancelAttack 시 호출.
        /// </summary>
        private void StopResetTimer()
        {
            if (_comboResetCoroutine == null) return;
            StopCoroutine(_comboResetCoroutine);
            _comboResetCoroutine = null;
        }

        /// <summary>
        /// 콤보 리셋 코루틴.
        /// ComboResetTime 초 후 _currentCombo = 0 초기화.
        /// </summary>
        private IEnumerator ComboResetRoutine()
        {
            yield return new WaitForSeconds(_data.ComboResetTime);
            _currentCombo = 0;
            _comboResetCoroutine = null;
            Debug.Log("[PlayerAttackController] 콤보 리셋 → 1콤보");
        }

        // ══════════════════════════════════════════════════════
        // 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 공격 방향 반환.
        /// PlayerMoveController.FacingDirection (마우스 → 플레이어 방향 기준).
        /// FacingDirection 이 zero 이면 기본값 Vector2.right 반환.
        /// </summary>
        private Vector2 GetAttackDir()
        {
            if (_moveController == null) return Vector2.right;
            Vector2 dir = _moveController.FacingDirection;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.right;
        }

        /// <summary>
        /// 현재 콤보 단계의 봉인도 누적량 반환.
        /// PlayerAttackDataSO 에서 콤보별 수치 읽기.
        /// </summary>
        private float GetComboSealAmount()
        {
            if (_data == null) return 10f;

            return _currentCombo switch
            {
                0 => _data.Combo1SealAmount,
                1 => _data.Combo2SealAmount,
                2 => _data.Combo3SealAmount,
                _ => _data.Combo1SealAmount,
            };
        }

        // ══════════════════════════════════════════════════════
        // Gizmos — 에디터 디버그 표시
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            // 공격 방향 표시
            if (_isAttacking)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawRay(transform.position, _currentAttackDir * 1.5f);
            }

            // 콤보 윈도우 열림 표시 (노란 구체)
            if (_comboWindowOpen)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, 0.4f);
            }
        }
    }
}