// ============================================================
// BossAttackManager.cs v3.1
// Boss_Warden AI + 공격 패턴 통합 관리자 — Step 19
//
// [Step 20]
//   Slam / Swing 2패턴 구조 유지.
//   BossPartManager의 4팔 구조에 맞춰 팔 취약 처리와 패턴 참조를 배열 기반으로 확장한다.
//
// [Step 21]
//   보스 일섬 집행 중 AI/패턴을 일시 정지/복구하는 API 추가.
//
// [담당]
//   - Idle / Chase / Warning / Active / Recovery 상태 관리
//   - 플레이어 방향 갱신 / 추적 이동
//   - 패턴 선택 / Warning / Active / Recovery 실행
//   - DilPhase / PhaseChanged / Dead 반응
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    [DefaultExecutionOrder(-5)]
    public class BossAttackManager : MonoBehaviour
    {
        public enum WardenAIState
        {
            Idle,
            Chase,
            Warning,
            Active,
            Recovery,
        }

        [Header("── DataSO ──────────────────────")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── Central Managers ──────────────────────")]
        [SerializeField] private BossDataManager _dataManager;
        [SerializeField] private BossPartManager _partManager;
        [SerializeField] private BossEventHub _eventHub;

        [Header("── Movement Components ──────────────────────")]
        [Tooltip("보스 Rigidbody2D. Manager 자식에 붙어 있어도 보스 루트 Rigidbody2D를 연결한다.")]
        [SerializeField] private Rigidbody2D _rigid2D;

        [Tooltip("이동/거리 계산 기준 Transform. 비워두면 Rigidbody2D Transform 또는 transform.root를 사용한다.")]
        [SerializeField] private Transform _moveRoot;

        [Header("── 패턴 목록 ──────────────────────")]
        [Tooltip("Step20 이후 Slam / Swing만 사용한다. 기존 Charge/RageCharge/Sweep/GuardBreak가 들어와도 무시된다.")]
        [SerializeField] private List<BossPatternBase> _patterns = new();

        [Header("── 팔 부위 연결 ──────────────────────")]
        [Tooltip("자동 수집된 모든 팔 파츠. Inspector 직접 연결도 가능.")]
        [SerializeField] private List<BossWardenPart> _armParts = new();

        [Header("── Options ──────────────────────")]
        [SerializeField] private bool _autoResolveReferences = true;
        [SerializeField] private bool _debugLog;

        private readonly List<BossPatternBase> _availablePatterns = new();

        private Transform _playerTransform;
        private BossPatternBase _currentPattern;
        private Coroutine _patternCoroutine;
        private bool _isExecuting;
        private bool _isStopped;
        private bool _pausedBySealExecution;
        private float _currentMoveSpeed;
        private float _flipCooldownTimer;
        private Vector2 _facingDir = Vector2.right;
        private WardenAIState _currentState = WardenAIState.Idle;
        private bool _initialized;

        public event Action<WardenAIState, BossPatternBase> OnStateChanged;
        public event Action<Vector2> OnFacingChanged;

        public bool IsInitialized => _initialized;
        public bool IsExecuting => _isExecuting;
        public bool IsStopped => _isStopped;
        public BossPatternBase CurrentPattern => _currentPattern;
        public WardenAIState CurrentState => _currentState;
        public Vector2 FacingDir => _facingDir;
        public Transform PlayerTransform => _playerTransform;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolvePlayer();
        }

        private void Update()
        {
            if (!_initialized) return;
            if (_isStopped) return;

            UpdateTimers();
            UpdateStateLogic();
        }

        private void FixedUpdate()
        {
            if (!_initialized) return;

            if (_isStopped)
            {
                StopVelocity();
                return;
            }

            UpdateMovement();
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }

        public void Initialize(BossWardenDataSO data, BossWardenPart armL = null, BossWardenPart armR = null)
        {
            ResolveReferences();
            ResolvePlayer();

            _data = data != null ? data : _dataManager != null ? _dataManager.WardenData : null;
            if (_data == null)
            {
                Debug.LogError("[BossAttackManager] BossWardenDataSO 미연결 — 초기화 중단.");
                enabled = false;
                return;
            }

            _armParts ??= new List<BossWardenPart>();

            if (armL != null && !_armParts.Contains(armL)) _armParts.Add(armL);
            if (armR != null && !_armParts.Contains(armR)) _armParts.Add(armR);

            RefreshArmPartsFromManager();

            if (_patterns == null || _patterns.Count == 0)
            {
                Debug.LogError("[BossAttackManager] 패턴 목록이 비어 있습니다. Patterns에 패턴을 연결하세요.");
                enabled = false;
                return;
            }

            int supportedPatternCount = 0;
            foreach (var pattern in _patterns)
            {
                if (pattern == null) continue;

                if (!IsSupportedPattern(pattern))
                {
                    Debug.LogWarning($"[BossAttackManager] Step20에서 사용하지 않는 패턴 무시: {pattern.GetType().Name}");
                    continue;
                }

                pattern.Initialize(_data);
                supportedPatternCount++;
            }

            if (supportedPatternCount <= 0)
            {
                Debug.LogError("[BossAttackManager] Step20 사용 가능 패턴이 없습니다. BossPattern_Slam 또는 BossPattern_Swing을 Patterns에 연결하세요.");
                enabled = false;
                return;
            }

            _currentMoveSpeed = _data.moveSpeed;
            _currentState = WardenAIState.Idle;
            _isStopped = false;
            _initialized = true;

            if (_debugLog)
                Debug.Log($"[BossAttackManager] Step20 초기화 완료 — Slam/Swing + ArmParts:{_armParts.Count}");
        }

        public bool RequestAttack()
        {
            if (!_initialized) return false;
            if (_isStopped) return false;
            if (_isExecuting) return false;
            if (_currentPattern != null) return false;
            if (_patterns == null || _patterns.Count == 0) return false;

            BossPatternBase selected = SelectPattern();
            if (selected == null) return false;

            _currentPattern = selected;
            _isExecuting = true;
            _patternCoroutine = StartCoroutine(ExecutePattern(selected));
            return true;
        }

        public void InterruptCurrentPattern()
        {
            BossPatternBase interrupted = _currentPattern;

            if (_currentPattern != null)
            {
                _currentPattern.Interrupt();
                _eventHub?.RaiseAttackInterrupted(_currentPattern);
            }

            StopAllCoroutines();
            _patternCoroutine = null;
            _currentPattern = null;
            _isExecuting = false;

            SetArmsRecoveryVuln(false);
            InterruptAttackState(interrupted);
        }


        public void PauseBySealExecution()
        {
            if (_pausedBySealExecution) return;

            _pausedBySealExecution = true;
            _isStopped = true;
            InterruptCurrentPattern();
            StopVelocity();

            if (_debugLog)
                Debug.Log("[BossAttackManager] ▶ 봉인 일섬 집행 → AI/패턴 일시 정지");
        }

        public void ResumeBySealExecution()
        {
            ResumeBySealExecution(allowResume: true);
        }

        public void ResumeBySealExecution(bool allowResume)
        {
            if (!_pausedBySealExecution) return;

            _pausedBySealExecution = false;

            if (!allowResume)
            {
                if (_debugLog)
                    Debug.Log("[BossAttackManager] ■ 봉인 일섬 집행 종료 → 현재 보스 상태상 AI 재개 보류");
                return;
            }

            if (!enabled) return;

            _isStopped = false;
            TurnTowardPlayerImmediate();
            ChangeState(WardenAIState.Idle);

            if (_debugLog)
                Debug.Log("[BossAttackManager] ■ 봉인 일섬 집행 종료 → AI 재개");
        }

        public void UnlockPhase2()
        {
            foreach (var pattern in _patterns)
            {
                if (pattern == null) continue;
                pattern.UnlockPhase2();
            }
        }

        public void OnDilPhaseEnter()
        {
            _pausedBySealExecution = false;
            _isStopped = true;
            SetArmsRecoveryVuln(false);
            InterruptCurrentPattern();
            StopVelocity();

            if (_debugLog)
                Debug.Log("[BossAttackManager] ▶ DilPhase 진입 → 이동/패턴 정지");
        }

        public void OnDilPhaseExit()
        {
            _isStopped = false;
            TurnTowardPlayerImmediate();
            ChangeState(WardenAIState.Idle);

            if (_debugLog)
                Debug.Log("[BossAttackManager] ■ DilPhase 종료 → Idle 복귀");
        }

        public void OnPhaseChanged(int newPhase)
        {
            if (newPhase != 2) return;

            if (_data != null)
                _currentMoveSpeed = _data.phase2MoveSpeed;

            UnlockPhase2();

            if (_debugLog)
                Debug.Log("[BossAttackManager] ▶ 2페이즈 전환 — 이동 속도/패턴 강화 적용");
        }

        public void OnDead()
        {
            _isStopped = true;
            SetArmsRecoveryVuln(false);
            InterruptCurrentPattern();
            StopVelocity();
            enabled = false;

            if (_debugLog)
                Debug.Log("[BossAttackManager] ✅ 처치 → AI/공격 정지");
        }

        private void ResolveReferences()
        {
            if (!_autoResolveReferences) return;

            Transform root = transform.root;

            if (_dataManager == null) _dataManager = root.GetComponentInChildren<BossDataManager>(true);
            if (_partManager == null) _partManager = root.GetComponentInChildren<BossPartManager>(true);
            if (_eventHub == null) _eventHub = root.GetComponentInChildren<BossEventHub>(true);
            if (_rigid2D == null) _rigid2D = root.GetComponentInChildren<Rigidbody2D>(true);

            if (_moveRoot == null)
                _moveRoot = _rigid2D != null ? _rigid2D.transform : root;

            RefreshArmPartsFromManager();
        }

        private void RefreshArmPartsFromManager()
        {
            _armParts ??= new List<BossWardenPart>();

            if (_partManager == null) return;

            foreach (var part in _partManager.GetAllArmParts())
            {
                if (part == null) continue;
                if (!_armParts.Contains(part))
                    _armParts.Add(part);
            }
        }

        private void ResolvePlayer()
        {
            if (_playerTransform != null) return;

            var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (players.Length > 0)
                _playerTransform = players[0].transform;
            else if (_debugLog)
                Debug.LogWarning("[BossAttackManager] PlayerMoveController 탐색 실패.");
        }

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
            if (_rigid2D == null) return;

            if (_currentState != WardenAIState.Chase)
            {
                StopVelocity();
                return;
            }

            _rigid2D.linearVelocity = _facingDir * _currentMoveSpeed;
        }

        private void StopVelocity()
        {
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;
        }

        private void CheckChaseTransition()
        {
            if (_isExecuting) return;
            if (_currentPattern != null) return;
            if (_isStopped) return;
            if (_playerTransform == null || _data == null || _moveRoot == null) return;

            float dist = Vector2.Distance(_moveRoot.position, _playerTransform.position);
            if (dist > _data.patternRange)
                ChangeState(WardenAIState.Chase);
        }

        private void CheckIdleTransition()
        {
            if (_playerTransform == null || _data == null || _moveRoot == null) return;

            float dist = Vector2.Distance(_moveRoot.position, _playerTransform.position);
            if (dist <= _data.patternRange)
                ChangeState(WardenAIState.Idle);
        }

        private void UpdateFacingTowardPlayer()
        {
            if (_playerTransform == null || _moveRoot == null) return;
            if (_flipCooldownTimer > 0f) return;

            Vector2 newDir = ((Vector2)_playerTransform.position - (Vector2)_moveRoot.position).normalized;
            if (newDir.sqrMagnitude < 0.01f) return;

            if (newDir != _facingDir)
            {
                _facingDir = newDir;
                _flipCooldownTimer = _data != null ? _data.flipCooldown : 0.5f;
                OnFacingChanged?.Invoke(_facingDir);
            }
        }

        private void TrySelectPattern()
        {
            RequestAttack();
        }


        private bool IsSupportedPattern(BossPatternBase pattern)
        {
            return pattern is BossPattern_Slam || pattern is BossPattern_Swing;
        }

        private BossPatternBase SelectPattern()
        {
            _availablePatterns.Clear();

            foreach (var pattern in _patterns)
            {
                if (pattern == null) continue;
                if (!IsSupportedPattern(pattern)) continue;
                if (!pattern.CanExecute) continue;
                if (!pattern.IsAvailable) continue;
                _availablePatterns.Add(pattern);
            }

            if (_availablePatterns.Count == 0)
                return null;

            int index = UnityEngine.Random.Range(0, _availablePatterns.Count);
            return _availablePatterns[index];
        }

        private IEnumerator ExecutePattern(BossPatternBase pattern)
        {
            string patternName = pattern.GetType().Name;

            _eventHub?.RaiseAttackStarted(pattern);

            Debug.Log($"[BossAttackManager] ▶ [{patternName}] Warning");
            SetAttackState(WardenAIState.Warning, pattern);
            _eventHub?.RaiseAttackWarning(pattern);
            yield return StartCoroutine(pattern.ExecuteWarning());

            if (ShouldStopExecution())
            {
                CleanupAfterStop(pattern);
                yield break;
            }

            Debug.Log($"[BossAttackManager] ▶ [{patternName}] Active");
            SetAttackState(WardenAIState.Active, pattern);
            _eventHub?.RaiseAttackActive(pattern);
            yield return StartCoroutine(pattern.ExecuteActive());

            if (ShouldStopExecution())
            {
                CleanupAfterStop(pattern);
                yield break;
            }

            Debug.Log($"[BossAttackManager] ▶ [{patternName}] Recovery");
            SetAttackState(WardenAIState.Recovery, pattern);
            _eventHub?.RaiseAttackRecovery(pattern);

            SetArmsRecoveryVuln(true);
            yield return StartCoroutine(pattern.ExecuteRecovery());
            SetArmsRecoveryVuln(false);

            if (ShouldStopExecution())
            {
                CleanupAfterStop(pattern);
                yield break;
            }

            Debug.Log($"[BossAttackManager] ✅ [{patternName}] 패턴 완료");
            CompletePattern(pattern);
        }

        private bool ShouldStopExecution()
        {
            return _isStopped;
        }

        private void SetAttackState(WardenAIState newState, BossPatternBase pattern)
        {
            _currentPattern = pattern;
            ChangeState(newState, pattern);
        }

        private void CompletePattern(BossPatternBase pattern)
        {
            _patternCoroutine = null;
            _currentPattern = null;
            _isExecuting = false;
            SetArmsRecoveryVuln(false);

            _eventHub?.RaiseAttackEnded(pattern);
            CompleteAttackState(pattern);
        }

        private void CleanupAfterStop(BossPatternBase pattern)
        {
            _patternCoroutine = null;
            _currentPattern = null;
            _isExecuting = false;
            SetArmsRecoveryVuln(false);

            _eventHub?.RaiseAttackInterrupted(pattern);
            InterruptAttackState(pattern);
        }

        private void CompleteAttackState(BossPatternBase pattern)
        {
            if (_currentPattern == pattern)
                _currentPattern = null;

            SetArmsRecoveryVuln(false);

            if (!_isStopped)
                ChangeState(WardenAIState.Idle, null);
        }

        private void InterruptAttackState(BossPatternBase pattern)
        {
            if (_currentPattern == pattern)
                _currentPattern = null;

            SetArmsRecoveryVuln(false);
            _currentState = WardenAIState.Idle;
            OnStateChanged?.Invoke(_currentState, null);
        }

        private void ChangeState(WardenAIState newState, BossPatternBase patternOverride = null)
        {
            if (_currentState == newState) return;
            _currentState = newState;
            OnStateChanged?.Invoke(_currentState, patternOverride ?? _currentPattern);

            if (_debugLog)
                Debug.Log($"[BossAttackManager] 상태 → {_currentState}");
        }

        private void TurnTowardPlayerImmediate()
        {
            if (_playerTransform == null || _moveRoot == null) return;

            Vector2 dir = ((Vector2)_playerTransform.position - (Vector2)_moveRoot.position).normalized;
            if (dir.sqrMagnitude < 0.01f) return;

            _facingDir = dir;
            _flipCooldownTimer = 0f;
            OnFacingChanged?.Invoke(_facingDir);
        }

        private void SetArmsRecoveryVuln(bool isVuln)
        {
            RefreshArmPartsFromManager();

            foreach (var part in _armParts)
            {
                if (part == null) continue;
                if (part.IsSealed) continue;
                part.SetRecoveryVuln(isVuln);
            }
        }
    }
}
