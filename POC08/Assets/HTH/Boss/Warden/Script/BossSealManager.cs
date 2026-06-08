// ============================================================
// BossSealManager.cs  v1.0
// Boss_Warden 봉인 시스템 중앙 Facade 관리자 — Step 8
//
// [역할]
//   현재 분산되어 있는 봉인 관련 컴포넌트들을 바로 삭제하지 않고,
//   BossSealManager 하나를 중앙 진입점으로 세운다.
//
//   기존 구성:
//     SealStateManager
//     SealGaugeManager
//     SealManager
//     SealExecutionEvent
//     SealExecutionRunner
//     SealEffectManager
//     SealableComponent
//
//   Step 8 구성:
//     BossSealManager
//       → 위 컴포넌트들을 자동 탐색 / 보관 / 초기화 / 이벤트 중계
//       → 이후 Step에서 실제 로직을 BossSealManager로 단계적으로 흡수
//
// [Step 8 범위]
//   - 봉인 관련 컴포넌트 참조 중앙화
//   - BossDataSO 주입 중앙화
//   - Core 연결 중앙화
//   - SealableComponent 이벤트를 BossEventHub 봉인 이벤트로 중계
//   - 기존 SealStateManager / SealGaugeManager / SealExecutionRunner 로직은 유지
//
// [부착 위치]
//   Boss_Warden Root 오브젝트
//
// [namespace] SEAL
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 봉인 시스템 중앙 Facade 관리자.
    /// Step 8에서는 기존 봉인 컴포넌트를 제거하지 않고, 한곳에서 잡아주는 역할만 한다.
    /// </summary>
    [DefaultExecutionOrder(-7)]
    public class BossSealManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector — 중앙 Manager 연결
        // ══════════════════════════════════════════════════════

        [Header("── Central Managers ──────────────────────")]

        [Tooltip("BossDataManager. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossDataManager _dataManager;

        [Tooltip("BossPartManager. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossPartManager _partManager;

        [Tooltip("BossEventHub. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossEventHub _eventHub;

        // ══════════════════════════════════════════════════════
        // Inspector — 기존 봉인 컴포넌트 연결
        // ══════════════════════════════════════════════════════

        [Header("── Legacy Seal Components ──────────────────────")]

        [Tooltip("봉인 상태 관리자. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private SealStateManager _stateManager;

        [Tooltip("봉인도 조율 관리자. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private SealGaugeManager _gaugeManager;

        [Tooltip("봉인 규칙 관리자. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private SealManager _ruleManager;

        [Tooltip("집행 가능 대상 관리자. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private SealExecutionEvent _executionEvent;

        [Tooltip("봉인 집행 실행 관리자. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private SealExecutionRunner _executionRunner;

        [Tooltip("봉인 이펙트 관리자. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private SealEffectManager _effectManager;

        [Header("── Core 연결 ──────────────────────")]

        [Tooltip("코어 GameObject. 미연결 시 BossPartManager.CoreObject 사용.")]
        [SerializeField] private GameObject _coreObject;

        [Header("── Event Bridge ──────────────────────")]

        [Tooltip("SealableComponent 이벤트를 BossEventHub 봉인 이벤트로 중계한다.")]
        [SerializeField] private bool _bridgeSealableEventsToEventHub = true;

        [Tooltip("디버그 로그 출력 여부.")]
        [SerializeField] private bool _debugLog;

        // ══════════════════════════════════════════════════════
        // Runtime
        // ══════════════════════════════════════════════════════

        private BossDataSO _bossData;
        private bool _initialized;
        private bool _subscribed;

        private static readonly IReadOnlyList<SealableComponent> EmptySealables =
            new List<SealableComponent>().AsReadOnly();

        private readonly Dictionary<SealableComponent, Action<float>> _gaugeChangedHandlers = new();
        private readonly Dictionary<SealableComponent, Action> _sealCompletedHandlers = new();
        private readonly Dictionary<SealableComponent, Action> _forceReleasedHandlers = new();
        private readonly Dictionary<SealableComponent, Action> _phaseTargetHandlers = new();

        // ══════════════════════════════════════════════════════
        // Public Accessors
        // ══════════════════════════════════════════════════════

        public SealStateManager StateManager => _stateManager;
        public SealGaugeManager GaugeManager => _gaugeManager;
        public SealManager RuleManager => _ruleManager;
        public SealExecutionEvent ExecutionEvent => _executionEvent;
        public SealExecutionRunner ExecutionRunner => _executionRunner;
        public SealEffectManager EffectManager => _effectManager;

        public bool IsInitialized => _initialized;
        public bool IsDilPhase => _stateManager != null && _stateManager.IsDilPhase;
        public bool IsFinalSeal => _stateManager != null && _stateManager.IsFinalSeal;
        public bool IsDead => _stateManager != null && _stateManager.IsDead;
        public int CurrentPhase => _stateManager != null ? _stateManager.CurrentPhase : 1;

        // ══════════════════════════════════════════════════════
        // Unity Lifecycle
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
        }

        // ══════════════════════════════════════════════════════
        // Initialize
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenCore에서 호출하는 중앙 초기화 진입점.
        /// 기존 Seal 관련 컴포넌트들에 DataSO를 주입하고 Core 연결을 처리한다.
        /// </summary>
        public void Initialize(BossDataSO data, GameObject coreObject = null)
        {
            ResolveReferences();

            _bossData = data;
            if (_bossData == null && _dataManager != null)
                _bossData = _dataManager.BossData;

            if (_bossData == null || !_bossData.IsValid())
            {
                Debug.LogError("[BossSealManager] BossDataSO 미연결 또는 유효하지 않음 — 초기화 중단.");
                return;
            }

            if (coreObject != null)
                _coreObject = coreObject;
            else if (_coreObject == null && _partManager != null)
                _coreObject = _partManager.CoreObject;

            // 기존 봉인 컴포넌트 초기화 — 실제 로직은 아직 각 컴포넌트가 유지한다.
            _stateManager?.Initialize(_bossData);
            _gaugeManager?.Initialize(_bossData);
            _ruleManager?.Initialize(_bossData);
            _effectManager?.Initialize(_bossData);
            _executionRunner?.Initialize(_bossData);

            ConnectCore(_coreObject);

            if (_bridgeSealableEventsToEventHub)
                SubscribeEvents();

            _initialized = true;

            if (_debugLog)
                Debug.Log("[BossSealManager] 초기화 완료 — 기존 Seal 컴포넌트 중앙 Facade 구성");
        }

        /// <summary>
        /// 코어 오브젝트 연결을 중앙화한다.
        /// </summary>
        public void ConnectCore(GameObject coreObject)
        {
            if (coreObject == null) return;

            _coreObject = coreObject;
            _stateManager?.ConnectCore(_coreObject);
            _effectManager?.SetCoreTransform(_coreObject.transform);
        }

        // ══════════════════════════════════════════════════════
        // Public Facade API
        // ══════════════════════════════════════════════════════

        public void ReleaseAllParts(bool resetSealCount = false)
            => _gaugeManager?.ReleaseAllParts(resetSealCount);

        public void ReleaseAll(bool resetSealCount = false)
            => _gaugeManager?.ReleaseAll(resetSealCount);

        public void ActivateCore(bool isActive)
            => _gaugeManager?.ActivateCore(isActive);

        public bool AreAllPartsSealed()
            => _gaugeManager != null && _gaugeManager.AreAllPartsSealed();

        public int GetSealedCount(SealGrade grade)
            => _gaugeManager != null ? _gaugeManager.GetSealedCount(grade) : 0;

        public int GetPartCount()
            => _gaugeManager != null ? _gaugeManager.GetPartCount() : 0;

        public SealableComponent GetSealable(string objectName)
            => _gaugeManager != null ? _gaugeManager.GetSealable(objectName) : null;

        public IReadOnlyList<SealableComponent> GetAllSealables()
            => _gaugeManager != null ? _gaugeManager.GetAllSealables() : EmptySealables;

        public SealableComponent GetBestExecutionTarget(Vector2 playerPosition)
            => _executionEvent != null ? _executionEvent.GetBestTarget(playerPosition) : null;

        public int GetReadyExecutionTargetCount()
            => _executionEvent != null ? _executionEvent.GetReadyCount() : 0;

        public void ForceStopExecution()
            => _executionRunner?.ForceStop();

        public void ForceKill()
            => _stateManager?.ForceKill();

        // ══════════════════════════════════════════════════════
        // Reference Resolve
        // ══════════════════════════════════════════════════════

        private void ResolveReferences()
        {
            if (_dataManager == null) _dataManager = GetComponent<BossDataManager>();
            if (_partManager == null) _partManager = GetComponent<BossPartManager>();
            if (_eventHub == null) _eventHub = GetComponent<BossEventHub>();

            if (_stateManager == null) _stateManager = GetComponent<SealStateManager>();
            if (_gaugeManager == null) _gaugeManager = GetComponent<SealGaugeManager>();
            if (_ruleManager == null) _ruleManager = GetComponent<SealManager>();
            if (_executionEvent == null) _executionEvent = GetComponent<SealExecutionEvent>();
            if (_executionRunner == null) _executionRunner = GetComponent<SealExecutionRunner>();
            if (_effectManager == null) _effectManager = GetComponent<SealEffectManager>();

            if (_coreObject == null && _partManager != null)
                _coreObject = _partManager.CoreObject;
        }

        // ══════════════════════════════════════════════════════
        // Event Bridge
        // ══════════════════════════════════════════════════════

        private void SubscribeEvents()
        {
            if (_subscribed) return;
            if (_gaugeManager == null) return;

            _gaugeManager.OnAllPartsSealed -= HandleAllPartsSealed;
            _gaugeManager.OnAllPartsSealed += HandleAllPartsSealed;
            _gaugeManager.OnAllPartsReleased -= HandleAllPartsReleased;
            _gaugeManager.OnAllPartsReleased += HandleAllPartsReleased;

            foreach (var sealable in _gaugeManager.GetAllSealables())
            {
                SubscribeSealable(sealable);
            }

            _subscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (_gaugeManager != null)
            {
                _gaugeManager.OnAllPartsSealed -= HandleAllPartsSealed;
                _gaugeManager.OnAllPartsReleased -= HandleAllPartsReleased;
            }

            foreach (var pair in _gaugeChangedHandlers)
            {
                if (pair.Key != null)
                    pair.Key.OnGaugeChanged -= pair.Value;
            }
            foreach (var pair in _sealCompletedHandlers)
            {
                if (pair.Key != null)
                    pair.Key.OnSealCompleted -= pair.Value;
            }
            foreach (var pair in _forceReleasedHandlers)
            {
                if (pair.Key != null)
                    pair.Key.OnForceReleased -= pair.Value;
            }
            foreach (var pair in _phaseTargetHandlers)
            {
                if (pair.Key != null)
                    pair.Key.OnPhaseTargetReached -= pair.Value;
            }

            foreach (var sealable in _gaugeChangedHandlers.Keys)
            {
                if (sealable != null)
                    sealable.OnSealRequested -= HandleSealRequested;
            }

            _gaugeChangedHandlers.Clear();
            _sealCompletedHandlers.Clear();
            _forceReleasedHandlers.Clear();
            _phaseTargetHandlers.Clear();
            _subscribed = false;
        }

        private void SubscribeSealable(SealableComponent sealable)
        {
            if (sealable == null) return;
            if (_gaugeChangedHandlers.ContainsKey(sealable)) return;

            Action<float> gaugeChanged = _ => HandleGaugeChanged(sealable);
            Action sealCompleted = () => HandleSealCompleted(sealable);
            Action forceReleased = () => HandleForceReleased(sealable);
            Action phaseTargetReached = () => HandlePhaseTargetReached(sealable);

            _gaugeChangedHandlers.Add(sealable, gaugeChanged);
            _sealCompletedHandlers.Add(sealable, sealCompleted);
            _forceReleasedHandlers.Add(sealable, forceReleased);
            _phaseTargetHandlers.Add(sealable, phaseTargetReached);

            sealable.OnGaugeChanged += gaugeChanged;
            sealable.OnSealRequested += HandleSealRequested;
            sealable.OnSealCompleted += sealCompleted;
            sealable.OnForceReleased += forceReleased;
            sealable.OnPhaseTargetReached += phaseTargetReached;
        }

        private void HandleAllPartsSealed()
        {
            _eventHub?.RaiseAllPartsSealed();
            if (_debugLog) Debug.Log("[BossSealManager] EventHub: OnAllPartsSealed 발행");
        }

        private void HandleAllPartsReleased()
        {
            RaiseReleasedForPart(WardenPartType.LeftArm);
            RaiseReleasedForPart(WardenPartType.RightArm);
            if (_debugLog) Debug.Log("[BossSealManager] EventHub: Part Release 이벤트 발행");
        }

        private void HandleSealRequested(SealableComponent sealable)
        {
            if (!TryGetPartType(sealable, out var partType)) return;
            _eventHub?.RaiseSealReady(partType);
        }

        private void HandleGaugeChanged(SealableComponent sealable)
        {
            if (sealable == null) return;

            if (sealable.Grade == SealGrade.Core)
            {
                _eventHub?.RaiseCoreSealGaugeChanged(sealable.UIPercent, 100f);
                return;
            }

            if (TryGetPartType(sealable, out var partType))
                _eventHub?.RaiseSealGaugeChanged(partType, sealable.UIPercent, 100f);
        }

        private void HandleSealCompleted(SealableComponent sealable)
        {
            if (sealable == null) return;

            if (sealable.Grade == SealGrade.Core)
            {
                _eventHub?.RaiseCoreSealCompleted();
                return;
            }

            if (TryGetPartType(sealable, out var partType))
                _eventHub?.RaiseSealExecuted(partType);
        }

        private void HandleForceReleased(SealableComponent sealable)
        {
            if (TryGetPartType(sealable, out var partType))
                RaiseReleasedForPart(partType);
        }

        private void HandlePhaseTargetReached(SealableComponent sealable)
        {
            if (sealable != null && sealable.Grade == SealGrade.Core)
                _eventHub?.RaiseCoreSealCompleted();
        }

        private void RaiseReleasedForPart(WardenPartType partType)
        {
            _eventHub?.RaiseSealReleased(partType);
        }

        private bool TryGetPartType(SealableComponent sealable, out WardenPartType partType)
        {
            partType = WardenPartType.LeftArm;
            if (sealable == null) return false;

            if (_partManager != null)
            {
                if (_partManager.LeftArmSealable == sealable)
                {
                    partType = WardenPartType.LeftArm;
                    return true;
                }
                if (_partManager.RightArmSealable == sealable)
                {
                    partType = WardenPartType.RightArm;
                    return true;
                }
                if (_partManager.CoreSealable == sealable)
                {
                    partType = WardenPartType.Core;
                    return true;
                }
            }

            if (sealable.Grade == SealGrade.Core)
            {
                partType = WardenPartType.Core;
                return true;
            }

            string lowerName = sealable.gameObject.name.ToLowerInvariant();
            if (lowerName.Contains("right"))
            {
                partType = WardenPartType.RightArm;
                return true;
            }
            if (lowerName.Contains("left"))
            {
                partType = WardenPartType.LeftArm;
                return true;
            }

            return false;
        }
    }
}
