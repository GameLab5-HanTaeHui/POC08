// ============================================================
// SealExecutionRunner.cs  v2.0
// 봉인 집행 실행 관리자
//
// [v2.0 변경 — 즉시 집행 방식으로 전환]
//
//   [제거]
//     DetectSealInput() 코루틴 루프 전체
//     _holdTimer 필드 + 홀드 타이머 누적 로직
//     ExecuteSeal() 내부 while (elapsed < holdTime) 2차 홀드 루프
//     BlockAll() / BlockExceptSeal() 입력 차단 (집행 중 이동/공격 허용)
//
//   [변경]
//     PlayerInputHandler.OnSeal 이벤트 구독
//     → F키 pressed 순간 GetBestTarget() 조회
//     → 대상 있으면 즉시 ExecuteSeal() 호출
//     → 대상 없으면 무시 (범위 밖)
//
//   [등급별 처리 — v2.0]
//     Normal : pressed → 즉시 완료
//     Part   : pressed → 즉시 완료 → 짧은 슬로우 연출
//     Core   : pressed → 즉시 완료 → 강한 슬로우 → FinalSeal
//
//   [재집행 방지]
//     _cooldownTimer: 집행 완료 후 0.3초 쿨다운 유지
//     (연속 입력으로 중복 집행 방지)
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 봉인 집행 실행 관리자. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [집행 흐름 v2.0]
    ///   F키 pressed (OnSeal 이벤트)
    ///   → GetBestTarget(playerPos)
    ///   → 대상 있음 → ExecuteSeal(target) 코루틴
    ///     → 등급별 슬로우 연출
    ///     → SealableComponent.ExecuteSeal()
    ///     → 슬로우 복구
    ///   → 대상 없음 → 무시
    ///
    /// [외부 API]
    ///   Initialize(BossDataSO)   DataSO 주입
    ///   ForceStop()              집행 강제 중단 (보스 처치 등)
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealExecutionRunner : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// 범용 보스 DataSO.
        /// 슬로우 배율 참조.
        /// BossWardenCore.Initialize() 에서 주입 or Inspector 직접 연결.
        /// </summary>
        [Tooltip("BossDataSO. 슬로우 배율 참조. 필수.")]
        [SerializeField] private BossDataSO _bossData;

        [Header("── 집행 이벤트 관리자 ──────────────────────")]

        /// <summary>
        /// SealExecutionEvent 참조.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("SealExecutionEvent. 미연결 시 자동 탐색.")]
        [SerializeField] private SealExecutionEvent _executionEvent;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>플레이어 Transform. 거리 계산용.</summary>
        private Transform _playerTransform;

        /// <summary>플레이어 입력 핸들러.</summary>
        private PlayerInputHandler _input;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>집행 실행 중 플래그. 중복 집행 방지.</summary>
        private bool _isExecuting;

        /// <summary>
        /// 집행 완료 후 쿨다운 타이머.
        /// 연속 입력으로 인한 중복 집행 방지.
        /// </summary>
        private float _cooldownTimer;

        /// <summary>강제 중단 플래그. ForceStop() 호출 시 true.</summary>
        private bool _forceStop;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_executionEvent == null)
                _executionEvent = GetComponent<SealExecutionEvent>();
        }

        private void Start()
        {
            if (_executionEvent == null)
            {
                Debug.LogError("[SealExecutionRunner] SealExecutionEvent 미연결 — 비활성.");
                enabled = false;
                return;
            }

            // 플레이어 탐색
            var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (players.Length > 0)
                _playerTransform = players[0].transform;
            else
                Debug.LogWarning("[SealExecutionRunner] PlayerMoveController 탐색 실패.");

            _input = PlayerInputHandler.Instance;

            // F키 pressed 이벤트 구독
            if (_input != null)
            {
                _input.OnSeal -= HandleSealPressed;
                _input.OnSeal += HandleSealPressed;
                Debug.Log("[SealExecutionRunner] OnSeal 이벤트 구독 완료");
            }
            else
            {
                Debug.LogError("[SealExecutionRunner] PlayerInputHandler 탐색 실패.");
            }
        }

        private void OnDestroy()
        {
            RestoreTimeScale();

            if (_input != null)
                _input.OnSeal -= HandleSealPressed;
        }

        private void Update()
        {
            // 쿨다운 타이머 감소
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.unscaledDeltaTime;
        }

        // ══════════════════════════════════════════════════════
        // 입력 처리 — F키 pressed
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerInputHandler.OnSeal 수신.
        /// F키 pressed 순간 1회 호출.
        ///
        /// [스킵 조건]
        ///   _isExecuting  : 집행 중
        ///   _cooldownTimer: 쿨다운 중
        ///   _forceStop    : 강제 중단
        ///
        /// [집행 조건]
        ///   GetBestTarget() 가 null 이 아님 → 범위 내 집행 가능 대상 존재
        /// </summary>
        private void HandleSealPressed()
        {
            // 스킵 조건
            if (_isExecuting)
            {
                Debug.Log("[SealExecutionRunner] 집행 중 — 입력 무시");
                return;
            }

            if (_cooldownTimer > 0f)
            {
                Debug.Log($"[SealExecutionRunner] 쿨다운 중({_cooldownTimer:F2}s) — 입력 무시");
                return;
            }

            if (_forceStop)
            {
                Debug.Log("[SealExecutionRunner] ForceStop 상태 — 입력 무시");
                return;
            }

            // 최적 집행 대상 조회
            Vector2 playerPos = _playerTransform != null
                ? (Vector2)_playerTransform.position
                : Vector2.zero;

            SealableComponent target = _executionEvent.GetBestTarget(playerPos);

            if (target == null)
            {
                Debug.Log("[SealExecutionRunner] 범위 내 집행 가능 대상 없음 — 무시");
                return;
            }

            // 즉시 집행
            StartCoroutine(ExecuteSeal(target));
        }

        // ══════════════════════════════════════════════════════
        // 집행 실행
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 집행 코루틴. (v2.0)
        ///
        /// [등급별 처리]
        ///   Normal : 즉시 완료. 슬로우 없음.
        ///   Part   : 즉시 완료 → 짧은 슬로우 연출 (타격감).
        ///            partSealSlowTimeScale / partSealSlowDuration
        ///   Core   : 즉시 완료 → 강한 슬로우.
        ///            finalSealSlowTimeScale → FinalSeal 진입
        ///
        /// [v2.0 제거]
        ///   while (elapsed < holdTime) 2차 홀드 루프 없음.
        ///   F키 pressed 순간 바로 완료 처리.
        /// </summary>
        private IEnumerator ExecuteSeal(SealableComponent target)
        {
            _isExecuting = true;

            var effect = GetEffectComponent(target);

            // 집행 시작 연출
            effect?.OnExecutionStart();

            // Core 등급: 강한 슬로우 시작
            if (target.Grade == SealGrade.Core && _bossData?.SealData != null)
            {
                Time.timeScale = _bossData.SealData.finalSealSlowTimeScale;
                Debug.Log($"[SealExecutionRunner] Core 슬로우 → {_bossData.SealData.finalSealSlowTimeScale}");
            }

            Debug.Log($"[SealExecutionRunner] ▶ {target.name} 집행 시작 | 등급:{target.Grade}");

            // ── 즉시 완료 처리 ──────────────────────
            // 슬로우 복구 (Core 슬로우는 SealStateManager가 Dead 진입 시 복구)
            if (target.Grade != SealGrade.Core)
                RestoreTimeScale();

            // SealableComponent 집행 완료
            target.ExecuteSeal();

            // 완료 연출
            effect?.OnExecutionComplete();

            Debug.Log($"[SealExecutionRunner] ✅ {target.name} 집행 완료 | 등급:{target.Grade}");

            // Part 등급: 짧은 슬로우 연출 (타격감)
            if (target.Grade == SealGrade.Part && _bossData?.SealData != null)
            {
                float slowScale = _bossData.SealData.partSealSlowTimeScale;
                float slowDuration = _bossData.SealData.partSealSlowDuration;

                Time.timeScale = slowScale;
                yield return new WaitForSecondsRealtime(slowDuration);
                RestoreTimeScale();
            }

            _isExecuting = false;
            _cooldownTimer = 0.3f;

            yield return null;
        }

        // ══════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 대상 오브젝트에서 SealExecutionEffect 탐색.
        /// 없으면 null 반환 (선택 컴포넌트).
        /// </summary>
        private SealExecutionEffect GetEffectComponent(SealableComponent target)
        {
            if (target == null) return null;
            return target.GetComponent<SealExecutionEffect>();
        }

        /// <summary>TimeScale 1f 복구.</summary>
        private void RestoreTimeScale()
        {
            Time.timeScale = 1f;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossDataSO 주입.
        /// BossWardenCore.Initialize() 에서 호출.
        /// </summary>
        public void Initialize(BossDataSO data)
        {
            _bossData = data;
        }

        /// <summary>
        /// 집행 강제 중단.
        /// 보스 처치 / 씬 전환 시 호출.
        /// </summary>
        public void ForceStop()
        {
            _forceStop = true;
            _isExecuting = false;
            RestoreTimeScale();
            Debug.Log("[SealExecutionRunner] ForceStop 호출");
        }

        // ══════════════════════════════════════════════════════
        // 디버그
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        [ContextMenu("DEBUG — 왼팔 즉시 집행 시도")]
        private void Debug_ForceSeal()
        {
            if (!Application.isPlaying) return;
            HandleSealPressed();
        }
#endif
    }
}