// ============================================================
// PlayerAttackController.cs  v2.4
// 플레이어 공격 컨트롤러
//
// [v2.4 변경 — 공격 중 이동 잠금 제거]
//   마우스 기반 조작으로 전환하면서
//   공격 중에도 자유롭게 이동 가능하도록 SetMoveLocked 제거.
//
//   [제거 위치]
//     ComboAttackRoutine()  : SetMoveLocked(true) / SetMoveLocked(false)
//     ChargeAttackRoutine() : SetMoveLocked(true) / SetMoveLocked(false)
//     CancelAttack()        : SetMoveLocked(false)
//
//   [유지]
//     봉인 집행(BossWardenSealExecutor.BlockPlayerInput) 에서의
//     SetMoveLocked 는 그대로 유지.
//     SetMoveLocked 함수 자체는 제거하지 않음.
//
// [v2.3 변경 — 공격 중 대시 시 공격 캔슬 (7단계)]
// [v2.2 변경 — 콤보 전환 시점 방향 결정 (6단계)]
// [v2.1 변경 — 히트박스 판정 경로 통합]

using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// 플레이어 A키 공격 컨트롤러. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [외부 이벤트 구독 예시]
    ///   _atk.OnHitTarget += (hitPos, sealAmt) =>
    ///       sealGauge.AddGauge(target, sealAmt);
    ///
    /// [요구 컴포넌트]
    ///   PlayerMoveController        (FacingDirection 참조)
    ///   PlayerWeaponSwingController (무기 연출 위임)
    ///   PlayerInputHandler          (OnAttack / OnAttackReleased 구독)
    ///   PlayerAttackDataSO          (_data 연결 필수)
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(PlayerMoveController))]
    [RequireComponent(typeof(PlayerWeaponSwingController))]
    public class PlayerAttackController : MonoBehaviour
    {
        // ──────────────────────────────────────────
        // Inspector — 데이터 SO
        // ──────────────────────────────────────────

        [Header("── 데이터 SO ──────────────────────")]

        /// <summary>
        /// 공격 수치 ScriptableObject.
        /// 콤보별 위치/회전/히트박스/봉인도 수치 포함.
        /// </summary>
        [Tooltip("공격 수치 SO. PlayerAttackDataSO 연결 필수.")]
        [SerializeField] private PlayerAttackDataSO _data;

        /// <summary>
        /// 플레이어 Visual Transform.
        /// 공격 전진(Lunge) 연출 대상.
        /// null 이면 자신 transform 사용.
        /// </summary>
        [Tooltip("공격 전진 Visual Transform. null=자신.")]
        [SerializeField] private Transform _visualTransform;

        // ──────────────────────────────────────────
        // 컴포넌트 참조
        // ──────────────────────────────────────────

        /// <summary>
        /// 이동 컨트롤러. 공격 방향(FacingDirection) 읽기 + 이동 잠금.
        /// </summary>
        private PlayerMoveController _moveController;

        /// <summary>
        /// 무기 스윙 연출 컨트롤러. 모든 DOTween 연출 위임.
        /// </summary>
        private PlayerWeaponSwingController _swingController;

        /// <summary>
        /// Rigidbody2D. 공격 전진 시 velocity 초기화에 사용.
        /// </summary>
        private Rigidbody2D _rigid2D;

        // ──────────────────────────────────────────
        // 콤보 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// 현재 공격 처리 중 여부.
        /// true 동안 콤보 윈도우에서만 입력 수신.
        /// </summary>
        private bool _isAttacking;

        /// <summary>
        /// 현재 콤보 단계 (0=1콤보, 1=2콤보, 2=3콤보).
        /// MaxComboCount 도달 후 다음 공격 시 0으로 초기화.
        /// </summary>
        private int _currentCombo;

        /// <summary>
        /// 콤보 윈도우 열림 여부.
        /// true: 다음 공격 입력을 받을 수 있는 구간.
        /// </summary>
        private bool _comboWindowOpen;

        /// <summary>
        /// 콤보 윈도우 내 입력 예약 여부.
        /// </summary>
        private bool _comboInputQueued;

        /// <summary>
        /// 공격 코루틴 참조.
        /// </summary>
        private Coroutine _attackCoroutine;

        /// <summary>공격 이동 코루틴 참조.</summary>
        private Coroutine _attackMoveCoroutine;

        /// <summary>
        /// 콤보 리셋 타이머 코루틴 참조.
        /// 일정 시간 입력 없을 때 콤보 초기화.
        /// </summary>
        private Coroutine _comboResetCoroutine;

        // ──────────────────────────────────────────
        // 강공격 상태
        // ──────────────────────────────────────────

        /// <summary>
        /// A키 누른 시각 (Time.time). 홀드 시간 계산에 사용.
        /// </summary>
        private float _attackPressTime;

        /// <summary>
        /// 강공격 홀드 중 여부.
        /// performed = true / canceled = false.
        /// </summary>
        private bool _isChargeHolding;

        /// <summary>
        /// 공격 방향 변경 허용 여부.
        /// true: 복귀 구간 — WASD/마우스 방향으로 _currentAttackDir 변경 가능.
        /// false: 백스윙/타격/타격완료 — 방향 고정.
        /// </summary>
        private bool _canChangeDir;

        // ──────────────────────────────────────────
        // 히트박스 참조
        // ──────────────────────────────────────────

        /// <summary>
        /// 히트박스 관리 컴포넌트.
        /// OnHit 이벤트를 구독하여 적중 시 OnHitTarget 발행.
        /// Awake 에서 GetComponent 로 자동 탐색.
        /// </summary>
        private PlayerAttackHitboxManager _hitboxManager;

        /// <summary>
        /// 현재 공격 방향. Lunge 연출 + Gizmos 표시에 사용.
        /// </summary>
        private Vector2 _currentAttackDir;

        /// <summary>
        /// 다음 콤보 공격 방향 스냅샷.
        /// 콤보 윈도우에서 A키를 누르는 순간(HandleAttackPress)에 저장.
        ///
        /// [6단계 추가 — 콤보 전환 시점에만 방향 결정]
        ///   공격 중 SetMoveLocked(true) → FacingDirection 갱신 안 됨.
        ///   → ComboAttackRoutine 진입 시 GetAttackDirection() 을 호출해도
        ///     항상 1콤보 시작 방향이 반환되는 문제.
        ///
        ///   해결: 콤보 윈도우에서 A키가 눌리는 순간
        ///         PlayerInputHandler.Instance.MoveInput (실제 눌린 방향키) 을
        ///         _nextComboDir 에 스냅샷 저장.
        ///         다음 콤보 시작 시 이 방향을 사용.
        ///         방향키 입력이 없으면 _currentAttackDir 유지 (이전 방향 그대로).
        /// </summary>
        private Vector2 _nextComboDir;

        // ──────────────────────────────────────────
        // 이벤트
        // ──────────────────────────────────────────

        /// <summary>
        /// 공격이 적에 적중 시 발행.
        /// 파라미터1: 적중 위치 / 파라미터2: 봉인도 누적량.
        /// SealGaugeSystem 에서 구독하여 봉인도 처리.
        /// </summary>
        public event Action<Vector2, float> OnHitTarget;

        /// <summary> 기본 공격 시작 시 발행. </summary>
        public event Action OnAttackStarted;

        /// <summary> 강공격 시작 시 발행. </summary>
        public event Action OnChargeAttackStarted;

        // ──────────────────────────────────────────
        // 프로퍼티
        // ──────────────────────────────────────────

        /// <summary> 현재 공격 중 여부. </summary>
        public bool IsAttacking => _isAttacking;

        /// <summary> 현재 콤보 단계 (0부터). </summary>
        public int CurrentCombo => _currentCombo;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _moveController = GetComponent<PlayerMoveController>();
            _swingController = GetComponent<PlayerWeaponSwingController>();
            _rigid2D = GetComponent<Rigidbody2D>();
            _hitboxManager = GetComponent<PlayerAttackHitboxManager>();

            if (_visualTransform == null)
                _visualTransform = transform;

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

            PlayerInputHandler.Instance.OnAttack -= HandleAttackPress;
            PlayerInputHandler.Instance.OnAttack += HandleAttackPress;
            PlayerInputHandler.Instance.OnAttackReleased -= HandleAttackRelease;
            PlayerInputHandler.Instance.OnAttackReleased += HandleAttackRelease;

            // HitboxManager.OnHit 구독 → 적중 시 OnHitTarget 발행
            if (_hitboxManager != null)
            {
                _hitboxManager.OnHit -= HandleHitboxHit;
                _hitboxManager.OnHit += HandleHitboxHit;
            }

            // [7단계] 대시 시작 시 공격 캔슬
            // 대시 입력 → OnDashStarted 발행 → CancelAttack()
            if (_moveController != null)
            {
                _moveController.OnDashStarted -= HandleDashStarted;
                _moveController.OnDashStarted += HandleDashStarted;
            }

            if(_swingController != null)
            {
                _swingController.OnReturnStart -= HandleReturnStart;
                _swingController.OnReturnStart += HandleReturnStart;
            }
        }

        // Update() 제거 — ProcessHitCheck OverlapCircle 경로 삭제
        // 히트박스 판정은 PlayerAttackHitboxManager.Update() 가 전담

        private void OnDestroy()
        {
            if (PlayerInputHandler.Instance != null)
            {
                PlayerInputHandler.Instance.OnAttack -= HandleAttackPress;
                PlayerInputHandler.Instance.OnAttackReleased -= HandleAttackRelease;
            }

            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandleHitboxHit;

            if (_moveController != null)
                _moveController.OnDashStarted -= HandleDashStarted;

            if (_swingController != null)
                _swingController.OnReturnStart -= HandleReturnStart;
        }

        // ══════════════════════════════════════════════════════
        // 입력 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// A키 누름. OnAttack 콜백.
        /// 홀드 타이머 시작 + 공격 실행 또는 콤보 예약.
        ///
        /// [6단계 — 콤보 전환 시점 방향 스냅샷]
        ///   콤보 윈도우에서 A키가 눌리는 순간
        ///   PlayerInputHandler.MoveInput (실제 눌린 방향키) 을 _nextComboDir 에 저장.
        ///   → 다음 콤보는 이 방향으로 실행.
        ///   → 방향키 입력 없으면 Vector2.zero → 이전 방향(_currentAttackDir) 유지.
        /// </summary>
        private void HandleAttackPress()
        {
            _attackPressTime = Time.time;
            _isChargeHolding = true;

            if(PlayerInputHandler.Instance != null && !PlayerInputHandler.Instance.IsActionBlocked)
                _swingController.StartChargePulse();
        }

        /// <summary>
        /// A키 뗌. OnAttackReleased 콜백.
        /// 홀드 시간 확인 후 강공격 판정.
        /// </summary>
        private void HandleAttackRelease()
        {
            if (!_isChargeHolding) return;

            float holdTime = Time.time - _attackPressTime;
            _isChargeHolding = false;
            _swingController.StopChargePulse();

            if (PlayerInputHandler.Instance !=  null && PlayerInputHandler.Instance.IsActionBlocked)
                return;

            // 강공격: 홀드 충족 + 현재 공격 중 아님
            if (holdTime >= _data.ChargeMinHoldTime && !_isAttacking)
            {
                ExecuteChargeAttack();
                return;
            }

            // 기본 공격: 짧게 클릭
            if (!_isAttacking)
            {
                ExecuteComboAttack();
                return;
            }

            // 공격 중 + 콤보 윈도우 열림 → 콤보 예약
            if (_comboWindowOpen)
            {
                _comboInputQueued = true;
                _nextComboDir = _moveController != null
                    ? _moveController.FacingDirection
                    : Vector2.zero;
            }
        }

        /// <summary>
        /// PlayerMoveController.OnDashStarted 수신 핸들러.
        /// 대시 시작 시 현재 공격을 즉시 캔슬.
        ///
        /// [7단계 — 공격 중 대시 시 공격 캔슬]
        ///   공격하면서 대시를 하는 것이 아니라
        ///   대시 입력이 들어오면 공격이 먼저 취소된 후 대시가 실행됨.
        /// </summary>
        private void HandleDashStarted()
        {
            if (!_isAttacking) return;

            CancelAttack();
            Debug.Log("[PlayerAttackController] 대시 입력 → 공격 캔슬");
        }

        /// <summary>
        /// 현재 공격을 즉시 캔슬하고 상태를 초기화한다.
        /// 대시 / 피격 / 봉인 집행 등 공격 중단이 필요한 상황에서 호출.
        ///
        /// [v2.4 변경 — SetMoveLocked 제거]
        ///   공격 중 이동 잠금을 사용하지 않으므로 SetMoveLocked(false) 제거.
        ///
        /// [캔슬 처리 순서]
        ///   ① _attackCoroutine StopCoroutine
        ///   ② SwingController.CancelSwing()       — DOTween 중단 + 무기 원점
        ///   ③ HitboxManager.DisableAllHitboxes()  — 히트박스 즉시 비활성
        ///   ④ 콤보 상태 전체 초기화
        /// </summary>
        public void CancelAttack()
        {
            // ① 공격 코루틴 중단
            if (_attackCoroutine != null)
            {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
            }

            // 콤보 리셋 타이머 중단
            if(_comboResetCoroutine != null)
            {
                StopCoroutine(_comboResetCoroutine);
                _comboResetCoroutine = null;
            }

            // ② 무기 스윙 DOTween 중단 + 원점 복귀
            _swingController?.CancelSwing();

            // ③ 히트박스 비활성화
            _hitboxManager?.DisableAllHitboxes();

            _moveController?.SetMoveLocked(false);

            // ④ 콤보 상태 초기화 ([v2.4] SetMoveLocked(false) 제거)
            _isAttacking        = false;
            _comboWindowOpen    = false;
            _comboInputQueued   = false;
            _currentCombo       = 0;
            _nextComboDir       = Vector2.zero;
            _isChargeHolding    = false;
            _canChangeDir       = false;

            _swingController?.StopChargePulse();
        }

        /// <summary>
        /// SwingController.OnReturnStart 수신.
        /// 복귀 구간 진입 → 방향 변경 허용.
        /// </summary>
        private void HandleReturnStart()
        {
            _canChangeDir = true;

            // 복귀 시작 시점의 마우스 방향으로 즉시 WeaponPivot 회전
            Vector2 newDir = GetAttackDirection();
            if (newDir.sqrMagnitude > 0.01f)
            {
                _currentAttackDir = newDir;
                _swingController.UpdatePivotToFacing(_currentAttackDir);
            }
        }

        // ══════════════════════════════════════════════════════
        // 공격 실행
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 콤보 단계에 맞는 기본 공격 실행.
        /// </summary>
        private void ExecuteComboAttack()
        {
            if (_isAttacking) return;

            if (_attackCoroutine != null) StopCoroutine(_attackCoroutine);
            _attackCoroutine = StartCoroutine(ComboAttackRoutine());
        }

        /// <summary>
        /// 강공격 실행.
        /// </summary>
        private void ExecuteChargeAttack()
        {
            if (_isAttacking) return;

            if (_attackCoroutine != null) StopCoroutine(_attackCoroutine);
            _attackCoroutine = StartCoroutine(ChargeAttackRoutine());
        }

        // ══════════════════════════════════════════════════════
        // 공격 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기본 공격 코루틴.
        ///
        /// [흐름]
        ///   1. 이동 잠금
        ///   2. SwingController.PlaySwing 호출 (무기 DOTween 연출 위임)
        ///      onHit 콜백 → _activeHitboxRadius 설정 → Update에서 판정
        ///   3. Lunge 전진 연출
        ///   4. SwingController 완료 대기 (IsSwinging 폴링)
        ///   5. 콤보 윈도우 → 다음 입력 대기
        ///   6. 콤보 예약 있으면 다음 콤보 / 없으면 리셋
        /// </summary>
        private IEnumerator ComboAttackRoutine()
        {
            _isAttacking        = true;
            _comboWindowOpen    = false;
            _comboInputQueued   = false;
            _canChangeDir       = false;

            OnAttackStarted?.Invoke();

            // [6단계] 공격 방향 결정
            //   1콤보 시작(_currentCombo = 0) : 현재 FacingDirection 사용
            //   2/3콤보 시작                  : 이전 콤보 종료 시 저장된 _nextComboDir 사용
            //     → _nextComboDir 에 방향키 입력 있으면 그 방향
            //     → _nextComboDir = zero 이면 이전 방향(_currentAttackDir) 유지
            if (_currentCombo == 0)
            {
                // 1콤보: 현재 이동 방향 기준
                _currentAttackDir = GetAttackDirection();
            }
            else
            {
                // 2/3콤보: 콤보 입력 시점에 스냅샷한 방향 사용
                // 방향키 입력이 없었으면 이전 콤보 방향 그대로 유지
                if (_nextComboDir.sqrMagnitude > 0.01f)
                    _currentAttackDir = _nextComboDir.normalized;
                // else: _currentAttackDir 변경 없음 (이전 방향 유지)
            }

            // 다음 콤보 방향 초기화 (매 콤보 시작 시 리셋)
            _nextComboDir = Vector2.zero;

            // [v2.4] SetMoveLocked 제거 — 공격 중 이동 자유

            var comboIndex = (PlayerWeaponSwingController.ComboIndex)
                Mathf.Clamp(_currentCombo, 0, 2);
            float sealAmount = GetComboSealAmount();

            _swingController.PlaySwing(comboIndex, _currentAttackDir, sealAmount);
            yield return null;

            float maxWait = (_data.BackswingDuration + _data.AttackDuration + _data.ReturnDuration) * 2f;
            float elapsed = 0f;
            while (_swingController.IsSwinging && elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // ── 콤보 윈도우 ──────────────────────
            _comboWindowOpen = true;
            float windowDuration = _data.BackswingDuration
                                 + _data.AttackDuration * (1f - _data.ComboWindowStartRatio);
            float windowElapsed = 0f;

            while (windowElapsed < windowDuration)
            {
                windowElapsed += Time.deltaTime;
                yield return null;
            }

            _comboWindowOpen = false;
            _isAttacking = false;

            if (_comboInputQueued && _currentCombo < _data.MaxComboCount - 1)
            {
                _comboInputQueued = false;
                _currentCombo = (_currentCombo + 1) % _data.MaxComboCount;
                ExecuteComboAttack();
            }
            else
            {
                StartComboResetTimer();
            }

            _attackCoroutine = null;
        }

        /// <summary>
        /// 강공격 코루틴.
        /// v2.1: sealAmount 직접 전달 → SwingController 내부에서 HitboxManager 제어.
        /// </summary>
        private IEnumerator ChargeAttackRoutine()
        {
            _isAttacking = true;
            OnChargeAttackStarted?.Invoke();

            _currentAttackDir = GetAttackDirection();

            // [v2.4] SetMoveLocked 제거 — 공격 중 이동 자유

            _swingController.PlayChargeSwing(_currentAttackDir, _data.ChargeSealAmount);
            yield return null;

            float maxWait = (_data.BackswingDuration + _data.AttackDuration + _data.ReturnDuration) * 3f;
            float elapsed = 0f;
            while (_swingController.IsSwinging && elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _currentCombo = 0;
            _isAttacking = false;
            _attackCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 히트박스 적중 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신 핸들러.
        /// 히트박스가 적 콜라이더에 Overlap 되면 이 함수가 호출됨.
        ///
        /// [v2.1 — OnSwingHitCallback + ProcessHitCheck 대체]
        ///   기존: SwingController onHit 콜백 → _activeHitboxRadius 설정
        ///         → Update() 매 프레임 OverlapCircle 판정
        ///   변경: HitboxManager.OnHit 이벤트 → 이 함수에서 OnHitTarget 발행
        ///         → 중복 판정 방지는 HitboxManager 내부 _hitTargets HashSet 이 담당
        ///         → ProcessHitCheck() 완전 제거
        /// </summary>
        private void HandleHitboxHit(Collider2D hitCol, float sealAmount)
        {
            // bounds.center 는 콜라이더 크기가 0이거나 초기화 전이면 원점(0,0)을 반환하는 경우 있음
            // → transform.position (오브젝트 월드 위치) 을 우선 사용
            // → bounds.size > 0 인 경우에만 bounds.center 사용
            Vector2 hitPos = hitCol.bounds.size.sqrMagnitude > 0.001f
                ? (Vector2)hitCol.bounds.center
                : (Vector2)hitCol.transform.position;

            OnHitTarget?.Invoke(hitPos, sealAmount);

            // 적 피격 파티클 재생
            HitFeedbackController.Instance?.PlayEnemyHit(hitPos);

            Debug.Log($"[PlayerAttackController] 적중: {hitCol.name} | " +
                      $"봉인도 +{sealAmount:F1} | 콤보: {_currentCombo + 1} | 위치: {hitPos}");
        }

        // ══════════════════════════════════════════════════════
        // 콤보 관리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 콤보 리셋 타이머 시작.
        /// 일정 시간 내 입력 없으면 콤보 0으로 초기화.
        /// </summary>
        private void StartComboResetTimer()
        {
            if (_comboResetCoroutine != null) StopCoroutine(_comboResetCoroutine);
            _comboResetCoroutine = StartCoroutine(ComboResetRoutine());
        }

        private IEnumerator ComboResetRoutine()
        {
            yield return new UnityEngine.WaitForSeconds(_data.ComboResetTime);
            _currentCombo = 0;
            _comboResetCoroutine = null;
            Debug.Log("콤보 초기화");
        }

        // ══════════════════════════════════════════════════════
        // 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 공격 방향 반환.
        /// PlayerMoveController.FacingDirection 기준 (마우스 없음).
        /// </summary>
        private Vector2 GetAttackDirection()
            => _moveController != null
                ? _moveController.FacingDirection.normalized
                : Vector2.right;

        /// <summary>
        /// 현재 콤보 단계에 맞는 봉인도 누적량 반환.
        /// </summary>
        private float GetComboSealAmount()
        {
            return _currentCombo switch
            {
                0 => _data.Combo1SealAmount,
                1 => _data.Combo2SealAmount,
                _ => _data.Combo3SealAmount,
            };
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_data == null) return;

            Vector2 dir = Application.isPlaying ? GetAttackDirection() : Vector2.right;
            Vector2 center = (Vector2)transform.position + dir * _data.HitboxOffset;

            // 기본 히트박스
            UnityEngine.Gizmos.color = new UnityEngine.Color(1f, 1f, 1f, 0.3f);
            UnityEngine.Gizmos.DrawWireSphere(center, _data.HitboxRadius);

            // 강공격 히트박스
            UnityEngine.Gizmos.color = new UnityEngine.Color(1f, 0.8f, 0f, 0.2f);
            UnityEngine.Gizmos.DrawWireSphere(center, _data.HitboxRadius * _data.ChargeHitboxScale);

            UnityEngine.Gizmos.color = UnityEngine.Color.cyan;
            UnityEngine.Gizmos.DrawRay(transform.position, (UnityEngine.Vector3)dir * 1.5f);

            UnityEditor.Handles.Label(
                transform.position + UnityEngine.Vector3.up * 2.2f,
                $"콤보: {_currentCombo + 1} | 공격중: {_isAttacking} | 윈도우: {_comboWindowOpen}");
        }
#endif
    }
}