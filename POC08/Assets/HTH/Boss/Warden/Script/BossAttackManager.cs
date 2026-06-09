// ============================================================
// BossAttackManager.cs  v2.0
// Boss_Warden 공격 패턴 선택 / 실행 관리자 — Step 6
//
// [역할]
//   BossWardenAI 에서 분리된 공격 패턴 선택/실행 책임을 담당한다.
//   AI는 "공격할 수 있다"고 판단하면 RequestAttack() 만 호출하고,
//   실제 패턴 선택 / Warning / Active / Recovery 실행은 이 매니저가 처리한다.
//
// [Step 13 변경]
//   - BossWardenAI 내부 패턴 fallback 제거
//   - BossAttackManager._patterns 를 유일한 패턴 목록으로 사용
//   - AI에서 패턴 목록을 주입받는 호환 경로 제거
//
// [부착 위치]
//   Boss_Warden Root 오브젝트
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 공격 패턴 선택 / 실행 관리자.
    /// </summary>
    [DefaultExecutionOrder(-5)]
    public class BossAttackManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO ──────────────────────")]

        [Tooltip("BossWardenDataSO. BossWardenCore/BossWardenAI 초기화에서 주입된다.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 연결 컴포넌트 ──────────────────────")]

        [Tooltip("BossWardenAI. 필수 연결.")]
        [SerializeField] private BossWardenAI _ai;

        [Tooltip("BossEventHub. 선택 연결.")]
        [SerializeField] private BossEventHub _eventHub;

        [Header("── 패턴 목록 ──────────────────────")]

        [Tooltip("BossPattern_XXX 목록. Step 13 이후 이 목록만 사용한다.")]
        [SerializeField] private List<BossPatternBase> _patterns = new();

        [Header("── 팔 부위 연결 ──────────────────────")]

        [Tooltip("왼팔 BossWardenPart. Recovery 취약 배율 제어용.")]
        [SerializeField] private BossWardenPart _armL;

        [Tooltip("오른팔 BossWardenPart. Recovery 취약 배율 제어용.")]
        [SerializeField] private BossWardenPart _armR;

        // ══════════════════════════════════════════════════════
        // Runtime
        // ══════════════════════════════════════════════════════

        private BossPatternBase _currentPattern;
        private Coroutine _patternCoroutine;
        private bool _isExecuting;

        private readonly List<BossPatternBase> _availablePatterns = new();

        // ══════════════════════════════════════════════════════
        // Properties
        // ══════════════════════════════════════════════════════

        public bool IsExecuting => _isExecuting;
        public BossPatternBase CurrentPattern => _currentPattern;

        // ══════════════════════════════════════════════════════
        // Unity Lifecycle
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();
            if (_ai == null) _ai = GetComponentInChildren<BossWardenAI>(true);

            if (_eventHub == null) _eventHub = GetComponent<BossEventHub>();
            if (_eventHub == null) _eventHub = GetComponentInParent<BossEventHub>();
        }

        // ══════════════════════════════════════════════════════
        // Initialize
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenAI.Initialize() 에서 호출된다.
        /// Step 13 이후 패턴 목록은 이 Manager의 _patterns만 사용한다.
        /// </summary>
        public void Initialize(
            BossWardenDataSO data,
            BossWardenAI ai,
            BossWardenPart armL,
            BossWardenPart armR)
        {
            _data = data;
            if (ai != null) _ai = ai;
            if (armL != null) _armL = armL;
            if (armR != null) _armR = armR;

            if (_ai == null)
                Debug.LogError("[BossAttackManager] BossWardenAI 미연결.");

            if (_patterns == null || _patterns.Count == 0)
            {
                Debug.LogError("[BossAttackManager] 패턴 목록이 비어 있습니다. BossAttackManager._patterns에 패턴 5개를 연결하세요.");
                return;
            }

            foreach (var pattern in _patterns)
            {
                if (pattern == null) continue;
                pattern.Initialize(data);
            }

            Debug.Log("[BossAttackManager] 초기화 완료 — Manager 패턴 목록 전용");
        }

        // ══════════════════════════════════════════════════════
        // Public API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// AI가 Idle 상태에서 공격을 요청할 때 호출한다.
        /// 사용 가능한 패턴이 없으면 false를 반환한다.
        /// </summary>
        public bool RequestAttack()
        {
            if (_isExecuting) return false;
            if (_currentPattern != null) return false;
            if (_ai != null && _ai.IsStopped) return false;
            if (_patterns == null || _patterns.Count == 0) return false;

            BossPatternBase selected = SelectPattern();
            if (selected == null) return false;

            _currentPattern = selected;
            _isExecuting = true;
            _patternCoroutine = StartCoroutine(ExecutePattern(selected));
            return true;
        }

        /// <summary>
        /// 현재 실행 중인 패턴을 강제 중단한다.
        /// DilPhase 진입 / Dead 시 BossWardenAI가 호출한다.
        /// </summary>
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
            _ai?.InterruptAttackState(interrupted);
        }

        /// <summary>2페이즈 전환 시 모든 패턴을 2페이즈 사용 가능 상태로 전환한다.</summary>
        public void UnlockPhase2()
        {
            foreach (var pattern in _patterns)
            {
                if (pattern == null) continue;
                pattern.UnlockPhase2();
            }
        }

        // ══════════════════════════════════════════════════════
        // Pattern Selection
        // ══════════════════════════════════════════════════════

        private BossPatternBase SelectPattern()
        {
            _availablePatterns.Clear();

            foreach (var pattern in _patterns)
            {
                if (pattern == null) continue;
                if (!pattern.CanExecute) continue;
                if (!pattern.IsAvailable) continue;

                _availablePatterns.Add(pattern);
            }

            if (_availablePatterns.Count == 0)
                return null;

            int index = Random.Range(0, _availablePatterns.Count);
            return _availablePatterns[index];
        }

        // ══════════════════════════════════════════════════════
        // Pattern Execution
        // ══════════════════════════════════════════════════════

        private IEnumerator ExecutePattern(BossPatternBase pattern)
        {
            string patternName = pattern.GetType().Name;

            _eventHub?.RaiseAttackStarted(pattern);

            // ── Warning ──
            Debug.Log($"[BossAttackManager] ▶ [{patternName}] Warning");
            _ai?.SetAttackState(BossWardenAI.WardenAIState.Warning, pattern);
            _eventHub?.RaiseAttackWarning(pattern);
            yield return StartCoroutine(pattern.ExecuteWarning());

            if (ShouldStopExecution())
            {
                CleanupAfterStop(pattern);
                yield break;
            }

            // ── Active ──
            Debug.Log($"[BossAttackManager] ▶ [{patternName}] Active");
            _ai?.SetAttackState(BossWardenAI.WardenAIState.Active, pattern);
            _eventHub?.RaiseAttackActive(pattern);
            yield return StartCoroutine(pattern.ExecuteActive());

            if (ShouldStopExecution())
            {
                CleanupAfterStop(pattern);
                yield break;
            }

            // ── Recovery ──
            Debug.Log($"[BossAttackManager] ▶ [{patternName}] Recovery");
            _ai?.SetAttackState(BossWardenAI.WardenAIState.Recovery, pattern);
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
            return _ai != null && _ai.IsStopped;
        }

        private void CompletePattern(BossPatternBase pattern)
        {
            _patternCoroutine = null;
            _currentPattern = null;
            _isExecuting = false;
            SetArmsRecoveryVuln(false);

            _eventHub?.RaiseAttackEnded(pattern);
            _ai?.CompleteAttackState(pattern);
        }

        private void CleanupAfterStop(BossPatternBase pattern)
        {
            _patternCoroutine = null;
            _currentPattern = null;
            _isExecuting = false;
            SetArmsRecoveryVuln(false);

            _eventHub?.RaiseAttackInterrupted(pattern);
            _ai?.InterruptAttackState(pattern);
        }

        private void SetArmsRecoveryVuln(bool isVulnerable)
        {
            // AI 쪽 public API를 우선 사용한다.
            // 이후 BossPartManager 단계가 더 진행되면 이 부분은 PartManager 기반으로 교체 가능하다.
            if (_ai != null)
            {
                _ai.SetRecoveryVulnerableFromAttackManager(isVulnerable);
                return;
            }

            if (_armL != null && !_armL.IsSealed) _armL.SetRecoveryVuln(isVulnerable);
            if (_armR != null && !_armR.IsSealed) _armR.SetRecoveryVuln(isVulnerable);
        }
    }
}
