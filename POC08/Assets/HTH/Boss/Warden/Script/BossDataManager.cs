// ============================================================
// BossDataManager.cs v3.1
// Boss 데이터 + 보스 생명주기 부트스트랩 관리자 — Step 17
//
// [Step 20]
//   BossPartManager의 4팔 구조에 맞춰 모든 팔 파츠를 일괄 초기화한다.
//   BossWardenCore/BossWardenAI 없이 BossDataManager가 초기화 진입점이다.
// ============================================================

using System;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 중앙 DataSO 관리자 + 런타임 부트스트랩.
    /// Step18부터 BossWardenCore/BossWardenAI 없이 이 컴포넌트가 초기화 진입점이 된다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class BossDataManager : MonoBehaviour, IBossCore
    {
        // ══════════════════════════════════════════════════════
        // Inspector — DataSO
        // ══════════════════════════════════════════════════════

        [Header("── Warden DataSO ──────────────────────")]
        [Tooltip("Boss_Warden 전용 DataSO. SealData / ColorData / PatternData를 포함한다.")]
        [SerializeField] private BossWardenDataSO _wardenData;

        [Header("── Central Managers ──────────────────────")]
        [SerializeField] private BossPartManager _partManager;
        [SerializeField] private BossSealManager _sealManager;
        [SerializeField] private BossVFXManager _vfxManager;
        [SerializeField] private BossEventHub _eventHub;
        [SerializeField] private BossAttackManager _attackManager;
        [SerializeField] private BossHitManager _hitManager;

        [Header("── Runtime Components ──────────────────────")]
        [Tooltip("보스 Rigidbody2D. DilPhase/Dead 진입 시 즉시 정지용.")]
        [SerializeField] private Rigidbody2D _rigid2D;

        [Header("── Options ──────────────────────")]
        [SerializeField] private bool _autoResolveReferences = true;
        [SerializeField] private bool _initializeOnStart = true;
        [SerializeField] private bool _debugLog;

        // ══════════════════════════════════════════════════════
        // Runtime Cache
        // ══════════════════════════════════════════════════════

        private readonly System.Collections.Generic.List<BossWardenPart> _armParts = new();
        private BossWardenPart _corePart;
        private GameObject _coreObject;
        private bool _initialized;
        private bool _sealEventsSubscribed;

        // ══════════════════════════════════════════════════════
        // IBossCore Events
        // ══════════════════════════════════════════════════════

        public event Action OnDilPhaseEnter;
        public event Action OnDilPhaseExit;
        public event Action OnDead;

        // 추가 이벤트 — 기존 BossWardenCore 호환용
        public event Action<int> OnPhaseChanged;
        public event Action OnFinalSealReady;

        // ══════════════════════════════════════════════════════
        // Public Accessors
        // ══════════════════════════════════════════════════════

        public BossWardenDataSO WardenData => _wardenData;
        public BossDataSO BossData => _wardenData;
        public SealDataSO SealData => _wardenData != null ? _wardenData.SealData : null;
        public SealColorDataSO ColorData => _wardenData != null ? _wardenData.ColorData : null;
        public BossWardenPatternDataSO PatternData => _wardenData != null ? _wardenData.PatternData : null;

        public BossPartManager PartManager => _partManager;
        public BossSealManager SealManager => _sealManager;
        public BossVFXManager VFXManager => _vfxManager;
        public BossEventHub EventHub => _eventHub;
        public BossAttackManager AttackManager => _attackManager;
        public BossHitManager HitManager => _hitManager;
        public bool IsInitialized => _initialized;
        public bool IsDilPhase => _sealManager != null && _sealManager.IsDilPhase;
        public bool IsDead => _sealManager != null && _sealManager.IsDead;
        public bool IsFinalSeal => _sealManager != null && _sealManager.IsFinalSeal;
        public int CurrentPhase => _sealManager != null ? _sealManager.CurrentPhase : 1;

        // ══════════════════════════════════════════════════════
        // Unity Lifecycle
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            if (_initializeOnStart)
                InitializeBoss();
        }

        private void OnDestroy()
        {
            UnsubscribeSealEvents();
        }

        // ══════════════════════════════════════════════════════
        // Initialization
        // ══════════════════════════════════════════════════════

        [ContextMenu("Initialize Boss")]
        public void InitializeBoss()
        {
            if (_initialized) return;

            ResolveReferences();

            if (!IsValid() || !ValidateRequiredManagers() || !ResolveParts())
            {
                enabled = false;
                return;
            }

            InjectDataToManagers();
            SubscribeSealEvents();

            _initialized = true;

            if (_debugLog)
                Debug.Log($"[BossDataManager] Step20 초기화 완료 — ArmParts:{_armParts.Count}");
        }

        private void ResolveReferences()
        {
            if (!_autoResolveReferences) return;

            Transform root = transform.root;

            if (_partManager == null) _partManager = root.GetComponentInChildren<BossPartManager>(true);
            if (_sealManager == null) _sealManager = root.GetComponentInChildren<BossSealManager>(true);
            if (_vfxManager == null) _vfxManager = root.GetComponentInChildren<BossVFXManager>(true);
            if (_eventHub == null) _eventHub = root.GetComponentInChildren<BossEventHub>(true);
            if (_attackManager == null) _attackManager = root.GetComponentInChildren<BossAttackManager>(true);
            if (_hitManager == null) _hitManager = root.GetComponentInChildren<BossHitManager>(true);
            if (_rigid2D == null) _rigid2D = root.GetComponentInChildren<Rigidbody2D>(true);
        }

        private bool ValidateRequiredManagers()
        {
            bool valid = true;

            if (_partManager == null) { Debug.LogError("[BossDataManager] BossPartManager 미연결."); valid = false; }
            if (_sealManager == null) { Debug.LogError("[BossDataManager] BossSealManager 미연결."); valid = false; }
            if (_vfxManager == null) { Debug.LogError("[BossDataManager] BossVFXManager 미연결."); valid = false; }
            if (_eventHub == null) { Debug.LogError("[BossDataManager] BossEventHub 미연결."); valid = false; }
            if (_attackManager == null) { Debug.LogError("[BossDataManager] BossAttackManager 미연결."); valid = false; }
            if (_hitManager == null) { Debug.LogError("[BossDataManager] BossHitManager 미연결."); valid = false; }

            return valid;
        }

        private bool ResolveParts()
        {
            if (_partManager == null || !_partManager.IsValid())
            {
                Debug.LogError("[BossDataManager] BossPartManager 필수 참조 누락.");
                return false;
            }

            _armParts.Clear();
            foreach (var part in _partManager.GetAllArmParts())
            {
                if (part != null && !_armParts.Contains(part))
                    _armParts.Add(part);
            }

            _corePart = _partManager.CorePart;
            _coreObject = _partManager.CoreObject;

            if (_armParts.Count <= 0 || _corePart == null || _coreObject == null)
            {
                Debug.LogError("[BossDataManager] 팔/코어 참조를 모두 가져오지 못했습니다.");
                return false;
            }

            return true;
        }

        private void InjectDataToManagers()
        {
            // 기존 BossWardenCore.InjectData() 역할 흡수
            _sealManager.Initialize(_wardenData, _coreObject);
            _attackManager.Initialize(_wardenData);
            _vfxManager.Initialize(_wardenData, _coreObject != null ? _coreObject.transform : null);

            foreach (var part in _armParts)
                part?.Initialize(_wardenData);

            _corePart.Initialize(_wardenData);
        }

        // ══════════════════════════════════════════════════════
        // Seal Event Bridge — 기존 BossWardenCore 역할 흡수
        // ══════════════════════════════════════════════════════

        private void SubscribeSealEvents()
        {
            if (_sealEventsSubscribed || _sealManager == null) return;

            _sealManager.OnDilPhaseEnter += HandleDilPhaseEnter;
            _sealManager.OnDilPhaseExit += HandleDilPhaseExit;
            _sealManager.OnFinalSealReady += HandleFinalSealReady;
            _sealManager.OnPhaseChanged += HandlePhaseChanged;
            _sealManager.OnDead += HandleDead;

            _sealEventsSubscribed = true;
        }

        private void UnsubscribeSealEvents()
        {
            if (!_sealEventsSubscribed || _sealManager == null) return;

            _sealManager.OnDilPhaseEnter -= HandleDilPhaseEnter;
            _sealManager.OnDilPhaseExit -= HandleDilPhaseExit;
            _sealManager.OnFinalSealReady -= HandleFinalSealReady;
            _sealManager.OnPhaseChanged -= HandlePhaseChanged;
            _sealManager.OnDead -= HandleDead;

            _sealEventsSubscribed = false;
        }

        private void HandleDilPhaseEnter()
        {
            _attackManager?.OnDilPhaseEnter();
            _vfxManager?.OnDilPhaseEnter();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            OnDilPhaseEnter?.Invoke();
        }

        private void HandleDilPhaseExit()
        {
            _attackManager?.OnDilPhaseExit();
            _vfxManager?.OnDilPhaseExit();
            OnDilPhaseExit?.Invoke();
        }

        private void HandleFinalSealReady()
        {
            _vfxManager?.OnFinalSealReady();
            OnFinalSealReady?.Invoke();
        }

        private void HandlePhaseChanged(int newPhase)
        {
            _attackManager?.OnPhaseChanged(newPhase);
            _vfxManager?.OnPhaseChanged(newPhase);
            OnPhaseChanged?.Invoke(newPhase);
        }

        private void HandleDead()
        {
            _attackManager?.OnDead();
            _vfxManager?.OnDead();
            OnDead?.Invoke();
        }

        // ══════════════════════════════════════════════════════
        // Validation
        // ══════════════════════════════════════════════════════

        public bool IsValid()
        {
            if (_wardenData == null)
            {
                Debug.LogError($"[BossDataManager] {name} — BossWardenDataSO 미연결.");
                return false;
            }

            return _wardenData.IsValid();
        }

        public int GetCurrentPhase() => CurrentPhase;

#if UNITY_EDITOR
        [ContextMenu("DEBUG: DilPhase 강제 진입")]
        public void DEBUG_ForceDilPhase()
        {
            if (!Application.isPlaying) return;
            _sealManager?.ForceEnterDilPhase();
        }

        [ContextMenu("DEBUG: ForceKill")]
        public void DEBUG_ForceKill()
        {
            if (!Application.isPlaying) return;
            _sealManager?.ForceKill();
        }

        private void OnValidate()
        {
            // DataSO는 명시 연결을 권장한다.
        }
#endif
    }
}
