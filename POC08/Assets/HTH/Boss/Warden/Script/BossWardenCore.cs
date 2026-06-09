// ============================================================
// BossWardenCore.cs  v5.0
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
// [BossWardenCore v5.0 역할 — Legacy fallback 제거]
//   1. BossDataManager에서 BossWardenDataSO 수신
//   2. BossPartManager에서 LeftArm / RightArm / Core 참조 수신
//   3. BossEventHub로 주요 상태 이벤트 발행
//   4. BossSealManager를 통한 봉인 시스템 초기화 중앙화
//   5. BossVFXManager를 통한 VFX 시스템 초기화/호출 중앙화
//   6. 모든 컴포넌트에 DataSO 주입
//   7. SealStateManager 이벤트 → AI / VFX 브리지
//   8. IBossCore v2.0 구현 (BattleManager 연동)
//   9. 신버전 Manager 기준 필수 연결 검증
//
// [v5.0 변경]
//   - Legacy DataSO 직접 연결 제거
//   - Legacy 부위 직접 연결 제거
//   - BossSealManager / BossVFXManager 미연결 시 구버전 직접 호출 fallback 제거
//   - Manager 누락 시 초기화 중단
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

        [Header("── Data Manager (권장) ──────────────────────")]

        /// <summary>
        /// Step 3에서 추가된 DataSO 중앙 관리자.
        /// BossWardenDataSO / SealDataSO / SealColorDataSO / PatternDataSO 접근을 한곳으로 모은다.
        /// </summary>
        [Tooltip("BossDataManager. 권장 연결. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossDataManager _dataManager;

        [Header("── Part Manager (권장) ──────────────────────")]

        /// <summary>
        /// Step 4에서 추가된 부위 참조 중앙 관리자.
        /// LeftArm / RightArm / Core 참조를 한곳에서 제공한다.
        /// </summary>
        [Tooltip("BossPartManager. 권장 연결. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossPartManager _partManager;


        [Header("── Seal Manager (권장) ──────────────────────")]

        /// <summary>
        /// Step 8에서 추가된 봉인 시스템 중앙 Facade 관리자.
        /// 기존 SealStateManager / SealGaugeManager / SealExecutionRunner 등을 한곳에서 초기화한다.
        /// </summary>
        [Tooltip("BossSealManager. 권장 연결. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossSealManager _sealManager;

        [Header("── VFX Manager (권장) ──────────────────────")]

        /// <summary>
        /// Step 9에서 추가된 VFX 중앙 Facade 관리자.
        /// 기존 BossWardenFeedback / BossWardenAttackRange / BossWardenShockwave / SealEffectManager를 한곳에서 초기화/호출한다.
        /// </summary>
        [Tooltip("BossVFXManager. 권장 연결. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossVFXManager _vfxManager;

        [Header("── Event Hub (권장) ──────────────────────")]

        /// <summary>
        /// Step 5에서 추가된 중앙 이벤트 허브.
        /// Core가 기존 C# event를 유지하면서, 동시에 EventHub에도 상태 이벤트를 발행한다.
        /// </summary>
        [Tooltip("BossEventHub. 권장 연결. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossEventHub _eventHub;

        // ══════════════════════════════════════════════════════
        // Runtime Cache — Manager에서 확정된 값
        // ══════════════════════════════════════════════════════

        /// <summary>BossDataManager에서 수신한 Warden DataSO.</summary>
        private BossWardenDataSO _data;

        /// <summary>BossPartManager에서 수신한 왼팔 부위.</summary>
        private BossWardenPart _armL;

        /// <summary>BossPartManager에서 수신한 오른팔 부위.</summary>
        private BossWardenPart _armR;

        /// <summary>BossPartManager에서 수신한 코어 GameObject.</summary>
        private GameObject _coreObject;

        /// <summary>BossPartManager에서 수신한 코어 부위.</summary>
        private BossWardenPart _corePart;

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

        /// <summary>Step 5 중앙 이벤트 허브 접근용.</summary>
        public BossEventHub EventHub => _eventHub;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // 신버전 Manager 기준 탐색.
            // 같은 오브젝트 또는 Manager 자식 오브젝트에 있어도 찾되,
            // 구버전 Data/Part 직접 연결 fallback은 사용하지 않는다.
            if (_dataManager == null) _dataManager = GetComponentInChildren<BossDataManager>(true);
            if (_partManager == null) _partManager = GetComponentInChildren<BossPartManager>(true);
            if (_sealManager == null) _sealManager = GetComponentInChildren<BossSealManager>(true);
            if (_vfxManager == null) _vfxManager = GetComponentInChildren<BossVFXManager>(true);
            if (_eventHub == null) _eventHub = GetComponentInChildren<BossEventHub>(true);

            _ai = GetComponent<BossWardenAI>();
            _rigid2D = GetComponent<Rigidbody2D>();

            ValidateRequiredManagers();
        }

        private void Start()
        {
            if (!ValidateRequiredManagers())
            {
                enabled = false;
                return;
            }

            if (!ResolveData())
            {
                enabled = false;
                return;
            }

            if (!ResolveParts())
            {
                enabled = false;
                return;
            }

            InjectData();

            _vfxManager.ConnectCore(_coreObject.transform);

            SubscribeStateEvents();

            Debug.Log("[BossWardenCore] v5.0 초기화 완료 — 신버전 Manager 전용 / Legacy fallback 제거");
        }

        private void OnDestroy()
        {
            UnsubscribeStateEvents();
        }

        // ══════════════════════════════════════════════════════
        // DataSO Resolve — Step 3 BossDataManager 연결
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossDataManager를 통해서만 BossWardenDataSO를 확정한다.
        /// Legacy _data 직접 연결 fallback은 사용하지 않는다.
        /// </summary>
        private bool ResolveData()
        {
            if (_dataManager == null)
            {
                Debug.LogError("[BossWardenCore] BossDataManager 미연결 — 신버전 구조에서는 필수입니다.");
                return false;
            }

            _data = _dataManager.WardenData;

            if (_data == null || !_data.IsValid())
            {
                Debug.LogError("[BossWardenCore] BossDataManager.WardenData 미연결 또는 유효하지 않음 — 초기화 중단.");
                return false;
            }

            return true;
        }

        // ══════════════════════════════════════════════════════
        // Part Resolve — Step 4 BossPartManager 연결
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossPartManager를 통해서만 LeftArm / RightArm / Core 참조를 확정한다.
        /// Legacy Inspector 직접 연결과 혼합하지 않는다.
        /// </summary>
        private bool ResolveParts()
        {
            if (_partManager == null)
            {
                Debug.LogError("[BossWardenCore] BossPartManager 미연결 — 신버전 구조에서는 필수입니다.");
                return false;
            }

            if (!_partManager.IsValid())
            {
                Debug.LogError("[BossWardenCore] BossPartManager 필수 참조 누락 — LeftArm / RightArm / CoreObject 연결을 확인하세요.");
                return false;
            }

            _armL = _partManager.LeftArm;
            _armR = _partManager.RightArm;
            _corePart = _partManager.CorePart;
            _coreObject = _partManager.CoreObject;

            if (_armL == null || _armR == null || _corePart == null || _coreObject == null)
            {
                Debug.LogError("[BossWardenCore] BossPartManager에서 부위 참조를 모두 가져오지 못했습니다.");
                return false;
            }

            return true;
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
            _sealManager.Initialize(_data, _coreObject);

            if (_sealManager.StateManager != null) _stateManager = _sealManager.StateManager;
            if (_sealManager.GaugeManager != null) _gaugeManager = _sealManager.GaugeManager;
            if (_sealManager.EffectManager != null) _effectManager = _sealManager.EffectManager;
            if (_sealManager.ExecutionRunner != null) _executionRunner = _sealManager.ExecutionRunner;

            _ai?.Initialize(_data);

            Transform coreTransform = _coreObject != null ? _coreObject.transform : null;
            _vfxManager.Initialize(_data, coreTransform);

            if (_vfxManager.Feedback != null) _feedback = _vfxManager.Feedback;
            if (_vfxManager.AttackRange != null) _attackRange = _vfxManager.AttackRange;
            if (_vfxManager.Shockwave != null) _shockwave = _vfxManager.Shockwave;
            if (_vfxManager.SealEffectManager != null) _effectManager = _vfxManager.SealEffectManager;

            _armL.Initialize(_data);
            _armR.Initialize(_data);
            _corePart.Initialize(_data);

            Debug.Log("[BossWardenCore] DataSO 전체 주입 완료 — Manager 전용");
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
            _vfxManager.OnDilPhaseEnter();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            OnDilPhaseEnter?.Invoke();
            _eventHub?.RaiseDilPhaseEnter();
            _eventHub?.RaiseBossStateChanged(BossEventHub.BossRuntimeState.DilPhase);

            Debug.Log("[BossWardenCore] ▶ DilPhase 진입 브리지 + EventHub 발행");
        }

        /// <summary>
        /// DilPhase 종료.
        /// AI 재개 + Feedback Idle 복귀.
        /// </summary>
        private void HandleDilPhaseExit()
        {
            _ai?.OnDilPhaseExit();
            _vfxManager.OnDilPhaseExit();

            OnDilPhaseExit?.Invoke();
            _eventHub?.RaiseDilPhaseExit();
            _eventHub?.RaiseBossStateChanged(BossEventHub.BossRuntimeState.Idle);

            Debug.Log("[BossWardenCore] ■ DilPhase 종료 브리지 + EventHub 발행");
        }

        /// <summary>
        /// FinalSeal 준비.
        /// Feedback 최종봉인 Pulse.
        /// </summary>
        private void HandleFinalSealReady()
        {
            _vfxManager.OnFinalSealReady();

            OnFinalSealReady?.Invoke();
            _eventHub?.RaiseFinalSealReady();
            _eventHub?.RaiseBossStateChanged(BossEventHub.BossRuntimeState.FinalSeal);

            Debug.Log("[BossWardenCore] ▶ FinalSeal 준비 브리지 + EventHub 발행");
        }

        /// <summary>
        /// 페이즈 전환.
        /// AI 2페이즈 강화 + Feedback 페이즈 전환 연출.
        /// </summary>
        private void HandlePhaseChanged(int newPhase)
        {
            _ai?.OnPhaseChanged(newPhase);
            _vfxManager.OnPhaseChanged(newPhase);

            OnPhaseChanged?.Invoke(newPhase);
            _eventHub?.RaisePhaseChanged(newPhase);

            Debug.Log($"[BossWardenCore] ▶ 페이즈 {newPhase} 전환 브리지 + EventHub 발행");
        }

        /// <summary>
        /// Dead.
        /// AI 비활성 + Feedback 처치 연출 + AttackRange 숨김
        /// + ExecutionRunner 강제 중단 + OnDead 발행.
        /// </summary>
        private void HandleDead()
        {
            _ai?.OnDead();
            _vfxManager.OnDead();
            _sealManager.ForceStopExecution();

            OnDead?.Invoke();
            _eventHub?.RaiseDead();
            _eventHub?.RaiseBossStateChanged(BossEventHub.BossRuntimeState.Dead);

            Debug.Log("[BossWardenCore] ✅ Dead 브리지 + OnDead + EventHub 발행");
        }

        // ══════════════════════════════════════════════════════
        // 유효성 검사
        // ══════════════════════════════════════════════════════

        private bool ValidateRequiredManagers()
        {
            bool valid = true;

            if (_dataManager == null)
            {
                Debug.LogError("[BossWardenCore] BossDataManager 미연결.");
                valid = false;
            }

            if (_partManager == null)
            {
                Debug.LogError("[BossWardenCore] BossPartManager 미연결.");
                valid = false;
            }

            if (_sealManager == null)
            {
                Debug.LogError("[BossWardenCore] BossSealManager 미연결.");
                valid = false;
            }

            if (_vfxManager == null)
            {
                Debug.LogError("[BossWardenCore] BossVFXManager 미연결.");
                valid = false;
            }

            if (_eventHub == null)
            {
                Debug.LogError("[BossWardenCore] BossEventHub 미연결.");
                valid = false;
            }

            if (_ai == null)
                Debug.LogWarning("[BossWardenCore] BossWardenAI 미연결.");

            return valid;
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