// ============================================================
// PlayerController.cs  v1.0
// 플레이어 행동 총괄 관리자
//
// [역할]
//   PlayerInputHandler       → 입력 수신
//   PlayerMoveController     → 이동 처리
//   PlayerAttackController   → 공격/콤보 처리
//   PlayerWeaponSwingController → DOTween 연출
//   위 4개 컴포넌트를 총괄하여 상태 관리 + 우선순위 결정
//
// [상태]
//   Idle    → 정지
//   Move    → WASD 이동
//   Attack  → 공격 이동 + 스윙
//   Dash    → 대시
//   Seal    → 봉인 집행
//
// [우선순위]
//   Seal > Dash > Attack > Move > Idle
//
// [이동 주도권 전환]
//   Idle/Move  → PlayerMoveController 가 WASD 처리
//   Attack     → PlayerController 가 공격 이동 처리
//                SetMoveLocked(true) → 공격 이동(AttackMoveRoutine)
//                WASD 있으면 → WASD 방향으로 AttackMoveSpeed 전진
//                WASD 없으면 → 마우스(공격) 방향으로 AttackMoveSpeed 전진
//   Attack 종료 → SetMoveLocked(false) → WASD 이동 복귀
//   Dash       → PlayerMoveController 가 대시 처리
//   Seal       → SetMoveLocked(true) + BlockAction → 전부 차단
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System.Collections;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 플레이어 행동 총괄 관리자. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [행동 우선순위]
    ///   Seal > Dash > Attack > Move > Idle
    ///
    /// [상태 전환]
    ///   Idle/Move + 좌클릭  → Attack
    ///   Attack    + Space   → Dash (공격 캔슬)
    ///   Attack    + 종료    → Move/Idle 복귀
    ///   F/우클릭  + 홀드    → Seal
    ///   Seal      + 해제    → 이전 상태 복귀
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(PlayerMoveController))]
    [RequireComponent(typeof(PlayerAttackController))]
    [RequireComponent(typeof(PlayerWeaponSwingController))]
    public class PlayerController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // 상태 정의
        // ══════════════════════════════════════════════════════

        /// <summary>플레이어 행동 상태.</summary>
        public enum PlayerState
        {
            /// <summary>정지 상태.</summary>
            Idle,
            /// <summary>WASD 이동 상태.</summary>
            Move,
            /// <summary>공격 이동 + 스윙 상태.</summary>
            Attack,
            /// <summary>대시 상태.</summary>
            Dash,
            /// <summary>봉인 집행 상태.</summary>
            Seal,
        }

        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 공격 이동 ──────────────────────")]

        /// <summary>
        /// 공격 중 이동 속도.
        /// 공격 상태 동안 Rigidbody velocity 에 적용.
        /// </summary>
        [Tooltip("공격 중 이동 속도. 권장: 2~4.")]
        [SerializeField] private float _attackMoveSpeed = 3f;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>이동 전담 컴포넌트.</summary>
        private PlayerMoveController _mover;

        /// <summary>공격/콤보 전담 컴포넌트.</summary>
        private PlayerAttackController _attacker;

        /// <summary>입력 핸들러.</summary>
        private PlayerInputHandler _input;

        /// <summary>Rigidbody2D. 공격 이동 velocity 직접 제어.</summary>
        private Rigidbody2D _rigid2D;

        // ══════════════════════════════════════════════════════
        // 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 플레이어 상태.</summary>
        private PlayerState _state = PlayerState.Idle;

        /// <summary>공격 이동 코루틴 참조.</summary>
        private Coroutine _attackMoveCoroutine;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 상태.</summary>
        public PlayerState State => _state;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _mover = GetComponent<PlayerMoveController>();
            _attacker = GetComponent<PlayerAttackController>();
            _rigid2D = GetComponent<Rigidbody2D>();
        }

        private void Start()
        {
            _input = PlayerInputHandler.Instance;

            if (_input == null)
            {
                Debug.LogError("[PlayerController] PlayerInputHandler 없음.");
                enabled = false;
                return;
            }

            // 공격 시작/종료 구독
            _attacker.OnAttackStarted -= HandleAttackStarted;
            _attacker.OnAttackStarted += HandleAttackStarted;
            _attacker.OnAttackEnded -= HandleAttackEnded;
            _attacker.OnAttackEnded += HandleAttackEnded;

            // 대시 시작/종료 구독
            _mover.OnDashStarted -= HandleDashStarted;
            _mover.OnDashStarted += HandleDashStarted;
            _mover.OnDashEnded -= HandleDashEnded;
            _mover.OnDashEnded += HandleDashEnded;
        }

        private void OnDestroy()
        {
            if (_attacker != null)
            {
                _attacker.OnAttackStarted -= HandleAttackStarted;
                _attacker.OnAttackEnded -= HandleAttackEnded;
            }

            if (_mover != null)
            {
                _mover.OnDashStarted -= HandleDashStarted;
                _mover.OnDashEnded -= HandleDashEnded;
            }
        }

        private void Update()
        {
            // Idle/Move 상태 갱신
            if (_state == PlayerState.Idle || _state == PlayerState.Move)
            {
                if (_input.MoveInput.sqrMagnitude > 0.01f)
                    SetState(PlayerState.Move);
                else
                    SetState(PlayerState.Idle);
            }
        }

        // ══════════════════════════════════════════════════════
        // 상태 전환
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 상태 전환.
        /// 우선순위에 따라 전환 허용 여부 결정.
        ///
        /// [우선순위]
        ///   Seal > Dash > Attack > Move > Idle
        /// </summary>
        private void SetState(PlayerState newState)
        {
            if (_state == newState) return;

            // Seal 중에는 Seal 해제만 허용
            if (_state == PlayerState.Seal && newState != PlayerState.Idle)
                return;

            PlayerState prev = _state;
            _state = newState;

            OnStateExit(prev);
            OnStateEnter(newState);

            Debug.Log($"[PlayerController] 상태 전환: {prev} → {newState}");
        }

        /// <summary>상태 진입 처리.</summary>
        private void OnStateEnter(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.Attack:
                    // 이동 주도권 → 공격으로 전환
                    _mover.SetMoveLocked(true);
                    StartAttackMove();
                    break;

                case PlayerState.Seal:
                    // 모든 이동/공격 차단
                    _mover.SetMoveLocked(true);
                    _attacker.CancelAttack();
                    break;

                case PlayerState.Idle:
                case PlayerState.Move:
                    // WASD 이동 복귀
                    _mover.SetMoveLocked(false);
                    break;
            }
        }

        /// <summary>상태 종료 처리.</summary>
        private void OnStateExit(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.Attack:
                    // 공격 이동 종료 → WASD 복귀
                    StopAttackMove();
                    _mover.SetMoveLocked(false);
                    break;

                case PlayerState.Seal:
                    // 봉인 해제 → 이동 복귀
                    _mover.SetMoveLocked(false);
                    break;
            }
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackController 공격 시작 수신.
        /// → Attack 상태 진입.
        /// </summary>
        private void HandleAttackStarted()
        {
            // Seal 중에는 공격 불가
            if (_state == PlayerState.Seal) return;

            SetState(PlayerState.Attack);
        }

        /// <summary>
        /// PlayerAttackController.OnAttackEnded 수신.
        /// 공격 완전 종료 → Idle/Move 복귀.
        /// </summary>
        private void HandleAttackEnded()
        {
            if (_state != PlayerState.Attack) return;

            SetState(_input != null && _input.MoveInput.sqrMagnitude > 0.01f
                ? PlayerState.Move
                : PlayerState.Idle);
        }
        private void HandleDashStarted()
        {
            if (_state == PlayerState.Seal) return;

            // 공격 중이면 캔슬
            if (_state == PlayerState.Attack)
                _attacker.CancelAttack();

            SetState(PlayerState.Dash);
        }

        /// <summary>
        /// PlayerMoveController 대시 종료 수신.
        /// → Idle/Move 복귀.
        /// </summary>
        private void HandleDashEnded()
        {
            if (_state != PlayerState.Dash) return;

            // WASD 입력 있으면 Move / 없으면 Idle
            SetState(_input.MoveInput.sqrMagnitude > 0.01f
                ? PlayerState.Move
                : PlayerState.Idle);
        }

        // ══════════════════════════════════════════════════════
        // 공격 이동
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 공격 이동 시작.
        /// Attack 상태 진입 시 호출.
        /// </summary>
        private void StartAttackMove()
        {
            StopAttackMove();
            _attackMoveCoroutine = StartCoroutine(AttackMoveRoutine());
        }

        /// <summary>
        /// 공격 이동 중단.
        /// Attack 상태 종료 시 호출.
        /// </summary>
        private void StopAttackMove()
        {
            if (_attackMoveCoroutine == null) return;
            StopCoroutine(_attackMoveCoroutine);
            _attackMoveCoroutine = null;
        }

        /// <summary>
        /// 공격 이동 코루틴.
        /// Attack 상태인 동안 계속 실행.
        /// OnAttackEnded → HandleAttackEnded → SetState 로 종료.
        /// </summary>
        private IEnumerator AttackMoveRoutine()
        {
            // _isAttacking 이 true 인 동안 계속 공격 이동 적용
            // OnAttackEnded 발행 시 HandleAttackEnded → SetState(Move/Idle)
            // → _state != Attack → 루프 종료
            while (_state == PlayerState.Attack)
            {
                Vector2 wasdInput = _input != null
                    ? _input.MoveInput
                    : Vector2.zero;

                Vector2 moveDir = wasdInput.sqrMagnitude > 0.01f
                    ? wasdInput.normalized
                    : _mover.FacingDirection;

                _rigid2D.linearVelocity = moveDir * _attackMoveSpeed;

                yield return new WaitForFixedUpdate();
            }

            _attackMoveCoroutine = null;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 상태 진입.
        /// BossWardenSealExecutor 에서 호출.
        /// </summary>
        public void EnterSeal()
        {
            SetState(PlayerState.Seal);
        }

        /// <summary>
        /// 봉인 집행 상태 종료.
        /// BossWardenSealExecutor 에서 호출.
        /// </summary>
        public void ExitSeal()
        {
            if (_state != PlayerState.Seal) return;

            SetState(_input != null && _input.MoveInput.sqrMagnitude > 0.01f
                ? PlayerState.Move
                : PlayerState.Idle);
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            Color c = _state switch
            {
                PlayerState.Attack => Color.red,
                PlayerState.Dash => Color.cyan,
                PlayerState.Seal => Color.magenta,
                PlayerState.Move => Color.green,
                _ => Color.white,
            };

            Gizmos.color = c;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
    }
}