// ============================================================
// PlayerAttackController.cs  v3.2
// 플레이어 공격 컨트롤러 — 공격 이동 연동
//
// [v3.2 변경 — SetAttackMove 연동]
//
//   [추가 기능]
//     ComboRoutine 시작 → _mover.SetAttackMove(true, attackDir, speed)
//     콤보 연결 시      → _mover.SetAttackMove(true, newDir, speed) 갱신
//     ComboRoutine 종료 → _mover.SetAttackMove(false, zero, 0)
//     CancelAttack 시   → _mover.SetAttackMove(false, zero, 0)
//
//   [공격 이동 방향 규칙]
//     1콤보: FacingDirection (마우스 방향) 기준
//     2/3콤보: HandleReturnStart 에서 저장한 _nextComboDir
//     WASD 입력 있으면 → PlayerMoveController 가 WASD 방향 우선 적용
//     WASD 입력 없으면 → attackDir (공격/마우스 방향) 으로 전진
//
//   [속도]
//     PlayerAttackDataSO.AttackMoveSpeed 사용
//
// [v3.1 유지 사항]
//   OnAttackEnded 단일 발행 (ComboRoutine 끝 1곳)
//   중복 ComboRoutine 방지 (ExecuteCombo 진입 시 StopCoroutine)
//   콤보 윈도우 구간에서만 다음 콤보 입력 허용
//   SetMoveLocked 호출 없음
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
    /// 플레이어 공격 컨트롤러. (v3.2)
    ///
    /// ────────────────────────────────────────────────────
    /// [공격 이동 흐름]
    ///   공격 시작 → SetAttackMove(true, attackDir, AttackMoveSpeed)
    ///   공격 중   → MoveController 가 WASD or attackDir 방향 전진
    ///   콤보 연결 → SetAttackMove(true, newDir) 방향 갱신
    ///   공격 종료 → SetAttackMove(false)
    ///
    /// [콤보 진입 조건]
    ///   !_isAttacking && !_comboWindowOpen → 1콤보 시작
    ///   !_isAttacking &&  _comboWindowOpen → 다음 콤보 순환
    ///   _isAttacking  && !_comboWindowOpen → 완전 무시
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

        /// <summary>공격 수치 SO. AttackMoveSpeed / ComboResetTime 등 포함.</summary>
        [Tooltip("공격 수치 SO. PlayerAttackDataSO 연결 필수.")]
        [SerializeField] private PlayerAttackDataSO _data;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 이동 컨트롤러.
        /// FacingDirection 읽기 + SetAttackMove 호출 전용.
        /// SetMoveLocked 호출 금지.
        /// </summary>
        private PlayerMoveController _moveController;

        /// <summary>무기 스윙 DOTween 연출 컨트롤러.</summary>
        private PlayerWeaponSwingController _swingController;

        /// <summary>히트박스 활성/비활성 전담.</summary>
        private PlayerAttackHitboxManager _hitboxManager;

        // ══════════════════════════════════════════════════════
        // 공격 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 공격 진행 중 여부.
        /// ComboRoutine 시작 → true / ComboRoutine 종료 or CancelAttack → false.
        /// HandleReturnStart 에서 변경 안 함 (v3.1 원칙 유지).
        /// </summary>
        private bool _isAttacking;

        /// <summary>현재 콤보 단계. 0=1콤보, 1=2콤보, 2=3콤보.</summary>
        private int _currentCombo;

        /// <summary>
        /// 콤보 윈도우 열림 여부.
        /// OnReturnStart 수신 → true.
        /// ExecuteCombo 진입 즉시 → false.
        /// </summary>
        private bool _comboWindowOpen;

        /// <summary>현재 공격 방향. 콤보 시작 시점 확정.</summary>
        private Vector2 _currentAttackDir;

        /// <summary>
        /// 다음 콤보 방향 스냅샷.
        /// HandleReturnStart 에서 그 시점 FacingDirection 저장.
        /// ComboRoutine 시작 시 적용 후 zero 초기화.
        /// </summary>
        private Vector2 _nextComboDir;

        // ══════════════════════════════════════════════════════
        // 강공격 홀드 (API 유지)
        // ══════════════════════════════════════════════════════

        /// <summary>마우스 좌클릭 누른 시각.</summary>
        private float _pressTime;

        /// <summary>마우스 좌클릭 홀드 여부.</summary>
        private bool _isHeld;

        // ══════════════════════════════════════════════════════
        // 코루틴 참조
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 ComboRoutine 참조.
        /// ExecuteCombo 진입 시 기존 코루틴 StopCoroutine 후 재시작.
        /// </summary>
        private Coroutine _attackCoroutine;

        /// <summary>콤보 리셋 타이머 코루틴 참조.</summary>
        private Coroutine _comboResetCoroutine;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>적 적중 시 발행. (적중 위치, 봉인도)</summary>
        public event Action<Vector2, float> OnHitTarget;

        /// <summary>공격 시작 시 1회 발행. PlayerController 모니터링용.</summary>
        public event Action OnAttackStarted;

        /// <summary>
        /// 공격 완전 종료 시 1회 발행.
        /// ComboRoutine 끝 1곳 + CancelAttack 에서만 발행.
        /// </summary>
        public event Action OnAttackEnded;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 공격 중 여부.</summary>
        public bool IsAttacking => _isAttacking;

        /// <summary>현재 콤보 단계 (0부터).</summary>
        public int CurrentCombo => _currentCombo;

        /// <summary>콤보 윈도우 열림 여부.</summary>
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

            PlayerInputHandler.Instance.OnAttack -= HandlePress;
            PlayerInputHandler.Instance.OnAttack += HandlePress;
            PlayerInputHandler.Instance.OnAttackReleased -= HandleRelease;
            PlayerInputHandler.Instance.OnAttackReleased += HandleRelease;

            if (_hitboxManager != null)
            {
                _hitboxManager.OnHit -= HandleHit;
                _hitboxManager.OnHit += HandleHit;
            }

            if (_moveController != null)
            {
                _moveController.OnDashStarted -= HandleDashStarted;
                _moveController.OnDashStarted += HandleDashStarted;
            }

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

        /// <summary>마우스 좌클릭 pressed. 홀드 타이머 + 맥동 시작.</summary>
        private void HandlePress()
        {
            _pressTime = Time.time;
            _isHeld = true;

            if (PlayerInputHandler.Instance != null &&
                !PlayerInputHandler.Instance.IsActionBlocked)
                _swingController?.StartChargePulse();
        }

        /// <summary>
        /// 마우스 좌클릭 released.
        ///
        /// [진입 조건]
        ///   A. 차단 → 무시
        ///   B. _isAttacking=true, _comboWindowOpen=false → 무시 (타격/백스윙 구간)
        ///   C. _isAttacking=false → 1콤보 시작
        ///   D. _isAttacking=true, _comboWindowOpen=true → 다음 콤보
        /// </summary>
        private void HandleRelease()
        {
            if (!_isHeld) return;

            _isHeld = false;
            _swingController?.StopChargePulse();

            if (PlayerInputHandler.Instance != null &&
                PlayerInputHandler.Instance.IsActionBlocked)
                return;

            if (_isAttacking && !_comboWindowOpen)
                return;

            ExecuteCombo();
        }

        /// <summary>대시 시작 → 공격 캔슬. 회피 최우선.</summary>
        private void HandleDashStarted()
        {
            if (!_isAttacking) return;
            CancelAttack();
            Debug.Log("[PlayerAttackController] 대시 → 공격 캔슬");
        }

        /// <summary>
        /// OnReturnStart 수신 = 복귀 모션 시작.
        /// 콤보 윈도우 열기 + 다음 콤보 방향 스냅샷 저장.
        /// _isAttacking 건드리지 않음.
        /// </summary>
        private void HandleReturnStart()
        {
            _comboWindowOpen = true;

            // 공격 이동 종료 (v3.2)
            _moveController?.SetAttackMove(false, Vector2.zero, 0f);

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
        /// 콤보 단계 결정 + 기존 코루틴 중단 + 새 ComboRoutine 시작.
        /// </summary>
        private void ExecuteCombo()
        {
            StopResetTimer();

            if (_comboWindowOpen)
            {
                _currentCombo = (_currentCombo + 1) % _data.MaxComboCount;
                _comboWindowOpen = false;
            }
            else
            {
                _currentCombo = 0;
                _comboWindowOpen = false;
            }

            if (_attackCoroutine != null)
            {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
            }

            _attackCoroutine = StartCoroutine(ComboRoutine());
        }

        // ══════════════════════════════════════════════════════
        // 공격 코루틴
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기본 공격 코루틴. 콤보 1회 전체 흐름.
        ///
        /// [v3.2 공격 이동]
        ///   시작: SetAttackMove(true, attackDir, AttackMoveSpeed)
        ///         → WASD 있으면 WASD방향 / 없으면 attackDir 방향으로 전진
        ///   종료: SetAttackMove(false) → 일반 WASD 이동 복귀
        ///
        /// [콤보 연결 시 공격 이동 방향 갱신]
        ///   ExecuteCombo → ComboRoutine 시작 시
        ///   → SetAttackMove(true, newDir) 새 방향으로 즉시 갱신
        /// </summary>
        private IEnumerator ComboRoutine()
        {
            // ── 상태 초기화 ──────────────────────
            _isAttacking = true;
            _comboWindowOpen = false;

            OnAttackStarted?.Invoke();

            // ── 공격 방향 결정 ──────────────────────
            if (_currentCombo == 0 || _nextComboDir.sqrMagnitude < 0.01f)
                _currentAttackDir = GetAttackDir();
            else
                _currentAttackDir = _nextComboDir.normalized;

            _nextComboDir = Vector2.zero;

            // ── 공격 이동 시작 (v3.2) ──────────────────────
            _moveController?.SetAttackMove(true, _currentAttackDir, _data.AttackMoveSpeed);

            // ── 스윙 실행 ──────────────────────
            var comboIndex = (PlayerWeaponSwingController.ComboIndex)
                Mathf.Clamp(_currentCombo, 0, 2);
            float sealAmount = GetComboSealAmount();

            _swingController.PlaySwing(comboIndex, _currentAttackDir, sealAmount);

            yield return null;

            // ── 스윙 완료 대기 ──────────────────────
            float maxWait = (_data.BackswingDuration + _data.AttackDuration + _data.ReturnDuration) * 2f;
            float elapsed = 0f;

            while (_swingController.IsSwinging && elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // ── 공격 완전 종료 ──────────────────────
            _comboWindowOpen = false;
            _isAttacking = false;

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
        /// SetAttackMove(false) 포함 — 공격 이동 즉시 해제.
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
            _swingController?.StopChargePulse();

            // 공격 이동 해제 (v3.2)
            _moveController?.SetAttackMove(false, Vector2.zero, 0f);

            _isAttacking = false;
            _comboWindowOpen = false;
            _currentCombo = 0;
            _nextComboDir = Vector2.zero;
            _isHeld = false;

            OnAttackEnded?.Invoke();

            Debug.Log("[PlayerAttackController] 공격 캔슬");
        }

        // ══════════════════════════════════════════════════════
        // 히트박스 적중
        // ══════════════════════════════════════════════════════

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
            Debug.Log("[PlayerAttackController] 콤보 리셋 → 1콤보");
        }

        // ══════════════════════════════════════════════════════
        // 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 마우스 방향 벡터 반환. zero면 Vector2.right.</summary>
        private Vector2 GetAttackDir()
        {
            if (_moveController == null) return Vector2.right;
            Vector2 dir = _moveController.FacingDirection;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.right;
        }

        /// <summary>현재 콤보 단계의 봉인도 반환.</summary>
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
            if (_isAttacking)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawRay(transform.position, _currentAttackDir * 1.5f);
            }

            if (_comboWindowOpen)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, 0.4f);
            }
        }
    }
}