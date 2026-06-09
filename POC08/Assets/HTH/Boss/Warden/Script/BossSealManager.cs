// ============================================================
// BossSealManager.cs v2.1
// Boss_Warden 봉인 시스템 완전 통합 관리자 — Step 15
//
// [Step 20]
//   4팔 구조 대응. BossPartManager.GetAllSealables() 기준으로
//   LeftArm_01/02, RightArm_01/02, Core를 모두 수집한다.
//
// [Step 21]
//   DummyEnemy의 자동 일섬 집행을 보스 파츠/코어 대상으로 통합한다.
//
// [Step 31]
//   Core 봉인도는 DilPhase 재개방/실패 시 초기화하지 않고 누적 유지한다.
//   FinalSeal 진입 시 Core를 Ready 대상으로 강제 등록해 최종 집행 가능하게 한다.
//
// [Step 32]
//   Core 100% 도달 시 불릿타임을 즉시 시작하지 않는다.
//   Core 봉인 집행 입력/일섬 도착 후 ExecuteSeal 시점에만 최종 불릿타임이 시작된다.
//
// [유지되는 부위 단위 컴포넌트]
//   SealableComponent
//   SealReadyNotifier
//   SealExecutionEffect
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    [DefaultExecutionOrder(-7)]
    public class BossSealManager : MonoBehaviour
    {
        public enum BossSealState
        {
            Idle,
            DilPhase,
            FinalSeal,
            Dead,
        }

        [Header("── Central Managers ──────────────────────")]
        [SerializeField] private BossDataManager _dataManager;
        [SerializeField] private BossPartManager _partManager;
        [SerializeField] private BossEventHub _eventHub;
        [SerializeField] private BossVFXManager _vfxManager;
        [SerializeField] private BossAttackManager _attackManager;

        [Header("── Core ──────────────────────")]
        [SerializeField] private GameObject _coreObject;

        [Header("── Player Input ──────────────────────")]
        [SerializeField] private PlayerInputHandler _input;
        [SerializeField] private Transform _playerTransform;
        [SerializeField] private PlayerController _playerController;
        [SerializeField] private PlayerMoveController _playerMoveController;
        [SerializeField] private PlayerAttackController _playerAttackController;
        [SerializeField] private Rigidbody2D _playerRigid2D;

        [Header("── 보스 일섬 집행 ──────────────────────")]
        [Tooltip("true면 봉인 입력 시 플레이어가 대상 ExecutionPoint로 이동한 뒤 집행합니다.")]
        [SerializeField] private bool _useIssenExecution = true;

        [Tooltip("true면 집행 완료 후 주변 Ready 대상에게 자동 연쇄합니다.")]
        [SerializeField] private bool _enableIssenChain = true;

        [Tooltip("일섬 집행 중 플레이어 이동/공격/대시 입력을 차단합니다.")]
        [SerializeField] private bool _blockPlayerInputDuringIssen = true;

        [Tooltip("일섬 집행 중 PlayerController를 Seal 상태로 잠급니다.")]
        [SerializeField] private bool _lockPlayerStateDuringIssen = true;

        [Tooltip("일섬 집행 중 보스 AI/패턴을 일시 정지합니다.")]
        [SerializeField] private bool _pauseBossAttackDuringIssen = true;

        [Tooltip("선택: 일섬 연결 고리 미리보기 LineRenderer.")]
        [SerializeField] private LineRenderer _issenChainPreviewLine;

        [Min(0.1f)][SerializeField] private float _issenMoveSpeed = 28f;
        [Min(0.01f)][SerializeField] private float _issenArriveDistance = 0.08f;
        [Min(0f)][SerializeField] private float _issenStopOffset = 0.15f;
        [Min(0.05f)][SerializeField] private float _issenMaxTravelTime = 0.8f;
        [Min(0.1f)][SerializeField] private float _issenChainRadius = 8.5f;
        [Min(0f)][SerializeField] private float _issenChainInterval = 0.04f;
        [Tooltip("0 이하이면 무제한.")]
        [SerializeField] private int _issenMaxChainCount = 12;

        [Header("── Options ──────────────────────")]
        [SerializeField] private bool _autoResolveReferences = true;
        [SerializeField] private bool _coreStartsInactive = true;
        [SerializeField] private bool _debugLog;

        private BossDataSO _bossData;
        private BossWardenDataSO _wardenData;

        private readonly List<SealableComponent> _allSealables = new();
        private readonly List<SealableComponent> _parts = new();
        private readonly List<SealableComponent> _cores = new();
        private readonly List<SealableComponent> _readyList = new();

        private readonly Dictionary<SealableComponent, Action<float>> _gaugeChangedHandlers = new();
        private readonly Dictionary<SealableComponent, Action<SealableComponent>> _sealRequestedHandlers = new();
        private readonly Dictionary<SealableComponent, Action> _sealCompletedHandlers = new();
        private readonly Dictionary<SealableComponent, Action> _forceReleasedHandlers = new();
        private readonly Dictionary<SealableComponent, Action> _phaseTargetHandlers = new();

        private BossSealState _state = BossSealState.Idle;
        private int _currentPhase = 1;
        private bool _initialized;
        private bool _isExecuting;
        private bool _forceStop;
        private float _cooldownTimer;
        private Coroutine _dilPhaseRoutine;
        private Coroutine _executionRoutine;
        private Rigidbody2D _rigid2D;
        private bool _lockedPlayerByIssen;
        private bool _blockedInputByIssen;
        private bool _pausedBossByIssen;

        private readonly List<SealableComponent> _chainPreview = new();

        public event Action<BossSealState, BossSealState> OnStateChanged;
        public event Action OnDilPhaseEnter;
        public event Action OnDilPhaseExit;
        public event Action<int> OnPhaseChanged;
        public event Action OnFinalSealReady;
        public event Action OnDead;

        public BossSealState State => _state;
        public bool IsInitialized => _initialized;
        public bool IsDilPhase => _state == BossSealState.DilPhase;
        public bool IsFinalSeal => _state == BossSealState.FinalSeal;
        public bool IsDead => _state == BossSealState.Dead;
        public int CurrentPhase => _currentPhase;
        public int ReadyExecutionTargetCount => _readyList.Count;

        public IReadOnlyList<SealableComponent> AllSealables => _allSealables;
        public IReadOnlyList<SealableComponent> PartSealables => _parts;
        public IReadOnlyList<SealableComponent> CoreSealables => _cores;
        public IReadOnlyList<SealableComponent> ReadyTargets => _readyList;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            SubscribeInput();
        }

        private void Update()
        {
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.unscaledDeltaTime;
        }

        private void OnDestroy()
        {
            CleanupIssenLock();
            RestoreTimeScale();
            UnsubscribeInput();
            UnsubscribeSealableEvents();
        }

        public void Initialize(BossDataSO data, GameObject coreObject = null)
        {
            ResolveReferences();

            _bossData = data != null ? data : _dataManager != null ? _dataManager.BossData : null;
            _wardenData = _bossData as BossWardenDataSO;

            if (_bossData == null || !_bossData.IsValid())
            {
                Debug.LogError("[BossSealManager] BossDataSO 미연결 또는 유효하지 않음 — 초기화 중단.");
                enabled = false;
                return;
            }

            if (coreObject != null)
                _coreObject = coreObject;
            else if (_coreObject == null && _partManager != null)
                _coreObject = _partManager.CoreObject;

            CollectSealables();
            InitializeSealables();
            SubscribeSealableEvents();
            ConnectReadyNotifiers();

            if (_coreStartsInactive)
                ActivateCore(false);

            _initialized = true;

            if (_debugLog)
                Debug.Log($"[BossSealManager] Step20 통합 초기화 완료 | 전체:{_allSealables.Count} Part:{_parts.Count} Core:{_cores.Count}");
        }

        private void ResolveReferences()
        {
            if (!_autoResolveReferences) return;

            Transform root = transform.root;

            if (_dataManager == null) _dataManager = root.GetComponentInChildren<BossDataManager>(true);
            if (_partManager == null) _partManager = root.GetComponentInChildren<BossPartManager>(true);
            if (_eventHub == null) _eventHub = root.GetComponentInChildren<BossEventHub>(true);
            if (_vfxManager == null) _vfxManager = root.GetComponentInChildren<BossVFXManager>(true);
            if (_attackManager == null) _attackManager = root.GetComponentInChildren<BossAttackManager>(true);
            if (_coreObject == null && _partManager != null) _coreObject = _partManager.CoreObject;
            if (_rigid2D == null) _rigid2D = root.GetComponentInChildren<Rigidbody2D>(true);
            if (_input == null) _input = PlayerInputHandler.Instance;

            if (_playerTransform == null)
            {
                var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
                if (players != null && players.Length > 0)
                    _playerTransform = players[0].transform;
            }

            if (_playerController == null && _playerTransform != null)
                _playerController = _playerTransform.GetComponentInParent<PlayerController>();

            if (_playerMoveController == null && _playerTransform != null)
                _playerMoveController = _playerTransform.GetComponentInParent<PlayerMoveController>();

            if (_playerAttackController == null && _playerTransform != null)
                _playerAttackController = _playerTransform.GetComponentInParent<PlayerAttackController>();

            if (_playerRigid2D == null && _playerTransform != null)
                _playerRigid2D = _playerTransform.GetComponentInParent<Rigidbody2D>();
        }

        private void CollectSealables()
        {
            _allSealables.Clear();
            _parts.Clear();
            _cores.Clear();
            _readyList.Clear();

            if (_partManager != null)
            {
                foreach (var sealable in _partManager.GetAllSealables(includeCore: true))
                    AddSealable(sealable);
            }

            // CoreObject 비활성 상태나 PartManager List 누락에도 Core를 놓치지 않도록 보스 루트에서 1회 보강 수집한다.
            Transform root = transform.root;
            if (root != null)
            {
                foreach (var sealable in root.GetComponentsInChildren<SealableComponent>(true))
                {
                    if (sealable == null) continue;
                    if (sealable.Grade != SealGrade.Part && sealable.Grade != SealGrade.Core) continue;
                    AddSealable(sealable);
                }
            }

            foreach (var sealable in _allSealables)
            {
                if (sealable == null) continue;
                if (sealable.Grade == SealGrade.Core) _cores.Add(sealable);
                else if (sealable.Grade == SealGrade.Part) _parts.Add(sealable);
            }
        }

        private void AddSealable(SealableComponent sealable)
        {
            if (sealable == null) return;
            if (_allSealables.Contains(sealable)) return;
            _allSealables.Add(sealable);
        }

        private void InitializeSealables()
        {
            foreach (var sealable in _allSealables)
            {
                if (sealable == null) continue;

                float maxGauge = 0f;
                if (sealable.Grade == SealGrade.Core && _bossData?.SealData != null)
                    maxGauge = _bossData.SealData.coreSealGaugeMax;

                sealable.Initialize(_bossData, maxGauge);

                var readyNotifier = sealable.GetComponent<SealReadyNotifier>();
                readyNotifier?.Initialize(_bossData);
            }
        }

        private void SubscribeSealableEvents()
        {
            UnsubscribeSealableEvents();

            foreach (var sealable in _allSealables)
            {
                if (sealable == null) continue;

                Action<float> gaugeChanged = _ => HandleGaugeChanged(sealable);
                Action<SealableComponent> sealRequested = _ => HandleSealRequested(sealable);
                Action sealCompleted = () => HandleSealCompleted(sealable);
                Action forceReleased = () => HandleForceReleased(sealable);
                Action phaseTargetReached = () => HandlePhaseTargetReached(sealable);

                _gaugeChangedHandlers[sealable] = gaugeChanged;
                _sealRequestedHandlers[sealable] = sealRequested;
                _sealCompletedHandlers[sealable] = sealCompleted;
                _forceReleasedHandlers[sealable] = forceReleased;
                _phaseTargetHandlers[sealable] = phaseTargetReached;

                sealable.OnGaugeChanged += gaugeChanged;
                sealable.OnSealRequested += sealRequested;
                sealable.OnSealCompleted += sealCompleted;
                sealable.OnForceReleased += forceReleased;
                sealable.OnPhaseTargetReached += phaseTargetReached;
            }
        }

        private void UnsubscribeSealableEvents()
        {
            foreach (var pair in _gaugeChangedHandlers)
                if (pair.Key != null) pair.Key.OnGaugeChanged -= pair.Value;
            foreach (var pair in _sealRequestedHandlers)
                if (pair.Key != null) pair.Key.OnSealRequested -= pair.Value;
            foreach (var pair in _sealCompletedHandlers)
                if (pair.Key != null) pair.Key.OnSealCompleted -= pair.Value;
            foreach (var pair in _forceReleasedHandlers)
                if (pair.Key != null) pair.Key.OnForceReleased -= pair.Value;
            foreach (var pair in _phaseTargetHandlers)
                if (pair.Key != null) pair.Key.OnPhaseTargetReached -= pair.Value;

            _gaugeChangedHandlers.Clear();
            _sealRequestedHandlers.Clear();
            _sealCompletedHandlers.Clear();
            _forceReleasedHandlers.Clear();
            _phaseTargetHandlers.Clear();
        }

        private void ConnectReadyNotifiers()
        {
            // SealReadyNotifier는 범위 원 표시/맥동 담당으로 유지한다.
            // 집행 목록 관리는 BossSealManager가 SealableComponent 이벤트로 직접 처리한다.
            var notifiers = transform.root.GetComponentsInChildren<SealReadyNotifier>(true);
            foreach (var notifier in notifiers)
            {
                if (notifier == null) continue;
                notifier.Initialize(_bossData);
            }
        }

        private void SubscribeInput()
        {
            if (_input == null) _input = PlayerInputHandler.Instance;
            if (_input == null) return;

            _input.OnSeal -= HandleSealPressed;
            _input.OnSeal += HandleSealPressed;
        }

        private void UnsubscribeInput()
        {
            if (_input != null)
                _input.OnSeal -= HandleSealPressed;
        }

        private void HandleGaugeChanged(SealableComponent sealable)
        {
            if (sealable == null) return;

            if (sealable.Grade == SealGrade.Core)
            {
                _eventHub?.RaiseCoreSealGaugeChanged(sealable.UIPercent, 100f);
                CheckCorePhaseTargetByGauge(sealable);
                return;
            }

            if (TryGetPartType(sealable, out var partType))
                _eventHub?.RaiseSealGaugeChanged(partType, sealable.UIPercent, 100f);
        }

        private void HandleSealRequested(SealableComponent sealable)
        {
            if (sealable == null) return;
            if (!_readyList.Contains(sealable))
                _readyList.Add(sealable);

            if (TryGetPartType(sealable, out var partType))
                _eventHub?.RaiseSealReady(partType);

            if (_debugLog)
                Debug.Log($"[BossSealManager] ▶ 집행 대상 추가: {sealable.name} | Ready:{_readyList.Count}");
        }

        private void HandleSealCompleted(SealableComponent sealable)
        {
            if (sealable == null) return;
            _readyList.Remove(sealable);

            if (sealable.Grade == SealGrade.Core)
            {
                _eventHub?.RaiseCoreSealCompleted();

                if (_state == BossSealState.FinalSeal)
                    EnterDead();

                return;
            }

            if (TryGetPartType(sealable, out var partType))
                _eventHub?.RaiseSealExecuted(partType);

            CheckAllPartsSealed();
        }

        private void HandleForceReleased(SealableComponent sealable)
        {
            if (sealable == null) return;
            _readyList.Remove(sealable);

            if (TryGetPartType(sealable, out var partType))
                _eventHub?.RaiseSealReleased(partType);
        }

        private void HandlePhaseTargetReached(SealableComponent sealable)
        {
            if (sealable == null || sealable.Grade != SealGrade.Core) return;
            if (_state != BossSealState.DilPhase) return;

            bool finalSeal = _currentPhase >= 2;
            ExitDilPhase(finalSeal);
        }

        private void CheckAllPartsSealed()
        {
            if (_state != BossSealState.Idle) return;
            if (!AreAllPartsSealed()) return;

            _eventHub?.RaiseAllPartsSealed();
            EnterDilPhase();
        }

        private void HandleSealPressed()
        {
            if (_isExecuting || _cooldownTimer > 0f || _forceStop || _state == BossSealState.Dead)
                return;

            Vector2 playerPos = GetPlayerPosition();
            SealableComponent target = GetBestExecutionTarget(playerPos);

            if (target == null)
            {
                if (_debugLog) Debug.Log("[BossSealManager] 범위 내 집행 가능 대상 없음 — 무시");
                return;
            }

            if (_useIssenExecution)
                _executionRoutine = StartCoroutine(AutoIssenChainRoutine(target));
            else
                _executionRoutine = StartCoroutine(ExecuteSealRoutine(target));
        }

        private IEnumerator ExecuteSealRoutine(SealableComponent target)
        {
            if (target == null) yield break;

            _isExecuting = true;
            yield return StartCoroutine(ExecuteSealTargetRoutine(target));
            _isExecuting = false;
            _cooldownTimer = 0.3f;
            _executionRoutine = null;
        }

        private IEnumerator AutoIssenChainRoutine(SealableComponent firstTarget)
        {
            _isExecuting = true;
            LockForIssen();

            HashSet<SealableComponent> executed = new HashSet<SealableComponent>();
            SealableComponent current = firstTarget;
            SealableComponent lastSealed = null;
            int count = 0;

            while (true)
            {
                if (!CanExecuteTarget(current))
                {
                    current = FindReplacementTarget(lastSealed, executed, current);
                    if (current == null)
                        break;
                }

                RefreshChainPreview(current, executed);

                SealableComponent arrivedTarget = null;
                bool arrived = false;

                yield return StartCoroutine(MovePlayerToTargetAuto(
                    current,
                    lastSealed,
                    executed,
                    (target, result) =>
                    {
                        arrivedTarget = target;
                        arrived = result;
                    }));

                if (!arrived || arrivedTarget == null)
                    break;

                if (!CanExecuteTarget(arrivedTarget))
                {
                    current = FindReplacementTarget(lastSealed, executed, arrivedTarget);
                    continue;
                }

                yield return StartCoroutine(ExecuteSealTargetRoutine(arrivedTarget));

                executed.Add(arrivedTarget);
                lastSealed = arrivedTarget;
                count++;

                if (!_enableIssenChain)
                    break;

                if (_issenMaxChainCount > 0 && count >= _issenMaxChainCount)
                    break;

                if (_issenChainInterval > 0f)
                    yield return new WaitForSecondsRealtime(_issenChainInterval);

                current = FindNearestReady(GetExecutionPosition(arrivedTarget), _issenChainRadius, executed);
            }

            FinishIssenExecution();
        }

        private IEnumerator ExecuteSealTargetRoutine(SealableComponent target)
        {
            if (target == null) yield break;

            var effect = target.GetComponent<SealExecutionEffect>();
            effect?.OnExecutionStart();

            if (target.Grade == SealGrade.Core && _bossData?.SealData != null)
                Time.timeScale = _bossData.SealData.finalSealSlowTimeScale;

            target.ExecuteSeal();
            effect?.OnExecutionComplete();

            if (target.Grade == SealGrade.Part && _bossData?.SealData != null)
            {
                Time.timeScale = _bossData.SealData.partSealSlowTimeScale;
                yield return new WaitForSecondsRealtime(_bossData.SealData.partSealSlowDuration);
                RestoreTimeScale();
            }
            else if (target.Grade != SealGrade.Core)
            {
                RestoreTimeScale();
            }
        }

        private IEnumerator MovePlayerToTargetAuto(
            SealableComponent startTarget,
            SealableComponent lastSealed,
            HashSet<SealableComponent> executed,
            Action<SealableComponent, bool> onComplete)
        {
            SealableComponent target = startTarget;
            float elapsed = 0f;

            while (true)
            {
                if (!CanExecuteTarget(target))
                {
                    target = FindReplacementTarget(lastSealed, executed, target);
                    elapsed = 0f;

                    if (target == null)
                    {
                        onComplete?.Invoke(null, false);
                        yield break;
                    }
                }

                Vector2 currentPos = GetPlayerPosition();
                Vector2 targetPos = GetExecutionMovePosition(target, currentPos);
                Vector2 toTarget = targetPos - currentPos;

                if (toTarget.magnitude <= _issenArriveDistance)
                {
                    SetPlayerPosition(targetPos);
                    onComplete?.Invoke(target, true);
                    yield break;
                }

                Vector2 next = Vector2.MoveTowards(currentPos, targetPos, _issenMoveSpeed * Time.fixedDeltaTime);
                SetPlayerPosition(next);

                elapsed += Time.fixedDeltaTime;
                if (elapsed >= _issenMaxTravelTime)
                {
                    SetPlayerPosition(targetPos);
                    onComplete?.Invoke(target, true);
                    yield break;
                }

                yield return new WaitForFixedUpdate();
            }
        }

        private void LockForIssen()
        {
            ResolveReferences();

            _playerAttackController?.CancelAttack();

            if (_blockPlayerInputDuringIssen && _input != null)
            {
                _input.BlockAll();
                _blockedInputByIssen = true;
            }

            if (_lockPlayerStateDuringIssen && _playerController != null)
            {
                _playerController.EnterSeal();
                _lockedPlayerByIssen = true;
            }

            if (_playerMoveController != null)
            {
                _playerMoveController.SetAttackMove(false, Vector2.zero, 0f);
                _playerMoveController.SetMoveLocked(true);
            }

            if (_playerRigid2D != null)
                _playerRigid2D.linearVelocity = Vector2.zero;

            if (_pauseBossAttackDuringIssen && _attackManager != null)
            {
                _attackManager.PauseBySealExecution();
                _pausedBossByIssen = true;
            }
        }

        private void FinishIssenExecution()
        {
            ClearChainPreview();
            CleanupIssenLock(stopRoutine: false);
            _executionRoutine = null;
            _isExecuting = false;
            _cooldownTimer = 0.3f;

            if (_debugLog)
                Debug.Log("[BossSealManager] 보스 파츠 일섬 집행 종료");
        }

        private void CleanupIssenLock(bool stopRoutine = true)
        {
            if (stopRoutine && _executionRoutine != null)
            {
                StopCoroutine(_executionRoutine);
                _executionRoutine = null;
            }

            ClearChainPreview();
            ForceRestorePlayerControl();

            if (_pausedBossByIssen && _attackManager != null)
            {
                bool allowResume = _state == BossSealState.Idle;
                _attackManager.ResumeBySealExecution(allowResume);
            }

            _lockedPlayerByIssen = false;
            _blockedInputByIssen = false;
            _pausedBossByIssen = false;
        }

        private void ForceRestorePlayerControl()
        {
            ResolveReferences();

            if (_playerRigid2D != null)
                _playerRigid2D.linearVelocity = Vector2.zero;

            if (_playerMoveController != null)
            {
                _playerMoveController.SetAttackMove(false, Vector2.zero, 0f);
                _playerMoveController.SetMoveLocked(false);
            }

            if (_playerController != null)
                _playerController.ExitSeal();

            if (_input != null)
                _input.UnblockAll();
        }

        private SealableComponent FindReplacementTarget(
            SealableComponent lastSealed,
            HashSet<SealableComponent> executed,
            SealableComponent invalidTarget)
        {
            HashSet<SealableComponent> ignore = new HashSet<SealableComponent>(executed);
            if (invalidTarget != null)
                ignore.Add(invalidTarget);

            if (lastSealed != null)
            {
                SealableComponent fromLast = FindNearestReady(
                    GetExecutionPosition(lastSealed),
                    _issenChainRadius,
                    ignore);

                if (fromLast != null)
                    return fromLast;
            }

            return GetBestExecutionTarget(GetPlayerPosition(), ignore);
        }

        private SealableComponent FindNearestReady(
            Vector2 center,
            float radius,
            HashSet<SealableComponent> ignore = null)
        {
            SealableComponent best = null;
            float bestDistSqr = float.MaxValue;
            float rangeSqr = radius * radius;

            for (int i = _readyList.Count - 1; i >= 0; i--)
            {
                SealableComponent s = _readyList[i];
                if (!CanExecuteTarget(s))
                {
                    _readyList.RemoveAt(i);
                    continue;
                }

                if (ignore != null && ignore.Contains(s))
                    continue;

                float distSqr = (GetExecutionPosition(s) - center).sqrMagnitude;
                if (distSqr > rangeSqr) continue;

                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = s;
                }
            }

            return best;
        }

        private bool CanExecuteTarget(SealableComponent target)
        {
            if (target == null || target.IsSealed || !target.gameObject.activeInHierarchy)
                return false;

            // FinalSeal 상태에서는 Core가 phase target 도달로 Ready 목록에 등록될 수 있다.
            // Core MaxGauge 100% 도달 여부와 무관하게 최종 집행 대상으로 허용한다.
            if (_state == BossSealState.FinalSeal && target.Grade == SealGrade.Core)
                return true;

            return target.IsSealReady;
        }

        private Vector2 GetExecutionPosition(SealableComponent target)
        {
            BossWardenPart part = GetPartBySealable(target);
            if (part != null)
                return part.ExecutionPosition;

            return target != null ? (Vector2)target.transform.position : GetPlayerPosition();
        }

        private Vector2 GetExecutionMovePosition(SealableComponent target, Vector2 currentPlayerPos)
        {
            Vector2 targetPos = GetExecutionPosition(target);
            if (_issenStopOffset <= 0f)
                return targetPos;

            Vector2 fromTargetToPlayer = currentPlayerPos - targetPos;
            if (fromTargetToPlayer.sqrMagnitude <= 0.0001f)
                fromTargetToPlayer = Vector2.left;

            return targetPos + fromTargetToPlayer.normalized * _issenStopOffset;
        }

        private Vector2 GetPlayerPosition()
        {
            if (_playerRigid2D != null)
                return _playerRigid2D.position;

            if (_playerTransform != null)
                return _playerTransform.position;

            return Vector2.zero;
        }

        private void SetPlayerPosition(Vector2 position)
        {
            if (_playerRigid2D != null)
            {
                _playerRigid2D.linearVelocity = Vector2.zero;
                _playerRigid2D.MovePosition(position);
            }
            else if (_playerTransform != null)
            {
                _playerTransform.position = position;
            }
        }

        private void RefreshChainPreview(SealableComponent start, HashSet<SealableComponent> executed)
        {
            if (_issenChainPreviewLine == null)
                return;

            _chainPreview.Clear();

            if (start != null)
                _chainPreview.Add(start);

            if (_enableIssenChain)
            {
                HashSet<SealableComponent> ignore = new HashSet<SealableComponent>(executed);
                SealableComponent current = start;
                int limit = _issenMaxChainCount > 0 ? _issenMaxChainCount : 12;

                for (int i = 1; i < limit && current != null; i++)
                {
                    ignore.Add(current);
                    current = FindNearestReady(GetExecutionPosition(current), _issenChainRadius, ignore);
                    if (current != null)
                        _chainPreview.Add(current);
                }
            }

            _issenChainPreviewLine.positionCount = _chainPreview.Count + 1;
            _issenChainPreviewLine.SetPosition(0, GetPlayerPosition());

            for (int i = 0; i < _chainPreview.Count; i++)
                _issenChainPreviewLine.SetPosition(i + 1, GetExecutionPosition(_chainPreview[i]));
        }

        private void ClearChainPreview()
        {
            _chainPreview.Clear();
            if (_issenChainPreviewLine != null)
                _issenChainPreviewLine.positionCount = 0;
        }

        public SealableComponent GetBestExecutionTarget(Vector2 playerPosition)
        {
            return GetBestExecutionTarget(playerPosition, null);
        }

        private SealableComponent GetBestExecutionTarget(Vector2 playerPosition, HashSet<SealableComponent> ignore)
        {
            SealableComponent best = null;
            float bestDistSqr = float.MaxValue;

            for (int i = _readyList.Count - 1; i >= 0; i--)
            {
                SealableComponent s = _readyList[i];
                if (!CanExecuteTarget(s))
                {
                    _readyList.RemoveAt(i);
                    continue;
                }

                if (ignore != null && ignore.Contains(s))
                    continue;

                float distSqr = (GetExecutionPosition(s) - playerPosition).sqrMagnitude;
                float range = Mathf.Max(0.01f, s.SealRange);
                if (distSqr > range * range) continue;

                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = s;
                }
            }

            return best;
        }

        private void EnterDilPhase()
        {
            if (_state == BossSealState.Dead) return;

            SetState(BossSealState.DilPhase);
            ActivateCore(true);
            PrepareCoreGaugeForDilPhase();

            OnDilPhaseEnter?.Invoke();
            _eventHub?.RaiseDilPhaseEnter();
            _eventHub?.RaiseBossStateChanged(BossEventHub.BossRuntimeState.DilPhase);

            if (_dilPhaseRoutine != null) StopCoroutine(_dilPhaseRoutine);
            _dilPhaseRoutine = StartCoroutine(DilPhaseTimerRoutine());

            if (_debugLog)
                Debug.Log($"[BossSealManager] ▶ DilPhase 진입 | 페이즈:{_currentPhase}");
        }

        private void PrepareCoreGaugeForDilPhase()
        {
            float target = GetCurrentCoreTarget();

            foreach (var core in _cores)
            {
                if (core == null) continue;

                // 중요: Core 봉인도는 누적형이다.
                // DilPhase가 다시 열릴 때 0%로 초기화하지 않는다.
                core.SetPhaseTarget(target);
                core.ActivateGauge(true);
                _eventHub?.RaiseCoreSealGaugeChanged(core.UIPercent, 100f);

                BossWardenPart corePart = GetPartBySealable(core);
                corePart?.SetColliderEnabled(true);
            }
        }

        private void DeactivateCoreGaugeForExit()
        {
            foreach (var core in _cores)
            {
                if (core == null) continue;

                // 실패/페이즈 전환 시에도 누적 게이지는 유지한다.
                // 다음 DilPhase에서 이전 누적치를 그대로 이어받는다.
                core.ActivateGauge(false);
                _eventHub?.RaiseCoreSealGaugeChanged(core.UIPercent, 100f);
            }
        }

        private float GetCurrentCoreTarget()
        {
            if (_bossData?.SealData == null)
                return 0f;

            return _currentPhase >= 2
                ? _bossData.SealData.phase2CoreSealTarget
                : _bossData.SealData.phase1CoreSealTarget;
        }

        private void CheckCorePhaseTargetByGauge(SealableComponent core)
        {
            if (core == null || _state != BossSealState.DilPhase) return;

            float target = GetCurrentCoreTarget();
            if (target <= 0f) return;
            if (core.CurrentGauge < target) return;

            bool finalSeal = _currentPhase >= 2;
            ExitDilPhase(finalSeal);
        }

        private IEnumerator DilPhaseTimerRoutine()
        {
            float duration = _wardenData != null ? _wardenData.dilPhaseDuration : 10f;
            yield return new WaitForSecondsRealtime(duration);
            _dilPhaseRoutine = null;

            if (_state == BossSealState.DilPhase)
                ExitDilPhase(false);
        }

        private void ExitDilPhase(bool finalSeal)
        {
            if (_dilPhaseRoutine != null)
            {
                StopCoroutine(_dilPhaseRoutine);
                _dilPhaseRoutine = null;
            }

            DeactivateCoreGaugeForExit();

            if (finalSeal)
            {
                EnterFinalSeal();
                return;
            }

            ActivateCore(false);
            ReleaseAllParts(false);

            OnDilPhaseExit?.Invoke();
            _eventHub?.RaiseDilPhaseExit();
            _eventHub?.RaiseBossStateChanged(BossEventHub.BossRuntimeState.Idle);

            if (_currentPhase == 1)
            {
                _currentPhase = 2;
                OnPhaseChanged?.Invoke(2);
                _eventHub?.RaisePhaseChanged(2);
            }

            SetState(BossSealState.Idle);
        }

        private void EnterFinalSeal()
        {
            if (_state == BossSealState.Dead) return;

            SetState(BossSealState.FinalSeal);

            SealableComponent core = GetCoreSealable();
            if (core != null)
            {
                // FinalSeal은 Core 누적치가 목표에 도달한 결과이므로 초기화하지 않는다.
                // IsSealReady가 MaxGauge 기준으로만 켜지는 경우를 대비해 Ready 목록에 직접 등록한다.
                core.SetPhaseTarget(0f);
                core.ActivateGauge(true);

                if (!_readyList.Contains(core) && !core.IsSealed)
                    _readyList.Add(core);

                _eventHub?.RaiseCoreSealGaugeChanged(core.UIPercent, 100f);

                BossWardenPart corePart = GetPartBySealable(core);
                corePart?.SetColliderEnabled(true);
            }

            // Core 목표치 도달은 "최종 봉인 집행 가능" 상태일 뿐이다.
            // 불릿타임/처치 연출은 플레이어가 Core 봉인 집행을 실제로 입력하고
            // ExecuteSealTargetRoutine()에서 Core ExecuteSeal이 호출될 때 시작한다.
            RestoreTimeScale();

            OnFinalSealReady?.Invoke();
            _eventHub?.RaiseFinalSealReady();
            _eventHub?.RaiseBossStateChanged(BossEventHub.BossRuntimeState.FinalSeal);
        }

        private void EnterDead()
        {
            if (_state == BossSealState.Dead) return;

            SetState(BossSealState.Dead);
            CleanupIssenLock(stopRoutine: false);
            RestoreTimeScale();
            StopAllCoroutines();
            _isExecuting = false;
            _forceStop = true;

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            ActivateCore(false);

            OnDead?.Invoke();
            _eventHub?.RaiseDead();
            _eventHub?.RaiseBossStateChanged(BossEventHub.BossRuntimeState.Dead);
        }

        private void SetState(BossSealState newState)
        {
            if (_state == newState) return;
            BossSealState prev = _state;
            _state = newState;
            OnStateChanged?.Invoke(prev, newState);
        }

        public void ReleaseAllParts(bool resetSealCount = false)
        {
            foreach (var part in _parts)
                part?.ForceRelease(resetSealCount);

            _readyList.RemoveAll(s => s == null || s.Grade == SealGrade.Part);
            _eventHub?.RaiseSealReleased(WardenPartType.LeftArm);
            _eventHub?.RaiseSealReleased(WardenPartType.RightArm);
        }

        public void ReleaseAll(bool resetSealCount = false)
        {
            foreach (var sealable in _allSealables)
                sealable?.ForceRelease(resetSealCount);

            _readyList.Clear();
        }

        public void ActivateCore(bool isActive)
        {
            if (_coreObject != null)
                _coreObject.SetActive(isActive);
        }

        public void ActivateCoreGauge(bool isActive)
        {
            foreach (var core in _cores)
                core?.ActivateGauge(isActive);
        }

        public bool AreAllPartsSealed()
        {
            if (_parts.Count <= 0) return false;
            foreach (var part in _parts)
            {
                if (part == null || !part.IsSealed)
                    return false;
            }
            return true;
        }

        public int GetSealedCount(SealGrade grade)
        {
            int count = 0;
            foreach (var sealable in _allSealables)
            {
                if (sealable != null && sealable.Grade == grade && sealable.IsSealed)
                    count++;
            }
            return count;
        }

        public int GetPartCount() => _parts.Count;

        public SealableComponent GetSealable(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            foreach (var sealable in _allSealables)
            {
                if (sealable != null && sealable.gameObject.name == objectName)
                    return sealable;
            }
            return null;
        }

        public IReadOnlyList<SealableComponent> GetAllSealables() => _allSealables;
        public int GetReadyExecutionTargetCount() => _readyList.Count;

        public void ForceStopExecution()
        {
            _forceStop = true;
            _isExecuting = false;
            CleanupIssenLock();
            RestoreTimeScale();
        }

        public void ForceKill()
        {
            EnterDead();
        }

        public void ForceEnterDilPhase()
        {
            EnterDilPhase();
        }

        private SealableComponent GetCoreSealable()
        {
            if (_cores.Count > 0) return _cores[0];
            return _partManager != null ? _partManager.CoreSealable : null;
        }

        private BossWardenPart GetPartBySealable(SealableComponent sealable)
        {
            if (sealable == null || _partManager == null) return null;

            foreach (var part in _partManager.GetAllParts(includeCore: true))
            {
                if (part == null || part.Sealable != sealable) continue;
                return part;
            }

            return null;
        }

        private bool TryGetPartType(SealableComponent sealable, out WardenPartType partType)
        {
            partType = WardenPartType.LeftArm;
            if (sealable == null) return false;

            if (_partManager != null)
            {
                foreach (var part in _partManager.GetAllParts(includeCore: true))
                {
                    if (part == null || part.Sealable != sealable) continue;
                    partType = part.PartType;
                    return true;
                }
            }

            return false;
        }

        private void RestoreTimeScale()
        {
            Time.timeScale = 1f;
        }

#if UNITY_EDITOR
        [ContextMenu("DEBUG — DilPhase 강제 진입")]
        private void DEBUG_ForceDilPhase()
        {
            if (!Application.isPlaying) return;
            ForceEnterDilPhase();
        }

        [ContextMenu("DEBUG — ForceKill")]
        private void DEBUG_ForceKill()
        {
            if (!Application.isPlaying) return;
            ForceKill();
        }
#endif
    }
}
