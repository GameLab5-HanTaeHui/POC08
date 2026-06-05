// ============================================================
// PlayerController.cs  v1.1
// 플레이어 행동 총괄 관리자 — 공격/이동 독립 확정판
//
// [v1.1 — 버그 수정 핵심 변경]
//
//   [수정 1] PlayerState.Attack 제거
//     기존: OnAttackStarted → Attack 상태 진입
//           → SetMoveLocked(true) + AttackMoveRoutine 실행
//           → 이동 하면서 공격 불가 (velocity 충돌)
//           → OnAttackEnded 이중발행 시 상태 꼬임
//     변경: Attack 상태 완전 제거
//           공격은 PlayerAttackController 가 독립 처리
//           PlayerController 는 이동 상태만 관리 (Idle/Move/Dash/Seal)
//
//   [수정 2] AttackMoveRoutine 제거
//     기존: Attack 상태 진입 시 AttackMoveRoutine 코루틴으로
//           Rigidbody velocity 를 공격 방향으로 강제 설정
//           → PlayerMoveController 의 WASD velocity 와 충돌
//           → 이동하면서 공격 불가 버그
//     변경: AttackMoveRoutine 완전 제거
//           이동 중 공격 = WASD 이동 + 공격 동시 처리 (독립)
//
//   [수정 3] SetMoveLocked Attack 연동 제거
//     기존: Attack 진입 → SetMoveLocked(true)
//           Attack 종료 → SetMoveLocked(false)
//     변경: SetMoveLocked 는 Seal 상태에서만 사용
//           대시는 PlayerMoveController 내부에서 처리
//
//   [OnAttackStarted / OnAttackEnded 구독 유지]
//     PlayerController 는 여전히 구독하되
//     받아도 아무 상태 전환 없이 Debug.Log 만 출력 (모니터링용)
//     → 향후 공격 중 HUD 표시 등 확장점으로 활용
//
// [v1.1 최종 상태 목록]
//   Idle  → 정지
//   Move  → WASD 이동
//   Dash  → 대시 (PlayerMoveController 처리)
//   Seal  → 봉인 집행 (이동/공격 전부 차단)
//
// [우선순위]
//   Seal > Dash > Move > Idle
//   (공격은 상태와 무관하게 항상 허용 — PlayerAttackController 독립 처리)
//
// [namespace]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 플레이어 행동 총괄 관리자. (v1.1)
    ///
    /// ────────────────────────────────────────────────────
    /// [상태 목록]
    ///   Idle  → 정지
    ///   Move  → WASD 이동
    ///   Dash  → 대시
    ///   Seal  → 봉인 집행 (모든 행동 차단)
    ///
    /// [v1.1 변경]
    ///   Attack 상태 완전 제거.
    ///   AttackMoveRoutine 완전 제거.
    ///   공격은 PlayerAttackController 가 이동과 독립 처리.
    ///   SetMoveLocked 는 Seal 상태에서만 사용.
    ///
    /// [우선순위]
    ///   Seal > Dash > Move > Idle
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

        /// <summary>
        /// 플레이어 행동 상태.
        ///
        /// [v1.1 변경]
        ///   Attack 제거 — 공격은 상태와 무관하게 PlayerAttackController 독립 처리.
        /// </summary>
        public enum PlayerState
        {
            /// <summary>정지 상태. WASD 입력 없음.</summary>
            Idle,

            /// <summary>WASD 이동 상태.</summary>
            Move,

            /// <summary>대시 상태. PlayerMoveController 가 물리 처리.</summary>
            Dash,

            /// <summary>
            /// 봉인 집행 상태.
            /// 이동 + 공격 전부 차단.
            /// BossWardenSealExecutor 에서 진입/종료.
            /// </summary>
            Seal,
        }

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 이동 전담 컴포넌트.
        /// SetMoveLocked / ForceStopDash 호출.
        /// </summary>
        private PlayerMoveController _mover;

        /// <summary>
        /// 공격/콤보 전담 컴포넌트.
        /// CancelAttack 호출 (봉인 집행 진입 시).
        /// OnAttackStarted / OnAttackEnded 모니터링 구독.
        /// </summary>
        private PlayerAttackController _attacker;

        /// <summary>입력 핸들러 싱글턴.</summary>
        private PlayerInputHandler _input;

        // ══════════════════════════════════════════════════════
        // 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 플레이어 상태.</summary>
        private PlayerState _state = PlayerState.Idle;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 상태 읽기.
        /// HUD, 애니메이터 등 외부 참조용.
        /// </summary>
        public PlayerState State => _state;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _mover = GetComponent<PlayerMoveController>();
            _attacker = GetComponent<PlayerAttackController>();
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

            // ── 대시 이벤트 구독 ──────────────────────
            _mover.OnDashStarted -= HandleDashStarted;
            _mover.OnDashStarted += HandleDashStarted;
            _mover.OnDashEnded -= HandleDashEnded;
            _mover.OnDashEnded += HandleDashEnded;

            // ── 공격 이벤트 구독 (모니터링용 — 상태 전환 없음) ──────────────────────
            // v1.1: 공격은 상태와 무관하게 독립 처리
            // 구독은 유지하여 향후 HUD 공격 표시 등 확장 가능
            _attacker.OnAttackStarted -= HandleAttackStarted;
            _attacker.OnAttackStarted += HandleAttackStarted;
            _attacker.OnAttackEnded -= HandleAttackEnded;
            _attacker.OnAttackEnded += HandleAttackEnded;
        }

        private void OnDestroy()
        {
            if (_mover != null)
            {
                _mover.OnDashStarted -= HandleDashStarted;
                _mover.OnDashEnded -= HandleDashEnded;
            }

            if (_attacker != null)
            {
                _attacker.OnAttackStarted -= HandleAttackStarted;
                _attacker.OnAttackEnded -= HandleAttackEnded;
            }
        }

        private void Update()
        {
            // ── Idle / Move 상태 갱신 ──────────────────────
            // Dash / Seal 은 이벤트로 진입/종료 처리
            // Idle / Move 는 WASD 입력 여부로 매 프레임 갱신
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
        ///
        /// [우선순위]
        ///   Seal > Dash > Move > Idle
        ///   Seal 중에는 Idle 복귀만 허용 (ExitSeal 에서 호출).
        /// </summary>
        private void SetState(PlayerState newState)
        {
            if (_state == newState) return;

            // Seal 중에는 Seal 해제(Idle)만 허용
            if (_state == PlayerState.Seal && newState != PlayerState.Idle)
                return;

            PlayerState prev = _state;
            _state = newState;

            OnStateExit(prev);
            OnStateEnter(newState);

            Debug.Log($"[PlayerController] 상태 전환: {prev} → {newState}");
        }

        /// <summary>
        /// 상태 진입 처리.
        ///
        /// [v1.1 변경]
        ///   Attack 케이스 제거.
        ///   Seal 진입 시 공격 캔슬 + 이동 잠금.
        ///   Idle/Move 진입 시 이동 잠금 해제.
        /// </summary>
        private void OnStateEnter(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.Seal:
                    // 봉인 집행: 이동 + 공격 전부 차단
                    _mover.SetMoveLocked(true);
                    _attacker.CancelAttack();
                    break;

                case PlayerState.Idle:
                case PlayerState.Move:
                    // WASD 이동 복귀
                    _mover.SetMoveLocked(false);
                    break;

                    // Dash: PlayerMoveController 가 내부에서 처리
                    // 이 컴포넌트는 OnDashStarted/Ended 이벤트로만 상태 추적
            }
        }

        /// <summary>
        /// 상태 종료 처리.
        ///
        /// [v1.1 변경]
        ///   Attack 케이스 제거.
        ///   Seal 종료 시 이동 잠금 해제.
        /// </summary>
        private void OnStateExit(PlayerState state)
        {
            switch (state)
            {
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
        /// 대시 시작 이벤트 수신.
        /// Seal 중에는 무시. 그 외 → Dash 상태 진입.
        /// </summary>
        private void HandleDashStarted()
        {
            if (_state == PlayerState.Seal) return;
            SetState(PlayerState.Dash);
        }

        /// <summary>
        /// 대시 종료 이벤트 수신.
        /// WASD 입력 여부에 따라 Move / Idle 복귀.
        /// </summary>
        private void HandleDashEnded()
        {
            if (_state != PlayerState.Dash) return;

            SetState(_input.MoveInput.sqrMagnitude > 0.01f
                ? PlayerState.Move
                : PlayerState.Idle);
        }

        /// <summary>
        /// 공격 시작 이벤트 수신. (v1.1 — 모니터링만)
        /// 상태 전환 없음.
        /// Attack 상태가 없으므로 상태 변경 필요 없음.
        /// 향후 HUD 공격 이펙트 트리거 등에서 활용.
        /// </summary>
        private void HandleAttackStarted()
        {
            // v1.1: 공격은 이동과 독립 — 상태 전환 없음
            Debug.Log("[PlayerController] 공격 시작 (모니터링)");
        }

        /// <summary>
        /// 공격 종료 이벤트 수신. (v1.1 — 모니터링만)
        /// 상태 전환 없음.
        /// </summary>
        private void HandleAttackEnded()
        {
            // v1.1: 공격 종료도 상태 전환 없음
            Debug.Log("[PlayerController] 공격 종료 (모니터링)");
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
        // Gizmos — 에디터 디버그 표시
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            // 상태별 색상으로 플레이어 범위 표시
            Color c = _state switch
            {
                PlayerState.Dash => Color.cyan,
                PlayerState.Seal => Color.magenta,
                PlayerState.Move => Color.green,
                _ => Color.white,   // Idle
            };

            Gizmos.color = c;
            Gizmos.DrawWireSphere(transform.position, 0.3f);

            // 공격 중이면 빨간 구체 추가 표시 (AttackController 독립)
            if (_attacker != null && _attacker.IsAttacking)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
                Gizmos.DrawWireSphere(transform.position, 0.5f);
            }
        }
    }
}