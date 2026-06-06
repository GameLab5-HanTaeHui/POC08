// ============================================================
// SealStateManager.cs  v2.0
// 보스 봉인 상태 총괄 관리자
//
// [v2.0 변경 — Groggy 제거, DilPhase 병합]
//   제거:
//     SealBossState.Groggy
//     EnterGroggy()
//     GroggyTimerRoutine()
//     ExitGroggyFailure()
//     OnGroggyEnter / OnGroggyExit 이벤트
//     _groggyCoroutine
//
//   변경:
//     HandleAllPartsSealed() → 직접 EnterDilPhase() 호출
//     HandleCoreSealCompleted() → Groggy 분기 제거
//     ExitDilPhase() → _groggyCoroutine 정리 코드 제거
//     IBossCore 인터페이스의 OnGroggyEnter/Exit → 제거 대상
//
// [상태 정의 v2.0]
//   Idle      : 전투 대기 / 패턴 실행 중
//   DilPhase  : Part 전체 봉인 완료 → 코어 활성 → 코어 봉인도 누적 구간
//               타이머 내 목표 미달 → 실패 → Idle 복귀
//   FinalSeal : 코어 봉인도 100% → 강한 슬로우, 최종 S키 대기
//   Dead      : 최종 봉인 완료 → 보스 처치
//
// [봉인 흐름 v2.0]
//   Idle
//     → Part 전체 봉인 완료 (SealGaugeManager.OnAllPartsSealed)
//     → 즉시 DilPhase 진입
//   DilPhase
//     → 1페이즈: 코어 봉인도 목표 도달 → ForceRelease + 충격파 + 2페이즈 + Idle
//     → 2페이즈: 코어 봉인도 목표 도달 → FinalSeal 진입
//     [실패] 타이머 만료 → ForceRelease + Idle 복귀
//   FinalSeal
//     → 코어 최종 집행 완료 → Dead
//   Dead
//     → OnDead 발행 → 보스 처치 연출
//
// [부착 위치]
//   Boss_Root 오브젝트에 부착. (보스 1개당 1개)
//
// [namespace] SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 보스 봉인 상태 총괄 관리자. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [상태 전환 흐름 v2.0]
    ///   Idle
    ///   → (OnAllPartsSealed)     → DilPhase
    ///   → (1페이즈 목표 도달)     → Idle (2페이즈 전환)
    ///   → (2페이즈 목표 도달)     → FinalSeal
    ///   → (코어 최종 집행)        → Dead
    ///   → (DilPhase 타이머 만료)  → Idle (실패 루프)
    ///
    /// [외부 API]
    ///   Initialize(BossDataSO)   DataSO 주입
    ///   ConnectCore(GameObject)  코어 오브젝트 연결
    ///   ForceKill()              즉시 Dead 전환 (테스트용)
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealStateManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // 상태 정의
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 보스 상태 열거형. (v2.0)
        /// Groggy 제거 — DilPhase 로 병합.
        /// </summary>
        public enum SealBossState
        {
            /// <summary>전투 대기 / 패턴 실행 중.</summary>
            Idle,

            /// <summary>
            /// Part 전체 봉인 완료 → 코어 활성 → 코어 봉인도 누적 구간.
            /// AI 정지. 타이머 내 목표 미달 시 실패 루프.
            /// </summary>
            DilPhase,

            /// <summary>코어 봉인도 100%. 강한 슬로우. 최종 봉인 S키 대기.</summary>
            FinalSeal,

            /// <summary>최종 봉인 완료. 보스 처치.</summary>
            Dead,
        }

        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 코어 오브젝트 (필수) ──────────────────────")]

        /// <summary>
        /// 코어 GameObject.
        /// DilPhase 진입 시 SetActive(true), 종료 시 false.
        /// Inspector 미연결 시 BossWardenCore.ConnectCore() 로 주입.
        /// </summary>
        [Tooltip("코어 GameObject. 기본 SetActive=false 필요.")]
        [SerializeField] private GameObject _coreObject;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealGaugeManager 참조.
        /// Awake 에서 GetComponent 자동 탐색.
        /// OnAllPartsSealed 이벤트 구독.
        /// </summary>
        private SealGaugeManager _gaugeManager;

        /// <summary>
        /// Rigidbody2D 참조.
        /// Dead 진입 시 linearVelocity = 0 강제 적용.
        /// </summary>
        private Rigidbody2D _rigid2D;

        /// <summary>
        /// BossDataSO 참조.
        /// 타이머 수치 / 슬로우 배율 참조.
        /// BossWardenCore.Initialize() 에서 주입.
        /// </summary>
        private BossDataSO _bossData;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 봉인 상태.</summary>
        private SealBossState _state = SealBossState.Idle;

        /// <summary>
        /// 현재 페이즈 (1 or 2).
        /// DilPhase 1페이즈 성공 종료 시 2로 증가.
        /// </summary>
        private int _currentPhase = 1;

        /// <summary>
        /// DilPhase 타이머 코루틴 핸들.
        /// ExitDilPhase() 호출 시 중단.
        /// </summary>
        private Coroutine _dilPhaseCoroutine;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 상태 전환 시 발행.
        /// 파라미터: (이전 상태, 새 상태).
        /// </summary>
        public event Action<SealBossState, SealBossState> OnStateChanged;

        /// <summary>
        /// DilPhase 진입 시 발행.
        /// BossWardenCore 브리지 → BossWardenAI 정지 / BossWardenFeedback 색상 전환.
        /// </summary>
        public event Action OnDilPhaseEnter;

        /// <summary>
        /// DilPhase 종료 시 발행 (성공 or 실패).
        /// BossWardenCore 브리지 → BossWardenAI 재개 / BossWardenFeedback Idle 복귀.
        /// </summary>
        public event Action OnDilPhaseExit;

        /// <summary>
        /// 페이즈 전환 시 발행.
        /// 파라미터: 새 페이즈 번호 (2).
        /// BossWardenAI 2페이즈 패턴 강화 / BossWardenFeedback 전환 연출.
        /// </summary>
        public event Action<int> OnPhaseChanged;

        /// <summary>
        /// FinalSeal 진입 시 발행.
        /// SealExecutionRunner 최종 봉인 대기 활성.
        /// BossWardenFeedback 코어 청백 Pulse 연출.
        /// </summary>
        public event Action OnFinalSealReady;

        /// <summary>
        /// Dead 진입 시 발행.
        /// BossWardenAI 비활성 / BossWardenFeedback 처치 연출 / BattleManager 연동.
        /// </summary>
        public event Action OnDead;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 상태.</summary>
        public SealBossState State => _state;

        /// <summary>현재 페이즈.</summary>
        public int CurrentPhase => _currentPhase;

        /// <summary>DilPhase 상태 여부.</summary>
        public bool IsDilPhase => _state == SealBossState.DilPhase;

        /// <summary>FinalSeal 상태 여부.</summary>
        public bool IsFinalSeal => _state == SealBossState.FinalSeal;

        /// <summary>Dead 상태 여부.</summary>
        public bool IsDead => _state == SealBossState.Dead;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _gaugeManager = GetComponent<SealGaugeManager>();
            _rigid2D = GetComponent<Rigidbody2D>();

            if (_gaugeManager == null)
                Debug.LogWarning("[SealStateManager] SealGaugeManager 미연결.");
        }

        private void Start()
        {
            // SealGaugeManager 이벤트 구독
            if (_gaugeManager != null)
            {
                _gaugeManager.OnAllPartsSealed -= HandleAllPartsSealed;
                _gaugeManager.OnAllPartsSealed += HandleAllPartsSealed;
                _gaugeManager.OnAllPartsReleased -= HandleAllPartsReleased;
                _gaugeManager.OnAllPartsReleased += HandleAllPartsReleased;
            }

            // 코어 SealableComponent 이벤트 구독
            SubscribeCoreEvents();
        }

        private void OnDestroy()
        {
            if (_gaugeManager != null)
            {
                _gaugeManager.OnAllPartsSealed -= HandleAllPartsSealed;
                _gaugeManager.OnAllPartsReleased -= HandleAllPartsReleased;
            }

            UnsubscribeCoreEvents();
        }

        // ══════════════════════════════════════════════════════
        // 코어 이벤트 구독
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 코어 SealableComponent 이벤트 구독.
        /// ConnectCore() 호출 시 재구독.
        /// </summary>
        private void SubscribeCoreEvents()
        {
            if (_coreObject == null) return;

            var coreSealable = _coreObject.GetComponent<SealableComponent>();
            if (coreSealable == null) return;

            coreSealable.OnSealCompleted -= HandleCoreSealCompleted;
            coreSealable.OnSealCompleted += HandleCoreSealCompleted;
            coreSealable.OnPhaseTargetReached -= HandlePhaseTargetReached;
            coreSealable.OnPhaseTargetReached += HandlePhaseTargetReached;
        }

        /// <summary>코어 SealableComponent 이벤트 해제.</summary>
        private void UnsubscribeCoreEvents()
        {
            if (_coreObject == null) return;

            var coreSealable = _coreObject.GetComponent<SealableComponent>();
            if (coreSealable == null) return;

            coreSealable.OnSealCompleted -= HandleCoreSealCompleted;
            coreSealable.OnPhaseTargetReached -= HandlePhaseTargetReached;
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러 — SealGaugeManager
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 모든 Part 봉인 완료 수신.
        /// Idle 상태에서만 처리 → 즉시 DilPhase 진입.
        /// </summary>
        private void HandleAllPartsSealed()
        {
            if (_state != SealBossState.Idle) return;
            EnterDilPhase();
        }

        /// <summary>
        /// 모든 Part 봉인 해제 수신.
        /// 실패 루프 후 Idle 복귀 알림용 로그.
        /// </summary>
        private void HandleAllPartsReleased()
        {
            Debug.Log("[SealStateManager] 모든 Part 봉인 해제됨 — Idle 복귀 준비");
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러 — 코어 SealableComponent
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 코어 SealableComponent.OnSealCompleted 수신.
        ///
        /// [상태별 분기 v2.0]
        ///   FinalSeal → Dead 진입 (최종 봉인 완료)
        ///   DilPhase  → 경고 (딜페이즈 중 집행은 PhaseTargetReached 로 처리)
        ///   그 외      → 경고 + 무시
        /// </summary>
        private void HandleCoreSealCompleted()
        {
            if (_state == SealBossState.Dead) return;

            switch (_state)
            {
                case SealBossState.FinalSeal:
                    Debug.Log("[SealStateManager] 최종 봉인 완료 → Dead");
                    EnterDead();
                    break;

                case SealBossState.DilPhase:
                    Debug.LogWarning("[SealStateManager] DilPhase 중 코어 집행 — PhaseTargetReached 로 처리됩니다.");
                    break;

                default:
                    Debug.LogWarning($"[SealStateManager] 예상치 못한 상태에서 코어 집행: {_state}");
                    break;
            }
        }

        /// <summary>
        /// 코어 봉인도 페이즈 목표치 도달 수신.
        /// DilPhase 상태에서만 처리.
        ///
        /// [페이즈 분기]
        ///   1페이즈 → ExitDilPhase(false) → ForceRelease + 충격파 + 2페이즈 전환 + Idle
        ///   2페이즈 → ExitDilPhase(true)  → FinalSeal 진입
        /// </summary>
        private void HandlePhaseTargetReached()
        {
            if (_state != SealBossState.DilPhase) return;

            bool isFinalSeal = _currentPhase >= 2;
            Debug.Log($"[SealStateManager] 코어 봉인도 목표 도달 | 페이즈:{_currentPhase} 최종봉인:{isFinalSeal}");

            ExitDilPhase(isFinalSeal);
        }

        // ══════════════════════════════════════════════════════
        // 상태 전환 — DilPhase
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// DilPhase 진입.
        /// Part 전체 봉인 완료 시 HandleAllPartsSealed() 에서 호출.
        ///
        /// [처리]
        ///   코어 SetActive(true)
        ///   코어 게이지 ActivateGauge(true) — 봉인도 누적 허용
        ///   타이머 시작 (dilPhaseDuration)
        ///   OnDilPhaseEnter 발행 → BossWardenCore 브리지 → AI 정지 / Feedback 색상
        /// </summary>
        private void EnterDilPhase()
        {
            if (_state == SealBossState.Dead) return;

            SetState(SealBossState.DilPhase);

            // 코어 활성화
            ActivateCore(true);

            // 코어 게이지 활성
            _gaugeManager?.ActivateCore(true);

            // AI 정지 + Feedback 색상 전환
            OnDilPhaseEnter?.Invoke();

            // 타이머 시작
            if (_dilPhaseCoroutine != null) StopCoroutine(_dilPhaseCoroutine);
            _dilPhaseCoroutine = StartCoroutine(DilPhaseTimerRoutine());

            float duration = (_bossData as BossWardenDataSO)?.dilPhaseDuration ?? 10f;
            Debug.Log($"[SealStateManager] ▶ DilPhase 진입 | 타이머:{duration:F1}초 | 페이즈:{_currentPhase}");
        }

        /// <summary>
        /// DilPhase 타이머 코루틴.
        /// dilPhaseDuration 경과 시 실패 종료.
        /// </summary>
        private IEnumerator DilPhaseTimerRoutine()
        {
            float duration = (_bossData as BossWardenDataSO)?.dilPhaseDuration ?? 10f;
            yield return new WaitForSecondsRealtime(duration);

            _dilPhaseCoroutine = null;

            if (_state == SealBossState.DilPhase)
            {
                Debug.Log("[SealStateManager] DilPhase 타이머 만료 → 실패 루프");
                ExitDilPhase(isFinalSeal: false);
            }
        }

        /// <summary>
        /// DilPhase 종료.
        ///
        /// [isFinalSeal = false — 실패 or 1페이즈 성공]
        ///   코어 비활성
        ///   ForceRelease (저항 횟수 유지)
        ///   OnDilPhaseExit 발행 → AI 재개
        ///   1페이즈 → 2페이즈 전환 OnPhaseChanged(2)
        ///   Idle 복귀
        ///
        /// [isFinalSeal = true — 2페이즈 성공]
        ///   코어 비활성 (최종 봉인 별도 처리)
        ///   FinalSeal 진입
        /// </summary>
        private void ExitDilPhase(bool isFinalSeal)
        {
            // 타이머 중단
            if (_dilPhaseCoroutine != null)
            {
                StopCoroutine(_dilPhaseCoroutine);
                _dilPhaseCoroutine = null;
            }

            // 코어 게이지 비활성
            _gaugeManager?.ActivateCore(false);

            if (isFinalSeal)
            {
                // 2페이즈 성공 → FinalSeal
                ActivateCore(false);
                EnterFinalSeal();
                return;
            }

            // 실패 or 1페이즈 성공 → Idle 복귀
            ActivateCore(false);

            // 모든 Part 봉인 해제 (저항 횟수 유지)
            _gaugeManager?.ReleaseAllParts(resetSealCount: false);

            // AI 재개 + Feedback Idle 복귀
            OnDilPhaseExit?.Invoke();

            // 1페이즈 → 2페이즈 전환
            if (_currentPhase == 1)
            {
                _currentPhase = 2;
                OnPhaseChanged?.Invoke(2);
                Debug.Log("[SealStateManager] 2페이즈 전환");
            }

            SetState(SealBossState.Idle);
            Debug.Log("[SealStateManager] ■ DilPhase 종료 → ForceRelease + Idle 복귀");
        }

        // ══════════════════════════════════════════════════════
        // 상태 전환 — FinalSeal
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// FinalSeal 진입.
        /// 2페이즈 DilPhase 성공 시 ExitDilPhase(true) 에서 호출.
        ///
        /// [처리]
        ///   강한 슬로우 적용 (finalSealSlowTimeScale)
        ///   OnFinalSealReady 발행 → SealExecutionRunner 최종 봉인 S키 대기 활성
        /// </summary>
        private void EnterFinalSeal()
        {
            if (_state == SealBossState.Dead) return;

            SetState(SealBossState.FinalSeal);

            // 강한 슬로우
            if (_bossData?.SealData != null)
                Time.timeScale = _bossData.SealData.finalSealSlowTimeScale;

            OnFinalSealReady?.Invoke();

            Debug.Log($"[SealStateManager] ▶ FinalSeal 진입 | 슬로우:{_bossData?.SealData?.finalSealSlowTimeScale}");
        }

        // ══════════════════════════════════════════════════════
        // 상태 전환 — Dead
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Dead 진입.
        /// FinalSeal 중 코어 최종 집행 완료 시 HandleCoreSealCompleted() 에서 호출.
        ///
        /// [처리]
        ///   Time.timeScale 복구
        ///   모든 코루틴 중단
        ///   Rigidbody2D 물리 정지
        ///   코어 비활성
        ///   OnDead 발행 → BossWardenFeedback 처치 연출
        /// </summary>
        private void EnterDead()
        {
            if (_state == SealBossState.Dead) return;

            SetState(SealBossState.Dead);

            // 슬로우 복구
            Time.timeScale = 1f;

            // 모든 코루틴 중단
            StopAllCoroutines();

            // 물리 정지
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 코어 비활성
            ActivateCore(false);

            OnDead?.Invoke();

            Debug.Log("[SealStateManager] ✅ Dead → OnDead 발행");
        }

        // ══════════════════════════════════════════════════════
        // 코어 오브젝트 제어
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 코어 GameObject SetActive 제어.
        /// DilPhase 진입 시 true / 종료 시 false.
        /// </summary>
        private void ActivateCore(bool isActive)
        {
            if (_coreObject == null)
            {
                if (isActive)
                    Debug.LogWarning("[SealStateManager] 코어 오브젝트 미연결 — 코어 활성화 스킵.");
                return;
            }

            _coreObject.SetActive(isActive);
            Debug.Log($"[SealStateManager] 코어 SetActive({isActive})");
        }

        // ══════════════════════════════════════════════════════
        // 상태 설정
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 상태 전환 + OnStateChanged 발행.
        /// 동일 상태 재진입 시 무시.
        /// </summary>
        private void SetState(SealBossState newState)
        {
            if (_state == newState) return;

            SealBossState prev = _state;
            _state = newState;

            OnStateChanged?.Invoke(prev, newState);
            Debug.Log($"[SealStateManager] 상태 전환: {prev} → {newState}");
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
        /// 코어 오브젝트 외부 연결.
        /// Inspector 미연결 시 BossWardenCore.Start() 에서 주입.
        /// </summary>
        public void ConnectCore(GameObject coreObject)
        {
            // 기존 구독 해제
            UnsubscribeCoreEvents();

            _coreObject = coreObject;

            // 새 코어 이벤트 구독
            SubscribeCoreEvents();
        }

        /// <summary>
        /// 즉시 Dead 전환 (테스트 / 치트용).
        /// </summary>
        public void ForceKill()
        {
            if (_state == SealBossState.Dead) return;
            Debug.Log("[SealStateManager] ForceKill 호출");
            EnterDead();
        }

        // ══════════════════════════════════════════════════════
        // 디버그 — ContextMenu
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        [ContextMenu("DEBUG — DilPhase 강제 진입")]
        private void Debug_ForceDilPhase()
        {
            if (!Application.isPlaying) return;
            EnterDilPhase();
        }

        [ContextMenu("DEBUG — FinalSeal 강제 진입")]
        private void Debug_ForceFinalSeal()
        {
            if (!Application.isPlaying) return;
            EnterFinalSeal();
        }

        [ContextMenu("DEBUG — ForceKill")]
        private void Debug_ForceKill()
        {
            if (!Application.isPlaying) return;
            ForceKill();
        }
#endif
    }
}