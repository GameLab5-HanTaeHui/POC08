// ============================================================
// BossWardenAI.cs  v2.0
// Boss_Warden 탑뷰 AI
//
// [v2.0 — BossWardenCore 직접 참조 제거 + 브리지 메서드 전환]
//
//   [변경 1] _core 필드 + SubscribeCoreEvents() 완전 제거
//     기존: Awake → _core = GetComponent<BossWardenCore>()
//           Start → SubscribeCoreEvents() → _core.OnGroggyEnter += HandleGroggyEnter 등
//           → BossWardenAI 가 BossWardenCore 에 직접 의존
//
//     변경: _core 필드 없음
//           SubscribeCoreEvents() / UnsubscribeCoreEvents() 없음
//           BossWardenCore v3.0 이 SealStateManager 이벤트 수신 후
//           _ai.OnGroggyEnter() 등을 직접 호출 (브리지 방식)
//
//   [변경 2] 상태 핸들러 메서드 이름 + 접근 제한자
//     기존: private HandleGroggyEnter() / private HandleGroggyExit() 등
//           일부 파일 기준으로 public 인 경우도 있었음
//
//     변경: 모두 public OnXxx() 형태로 통일
//           OnGroggyEnter / OnGroggyExit
//           OnDilPhaseEnter / OnDilPhaseExit
//           OnPhaseChanged(int) / OnDead()
//           → BossWardenCore v3.0 브리지에서 호출 가능
//
//   [변경 3] HandlePatternGroggy() → _core.EnterGroggy() 제거
//     기존: HandlePatternGroggy() → _core.EnterGroggy() 직접 호출
//           → BossWardenCore 에 의존
//
//     변경: HandlePatternGroggy() 에서 아무것도 하지 않음
//           SealGaugeManager 가 OnAllPartsSealed 자동 처리
//           → AI 는 패턴 Recovery 완료만 알리고
//              그로기 진입 조건 판단은 SealStateManager 에서 수행
//           → AI 의 역할: 패턴 실행 + 이동만
//
//   [v1.0 유지]
//     WardenAIState enum (Idle / Chase / Warning / Active / Recovery)
//     탑뷰 8방향 이동 (Rigidbody2D.linearVelocity)
//     패턴 선택 + ExecutePattern() 코루틴 (Warning → Active → Recovery)
//     _isStopped 플래그 (그로기/딜페이즈 중 이동/패턴 정지)
//     Recovery 취약 구간 SetArmsRecoveryVuln()
//     OnStateChanged / OnFacingChanged 이벤트
//     FacingDir / PlayerTransform 프로퍼티
//     SubscribePatternEvents() — 패턴 이벤트는 그대로 유지
//     TurnTowardPlayerImmediate() / InterruptCurrentPattern()
//
// [namespace] SEAL
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 탑뷰 AI. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [이 스크립트가 하는 것]
    ///   - 상태 관리 (Idle / Chase / Warning / Active / Recovery)
    ///   - 탑뷰 8방향 플레이어 추적 이동
    ///   - 패턴 선택 및 실행 코루틴 관리
    ///   - 이동/패턴 정지 (_isStopped)
    ///   - Recovery 취약 구간 팔 전달
    ///   - 2페이즈 패턴 강화 적용
    ///
    /// [이 스크립트가 하지 않는 것]
    ///   - 그로기 진입 판단 → SealStateManager (v2.0 변경)
    ///   - Core 이벤트 직접 구독 → BossWardenCore v3.0 브리지 (v2.0 변경)
    ///   - 봉인 집행 처리 → SealExecutionRunner
    ///   - 색상 피드백 → BossWardenFeedback
    ///
    /// [BossWardenCore 브리지 메서드 — public]
    ///   OnGroggyEnter() / OnGroggyExit()
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
        /// Groggy / DilPhase 는 SealStateManager 가 관리.
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
        /// Warden 수치. BossWardenCore.Start() 에서 Initialize() 주입.
        /// </summary>
        [Tooltip("BossWardenDataSO. 필수 연결.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 패턴 목록 (필수) ──────────────────────")]

        /// <summary>
        /// 패턴 목록. Inspector 에서 BossPattern_XX 연결.
        /// CanExecute + IsAvailable 로 실행 가능 패턴 필터.
        /// </summary>
        [Tooltip("패턴 목록. Inspector 에서 BossPattern_XX 연결.")]
        [SerializeField] private List<BossPatternBase> _patterns = new();

        [Header("── 팔 부위 연결 (필수) ──────────────────────")]

        /// <summary>
        /// 왼팔. Recovery 취약 구간 SetRecoveryVuln() 전달 대상.
        /// </summary>
        [Tooltip("왼팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armL;

        /// <summary>오른팔.</summary>
        [Tooltip("오른팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armR;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>Rigidbody2D. Chase 이동 시 linearVelocity 직접 제어.</summary>
        private Rigidbody2D _rigid2D;

        /// <summary>SpriteRenderer. OnFacingChanged 발행 시 flipX 처리 대상.</summary>
        private SpriteRenderer _spriteRenderer;

        /// <summary>플레이어 Transform. Start 1회 캐싱.</summary>
        private Transform _playerTransform;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 AI 상태.</summary>
        private WardenAIState _currentState = WardenAIState.Idle;

        /// <summary>현재 실행 중인 패턴. ExecutePattern 시작 시 설정.</summary>
        private BossPatternBase _currentPattern;

        /// <summary>현재 패턴 코루틴 핸들.</summary>
        private Coroutine _patternCoroutine;

        /// <summary>
        /// 이동/패턴 정지 플래그.
        /// true: Groggy / DilPhase / Dead 중.
        /// FixedUpdate 에서 linearVelocity = 0 강제 적용.
        /// </summary>
        private bool _isStopped;

        /// <summary>현재 이동 속도. 페이즈 전환 시 갱신.</summary>
        private float _currentMoveSpeed;

        /// <summary>방향 전환 쿨타임 잔여 시간.</summary>
        private float _flipCooldownTimer;

        /// <summary>현재 플레이어 방향 벡터 (정규화).</summary>
        private Vector2 _facingDir = Vector2.right;

        /// <summary>패턴 선택 캐시 리스트 (GC 할당 방지).</summary>
        private readonly List<BossPatternBase> _availablePatterns = new();

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 상태 전환 시 발행.
        /// BossWardenFeedback 이 구독하여 상태별 DOTween 색상 연출.
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
            // v2.0: _core 참조 제거
            _currentMoveSpeed = _data != null ? _data.moveSpeed : 3.5f;
        }

        private void Start()
        {
            // 플레이어 탐색
            var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (players.Length > 0)
                _playerTransform = players[0].transform;
            else
                Debug.LogWarning("[BossWardenAI] PlayerMoveController 탐색 실패.");

            // v2.0: SubscribeCoreEvents() 제거 — BossWardenCore 브리지 방식으로 전환
            // 패턴 이벤트만 구독
            SubscribePatternEvents();
        }

        private void OnDestroy()
        {
            // v2.0: UnsubscribeCoreEvents() 제거
            UnsubscribePatternEvents();
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
            Debug.Log("[BossWardenAI] 초기화 완료");
        }

        // ══════════════════════════════════════════════════════
        // 패턴 이벤트 구독 (Core 이벤트 구독 제거됨)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 패턴 이벤트 구독.
        /// OnPatternEnd / OnPatternGroggy 수신.
        /// </summary>
        private void SubscribePatternEvents()
        {
            foreach (var p in _patterns)
            {
                if (p == null) continue;
                p.OnPatternEnd -= HandlePatternEnd;
                p.OnPatternEnd += HandlePatternEnd;
                p.OnPatternGroggy -= HandlePatternGroggy;
                p.OnPatternGroggy += HandlePatternGroggy;
            }
        }

        private void UnsubscribePatternEvents()
        {
            foreach (var p in _patterns)
            {
                if (p == null) continue;
                p.OnPatternEnd -= HandlePatternEnd;
                p.OnPatternGroggy -= HandlePatternGroggy;
            }
        }

        // ══════════════════════════════════════════════════════
        // BossWardenCore 브리지 메서드 — public
        // BossWardenCore v3.0 이 SealStateManager 이벤트 수신 후 직접 호출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Groggy 진입.
        /// 이동 정지 + 현재 패턴 강제 중단.
        /// BossWardenCore.HandleGroggyEnter() 에서 호출.
        /// </summary>
        public void OnGroggyEnter()
        {
            _isStopped = true;
            SetArmsRecoveryVuln(false);
            InterruptCurrentPattern();
            Debug.Log("[BossWardenAI] ▶ 그로기 진입 → 이동/패턴 정지");
        }

        /// <summary>
        /// Groggy 실패 종료.
        /// 이동/패턴 재개 + 플레이어 방향 즉시 전환 후 Idle 복귀.
        /// BossWardenCore.HandleGroggyExit() 에서 호출.
        /// </summary>
        public void OnGroggyExit()
        {
            _isStopped = false;
            TurnTowardPlayerImmediate();
            ChangeState(WardenAIState.Idle);
            Debug.Log("[BossWardenAI] ■ 그로기 종료 → Idle 복귀");
        }

        /// <summary>
        /// DilPhase 진입.
        /// 이동/패턴 정지 (그로기와 동일).
        /// BossWardenCore.HandleDilPhaseEnter() 에서 호출.
        /// </summary>
        public void OnDilPhaseEnter()
        {
            _isStopped = true;
            SetArmsRecoveryVuln(false);
            InterruptCurrentPattern();
            Debug.Log("[BossWardenAI] ▶ 딜 페이즈 진입 → 이동/패턴 정지");
        }

        /// <summary>
        /// DilPhase 종료.
        /// 이동/패턴 재개 + Idle 복귀.
        /// BossWardenCore.HandleDilPhaseExit() 에서 호출.
        /// </summary>
        public void OnDilPhaseExit()
        {
            _isStopped = false;
            TurnTowardPlayerImmediate();
            ChangeState(WardenAIState.Idle);
            Debug.Log("[BossWardenAI] ■ 딜 페이즈 종료 → Idle 복귀");
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

            foreach (var p in _patterns)
            {
                if (p == null) continue;
                p.UnlockPhase2();
            }

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
            InterruptCurrentPattern();
            enabled = false;
            Debug.Log("[BossWardenAI] ✅ 처치 → AI 정지");
        }

        // ══════════════════════════════════════════════════════
        // 패턴 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 패턴 Recovery 완전 종료 수신.
        /// 패턴 정리 후 Idle 복귀.
        /// </summary>
        private void HandlePatternEnd(BossPatternBase pattern)
        {
            CleanupPattern();
            if (!_isStopped)
                ChangeState(WardenAIState.Idle);
        }

        /// <summary>
        /// 패턴 Recovery 중 그로기 조건 트리거 수신.
        ///
        /// [v2.0 변경]
        ///   기존: _core.EnterGroggy() 직접 호출
        ///   변경: 아무것도 하지 않음
        ///         SealGaugeManager 가 OnAllPartsSealed 이벤트로 자동 처리
        ///         → AI 는 패턴 실행만 담당, 그로기 진입 판단은 SealStateManager
        ///
        /// [그로기 진입 흐름 v2.0]
        ///   패턴 Recovery 완료
        ///   → SealGaugeManager.AreAllPartsSealed() 체크
        ///   → OnAllPartsSealed 이벤트 발행
        ///   → SealStateManager.HandleAllPartsSealed()
        ///   → EnterGroggy() → OnGroggyEnter 발행
        ///   → BossWardenCore 브리지 → AI.OnGroggyEnter()
        /// </summary>
        private void HandlePatternGroggy()
        {
            // v2.0: _core.EnterGroggy() 제거
            // 그로기 진입은 SealStateManager 가 자동 처리
            Debug.Log("[BossWardenAI] 패턴 그로기 트리거 — SealStateManager 에서 자동 처리");
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
            if (_currentPattern != null) return;
            if (_patterns == null || _patterns.Count == 0) return;

            _availablePatterns.Clear();
            foreach (var p in _patterns)
            {
                if (p == null) continue;
                if (!p.CanExecute) continue;
                if (!p.IsAvailable) continue;
                _availablePatterns.Add(p);
            }

            if (_availablePatterns.Count == 0) return;

            int idx = UnityEngine.Random.Range(0, _availablePatterns.Count);
            var selected = _availablePatterns[idx];

            _currentPattern = selected;
            _patternCoroutine = StartCoroutine(ExecutePattern(selected));
        }

        /// <summary>
        /// 패턴 실행 코루틴. Warning → Active → Recovery.
        /// 각 단계 전후 _isStopped + 상태 이중 체크.
        /// </summary>
        private IEnumerator ExecutePattern(BossPatternBase pattern)
        {
            string name = pattern.GetType().Name;

            // ── Warning ──
            Debug.Log($"[BossWardenAI] ▶ [{name}] Warning");
            ChangeState(WardenAIState.Warning);
            yield return StartCoroutine(pattern.ExecuteWarning());

            if (_isStopped || _currentState != WardenAIState.Warning)
            { CleanupPattern(); yield break; }

            // ── Active ──
            Debug.Log($"[BossWardenAI] ▶ [{name}] Active");
            ChangeState(WardenAIState.Active);
            yield return StartCoroutine(pattern.ExecuteActive());

            if (_isStopped || _currentState != WardenAIState.Active)
            { CleanupPattern(); yield break; }

            // ── Recovery ──
            Debug.Log($"[BossWardenAI] ▶ [{name}] Recovery");
            ChangeState(WardenAIState.Recovery);
            SetArmsRecoveryVuln(true);

            yield return StartCoroutine(pattern.ExecuteRecovery());

            SetArmsRecoveryVuln(false);

            if (_isStopped)
            { CleanupPattern(); yield break; }

            // ── 정상 완료 ──
            Debug.Log($"[BossWardenAI] ✅ [{name}] 패턴 완료 → Idle");
            CleanupPattern();
            ChangeState(WardenAIState.Idle);
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸
        // ══════════════════════════════════════════════════════

        private void ChangeState(WardenAIState newState)
        {
            if (_currentState == newState) return;
            _currentState = newState;
            OnStateChanged?.Invoke(_currentState, _currentPattern);
            Debug.Log($"[BossWardenAI] 상태 → {_currentState}");
        }

        /// <summary>
        /// 현재 패턴 강제 중단.
        /// Interrupt() → StopCoroutine() → CleanupPattern() 순서.
        /// </summary>
        public void InterruptCurrentPattern()
        {
            if (_currentPattern != null)
            {
                _currentPattern.Interrupt();
                _currentPattern = null;
            }

            if (_patternCoroutine != null)
            {
                StopCoroutine(_patternCoroutine);
                _patternCoroutine = null;
            }

            CleanupPattern();
        }

        private void CleanupPattern()
        {
            _currentPattern = null;
            _patternCoroutine = null;
        }

        /// <summary>
        /// 플레이어 방향 즉시 갱신 (쿨타임 무시).
        /// Groggy 종료 / DilPhase 종료 시 호출.
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