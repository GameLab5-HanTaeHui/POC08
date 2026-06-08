// ============================================================
// BossWardenCore.cs  v4.0
// Boss_Warden 루트 초기화 허브 + 이벤트 중계 컴포넌트
//
// [v4.0 변경 — Groggy 브리지 제거]
//   제거:
//     HandleGroggyEnter() 브리지 메서드
//     HandleGroggyExit() 브리지 메서드
//     OnGroggyEnter 이벤트 (IBossCore v2.0 기준)
//     OnGroggyExit 이벤트
//     SubscribeStateEvents() 에서 Groggy 구독 2줄
//
//   변경:
//     HandleDilPhaseEnter() → AI 정지 + Feedback 딜페이즈 색상 담당
//     HandleDilPhaseExit()  → AI 재개 + Feedback Idle 복귀 담당
//
// [BossWardenCore v4.0 역할]
//   1. BossWardenDataSO 모든 컴포넌트 주입 (단일 연결 지점)
//   2. SealStateManager 이벤트 → AI / Feedback 브리지
//   3. IBossCore v2.0 구현 (BattleManager 연동)
//   4. 씬 조립 Inspector 연결 허브
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 루트 초기화 허브 + 이벤트 중계 컴포넌트. (v4.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [이 컴포넌트가 하는 것]
    ///   - BossWardenDataSO 모든 컴포넌트에 주입
    ///   - SealStateManager 이벤트 → AI / Feedback 브리지
    ///   - IBossCore 인터페이스 구현
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
        /// BossDataSO 상속 → SealData + ColorData 포함.
        /// </summary>
        [Tooltip("BossWardenDataSO. 필수 연결. 모든 컴포넌트에 이 하나를 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        // ══════════════════════════════════════════════════════
        // Inspector — 부위 / 코어 연결
        // ══════════════════════════════════════════════════════

        [Header("── 부위 연결 (필수) ──────────────────────")]

        /// <summary>왼팔 BossWardenArmPart. Initialize 주입용.</summary>
        [Tooltip("왼팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenPart _armL;

        /// <summary>오른팔 BossWardenArmPart.</summary>
        [Tooltip("오른팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenPart _armR;

        [Header("── 코어 연결 (필수) ──────────────────────")]

        /// <summary>
        /// 코어 GameObject.
        /// 기본 SetActive = false.
        /// SealStateManager.ConnectCore() 에 주입.
        /// </summary>
        [Tooltip("코어 GameObject. 기본 SetActive=false.")]
        [SerializeField] private GameObject _coreObject;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조 (Awake 자동 탐색)
        // ══════════════════════════════════════════════════════

        /// <summary>봉인 상태 총괄. SealStateManager v2.0.</summary>
        private SealStateManager _stateManager;

        /// <summary>봉인도 수치 조율.</summary>
        private SealGaugeManager _gaugeManager;

        /// <summary>이펙트/UI 총괄.</summary>
        private SealEffectManager _effectManager;

        /// <summary>S키 홀드 집행 실행.</summary>
        private SealExecutionRunner _executionRunner;

        /// <summary>AI 이동/패턴.</summary>
        private BossWardenAI _ai;

        /// <summary>시각 피드백.</summary>
        private BossWardenFeedback _feedback;

        /// <summary>공격 범위 표시.</summary>
        private BossWardenAttackRange _attackRange;

        /// <summary>충격파.</summary>
        private BossWardenShockwave _shockwave;

        /// <summary>Rigidbody2D. DilPhase 진입 시 속도 0 강제.</summary>
        private Rigidbody2D _rigid2D;

        // ══════════════════════════════════════════════════════
        // IBossCore 이벤트 — v2.0 (Groggy 제거)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// DilPhase 진입 시 발행.
        /// Part 전체 봉인 완료 → 코어 활성 → AI 정지.
        /// </summary>
        public event Action OnDilPhaseEnter;

        /// <summary>
        /// DilPhase 종료 시 발행 (성공 or 실패).
        /// AI 재개 + Feedback Idle 복귀.
        /// </summary>
        public event Action OnDilPhaseExit;

        /// <summary>
        /// 페이즈 전환 시 발행.
        /// 파라미터: 새 페이즈 번호 (2).
        /// </summary>
        public event Action<int> OnPhaseChanged;

        /// <summary>
        /// FinalSeal 준비 시 발행.
        /// Feedback 최종봉인 Pulse 연출.
        /// </summary>
        public event Action OnFinalSealReady;

        /// <summary>
        /// 보스 처치 시 발행.
        /// BattleManager 연동.
        /// </summary>
        public event Action OnDead;

        // ══════════════════════════════════════════════════════
        // 프로퍼티 — IBossCore v2.0
        // ══════════════════════════════════════════════════════

        /// <summary>현재 DilPhase 여부.</summary>
        public bool IsDilPhase => _stateManager != null && _stateManager.IsDilPhase;

        /// <summary>현재 FinalSeal 여부.</summary>
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
            if (_data == null || !_data.IsValid())
            {
                Debug.LogError("[BossWardenCore] BossWardenDataSO 미연결 — 초기화 중단.");
                enabled = false;
                return;
            }

            // 1. 모든 컴포넌트에 DataSO 주입
            InjectData();

            // 2. SealStateManager 코어 오브젝트 연결
            _stateManager?.ConnectCore(_coreObject);

            // 3. SealEffectManager 코어 Transform 연결
            if (_coreObject != null)
                _effectManager?.SetCoreTransform(_coreObject.transform);

            // 4. SealStateManager 이벤트 구독 → AI / Feedback 브리지
            SubscribeStateEvents();

            Debug.Log("[BossWardenCore] v4.0 초기화 완료");
        }

        private void OnDestroy()
        {
            UnsubscribeStateEvents();
        }

        // ══════════════════════════════════════════════════════
        // DataSO 주입 — 단일 연결 지점
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenDataSO 를 모든 하위 컴포넌트에 주입.
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
        /// SealStateManager v2.0 이벤트 구독.
        /// Groggy 이벤트 없음.
        /// </summary>
        private void SubscribeStateEvents()
        {
            if (_stateManager == null) return;

            _stateManager.OnDilPhaseEnter += HandleDilPhaseEnter;
            _stateManager.OnDilPhaseExit += HandleDilPhaseExit;
            _stateManager.OnFinalSealReady += HandleFinalSealReady;
            _stateManager.OnPhaseChanged += HandlePhaseChanged;
            _stateManager.OnDead += HandleDead;
        }

        private void UnsubscribeStateEvents()
        {
            if (_stateManager == null) return;

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
        /// DilPhase 진입.
        /// AI 정지 + Feedback 딜페이즈 색상 + AttackRange HideAll + 속도 0.
        /// </summary>
        private void HandleDilPhaseEnter()
        {
            _ai?.OnDilPhaseEnter();
            _feedback?.OnDilPhaseEnter();
            _attackRange?.HideAll();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            OnDilPhaseEnter?.Invoke();

            Debug.Log("[BossWardenCore] ▶ DilPhase 진입 브리지");
        }

        /// <summary>
        /// DilPhase 종료.
        /// AI 재개 + Feedback Idle 복귀.
        /// </summary>
        private void HandleDilPhaseExit()
        {
            _ai?.OnDilPhaseExit();
            _feedback?.OnDilPhaseExit();

            OnDilPhaseExit?.Invoke();

            Debug.Log("[BossWardenCore] ■ DilPhase 종료 브리지");
        }

        /// <summary>
        /// FinalSeal 준비.
        /// Feedback 최종봉인 Pulse.
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

            OnPhaseChanged?.Invoke(newPhase);

            Debug.Log($"[BossWardenCore] ▶ 페이즈 {newPhase} 전환 브리지");
        }

        /// <summary>
        /// Dead.
        /// AI 비활성 + Feedback 처치 연출 + AttackRange 숨김
        /// + ExecutionRunner 강제 중단 + OnDead 발행.
        /// </summary>
        private void HandleDead()
        {
            _ai?.OnDead();
            _feedback?.OnDead();
            _attackRange?.HideAll();
            _executionRunner?.ForceStop();

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
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>현재 페이즈 반환. 외부 참조용.</summary>
        public int GetCurrentPhase() => CurrentPhase;

        // ══════════════════════════════════════════════════════
        // 디버그 — ContextMenu
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        [ContextMenu("DEBUG: DilPhase 강제 진입")]
        public void DEBUG_ForceDilPhase()
        {
            if (!Application.isPlaying) return;
            _stateManager?.ForceKill();
            Debug.Log("[BossWardenCore] DEBUG DilPhase 강제 진입");
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