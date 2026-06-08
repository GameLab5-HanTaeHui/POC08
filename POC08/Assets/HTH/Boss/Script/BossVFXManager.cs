// ============================================================
// BossVFXManager.cs  v1.0
// Boss_Warden VFX 중앙 Facade 관리자 — Step 9
//
// [역할]
//   현재 분산되어 있는 보스 시각 연출 컴포넌트들을 바로 삭제하지 않고,
//   BossVFXManager 하나를 중앙 진입점으로 세운다.
//
//   기존 구성:
//     BossWardenFeedback      — 본체/코어 상태 색상 피드백
//     BossWardenAttackRange   — 공격 예고 범위 표시
//     BossWardenShockwave     — 충격파 물리 + 시각 연출
//     SealEffectManager       — 봉인 시스템 월드 연출 / 코어 범위 / UI 중계
//
//   Step 9 구성:
//     BossVFXManager
//       → 위 컴포넌트들을 자동 탐색 / 보관 / 초기화 / 중앙 호출
//       → 이후 Step에서 실제 VFX 로직을 BossVFXManager 또는 하위 View로 단계적 흡수
//
// [Step 9 범위]
//   - VFX 관련 컴포넌트 참조 중앙화
//   - BossWardenDataSO / BossDataSO 주입 중앙화
//   - Core Transform 연결 중앙화
//   - BossWardenCore의 Feedback / AttackRange 직접 호출을 BossVFXManager 경유로 변경 가능
//   - 기존 VFX 로직은 유지
//
// [부착 위치]
//   Boss_Warden Root 오브젝트
//
// [namespace] SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 시각 연출 중앙 Facade 관리자.
    /// Step 9에서는 기존 VFX 컴포넌트를 제거하지 않고, 한곳에서 잡아주는 역할만 한다.
    /// </summary>
    [DefaultExecutionOrder(-6)]
    public class BossVFXManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector — Central Managers
        // ══════════════════════════════════════════════════════

        [Header("── Central Managers ──────────────────────")]

        [Tooltip("BossDataManager. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossDataManager _dataManager;

        [Tooltip("BossPartManager. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossPartManager _partManager;

        [Tooltip("BossEventHub. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossEventHub _eventHub;

        // ══════════════════════════════════════════════════════
        // Inspector — Legacy VFX Components
        // ══════════════════════════════════════════════════════

        [Header("── Legacy VFX Components ──────────────────────")]

        [Tooltip("본체/코어 상태 색상 피드백. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossWardenFeedback _feedback;

        [Tooltip("공격 예고 범위 표시. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        [Tooltip("충격파 연출/넉백. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private BossWardenShockwave _shockwave;

        [Tooltip("봉인 시스템 월드 연출. 미연결 시 같은 오브젝트에서 자동 탐색.")]
        [SerializeField] private SealEffectManager _sealEffectManager;

        [Header("── Core 연결 ──────────────────────")]

        [Tooltip("코어 Transform. 미연결 시 BossPartManager.CoreObject 사용.")]
        [SerializeField] private Transform _coreTransform;

        [Header("── EventHub Bridge ──────────────────────")]

        [Tooltip("EventHub의 VFX 요청 이벤트를 구독한다. 상태 이벤트는 Core에서 직접 호출하므로 기본적으로 구독하지 않는다.")]
        [SerializeField] private bool _listenToEventHubRequests = true;

        [Tooltip("디버그 로그 출력 여부.")]
        [SerializeField] private bool _debugLog;

        // ══════════════════════════════════════════════════════
        // Runtime
        // ══════════════════════════════════════════════════════

        private BossWardenDataSO _wardenData;
        private BossDataSO _bossData;
        private bool _initialized;
        private bool _subscribed;

        // ══════════════════════════════════════════════════════
        // Public Accessors
        // ══════════════════════════════════════════════════════

        public BossWardenFeedback Feedback => _feedback;
        public BossWardenAttackRange AttackRange => _attackRange;
        public BossWardenShockwave Shockwave => _shockwave;
        public SealEffectManager SealEffectManager => _sealEffectManager;
        public bool IsInitialized => _initialized;

        // ══════════════════════════════════════════════════════
        // Unity Lifecycle
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDestroy()
        {
            UnsubscribeEventHubRequests();
        }

        // ══════════════════════════════════════════════════════
        // Initialize
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenCore에서 호출하는 VFX 중앙 초기화 진입점.
        /// </summary>
        public void Initialize(BossWardenDataSO data, Transform coreTransform = null)
        {
            ResolveReferences();

            _wardenData = data;
            if (_wardenData == null && _dataManager != null)
                _wardenData = _dataManager.WardenData;

            _bossData = _wardenData != null ? _wardenData : _dataManager != null ? _dataManager.BossData : null;

            if (_wardenData == null || !_wardenData.IsValid())
            {
                Debug.LogError("[BossVFXManager] BossWardenDataSO 미연결 또는 유효하지 않음 — 초기화 중단.");
                return;
            }

            if (coreTransform != null)
                _coreTransform = coreTransform;
            else if (_coreTransform == null && _partManager != null && _partManager.CoreObject != null)
                _coreTransform = _partManager.CoreObject.transform;

            // 기존 VFX 컴포넌트 초기화 — 실제 연출 로직은 아직 각 컴포넌트가 유지한다.
            _feedback?.Initialize(_wardenData);
            _attackRange?.Initialize(_wardenData);
            _shockwave?.Initialize(_wardenData);
            _sealEffectManager?.Initialize(_bossData);

            ConnectCore(_coreTransform);

            if (_listenToEventHubRequests)
                SubscribeEventHubRequests();

            _initialized = true;

            if (_debugLog)
                Debug.Log("[BossVFXManager] 초기화 완료 — 기존 VFX 컴포넌트 중앙 Facade 구성");
        }

        /// <summary>
        /// 코어 Transform 연결을 중앙화한다.
        /// </summary>
        public void ConnectCore(Transform coreTransform)
        {
            if (coreTransform == null) return;

            _coreTransform = coreTransform;
            _sealEffectManager?.SetCoreTransform(_coreTransform);
        }

        // ══════════════════════════════════════════════════════
        // Public Facade API — 상태 연출
        // ══════════════════════════════════════════════════════

        public void OnDilPhaseEnter()
        {
            _feedback?.OnDilPhaseEnter();
            HideAllAttackRanges();
        }

        public void OnDilPhaseExit()
        {
            _feedback?.OnDilPhaseExit();
        }

        public void OnFinalSealReady()
        {
            _feedback?.OnFinalSealReady();
        }

        public void OnPhaseChanged(int newPhase)
        {
            _feedback?.OnPhaseChanged(newPhase);
        }

        public void OnDead()
        {
            _feedback?.OnDead();
            HideAllAttackRanges();
            _sealEffectManager?.HideAll();
        }

        // ══════════════════════════════════════════════════════
        // Public Facade API — 범위 / 충격파
        // ══════════════════════════════════════════════════════

        public void HideAllAttackRanges()
            => _attackRange?.HideAll();

        public void HideAllSealEffects()
            => _sealEffectManager?.HideAll();

        public void HideAll()
        {
            HideAllAttackRanges();
            HideAllSealEffects();
        }

        public void TriggerShockwave(Vector3 origin)
            => _shockwave?.Trigger(origin);

        // ══════════════════════════════════════════════════════
        // EventHub Request Bridge
        // ══════════════════════════════════════════════════════

        private void SubscribeEventHubRequests()
        {
            if (_subscribed || _eventHub == null) return;

            _eventHub.OnRequestShockwave += HandleRequestShockwave;
            _subscribed = true;
        }

        private void UnsubscribeEventHubRequests()
        {
            if (!_subscribed || _eventHub == null) return;

            _eventHub.OnRequestShockwave -= HandleRequestShockwave;
            _subscribed = false;
        }

        private void HandleRequestShockwave(Vector3 origin)
        {
            TriggerShockwave(origin);
        }

        // ══════════════════════════════════════════════════════
        // Reference Resolve
        // ══════════════════════════════════════════════════════

        private void ResolveReferences()
        {
            if (_dataManager == null) _dataManager = GetComponent<BossDataManager>();
            if (_partManager == null) _partManager = GetComponent<BossPartManager>();
            if (_eventHub == null) _eventHub = GetComponent<BossEventHub>();

            if (_feedback == null) _feedback = GetComponent<BossWardenFeedback>();
            if (_attackRange == null) _attackRange = GetComponent<BossWardenAttackRange>();
            if (_shockwave == null) _shockwave = GetComponent<BossWardenShockwave>();
            if (_sealEffectManager == null) _sealEffectManager = GetComponent<SealEffectManager>();

            if (_coreTransform == null && _partManager != null && _partManager.CoreObject != null)
                _coreTransform = _partManager.CoreObject.transform;
        }

#if UNITY_EDITOR
        [ContextMenu("DEBUG: VFX HideAll")]
        private void DEBUG_HideAll()
        {
            HideAll();
        }
#endif
    }
}
