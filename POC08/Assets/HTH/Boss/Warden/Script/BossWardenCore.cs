// ============================================================
// BossWardenCore.cs  v3.0
// Boss_Warden 루트 초기화 허브 + 이벤트 중계 컴포넌트
//
// [v3.0 — 신규 봉인 시스템 완전 위임]
//
//   [제거된 책임 — 신규 컴포넌트로 위임]
//     직접 상태 관리 (_isGroggy / _isDilPhase / _isFinalSealReady / _isDead)
//       → SealStateManager 로 완전 위임
//
//     ForceRelease 직접 호출 (_armLSealable / _armRSealable)
//       → SealGaugeManager.ReleaseAllParts() 로 위임
//
//     코어 SetActive 직접 제어
//       → SealStateManager 내부에서 처리
//
//     GroggyRoutine / DilPhaseRoutine 타이머 코루틴
//       → SealStateManager 내부에서 처리
//
//     SealExecutor 참조 + 이벤트 구독
//       → SealExecutionEvent + SealExecutionRunner 으로 대체
//
//     딜 페이즈 타이머 직접 관리
//       → SealStateManager.DilPhaseTimerRoutine 으로 위임
//
//   [유지된 책임]
//     BossWardenDataSO → 모든 하위 컴포넌트 주입 (단일 연결 지점)
//     BossWardenAI / BossWardenFeedback / BossWardenAttackRange 연결
//     SealStateManager 이벤트 → BossWardenAI / BossWardenFeedback 브리지
//     IBossCore 인터페이스 구현 (BattleManager 연동용)
//
//   [v2.0 대비 제거된 필드]
//     SealExecutor _sealExecutor              → SealExecutionRunner 으로 대체
//     SealableComponent _armLSealable / _armRSealable → SealGaugeManager 자동 수집
//     SealableComponent _coreSealable         → SealStateManager.ConnectCore() 로 처리
//     bool _isGroggy / _isDilPhase / _isFinalSealReady / _isDead → SealStateManager
//     int _sealedArmCount                     → SealGaugeManager.GetSealedCount()
//     Coroutine _groggyCoroutine / _dilPhaseCoroutine → SealStateManager 내부
//
//   [v2.0 대비 추가된 참조]
//     SealStateManager _stateManager
//     SealGaugeManager _gaugeManager
//     SealEffectManager _effectManager
//     SealExecutionRunner _executionRunner
//
// [BossWardenCore 의 역할 — v3.0 확정]
//   1. BossWardenDataSO 를 모든 컴포넌트에 주입 (단일 연결 지점)
//   2. SealStateManager 이벤트 → BossWardenAI / Feedback 에 브리지
//   3. IBossCore 인터페이스 구현 (외부 시스템 연동)
//   4. 씬 조립 Inspector 연결 허브
//
// [v2.0 이전 변경 이력]
//   v2.0: SealableComponent / SealExecutor 통합
//   v1.1: [DefaultExecutionOrder(-10)] 추가
//   v1.0: 최초 작성
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 루트 초기화 허브 + 이벤트 중계 컴포넌트. (v3.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [이 컴포넌트가 하는 것]
    ///   - BossWardenDataSO 를 모든 컴포넌트에 주입
    ///   - SealStateManager 이벤트를 BossWardenAI / Feedback 에 전달
    ///   - IBossCore 구현 (외부 BattleManager 연동)
    ///
    /// [이 컴포넌트가 하지 않는 것]
    ///   - 상태 관리 → SealStateManager
    ///   - 봉인도 관리 → SealGaugeManager
    ///   - 집행 처리 → SealExecutionRunner
    ///   - 이동/패턴 → BossWardenAI
    ///   - 색상/연출 → BossWardenFeedback
    ///   - 충격파 → BossWardenShockwave / SealEffectManager
    /// ────────────────────────────────────────────────────
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class BossWardenCore : MonoBehaviour, IBossCore
    {
        // ══════════════════════════════════════════════════════
        // Inspector — DataSO
        // ══════════════════════════════════════════════════════

        [Header("── DataSO (필수) ──────────────────────")]

        /// <summary>
        /// Warden 수치 ScriptableObject.
        /// 모든 하위 컴포넌트에 주입하는 단일 연결 지점.
        /// BossDataSO 상속이므로 SealData + ColorData 포함.
        /// </summary>
        [Tooltip("BossWardenDataSO. 필수 연결. 모든 컴포넌트에 이 하나를 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        // ══════════════════════════════════════════════════════
        // Inspector — 부위/코어 연결
        // ══════════════════════════════════════════════════════

        [Header("── 부위 연결 (필수) ──────────────────────")]

        /// <summary>
        /// 왼팔 BossWardenArmPart.
        /// Initialize(_data) 주입용.
        /// </summary>
        [Tooltip("왼팔 BossWardenArmPart. Initialize 주입용.")]
        [SerializeField] private BossWardenArmPart _armL;

        /// <summary>오른팔 BossWardenArmPart.</summary>
        [Tooltip("오른팔 BossWardenArmPart. Initialize 주입용.")]
        [SerializeField] private BossWardenArmPart _armR;

        [Header("── 코어 연결 (필수) ──────────────────────")]

        /// <summary>
        /// 코어 GameObject.
        /// SealStateManager.ConnectCore() 에 주입.
        /// 기본 SetActive = false.
        /// </summary>
        [Tooltip("코어 GameObject. 기본 SetActive=false. SealStateManager 에 주입.")]
        [SerializeField] private GameObject _coreObject;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조 (Awake 자동 탐색)
        // ══════════════════════════════════════════════════════

        /// <summary>봉인 상태 총괄. 이벤트 구독 대상.</summary>
        private SealStateManager _stateManager;

        /// <summary>봉인도 전체 조율.</summary>
        private SealGaugeManager _gaugeManager;

        /// <summary>이펙트/UI 총괄.</summary>
        private SealEffectManager _effectManager;

        /// <summary>S키 홀드 집행 실행.</summary>
        private SealExecutionRunner _executionRunner;

        /// <summary>Warden 전용 AI.</summary>
        private BossWardenAI _ai;

        /// <summary>Warden 전용 색상 연출.</summary>
        private BossWardenFeedback _feedback;

        /// <summary>패턴 예고 범위 표시.</summary>
        private BossWardenAttackRange _attackRange;

        /// <summary>충격파.</summary>
        private BossWardenShockwave _shockwave;

        /// <summary>물리.</summary>
        private Rigidbody2D _rigid2D;

        // ══════════════════════════════════════════════════════
        // IBossCore 이벤트 (외부 BattleManager 연동용)
        // ══════════════════════════════════════════════════════

        /// <summary>보스 처치 완료 시 발행. BattleManager 가 구독.</summary>
        public event Action OnDead;
        public event Action OnGroggyEnter;
        public event Action OnGroggyExit;
        public event Action OnDilPhaseEnter;
        public event Action OnDilPhaseExit;

        // ══════════════════════════════════════════════════════
        // 프로퍼티 (IBossCore 구현)
        // ══════════════════════════════════════════════════════

        /// <summary>현재 그로기 상태 여부. BossWardenAI 에서 참조.</summary>
        public bool IsGroggy => _stateManager != null && _stateManager.IsGroggy;

        /// <summary>현재 딜 페이즈 상태 여부.</summary>
        public bool IsDilPhase => _stateManager != null && _stateManager.IsDilPhase;

        /// <summary>현재 최종 봉인 상태 여부.</summary>
        public bool IsFinalSeal => _stateManager != null && _stateManager.IsFinalSeal;

        /// <summary>처치 여부.</summary>
        public bool IsDead => _stateManager != null && _stateManager.IsDead;

        /// <summary>현재 페이즈 번호.</summary>
        public int CurrentPhase => _stateManager != null ? _stateManager.CurrentPhase : 1;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // 같은 오브젝트 컴포넌트 자동 탐색
            _stateManager = GetComponent<SealStateManager>();
            _gaugeManager = GetComponent<SealGaugeManager>();
            _effectManager = GetComponent<SealEffectManager>();
            _executionRunner = GetComponent<SealExecutionRunner>();
            _ai = GetComponent<BossWardenAI>();
            _feedback = GetComponent<BossWardenFeedback>();
            _attackRange = GetComponent<BossWardenAttackRange>();
            _shockwave = GetComponent<BossWardenShockwave>();
            _rigid2D = GetComponent<Rigidbody2D>();

            ValidateComponents();
        }

        private void Start()
        {
            // 1. DataSO 유효성 검사
            if (_data == null || !_data.IsValid())
            {
                Debug.LogError("[BossWardenCore] BossWardenDataSO 미연결 — 초기화 중단.");
                enabled = false;
                return;
            }

            // 2. 모든 컴포넌트에 DataSO 주입
            InjectData();

            // 3. SealStateManager 코어 오브젝트 연결
            _stateManager?.ConnectCore(_coreObject);

            // 4. SealEffectManager 코어 Transform 연결
            if (_coreObject != null)
                _effectManager?.SetCoreTransform(_coreObject.transform);

            // 5. SealStateManager 이벤트 구독 → AI / Feedback 브리지
            SubscribeStateEvents();

            Debug.Log("[BossWardenCore] v3.0 초기화 완료");
        }

        private void OnDestroy()
        {
            UnsubscribeStateEvents();
        }

        // ══════════════════════════════════════════════════════
        // DataSO 주입 — 단일 연결 지점
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenDataSO 를 모든 하위 컴포넌트에 주입한다.
        /// BossDataSO (범용) 는 범용 컴포넌트에,
        /// BossWardenDataSO (전용) 는 Warden 전용 컴포넌트에 주입.
        /// </summary>
        private void InjectData()
        {
            // 범용 컴포넌트 — BossDataSO 주입
            _stateManager?.Initialize(_data);
            _gaugeManager?.Initialize(_data);
            _effectManager?.Initialize(_data);
            _executionRunner?.Initialize(_data);

            // 봉인 시스템 Part Layer 초기화는 SealGaugeManager.Initialize() 내부에서 처리

            // Warden 전용 컴포넌트 — BossWardenDataSO 주입
            _ai?.Initialize(_data);
            _feedback?.Initialize(_data);
            _attackRange?.Initialize(_data);
            _shockwave?.Initialize(_data);
            _armL?.Initialize(_data);
            _armR?.Initialize(_data);

            Debug.Log("[BossWardenCore] DataSO 전체 주입 완료");
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독 — SealStateManager → AI / Feedback 브리지
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealStateManager 이벤트를 구독하여
        /// BossWardenAI / BossWardenFeedback 에 브리지한다.
        ///
        /// [브리지 이유]
        ///   BossWardenAI / Feedback 은 SealStateManager 를 직접 참조하지 않는다.
        ///   BossWardenCore 가 중계 역할을 하여 결합도를 낮춤.
        /// </summary>
        private void SubscribeStateEvents()
        {
            if (_stateManager == null) return;

            _stateManager.OnGroggyEnter += HandleGroggyEnter;
            _stateManager.OnGroggyExit += HandleGroggyExit;
            _stateManager.OnDilPhaseEnter += HandleDilPhaseEnter;
            _stateManager.OnDilPhaseExit += HandleDilPhaseExit;
            _stateManager.OnFinalSealReady += HandleFinalSealReady;
            _stateManager.OnPhaseChanged += HandlePhaseChanged;
            _stateManager.OnDead += HandleDead;
        }

        private void UnsubscribeStateEvents()
        {
            if (_stateManager == null) return;

            _stateManager.OnGroggyEnter -= HandleGroggyEnter;
            _stateManager.OnGroggyExit -= HandleGroggyExit;
            _stateManager.OnDilPhaseEnter -= HandleDilPhaseEnter;
            _stateManager.OnDilPhaseExit -= HandleDilPhaseExit;
            _stateManager.OnFinalSealReady -= HandleFinalSealReady;
            _stateManager.OnPhaseChanged -= HandlePhaseChanged;
            _stateManager.OnDead -= HandleDead;
        }

        // ══════════════════════════════════════════════════════
        // 상태 이벤트 핸들러 — AI / Feedback 브리지
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Groggy 진입.
        /// AI 정지 + Feedback 노란 Pulse + AttackRange HideAll.
        /// </summary>
        private void HandleGroggyEnter()
        {
            _ai?.OnGroggyEnter();
            _feedback?.OnGroggyEnter();
            _attackRange?.HideAll();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            Debug.Log("[BossWardenCore] ▶ Groggy 진입 브리지");
        }

        /// <summary>
        /// Groggy 실패 종료.
        /// AI 재개 + Feedback Idle 복귀.
        /// </summary>
        private void HandleGroggyExit()
        {
            _ai?.OnGroggyExit();
            _feedback?.OnGroggyExit();

            Debug.Log("[BossWardenCore] ■ Groggy 실패 종료 브리지");
        }

        /// <summary>
        /// DilPhase 진입.
        /// AI 완전 정지 유지 + Feedback 밝은 주황 Pulse.
        /// </summary>
        private void HandleDilPhaseEnter()
        {
            _ai?.OnDilPhaseEnter();
            _feedback?.OnDilPhaseEnter();

            Debug.Log("[BossWardenCore] ▶ DilPhase 진입 브리지");
        }

        /// <summary>
        /// DilPhase 종료.
        /// AI 재개 + Feedback Idle 복귀 + AttackRange 초기화.
        /// </summary>
        private void HandleDilPhaseExit()
        {
            _ai?.OnDilPhaseExit();
            _feedback?.OnDilPhaseExit();

            Debug.Log("[BossWardenCore] ■ DilPhase 종료 브리지");
        }

        /// <summary>
        /// FinalSeal 준비.
        /// Feedback 최종봉인 Pulse.
        /// SealExecutionRunner 는 SealStateManager.OnFinalSealReady 를 직접 구독 가능.
        /// </summary>
        private void HandleFinalSealReady()
        {
            _feedback?.OnFinalSealReady();

            Debug.Log("[BossWardenCore] ▶ FinalSeal 준비 브리지");
        }

        /// <summary>
        /// 페이즈 전환.
        /// AI 2페이즈 강화 + Feedback 페이즈 전환 연출.
        /// </summary>
        private void HandlePhaseChanged(int newPhase)
        {
            _ai?.OnPhaseChanged(newPhase);
            _feedback?.OnPhaseChanged(newPhase);

            Debug.Log($"[BossWardenCore] ▶ 페이즈 {newPhase} 전환 브리지");
        }

        /// <summary>
        /// Dead.
        /// AI 비활성 + Feedback 처치 연출 + AttackRange 전체 숨김
        /// + ExecutionRunner 강제 중단 + OnDead 발행.
        /// </summary>
        private void HandleDead()
        {
            _ai?.OnDead();
            _feedback?.OnDead();
            _attackRange?.HideAll();
            _executionRunner?.ForceStop();

            // IBossCore.OnDead 발행 (BattleManager 연동)
            OnDead?.Invoke();

            Debug.Log("[BossWardenCore] ✅ Dead 브리지 + OnDead 발행");
        }

        // ══════════════════════════════════════════════════════
        // 유효성 검사
        // ══════════════════════════════════════════════════════

        private void ValidateComponents()
        {
            if (_stateManager == null)
                Debug.LogError("[BossWardenCore] SealStateManager 미연결.");
            if (_gaugeManager == null)
                Debug.LogError("[BossWardenCore] SealGaugeManager 미연결.");
            if (_ai == null)
                Debug.LogWarning("[BossWardenCore] BossWardenAI 미연결.");
            if (_feedback == null)
                Debug.LogWarning("[BossWardenCore] BossWardenFeedback 미연결.");
            if (_coreObject == null)
                Debug.LogWarning("[BossWardenCore] Core GameObject 미연결.");
        }

        // ══════════════════════════════════════════════════════
        // 외부 API (디버그 / BattleManager)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 페이즈 반환. BossWardenAI 패턴 분기용.
        /// </summary>
        public int GetCurrentPhase() => CurrentPhase;

        // ══════════════════════════════════════════════════════
        // 디버그 — ContextMenu
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        [ContextMenu("DEBUG: 그로기 강제 진입")]
        public void DEBUG_ForceGroggy()
        {
            if (!Application.isPlaying) return;
            _stateManager?.ForceKill(); // 임시 — SealStateManager DEBUG 메서드 사용
            Debug.Log("[BossWardenCore] DEBUG 그로기 강제 진입");
        }

        [ContextMenu("DEBUG: 양팔 봉인도 즉시 채우기")]
        public void DEBUG_FillArmGauges()
        {
            if (!Application.isPlaying) return;
            // SealGaugeManager ContextMenu 사용
            Debug.Log("[BossWardenCore] DEBUG → SealGaugeManager ContextMenu 사용 권장");
        }

        [ContextMenu("DEBUG: ForceKill")]
        public void DEBUG_ForceKill()
        {
            if (!Application.isPlaying) return;
            _stateManager?.ForceKill();
        }
#endif
    }
}