// ============================================================
// PlayerAttackController.cs  v3.0
// 플레이어 공격 컨트롤러 — 세피리아 스타일 리팩토링
//
// [v3.0 — 전면 리팩토링]
//
//   [핵심 설계 원칙]
//     1. 이동은 PlayerMoveController 가 완전히 독립 처리
//        이 컴포넌트는 SetMoveLocked 를 절대 호출하지 않음
//        SetMoveLocked = 봉인 집행(BossWardenSealExecutor) 전용
//
//     2. 공격은 이동 상태와 무관하게 즉시 실행
//        정지 중이든 이동 중이든 마우스 좌클릭 → 공격
//
//     3. 회피(대시) 최우선
//        공격 중 대시 → CancelAttack() → 대시 실행
//
//   [입력 구조]
//     OnAttack (pressed)    → _pressTime 기록, _isHeld = true
//     OnAttackReleased (released) → holdTime 계산
//       짧게 클릭 → 기본 공격 (ExecuteCombo)
//       길게 홀드 → 강공격 (ExecuteCharge)
//
//   [콤보 규칙]
//     0 → 1 → 2 → 0 (순환)
//     ComboResetTime 내 입력 없음 → 0 초기화
//
//   [공격 방향]
//     콤보 시작 시점 마우스 방향으로 고정
//     복귀 구간(OnReturnStart) 에서만 변경 허용
//
//   [제거된 것들]
//     SetMoveLocked 호출 전부 제거
//     AttackMoveRoutine 제거
//     PlayLunge / _visualTransform 제거
//     _attackMoveCoroutine 제거
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
    /// 플레이어 공격 컨트롤러. (v3.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [행동 우선순위]
    ///   1. 봉인 집행 (BossWardenSealExecutor 전담)
    ///   2. 회피 대시 (공격 캔슬 후 즉시 실행)
    ///   3. 공격 (이동과 완전 독립)
    ///   4. 이동 (PlayerMoveController 전담)
    ///
    /// [외부 이벤트 구독]
    ///   OnHitTarget += (pos, sealAmount) => ...
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

        /// <summary>공격 수치 SO. 필수 연결.</summary>
        [Tooltip("공격 수치 SO. PlayerAttackDataSO 연결 필수.")]
        [SerializeField] private PlayerAttackDataSO _data;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>이동 컨트롤러. FacingDirection 참조 전용. 이동 잠금 호출 없음.</summary>
        private PlayerMoveController _moveController;

        /// <summary>무기 스윙 연출 컨트롤러.</summary>
        private PlayerWeaponSwingController _swingController;

        /// <summary>히트박스 관리 컴포넌트.</summary>
        private PlayerAttackHitboxManager _hitboxManager;

        // ══════════════════════════════════════════════════════
        // 공격 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 공격 진행 중 여부.</summary>
        private bool _isAttacking;

        /// <summary>현재 콤보 단계 (0=1콤보, 1=2콤보, 2=3콤보).</summary>
        private int _currentCombo;

        /// <summary>콤보 윈도우 열림 여부. true = 다음 공격 입력 허용 구간.</summary>
        private bool _comboWindowOpen;

        /// <summary>콤보 윈도우 내 입력 예약 여부.</summary>
        private bool _comboInputQueued;

        /// <summary>현재 공격 방향. 콤보 시작 시점에 확정.</summary>
        private Vector2 _currentAttackDir;

        /// <summary>
        /// 다음 콤보 방향 스냅샷.
        /// 콤보 윈도우에서 클릭 시 그 시점의 FacingDirection 저장.
        /// </summary>
        private Vector2 _nextComboDir;

        /// <summary>
        /// 복귀 구간 방향 변경 허용 여부.
        /// OnReturnStart → true / 공격 시작 → false.
        /// </summary>
        private bool _canChangeDir;

        // ══════════════════════════════════════════════════════
        // 강공격 상태
        // ══════════════════════════════════════════════════════

        /// <summary>마우스 좌클릭 누른 시각.</summary>
        private float _pressTime;

        /// <summary>마우스 좌클릭 홀드 여부.</summary>
        private bool _isHeld;

        // ══════════════════════════════════════════════════════
        // 코루틴 참조
        // ══════════════════════════════════════════════════════

        private Coroutine _attackCoroutine;
        private Coroutine _comboResetCoroutine;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>적 적중 시 발행. (적중 위치, 봉인도)</summary>
        public event Action<Vector2, float> OnHitTarget;

        /// <summary>기본 공격 시작 시 발행.</summary>
        public event Action OnAttackStarted;

        /// <summary>강공격 시작 시 발행.</summary>
        public event Action OnChargeAttackStarted;

        /// <summary>공격 완전 종료 시 발행. PlayerController 상태 복귀용.</summary>
        public event Action OnAttackEnded;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 공격 중 여부.</summary>
        public bool IsAttacking => _isAttacking;

        /// <summary>현재 콤보 단계 (0부터).</summary>
        public int CurrentCombo => _currentCombo;

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

            // 입력 구독
            PlayerInputHandler.Instance.OnAttack -= HandlePress;
            PlayerInputHandler.Instance.OnAttack += HandlePress;
            PlayerInputHandler.Instance.OnAttackReleased -= HandleRelease;
            PlayerInputHandler.Instance.OnAttackReleased += HandleRelease;

            // 히트박스 구독
            if (_hitboxManager != null)
            {
                _hitboxManager.OnHit -= HandleHit;
                _hitboxManager.OnHit += HandleHit;
            }

            // 대시 캔슬 구독
            if (_moveController != null)
            {
                _moveController.OnDashStarted -= HandleDashStarted;
                _moveController.OnDashStarted += HandleDashStarted;
            }

            // 복귀 구간 방향 변경 구독
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

            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandleHit;

            if (_moveController != null)
                _moveController.OnDashStarted -= HandleDashStarted;

            if (_swingController != null)
                _swingController.OnReturnStart -= HandleReturnStart;
        }

        // ══════════════════════════════════════════════════════
        // 입력 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 마우스 좌클릭 pressed.
        /// 홀드 타이머 시작 + 차지 맥동 연출 시작.
        /// 차단 상태여도 타이머는 기록 (IsActionBlocked 는 Release 에서 체크).
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
        /// 마우스 좌클릭 released.
        /// holdTime 기준으로 기본 공격 / 강공격 분기.
        ///
        /// [분기 규칙]
        ///   _isHeld = false 이면 (HandlePress 미호출) → 무시
        ///   IsActionBlocked = true → 맥동만 종료 후 리턴
        ///   holdTime >= ChargeMinHoldTime + !_isAttacking → 강공격
        ///   holdTime <  ChargeMinHoldTime + !_isAttacking → 기본 공격
        ///   _isAttacking + _comboWindowOpen → 콤보 예약
        ///   _isAttacking + !_comboWindowOpen → 무시
        /// </summary>
        private void HandleRelease()
        {
            if (!_isHeld) return;

            float holdTime = Time.time - _pressTime;
            _isHeld = false;
            _swingController?.StopChargePulse();

            // 차단 상태 → 공격 진입 불가
            if (PlayerInputHandler.Instance != null &&
                PlayerInputHandler.Instance.IsActionBlocked)
                return;

            // 공격 중 아닌 경우
            if (!_isAttacking)
            {
                if (holdTime >= _data.ChargeMinHoldTime)
                    ExecuteCharge();
                else
                    ExecuteCombo();
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
        /// 대시 시작 → 공격 즉시 캔슬.
        /// 회피 최우선 원칙 적용.
        /// </summary>
        private void HandleDashStarted()
        {
            if (!_isAttacking) return;
            CancelAttack();
            Debug.Log("[PlayerAttackController] 대시 → 공격 캔슬");
        }

        /// <summary>
        /// SwingController 복귀 구간 진입 시 방향 변경 허용.
        /// </summary>
        private void HandleReturnStart()
        {
            _canChangeDir = true;

            // 복귀 시작 시점 마우스 방향으로 WeaponPivot 갱신
            Vector2 newDir = GetAttackDir();
            if (newDir.sqrMagnitude > 0.01f)
            {
                _currentAttackDir = newDir;
                _swingController?.UpdatePivotToFacing(_currentAttackDir);
            }
        }

        // ══════════════════════════════════════════════════════
        // 공격 실행
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기본 공격 실행. 이미 공격 중이면 무시.
        /// </summary>
        private void ExecuteCombo()
        {
            if (_isAttacking) return;
            StopResetTimer();
            _attackCoroutine = StartCoroutine(ComboRoutine());
        }

        /// <summary>
        /// 강공격 실행. 이미 공격 중이면 무시.
        /// </summary>
        private void ExecuteCharge()
        {
            if (_isAttacking) return;
            StopResetTimer();
            _attackCoroutine = StartCoroutine(ChargeRoutine());
        }

        // ══════════════════════════════════════════════════════
        // 공격 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기본 공격 코루틴.
        ///
        /// [흐름]
        ///   1. 상태 초기화 + 공격 방향 결정
        ///   2. SwingController.PlaySwing 호출
        ///   3. 1프레임 대기 (IsSwinging = true 보장)
        ///   4. IsSwinging 완료 대기
        ///   5. 콤보 윈도우 대기
        ///   6. 콤보 예약 있으면 순환 / 없으면 리셋 타이머
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
            _comboInputQueued = false;
            _canChangeDir = false;

            OnAttackStarted?.Invoke();

            // ── 공격 방향 결정 ──────────────────────
            // 1콤보 시작: 마우스 방향으로 새로 결정
            // 2/3콤보:    콤보 윈도우 시점 스냅샷 사용 (없으면 이전 방향 유지)
            if (_currentCombo == 0)
                _currentAttackDir = GetAttackDir();
            else if (_nextComboDir.sqrMagnitude > 0.01f)
                _currentAttackDir = _nextComboDir.normalized;

            _nextComboDir = Vector2.zero;

            // ── 스윙 실행 ──────────────────────
            var comboIndex = (PlayerWeaponSwingController.ComboIndex)
                Mathf.Clamp(_currentCombo, 0, 2);
            float sealAmount = GetComboSealAmount();

            _swingController.PlaySwing(comboIndex, _currentAttackDir, sealAmount);

            // IsSwinging = true 가 될 때까지 1프레임 대기 (코루틴 시작 보장)
            yield return null;

            // ── 스윙 완료 대기 ──────────────────────
            float maxWait = (_data.BackswingDuration + _data.AttackDuration + _data.ReturnDuration) * 2f;
            float elapsed = 0f;
            while (_swingController.IsSwinging && elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // ── 콤보 윈도우 ──────────────────────
            _comboWindowOpen = true;
            float windowTime = _data.BackswingDuration
                                + _data.AttackDuration * (1f - _data.ComboWindowStartRatio);
            float windowElapsed = 0f;

            while (windowElapsed < windowTime)
            {
                windowElapsed += Time.deltaTime;
                yield return null;
            }

            _comboWindowOpen = false;
            _isAttacking = false;
            OnAttackEnded?.Invoke();

            // ── 콤보 순환 or 리셋 ──────────────────────
            if (_comboInputQueued)
            {
                _comboInputQueued = false;
                _currentCombo = (_currentCombo + 1) % _data.MaxComboCount;
                ExecuteCombo();
            }
            else
            {
                StartResetTimer();
            }

            _attackCoroutine = null;
        }

        /// <summary>
        /// 강공격 코루틴.
        ///
        /// [이동]
        ///   SetMoveLocked 호출 없음.
        ///   PlayerMoveController 가 WASD 이동 독립 처리.
        /// </summary>
        private IEnumerator ChargeRoutine()
        {
            _isAttacking = true;
            _canChangeDir = false;

            OnChargeAttackStarted?.Invoke();

            _currentAttackDir = GetAttackDir();
            _swingController.PlayChargeSwing(_currentAttackDir, _data.ChargeSealAmount);

            // 1프레임 대기
            yield return null;

            // 스윙 완료 대기
            float maxWait = (_data.BackswingDuration + _data.AttackDuration + _data.ReturnDuration) * 3f;
            float elapsed = 0f;
            while (_swingController.IsSwinging && elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _currentCombo = 0;
            _isAttacking = false;
            OnAttackEnded?.Invoke();

            _attackCoroutine = null;

            StartResetTimer();
        }

        // ══════════════════════════════════════════════════════
        // 공격 캔슬
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 공격 즉시 캔슬. 대시 / 봉인 집행 / 피격 시 호출.
        ///
        /// [v3.0]
        ///   SetMoveLocked 호출 없음.
        ///   이동은 PlayerMoveController 가 독립 처리.
        /// </summary>
        public void CancelAttack()
        {
            if (_attackCoroutine != null)
            {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
            }

            StopResetTimer();
            _swingController?.CancelSwing();
            _hitboxManager?.DisableAllHitboxes();

            _isAttacking = false;
            OnAttackEnded?.Invoke();

            _comboWindowOpen = false;
            _comboInputQueued = false;
            _currentCombo = 0;
            _nextComboDir = Vector2.zero;
            _isHeld = false;
            _canChangeDir = false;

            _swingController?.StopChargePulse();

            Debug.Log("[PlayerAttackController] 공격 캔슬");
        }

        // ══════════════════════════════════════════════════════
        // 히트박스 적중
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신.
        /// 적중 위치 계산 → OnHitTarget 발행 → HitFeedback 재생.
        /// </summary>
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

        private void StartResetTimer()
        {
            StopResetTimer();
            _comboResetCoroutine = StartCoroutine(ComboResetRoutine());
        }

        private void StopResetTimer()
        {
            if (_comboResetCoroutine == null) return;
            StopCoroutine(_comboResetCoroutine);
            _comboResetCoroutine = null;
        }

        private IEnumerator ComboResetRoutine()
        {
            yield return new WaitForSeconds(_data.ComboResetTime);
            _currentCombo = 0;
            _comboResetCoroutine = null;
            Debug.Log("[PlayerAttackController] 콤보 초기화 → 1콤보");
        }

        // ══════════════════════════════════════════════════════
        // 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 공격 방향 반환.
        /// PlayerMoveController.FacingDirection (마우스 방향 기준).
        /// </summary>
        private Vector2 GetAttackDir()
        {
            if (_moveController == null) return Vector2.right;
            Vector2 dir = _moveController.FacingDirection;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.right;
        }

        /// <summary>
        /// 현재 콤보 단계의 봉인도 누적량 반환.
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
        // Gizmos
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            if (!_isAttacking) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, _currentAttackDir * 1.5f);
        }
    }
}