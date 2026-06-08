// ============================================================
// BossWardenAI.cs  v3.0
// Boss_Warden 탑뷰 AI
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
// [v2.0 유지]
//   WardenAIState enum (Idle / Chase / Warning / Active / Recovery)
//   탑뷰 8방향 이동 (Rigidbody2D.linearVelocity)
//   패턴 선택 + ExecutePattern() 코루틴
//   _isStopped 플래그
//   SubscribePatternEvents()
//   HandlePatternGroggy() — 빈 함수 유지
//     (패턴 그로기 트리거는 SealGaugeManager.OnAllPartsSealed 경로로 처리)
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
    /// Boss_Warden 탑뷰 AI. (v3.0)
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

        [Header("── 패턴 목록 (필수) ──────────────────────")]

        /// <summary>
        /// 패턴 목록. Inspector 에서 BossPattern_XX 연결.
        /// CanExecute + IsAvailable 로 실행 가능 패턴 필터.
        /// </summary>
        [Tooltip("패턴 목록. Inspector 에서 BossPattern_XX 연결.")]
        [SerializeField] private List<BossPatternBase> _patterns = new();

        [Header("── 팔 부위 연결 (필수) ──────────────────────")]

        /// <summary>왼팔. Recovery 취약 구간 SetRecoveryVuln() 전달 대상.</summary>
        [Tooltip("왼팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armL;

        /// <summary>오른팔.</summary>
        [Tooltip("오른팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armR;

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

        /// <summary>현재 패턴 코루틴 핸들.</summary>
        private Coroutine _patternCoroutine;

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

        /// <summary>패턴 선택 캐시 리스트 (GC 할당 방지).</summary>
        private readonly List<BossPatternBase> _availablePatterns = new();

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

            // 패턴 이벤트 구독
            SubscribePatternEvents();
        }

        private void OnDestroy()
        {
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
        // 패턴 이벤트 구독
        // ══════════════════════════════════════════════════════

        /// <summary>패턴 이벤트 구독. OnPatternEnd / OnPatternGroggy 수신.</summary>
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
        // BossWardenCore v4.0 이 SealStateManager 이벤트 수신 후 직접 호출
        // ══════════════════════════════════════════════════════

        // 수정 — InterruptCurrentPattern() 이 이미 _currentState=Idle 처리하므로
        // 추가 작업 없음. 단, velocity 명시 정지 추가.
        public void OnDilPhaseEnter()
        {
            _isStopped = true;
            SetArmsRecoveryVuln(false);
            InterruptCurrentPattern(); // 내부에서 StopAllCoroutines + 상태 복귀 처리

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
        /// 패턴 Recovery 중 그로기 트리거 수신.
        ///
        /// [v3.0 설계]
        ///   그로기 개념 제거 — DilPhase 진입은 SealGaugeManager.OnAllPartsSealed 경로만 사용.
        ///   패턴 자체가 그로기를 유발하는 경로 없음.
        ///   → 이 핸들러는 빈 함수로 유지 (OnPatternGroggy 이벤트는 패턴 내부용으로 보존).
        ///
        /// [패턴별 주의]
        ///   BossPattern_GuardBreak._triggerGroggyOnRecovery = true 로 설정되어 있으나
        ///   이 핸들러에서 아무것도 하지 않으므로 실제 영향 없음.
        ///   GuardBreak 의 그로기 유도 의도는 향후 기획 결정에 따라 재설계 필요.
        /// </summary>
        private void HandlePatternGroggy()
        {
            // v3.0: DilPhase 진입은 SealGaugeManager.OnAllPartsSealed 경로로만 처리
            // 패턴 그로기 트리거는 현재 사용하지 않음
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
            // 이미 패턴 실행 중이면 무시.
            if (_currentPattern != null) return;

            // DilPhase 중 (_isStopped = true) 이면 패턴 선택 금지.
            // DilPhase 진입 직후 1프레임 내 패턴 선택을 방지.
            if (_isStopped) return;

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
        /// 각 단계 전후 _isStopped 이중 체크.
        /// </summary>
        /// <summary>
        /// 패턴 실행 코루틴. Warning → Active → Recovery.
        ///
        /// [중단 조건 — _isStopped 단독 가드]
        ///   _isStopped = true 는 DilPhase 진입 / Dead 시에만 발생.
        ///   Warning 중 Chase 전환은 CheckChaseTransition() 에서 이미 차단되므로
        ///   _currentState 체크는 불필요. _isStopped 만으로 충분.
        ///
        /// [패턴 완수 원칙]
        ///   Warning 이 시작되면 _isStopped 가 아닌 한 Active 까지 반드시 완수.
        ///   플레이어가 범위 밖으로 나가도 패턴은 끝까지 실행된다.
        /// </summary>
        private IEnumerator ExecutePattern(BossPatternBase pattern)
        {
            string pName = pattern.GetType().Name;

            // ── Warning ──
            Debug.Log($"[BossWardenAI] ▶ [{pName}] Warning");
            ChangeState(WardenAIState.Warning);
            yield return StartCoroutine(pattern.ExecuteWarning());

            // DilPhase 진입 등 강제 중단 시에만 취소
            if (_isStopped) { CleanupPattern(); yield break; }

            // ── Active ──
            Debug.Log($"[BossWardenAI] ▶ [{pName}] Active");
            ChangeState(WardenAIState.Active);
            yield return StartCoroutine(pattern.ExecuteActive());

            if (_isStopped) { CleanupPattern(); yield break; }

            // ── Recovery ──
            Debug.Log($"[BossWardenAI] ▶ [{pName}] Recovery");
            ChangeState(WardenAIState.Recovery);
            SetArmsRecoveryVuln(true);

            yield return StartCoroutine(pattern.ExecuteRecovery());

            SetArmsRecoveryVuln(false);

            if (_isStopped) { CleanupPattern(); yield break; }

            // ── 정상 완료 ──
            Debug.Log($"[BossWardenAI] ✅ [{pName}] 패턴 완료 → Idle");
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

        // 수정
        /// <summary>
        /// 현재 실행 중인 패턴을 안전하게 강제 중단한다.
        ///
        /// [핵심 수정 — StopAllCoroutines()]
        ///   기존 StopCoroutine(_patternCoroutine) 은 ExecutePattern() 만 중단.
        ///   그 안에서 StartCoroutine(pattern.ExecuteWarning()) 으로 실행된
        ///   OnWarning() / OnActive() 중첩 코루틴은 계속 실행됨 → 패턴 캔슬 불가.
        ///   StopAllCoroutines() 로 이 MonoBehaviour 의 모든 코루틴을 종료해야
        ///   OnWarning() → OnActive() 내부 로직까지 완전 중단.
        ///
        /// [팔 상태 복구]
        ///   중단 후 SetArmsRecoveryVuln(false) 로 취약 배율 해제.
        ///   _currentState = Idle 로 강제 복귀 (ChangeState 이벤트 발행 없이).
        /// </summary>
        private void InterruptCurrentPattern()
        {
            if (_currentPattern != null)
            {
                _currentPattern.Interrupt(); // 패턴 내부 DOTween 정리 + _isInterrupted = true
                _currentPattern = null;
            }

            // StopAllCoroutines: ExecutePattern + 그 안의 OnWarning/OnActive 자식 코루틴 전부 중단
            StopAllCoroutines();
            _patternCoroutine = null;

            // 상태 직접 복귀 (팔 일그러짐 방지)
            SetArmsRecoveryVuln(false);

            // ChangeState() 대신 직접 설정: 이미 _isStopped=true 상태이므로
            // 이벤트 발행 없이 내부 변수만 정리
            _currentState = WardenAIState.Idle;
        }

        private void CleanupPattern()
        {
            _currentPattern = null;
            _patternCoroutine = null;
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