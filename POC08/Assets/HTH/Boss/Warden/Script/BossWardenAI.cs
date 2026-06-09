// ============================================================
// BossWardenAI.cs  v4.0
// Boss_Warden 탑뷰 AI — Step 6 AttackManager 분리
//
// [v3.0 변경 — Groggy 브리지 메서드 제거]
//   제거:
//     OnGroggyEnter() public 브리지 메서드
//     OnGroggyExit()  public 브리지 메서드
//
//   변경:
//     OnDilPhaseEnter() → AI 정지 + 패턴 중단 전담
//                         (기존 OnGroggyEnter 역할 흡수)
//     OnDilPhaseExit()  → AI 재개 + Idle 복귀 전담
//                         (기존 OnGroggyExit 역할 흡수)
//
// [브리지 메서드 — public (BossWardenCore v4.0 에서 직접 호출)]
//   OnDilPhaseEnter()  : AI 정지
//   OnDilPhaseExit()   : AI 재개 + Idle 복귀
//   OnPhaseChanged(int): 2페이즈 속도/패턴 강화
//   OnDead()           : AI 완전 정지
//
// [v3.1 변경 — BossAttackManager 분리]
//   변경:
//     패턴 선택/실행 책임을 BossAttackManager 로 이동
//     BossWardenAI 는 이동/상태 전환/공격 요청만 담당
//   Step 13 변경:
//     BossAttackManager 미연결 fallback 제거
//     AI 내부 패턴 선택/실행 코드 제거
//     공격은 BossAttackManager.RequestAttack() 로만 요청
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 탑뷰 AI. (v3.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [이 스크립트가 하는 것]
    ///   - 상태 관리 (Idle / Chase / Warning / Active / Recovery)
    ///   - 탑뷰 8방향 플레이어 추적 이동
    ///   - BossAttackManager 에 공격 요청
    ///   - 이동/패턴 정지 (_isStopped)
    ///   - Recovery 취약 구간 팔 전달
    ///   - 2페이즈 패턴 강화 적용
    ///
    /// [이 스크립트가 하지 않는 것]
    ///   - 상태 판단 → SealStateManager
    ///   - 봉인 집행 처리 → SealExecutionRunner
    ///   - 색상 피드백 → BossWardenFeedback
    ///
    /// [BossWardenCore 브리지 메서드 — public]
    ///   OnDilPhaseEnter() / OnDilPhaseExit()
    ///   OnPhaseChanged(int) / OnDead()
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class BossWardenAI : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // 상태 열거형
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Warden AI 상태.
        /// DilPhase 는 SealStateManager 가 관리.
        /// AI 는 _isStopped 플래그만으로 이 구간을 처리.
        /// </summary>
        public enum WardenAIState
        {
            /// <summary>패턴 대기. 플레이어 방향 유지 + 패턴 선택 시도.</summary>
            Idle,
            /// <summary>플레이어 추적 이동. _patternRange 이내 진입 시 Idle 전환.</summary>
            Chase,
            /// <summary>패턴 예고 중. 공격 예고 범위 표시 구간.</summary>
            Warning,
            /// <summary>패턴 시전 중. 실제 히트박스 판정 구간.</summary>
            Active,
            /// <summary>패턴 후딜레이. 취약 구간 — recoveryVulnMultiplier 배율 적용.</summary>
            Recovery,
        }

        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO 연결 (필수) ──────────────────────")]

        /// <summary>
        /// Warden 수치.
        /// BossWardenCore.InjectData() 에서 Initialize() 주입.
        /// </summary>
        [Tooltip("BossWardenDataSO. 필수 연결.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── Attack Manager (권장) ──────────────────────")]

        /// <summary>
        /// Step 6에서 추가된 공격 패턴 선택/실행 관리자.
        /// Step 13 이후 필수 연결이다.
        /// </summary>
        [Tooltip("BossAttackManager. 필수 연결.")]
        [SerializeField] private BossAttackManager _attackManager;


        [Header("── 팔 부위 연결 (필수) ──────────────────────")]

        /// <summary>왼팔. Recovery 취약 구간 SetRecoveryVuln() 전달 대상.</summary>
        [Tooltip("왼팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenPart _armL;

        /// <summary>오른팔.</summary>
        [Tooltip("오른팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenPart _armR;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>Chase 이동 시 linearVelocity 직접 제어.</summary>
        private Rigidbody2D _rigid2D;

        /// <summary>OnFacingChanged 발행 시 flipX 처리 대상.</summary>
        private SpriteRenderer _spriteRenderer;

        /// <summary>플레이어 Transform. Start 1회 캐싱.</summary>
        private Transform _playerTransform;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 AI 상태.</summary>
        private WardenAIState _currentState = WardenAIState.Idle;

        /// <summary>현재 실행 중인 패턴.</summary>
        private BossPatternBase _currentPattern;

        /// <summary>
        /// 이동/패턴 정지 플래그.
        /// true: DilPhase / Dead 중.
        /// FixedUpdate 에서 linearVelocity = 0 강제 적용.
        /// </summary>
        private bool _isStopped;

        /// <summary>현재 이동 속도. 2페이즈 전환 시 갱신.</summary>
        private float _currentMoveSpeed;

        /// <summary>방향 전환 쿨타임 잔여 시간.</summary>
        private float _flipCooldownTimer;

        /// <summary>현재 플레이어 방향 벡터 (정규화).</summary>
        private Vector2 _facingDir = Vector2.right;


        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 상태 전환 시 발행.
        /// BossWardenFeedback 이 구독 → 상태별 DOTween 색상 연출.
        /// 파라미터: (새 상태, 현재 패턴 — null 가능)
        /// </summary>
        public event Action<WardenAIState, BossPatternBase> OnStateChanged;

        /// <summary>
        /// 플레이어 방향 변화 시 발행.
        /// SpriteRenderer flipX 처리용.
        /// </summary>
        public event Action<Vector2> OnFacingChanged;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 AI 상태.</summary>
        public WardenAIState CurrentState => _currentState;

        /// <summary>이동/패턴 정지 여부.</summary>
        public bool IsStopped => _isStopped;

        /// <summary>현재 플레이어 방향 벡터. 패턴 Warning 방향 결정 시 참조.</summary>
        public Vector2 FacingDir => _facingDir;

        /// <summary>플레이어 Transform. 패턴 스크립트에서 참조.</summary>
        public Transform PlayerTransform => _playerTransform;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _rigid2D = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_attackManager == null) _attackManager = GetComponentInChildren<BossAttackManager>(true);
            // v3.0: _core 참조 없음 — BossWardenCore 브리지 방식 유지
            _currentMoveSpeed = _data != null ? _data.moveSpeed : 3.5f;
        }

        private void Start()
        {
            // 플레이어 탐색 — 1회 캐싱
            var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (players.Length > 0)
                _playerTransform = players[0].transform;
            else
                Debug.LogWarning("[BossWardenAI] PlayerMoveController 탐색 실패.");

            if (_attackManager == null)
                Debug.LogError("[BossWardenAI] BossAttackManager 미연결 — 공격 실행 불가.");
        }

        private void OnDestroy()
        {
        }

        private void Update()
        {
            if (_isStopped) return;
            UpdateTimers();
            UpdateStateLogic();
        }

        private void FixedUpdate()
        {
            if (_isStopped)
            {
                _rigid2D.linearVelocity = Vector2.zero;
                return;
            }
            UpdateMovement();
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenDataSO 주입.
        /// BossWardenCore.InjectData() 에서 호출.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
            _currentMoveSpeed = data.moveSpeed;

            // Step 13:
            // 패턴 초기화/선택/실행은 BossAttackManager만 담당한다.
            if (_attackManager == null)
            {
                Debug.LogError("[BossWardenAI] BossAttackManager 미연결 — Initialize 중단.");
                enabled = false;
                return;
            }

            _attackManager.Initialize(data, this, _armL, _armR);

            Debug.Log("[BossWardenAI] 초기화 완료");
        }


        // ══════════════════════════════════════════════════════
        // BossWardenCore 브리지 메서드 — public
        // BossWardenCore v4.0 이 SealStateManager 이벤트 수신 후 직접 호출
        // ══════════════════════════════════════════════════════

        // 수정 — BossAttackManager를 통해 실행 중인 패턴을 중단한다.
        public void OnDilPhaseEnter()
        {
            _isStopped = true;
            SetArmsRecoveryVuln(false);
            _attackManager?.InterruptCurrentPattern();

            // velocity 즉시 정지 (Rigidbody2D 관성 제거)
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            Debug.Log("[BossWardenAI] ▶ DilPhase 진입 → 이동/패턴 정지");
        }

        /// <summary>
        /// DilPhase 종료.
        /// 이동/패턴 재개 + 플레이어 방향 즉시 전환 후 Idle 복귀.
        /// BossWardenCore.HandleDilPhaseExit() 에서 호출.
        /// (v3.0: OnGroggyExit 역할 흡수)
        /// </summary>
        public void OnDilPhaseExit()
        {
            _isStopped = false;
            TurnTowardPlayerImmediate();
            ChangeState(WardenAIState.Idle);
            Debug.Log("[BossWardenAI] ■ DilPhase 종료 → Idle 복귀");
        }

        /// <summary>
        /// 페이즈 전환.
        /// 2페이즈 진입 시 이동 속도 + 패턴 강화 적용.
        /// BossWardenCore.HandlePhaseChanged() 에서 호출.
        /// </summary>
        public void OnPhaseChanged(int newPhase)
        {
            if (newPhase != 2) return;

            if (_data != null)
                _currentMoveSpeed = _data.phase2MoveSpeed;

            _attackManager?.UnlockPhase2();

            Debug.Log("[BossWardenAI] ▶ 2페이즈 전환 — 이동 속도/패턴 강화 적용");
        }

        /// <summary>
        /// Dead.
        /// AI 완전 정지 + 컴포넌트 비활성.
        /// BossWardenCore.HandleDead() 에서 호출.
        /// </summary>
        public void OnDead()
        {
            _isStopped = true;
            SetArmsRecoveryVuln(false);
            _attackManager?.InterruptCurrentPattern();
            enabled = false;
            Debug.Log("[BossWardenAI] ✅ 처치 → AI 정지");
        }


        // ══════════════════════════════════════════════════════
        // AI 로직 — Update / FixedUpdate
        // ══════════════════════════════════════════════════════

        private void UpdateTimers()
        {
            if (_flipCooldownTimer > 0f)
                _flipCooldownTimer -= Time.deltaTime;
        }

        private void UpdateStateLogic()
        {
            switch (_currentState)
            {
                case WardenAIState.Idle:
                    UpdateFacingTowardPlayer();
                    TrySelectPattern();
                    CheckChaseTransition();
                    break;

                case WardenAIState.Chase:
                    UpdateFacingTowardPlayer();
                    CheckIdleTransition();
                    break;
            }
        }

        private void UpdateMovement()
        {
            if (_currentState != WardenAIState.Chase)
            {
                _rigid2D.linearVelocity = Vector2.zero;
                return;
            }
            _rigid2D.linearVelocity = _facingDir * _currentMoveSpeed;
        }

        private void CheckChaseTransition()
        {
            // 패턴 실행 중 (Warning / Active / Recovery) 이면 Chase 전환 금지.
            // Warning 예고가 시작되면 보스는 반드시 Active 까지 패턴을 완수해야 한다.
            if (_attackManager != null && _attackManager.IsExecuting) return;
            if (_currentPattern != null) return;

            // DilPhase 중 (_isStopped = true) 이면 Chase 전환 금지.
            // _isStopped 가드는 FixedUpdate 에서 이미 처리하지만
            // ChangeState 호출 자체를 막아 상태 오염을 방지한다.
            if (_isStopped) return;

            if (_playerTransform == null || _data == null) return;

            float dist = Vector2.Distance(transform.position, _playerTransform.position);
            if (dist > _data.patternRange)
                ChangeState(WardenAIState.Chase);
        }

        private void CheckIdleTransition()
        {
            if (_playerTransform == null || _data == null) return;
            float dist = Vector2.Distance(transform.position, _playerTransform.position);
            if (dist <= _data.patternRange)
                ChangeState(WardenAIState.Idle);
        }

        private void UpdateFacingTowardPlayer()
        {
            if (_playerTransform == null) return;
            if (_flipCooldownTimer > 0f) return;

            Vector2 newDir = ((Vector2)_playerTransform.position
                - (Vector2)transform.position).normalized;

            if (newDir.sqrMagnitude < 0.01f) return;

            if (newDir != _facingDir)
            {
                _facingDir = newDir;
                _flipCooldownTimer = _data?.flipCooldown ?? 0.5f;
                OnFacingChanged?.Invoke(_facingDir);
            }
        }

        // ══════════════════════════════════════════════════════
        // 패턴 선택 + 실행
        // ══════════════════════════════════════════════════════

        private void TrySelectPattern()
        {
            if (_attackManager == null) return;
            _attackManager.RequestAttack();
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸
        // ══════════════════════════════════════════════════════

        private void ChangeState(WardenAIState newState, BossPatternBase patternOverride = null)
        {
            if (_currentState == newState) return;
            _currentState = newState;
            OnStateChanged?.Invoke(_currentState, patternOverride ?? _currentPattern);
            Debug.Log($"[BossWardenAI] 상태 → {_currentState}");
        }

        // ══════════════════════════════════════════════════════
        // Step 6 — BossAttackManager 연동 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossAttackManager 가 패턴 단계 전환을 AI 상태로 반영할 때 호출한다.
        /// Feedback 은 기존 OnStateChanged 이벤트를 그대로 받는다.
        /// </summary>
        public void SetAttackState(WardenAIState newState, BossPatternBase pattern)
        {
            _currentPattern = pattern;
            ChangeState(newState, pattern);
        }

        /// <summary>BossAttackManager 가 패턴 정상 종료를 알릴 때 호출한다.</summary>
        public void CompleteAttackState(BossPatternBase pattern)
        {
            if (_currentPattern == pattern)
                _currentPattern = null;

            SetArmsRecoveryVuln(false);

            if (!_isStopped)
                ChangeState(WardenAIState.Idle, null);
        }

        /// <summary>BossAttackManager 가 패턴 강제 중단을 알릴 때 호출한다.</summary>
        public void InterruptAttackState(BossPatternBase pattern)
        {
            if (_currentPattern == pattern)
                _currentPattern = null;

            SetArmsRecoveryVuln(false);
            _currentState = WardenAIState.Idle;
        }

        /// <summary>BossAttackManager 가 Recovery 취약 구간을 제어할 때 사용한다.</summary>
        public void SetRecoveryVulnerableFromAttackManager(bool isVulnerable)
        {
            SetArmsRecoveryVuln(isVulnerable);
        }



        /// <summary>
        /// 플레이어 방향 즉시 갱신 (쿨타임 무시).
        /// DilPhase 종료 시 호출.
        /// </summary>
        private void TurnTowardPlayerImmediate()
        {
            if (_playerTransform == null) return;

            Vector2 dir = ((Vector2)_playerTransform.position
                - (Vector2)transform.position).normalized;

            if (dir.sqrMagnitude < 0.01f) return;

            _facingDir = dir;
            _flipCooldownTimer = 0f;
            OnFacingChanged?.Invoke(_facingDir);
        }

        /// <summary>
        /// 양팔 Recovery 취약 구간 활성/비활성.
        /// IsSealed 팔은 스킵.
        /// </summary>
        private void SetArmsRecoveryVuln(bool isVuln)
        {
            if (_armL != null && !_armL.IsSealed) _armL.SetRecoveryVuln(isVuln);
            if (_armR != null && !_armR.IsSealed) _armR.SetRecoveryVuln(isVuln);
        }
    }
}