// ============================================================
// SealDebugTracker.cs  v1.0
// 봉인 집행 경로 전체 추적 디버그 컴포넌트
//
// [역할]
//   봉인 집행이 작동하지 않을 때 어디서 끊기는지 추적.
//
// [추적 항목]
//   1. SealableComponent 봉인도 수치 (매 프레임 or 변화 시)
//   2. SealReadyNotifier 등록 여부
//   3. SealExecutionEvent 대상 목록 상태
//   4. PlayerInputHandler.IsSealHeld 감지 여부
//   5. SealExecutionRunner 홀드 타이머 상태
//   6. GetBestTarget() 결과
//
// [부착 위치]
//   Boss_Warden Root 오브젝트에 부착.
//   플레이 중 Inspector 에서 실시간 확인 가능.
//
// [제거 방법]
//   버티컬 슬라이스 이후 이 컴포넌트 전체 삭제.
//
// [namespace] SEAL
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 봉인 집행 경로 전체 추적 디버그 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [Inspector 실시간 확인 항목]
    ///   _leftArmGauge   : 왼팔 현재 봉인도 %
    ///   _rightArmGauge  : 오른팔 현재 봉인도 %
    ///   _leftArmReady   : 왼팔 집행 가능 여부
    ///   _rightArmReady  : 오른팔 집행 가능 여부
    ///   _notifierCount  : SealReadyNotifier 등록 수
    ///   _readyListCount : 집행 가능 대상 수
    ///   _isSealHeld     : F키 홀드 여부
    ///   _holdTimer      : 홀드 타이머
    ///   _bestTarget     : 현재 최적 집행 대상 이름
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealDebugTracker : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector — 실시간 상태 표시 (ReadOnly)
        // ══════════════════════════════════════════════════════

        [Header("── 봉인도 수치 ──────────────────────")]

        [Tooltip("왼팔 봉인도 % (0~100)")]
        [SerializeField] private float _leftArmGaugePercent;

        [Tooltip("오른팔 봉인도 % (0~100)")]
        [SerializeField] private float _rightArmGaugePercent;

        [Tooltip("왼팔 IsSealReady")]
        [SerializeField] private bool _leftArmReady;

        [Tooltip("오른팔 IsSealReady")]
        [SerializeField] private bool _rightArmReady;

        [Tooltip("왼팔 IsSealed")]
        [SerializeField] private bool _leftArmSealed;

        [Tooltip("오른팔 IsSealed")]
        [SerializeField] private bool _rightArmSealed;

        [Header("── SealExecutionEvent 상태 ──────────────────────")]

        [Tooltip("등록된 SealReadyNotifier 수. 0이면 Prefab 구조 문제.")]
        [SerializeField] private int _notifierCount;

        [Tooltip("현재 집행 가능 대상 수.")]
        [SerializeField] private int _readyListCount;

        [Tooltip("현재 최적 집행 대상 이름. None이면 대상 없음.")]
        [SerializeField] private string _bestTargetName = "None";

        [Header("── 입력 / 홀드 상태 ──────────────────────")]

        [Tooltip("PlayerInputHandler.IsSealHeld (F키 또는 우클릭)")]
        [SerializeField] private bool _isSealHeld;

        [Tooltip("SealExecutionRunner 내부 홀드 타이머. 0이면 홀드 미감지.")]
        [SerializeField] private float _holdTimer;

        [Tooltip("집행 실행 중 여부.")]
        [SerializeField] private bool _isExecuting;

        [Header("── SealStateManager 상태 ──────────────────────")]

        [Tooltip("현재 봉인 상태.")]
        [SerializeField] private string _sealState = "None";

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        private SealGaugeManager _gaugeManager;
        private SealExecutionEvent _executionEvent;
        private SealExecutionRunner _executionRunner;
        private SealStateManager _stateManager;
        private PlayerInputHandler _input;

        /// <summary>
        /// 왼팔 SealableComponent. SealGaugeManager 에서 탐색.
        /// </summary>
        private SealableComponent _leftArmSealable;

        /// <summary>
        /// 오른팔 SealableComponent. SealGaugeManager 에서 탐색.
        /// </summary>
        private SealableComponent _rightArmSealable;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _gaugeManager = GetComponent<SealGaugeManager>();
            _executionEvent = GetComponent<SealExecutionEvent>();
            _executionRunner = GetComponent<SealExecutionRunner>();
            _stateManager = GetComponent<SealStateManager>();
        }

        private void Start()
        {
            _input = PlayerInputHandler.Instance;

            // SealGaugeManager 에서 Part 등급 SealableComponent 탐색
            if (_gaugeManager != null)
            {
                foreach (var s in _gaugeManager.GetAllSealables())
                {
                    if (s == null || s.grade != SealGrade.Part) continue;

                    if (s.gameObject.name.Contains("Left") || s.gameObject.name.Contains("left"))
                        _leftArmSealable = s;
                    else if (s.gameObject.name.Contains("Right") || s.gameObject.name.Contains("right"))
                        _rightArmSealable = s;
                }
            }

            // SealReadyNotifier 자동 수집 수 확인
            var notifiers = GetComponentsInChildren<SealReadyNotifier>(includeInactive: true);
            _notifierCount = notifiers.Length;

            // 초기 상태 로그
            Debug.Log($"[SealDebugTracker] 초기화 완료\n" +
                      $"  LeftArm SealableComponent: {(_leftArmSealable != null ? "✅ 탐색됨" : "❌ 없음")}\n" +
                      $"  RightArm SealableComponent: {(_rightArmSealable != null ? "✅ 탐색됨" : "❌ 없음")}\n" +
                      $"  SealReadyNotifier 수: {_notifierCount}\n" +
                      $"  SealExecutionEvent: {(_executionEvent != null ? "✅ 탐색됨" : "❌ 없음")}\n" +
                      $"  SealExecutionRunner: {(_executionRunner != null ? "✅ 탐색됨" : "❌ 없음")}\n" +
                      $"  PlayerInputHandler: {(_input != null ? "✅ 탐색됨" : "❌ 없음")}");

            if (_notifierCount == 0)
            {
                Debug.LogError("[SealDebugTracker] ⚠️ SealReadyNotifier 가 0개!\n" +
                               "LeftArm / RightArm 오브젝트에 SealReadyNotifier 컴포넌트가 부착되어 있는지 확인 필요.\n" +
                               "또는 Boss_Warden 하위 구조에 있지 않을 수 있음.");
            }

            // SealableComponent 이벤트 구독 — 봉인도 변화 감지
            SubscribeGaugeEvents();
        }

        private void Update()
        {
            UpdateGaugeDisplay();
            UpdateInputDisplay();
            UpdateStateDisplay();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독
        // ══════════════════════════════════════════════════════

        private void SubscribeGaugeEvents()
        {
            if (_leftArmSealable != null)
            {
                _leftArmSealable.OnGaugeChanged += (p) =>
                {
                    Debug.Log($"[SealDebugTracker] 왼팔 봉인도: {p * 100f:F1}%");
                };
                _leftArmSealable.OnSealRequested += (s) =>
                {
                    Debug.Log($"[SealDebugTracker] ▶ 왼팔 집행 가능! OnSealRequested 발행됨\n" +
                              $"  SealReadyNotifier 가 구독 중인지 확인 필요.");
                };
                _leftArmSealable.OnSealCompleted += () =>
                {
                    Debug.Log($"[SealDebugTracker] ✅ 왼팔 봉인 집행 완료!");
                };
            }

            if (_rightArmSealable != null)
            {
                _rightArmSealable.OnGaugeChanged += (p) =>
                {
                    Debug.Log($"[SealDebugTracker] 오른팔 봉인도: {p * 100f:F1}%");
                };
                _rightArmSealable.OnSealRequested += (s) =>
                {
                    Debug.Log($"[SealDebugTracker] ▶ 오른팔 집행 가능! OnSealRequested 발행됨\n" +
                              $"  SealReadyNotifier 가 구독 중인지 확인 필요.");
                };
                _rightArmSealable.OnSealCompleted += () =>
                {
                    Debug.Log($"[SealDebugTracker] ✅ 오른팔 봉인 집행 완료!");
                };
            }

            if (_executionEvent != null)
            {
                _executionEvent.OnTargetAdded += (s) =>
                {
                    Debug.Log($"[SealDebugTracker] ▶ 집행 대상 추가됨: {s?.gameObject.name}\n" +
                              $"  현재 총 대상: {_executionEvent.GetReadyCount()}개");
                };
                _executionEvent.OnTargetRemoved += (s) =>
                {
                    Debug.Log($"[SealDebugTracker] ■ 집행 대상 제거됨: {s?.gameObject.name}\n" +
                              $"  현재 총 대상: {_executionEvent.GetReadyCount()}개");
                };
            }
        }

        // ══════════════════════════════════════════════════════
        // 실시간 업데이트
        // ══════════════════════════════════════════════════════

        /// <summary>봉인도 수치 실시간 Inspector 표시.</summary>
        private void UpdateGaugeDisplay()
        {
            if (_leftArmSealable != null)
            {
                _leftArmGaugePercent = _leftArmSealable.UIPercent * 100f;
                _leftArmReady = _leftArmSealable.IsSealReady;
                _leftArmSealed = _leftArmSealable.IsSealed;
            }

            if (_rightArmSealable != null)
            {
                _rightArmGaugePercent = _rightArmSealable.UIPercent * 100f;
                _rightArmReady = _rightArmSealable.IsSealReady;
                _rightArmSealed = _rightArmSealable.IsSealed;
            }

            if (_executionEvent != null)
            {
                _readyListCount = _executionEvent.GetReadyCount();

                // 최적 대상 이름 표시
                var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
                if (players.Length > 0)
                {
                    var best = _executionEvent.GetBestTarget(players[0].transform.position);
                    _bestTargetName = best != null ? best.gameObject.name : "None";
                }
            }
        }

        /// <summary>입력 / 홀드 상태 실시간 Inspector 표시.</summary>
        private void UpdateInputDisplay()
        {
            if (_input != null)
                _isSealHeld = _input.IsSealHeld;

            // SealExecutionRunner 내부 필드는 직접 접근 불가 → 로그로만 추적
            // F키 홀드 감지 시 로그
            if (_isSealHeld && _readyListCount == 0)
            {
                // 매 프레임 스팸 방지 — 0.5초마다
                if (Time.unscaledTime - _lastWarnTime > 0.5f)
                {
                    Debug.LogWarning($"[SealDebugTracker] ⚠️ F키 홀드 중이지만 집행 가능 대상이 없음!\n" +
                                     $"  왼팔 봉인도: {_leftArmGaugePercent:F1}% (Ready:{_leftArmReady})\n" +
                                     $"  오른팔 봉인도: {_rightArmGaugePercent:F1}% (Ready:{_rightArmReady})\n" +
                                     $"  SealReadyNotifier 등록 수: {_notifierCount}");
                    _lastWarnTime = Time.unscaledTime;
                }
            }

            if (_isSealHeld && _readyListCount > 0)
            {
                if (Time.unscaledTime - _lastInfoTime > 0.3f)
                {
                    Debug.Log($"[SealDebugTracker] F키 홀드 + 대상 있음 → 집행 진행 중\n" +
                              $"  대상: {_bestTargetName}");
                    _lastInfoTime = Time.unscaledTime;
                }
            }
        }

        private float _lastWarnTime;
        private float _lastInfoTime;

        /// <summary>SealStateManager 상태 실시간 Inspector 표시.</summary>
        private void UpdateStateDisplay()
        {
            if (_stateManager != null)
                _sealState = _stateManager.State.ToString();
        }

        // ══════════════════════════════════════════════════════
        // ContextMenu 디버그 명령
        // ══════════════════════════════════════════════════════

        [ContextMenu("디버그 — 왼팔 봉인도 즉시 100%")]
        private void Debug_FillLeftArm()
        {
            if (!Application.isPlaying) return;
            _leftArmSealable?.AddGauge(99999f);
            Debug.Log("[SealDebugTracker] 왼팔 봉인도 강제 100%");
        }

        [ContextMenu("디버그 — 오른팔 봉인도 즉시 100%")]
        private void Debug_FillRightArm()
        {
            if (!Application.isPlaying) return;
            _rightArmSealable?.AddGauge(99999f);
            Debug.Log("[SealDebugTracker] 오른팔 봉인도 강제 100%");
        }

        [ContextMenu("디버그 — 양팔 봉인도 즉시 100%")]
        private void Debug_FillBothArms()
        {
            if (!Application.isPlaying) return;
            _leftArmSealable?.AddGauge(99999f);
            _rightArmSealable?.AddGauge(99999f);
            Debug.Log("[SealDebugTracker] 양팔 봉인도 강제 100%");
        }

        [ContextMenu("디버그 — 현재 봉인 상태 전체 출력")]
        private void Debug_PrintStatus()
        {
            if (!Application.isPlaying) return;

            Debug.Log($"[SealDebugTracker] ══ 봉인 상태 전체 ══\n" +
                      $"  왼팔  봉인도: {_leftArmGaugePercent:F1}% | Ready:{_leftArmReady} | Sealed:{_leftArmSealed}\n" +
                      $"  오른팔 봉인도: {_rightArmGaugePercent:F1}% | Ready:{_rightArmReady} | Sealed:{_rightArmSealed}\n" +
                      $"  집행 대상 수: {_readyListCount}\n" +
                      $"  최적 대상: {_bestTargetName}\n" +
                      $"  SealReadyNotifier 등록: {_notifierCount}개\n" +
                      $"  F키 홀드: {_isSealHeld}\n" +
                      $"  SealState: {_sealState}\n" +
                      $"  PlayerInputHandler: {(_input != null ? "연결됨" : "❌ 없음")}");
        }
    }
}