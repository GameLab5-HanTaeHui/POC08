// ============================================================
// SealStateManager.cs  v1.0
// 보스 봉인 상태 총괄 관리자
//
// [역할]
//   봉인 시스템의 상태 전환을 총괄 관리.
//   Idle → Groggy → DilPhase → FinalSeal → Dead 전환 담당.
//
//   Warden 전용 로직 없음 — BossDataSO 수치만으로 동작.
//   어떤 보스든 이 컴포넌트 그대로 사용 가능.
//
// [BossWardenCore 와의 역할 분리]
//   기존 BossWardenCore 가 상태 + 봉인도 + 연출을 전부 담당.
//   신규 분리:
//     SealStateManager   → 상태 전환 + 이벤트 발행
//     SealGaugeManager   → 봉인도 데이터 조작
//     BossWardenFeedback → DOTween 연출 (Warden 전용 유지)
//     BossWardenAI       → 이동/패턴 (Warden 전용 유지)
//
// [상태 정의]
//   Idle       : 전투 대기 / 패턴 실행 중
//   Groggy     : 양팔 봉인 완료 → AI 정지, 코어 활성, 딜페이즈 즉시 시작
//   DilPhase   : 코어 공격 구간. 타이머 내 코어 봉인도 목표 미달 → 실패 루프
//   FinalSeal  : 코어 봉인도 100% → 강한 슬로우, 최종 봉인 S키 대기
//   Dead       : 최종 봉인 완료 → 보스 처치
//
// [봉인 흐름 — 최신 기획 기준]
//   Idle
//     → 양팔 봉인 완료 (SealGaugeManager.OnAllPartsSealed)
//     → Groggy 진입 (즉시 딜페이즈 시작, 코어 S키 단계 없음)
//   Groggy
//     → 코어 SealableComponent.OnSealCompleted → DilPhase 진입
//     [실패] 그로기 타이머 만료 → ReleaseAllParts + Idle 복귀
//   DilPhase
//     → 코어 봉인도 페이즈 목표치 도달 → ExitDilPhase
//     → 1페이즈: Idle 복귀 + ForceRelease + 충격파 + 2페이즈 전환
//     → 2페이즈: FinalSeal 진입
//     [실패] 딜페이즈 타이머 만료 → ReleaseAllParts + Idle 복귀 + 충격파
//   FinalSeal
//     → 코어 SealableComponent.OnSealCompleted → Dead 진입
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
    /// 보스 봉인 상태 총괄 관리자. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [상태 전환 흐름]
    ///   Idle
    ///   → (OnAllPartsSealed) → Groggy
    ///   → (코어 집행 완료)   → DilPhase
    ///   → (페이즈 목표 도달) → Idle(실패) or FinalSeal(성공)
    ///   → (코어 최종 집행)   → Dead
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
        /// 봉인 보스 상태 열거형.
        /// </summary>
        public enum SealBossState
        {
            /// <summary>전투 대기 / 패턴 실행 중.</summary>
            Idle,
            /// <summary>양팔 봉인 완료. AI 정지. 코어 활성. 딜페이즈 즉시 시작.</summary>
            Groggy,
            /// <summary>코어 공격 구간. 타이머 내 처리 못하면 실패 루프.</summary>
            DilPhase,
            /// <summary>코어 봉인도 100%. 강한 슬로우. 최종 봉인 S키 대기.</summary>
            FinalSeal,
            /// <summary>최종 봉인 완료. 보스 처치.</summary>
            Dead,
        }

        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// 범용 보스 DataSO.
        /// 그로기 타이머 / 딜페이즈 타이머 등 수치 참조.
        /// </summary>
        [Tooltip("BossDataSO. 타이머 수치 참조. 필수.")]
        [SerializeField] private BossDataSO _bossData;

        [Header("── 코어 오브젝트 ──────────────────────")]

        /// <summary>
        /// 코어 GameObject.
        /// Groggy 진입 시 SetActive(true) / 종료 시 SetActive(false).
        /// 미연결 시 코어 활성화 스킵 + 경고.
        /// </summary>
        [Tooltip("코어 GameObject. Groggy 시 활성. 미연결 시 경고.")]
        [SerializeField] private GameObject _coreObject;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>봉인도 전체 조율. ReleaseAllParts / ActivateCore 호출.</summary>
        private SealGaugeManager _gaugeManager;

        /// <summary>Rigidbody2D. Dead 시 velocity 정지.</summary>
        private Rigidbody2D _rigid2D;

        // ══════════════════════════════════════════════════════
        // 상태
        // ══════════════════════════════════════════════════════

        /// <summary>현재 상태.</summary>
        private SealBossState _state = SealBossState.Idle;

        /// <summary>현재 페이즈 (1 or 2).</summary>
        private int _currentPhase = 1;

        // ══════════════════════════════════════════════════════
        // 코루틴 참조
        // ══════════════════════════════════════════════════════

        /// <summary>그로기 타이머 코루틴.</summary>
        private Coroutine _groggyCoroutine;

        /// <summary>딜페이즈 타이머 코루틴.</summary>
        private Coroutine _dilPhaseCoroutine;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>상태 전환 시 발행. 파라미터: (이전 상태, 새 상태).</summary>
        public event Action<SealBossState, SealBossState> OnStateChanged;

        /// <summary>Groggy 진입 시 발행. BossWardenAI / BossWardenFeedback 구독.</summary>
        public event Action OnGroggyEnter;

        /// <summary>Groggy 실패 종료 시 발행 (타이머 만료). AI 재개.</summary>
        public event Action OnGroggyExit;

        /// <summary>DilPhase 진입 시 발행. BossWardenAI / BossWardenFeedback 구독.</summary>
        public event Action OnDilPhaseEnter;

        /// <summary>DilPhase 종료 시 발행 (성공 or 실패). AI 재개.</summary>
        public event Action OnDilPhaseExit;

        /// <summary>페이즈 전환 시 발행. 파라미터: 새 페이즈 번호.</summary>
        public event Action<int> OnPhaseChanged;

        /// <summary>FinalSeal 진입 시 발행. SealExecutionRunner 최종 봉인 감지 활성.</summary>
        public event Action OnFinalSealReady;

        /// <summary>Dead 진입 시 발행. BattleManager / BossWardenFeedback 구독.</summary>
        public event Action OnDead;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>현재 상태.</summary>
        public SealBossState State => _state;

        /// <summary>현재 페이즈.</summary>
        public int CurrentPhase => _currentPhase;

        /// <summary>Groggy 상태 여부.</summary>
        public bool IsGroggy => _state == SealBossState.Groggy;

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
                Debug.LogWarning($"[SealStateManager] SealGaugeManager 미연결.");
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

            // 코어 SealableComponent.OnSealCompleted / OnPhaseTargetReached 구독
            if (_coreObject != null)
            {
                var coreSealable = _coreObject.GetComponent<SealableComponent>();
                if (coreSealable != null)
                {
                    coreSealable.OnSealCompleted -= HandleCoreSealCompleted;
                    coreSealable.OnSealCompleted += HandleCoreSealCompleted;
                    coreSealable.OnPhaseTargetReached -= HandlePhaseTargetReached;
                    coreSealable.OnPhaseTargetReached += HandlePhaseTargetReached;
                }
            }
        }

        private void OnDestroy()
        {
            if (_gaugeManager != null)
            {
                _gaugeManager.OnAllPartsSealed -= HandleAllPartsSealed;
                _gaugeManager.OnAllPartsReleased -= HandleAllPartsReleased;
            }

            if (_coreObject != null)
            {
                var coreSealable = _coreObject.GetComponent<SealableComponent>();
                if (coreSealable != null)
                {
                    coreSealable.OnSealCompleted -= HandleCoreSealCompleted;
                    coreSealable.OnPhaseTargetReached -= HandlePhaseTargetReached;
                }
            }
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 모든 Part 봉인 완료 수신.
        /// Idle 상태에서만 처리 → Groggy 진입.
        /// </summary>
        private void HandleAllPartsSealed()
        {
            if (_state != SealBossState.Idle) return;
            EnterGroggy();
        }

        /// <summary>
        /// 모든 Part 봉인 해제 수신.
        /// 상태 복귀 알림용. 실제 상태 전환은 각 종료 함수에서 처리.
        /// </summary>
        private void HandleAllPartsReleased()
        {
            Debug.Log("[SealStateManager] 모든 Part 봉인 해제됨");
        }

        /// <summary>
        /// 코어 SealableComponent.OnSealCompleted 수신.
        ///
        /// [상태별 분기]
        ///   Groggy   → DilPhase 진입 (양팔 봉인 완료 후 코어 즉시 딜페이즈)
        ///   FinalSeal → Dead 진입 (최종 봉인 완료)
        ///   DilPhase  → 경고 (딜페이즈 중 코어 집행은 PhaseTargetReached 로 처리)
        ///   그 외      → 경고 + 무시
        /// </summary>
        private void HandleCoreSealCompleted()
        {
            if (_state == SealBossState.Dead) return;

            switch (_state)
            {
                case SealBossState.Groggy:
                    Debug.Log("[SealStateManager] 코어 집행 완료 (그로기) → DilPhase 진입");
                    EnterDilPhase();
                    break;

                case SealBossState.FinalSeal:
                    Debug.Log("[SealStateManager] 최종 봉인 완료 → Dead");
                    EnterDead();
                    break;

                default:
                    Debug.LogWarning($"[SealStateManager] 예상치 못한 상태에서 코어 집행: {_state}");
                    break;
            }
        }

        /// <summary>
        /// 코어 SealableComponent.OnPhaseTargetReached 수신.
        /// DilPhase 중 코어 봉인도 페이즈 목표치 도달.
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
        // 상태 전환 — Groggy
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Groggy 진입.
        /// 양팔 봉인 완료 시 SealGaugeManager.OnAllPartsSealed 에서 호출.
        ///
        /// [처리]
        ///   코어 SetActive(true)
        ///   딜페이즈 즉시 시작 (코어 S키 단계 없음 — 기획 확정)
        ///   그로기 타이머 시작 (groggyDuration)
        ///   OnGroggyEnter 발행 → BossWardenAI 정지
        /// </summary>
        private void EnterGroggy()
        {
            if (_state == SealBossState.Dead) return;

            SetState(SealBossState.Groggy);

            // 코어 활성화
            ActivateCore(true);

            // 딜페이즈 즉시 시작 (양팔 봉인 완료 = 즉시 딜페이즈)
            EnterDilPhase();

            OnGroggyEnter?.Invoke();

            // 그로기 타이머 (이 안에서 딜페이즈도 같이 돌아감)
            if (_groggyCoroutine != null) StopCoroutine(_groggyCoroutine);
            _groggyCoroutine = StartCoroutine(GroggyTimerRoutine());

            Debug.Log($"[SealStateManager] ▶ Groggy 진입 | 타이머:{_bossData?.SealData?.groggyDuration:F1}초");
        }

        /// <summary>
        /// 그로기 타이머 코루틴.
        /// groggyDuration 경과 시 딜페이즈 실패 처리.
        /// 딜페이즈 성공(목표치 도달) 시 이 코루틴은 외부에서 StopCoroutine.
        /// </summary>
        private IEnumerator GroggyTimerRoutine()
        {
            float duration = _bossData?.SealData?.groggyDuration ?? 10f;
            yield return new WaitForSecondsRealtime(duration);

            _groggyCoroutine = null;

            if (_state == SealBossState.DilPhase || _state == SealBossState.Groggy)
            {
                Debug.Log("[SealStateManager] 그로기 타이머 만료 → 실패 루프");
                ExitGroggyFailure();
            }
        }

        /// <summary>
        /// 그로기 실패 종료 (타이머 만료).
        /// ForceRelease + 충격파 + Idle 복귀.
        /// </summary>
        private void ExitGroggyFailure()
        {
            // 딜페이즈 타이머 중단
            if (_dilPhaseCoroutine != null)
            {
                StopCoroutine(_dilPhaseCoroutine);
                _dilPhaseCoroutine = null;
            }

            // 코어 비활성
            ActivateCore(false);

            // 코어 게이지 비활성
            _gaugeManager?.ActivateCore(false);

            // 모든 Part 봉인 해제 (저항 횟수 유지)
            _gaugeManager?.ReleaseAllParts(resetSealCount: false);

            SetState(SealBossState.Idle);

            OnGroggyExit?.Invoke();
            OnDilPhaseExit?.Invoke();

            Debug.Log("[SealStateManager] ■ 그로기 실패 → ForceRelease + Idle 복귀");
        }

        // ══════════════════════════════════════════════════════
        // 상태 전환 — DilPhase
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// DilPhase 진입.
        /// EnterGroggy() 에서 직접 호출 (즉시 딜페이즈).
        ///
        /// [처리]
        ///   코어 ActivateGauge(true) — 봉인도 누적 허용
        ///   딜페이즈 타이머 시작 (dilPhaseDuration)
        ///   OnDilPhaseEnter 발행 → BossWardenFeedback 색상 전환
        /// </summary>
        private void EnterDilPhase()
        {
            if (_state == SealBossState.Dead) return;

            SetState(SealBossState.DilPhase);

            // 코어 게이지 활성
            _gaugeManager?.ActivateCore(true);

            OnDilPhaseEnter?.Invoke();

            // 딜페이즈 타이머
            if (_dilPhaseCoroutine != null) StopCoroutine(_dilPhaseCoroutine);
            _dilPhaseCoroutine = StartCoroutine(DilPhaseTimerRoutine());

            float duration = (_bossData as BossWardenDataSO)?.dilPhaseDuration ?? 10f;
            Debug.Log($"[SealStateManager] ▶ DilPhase 진입 | 타이머:{duration:F1}초");
        }

        /// <summary>
        /// 딜페이즈 타이머 코루틴.
        /// dilPhaseDuration 경과 시 딜페이즈 실패 종료.
        /// </summary>
        private IEnumerator DilPhaseTimerRoutine()
        {
            float duration = (_bossData as BossWardenDataSO)?.dilPhaseDuration ?? 10f;
            yield return new WaitForSecondsRealtime(duration);

            _dilPhaseCoroutine = null;

            if (_state == SealBossState.DilPhase)
            {
                Debug.Log("[SealStateManager] 딜페이즈 타이머 만료 → 실패 루프");
                ExitDilPhase(isFinalSeal: false);
            }
        }

        /// <summary>
        /// DilPhase 종료.
        ///
        /// [isFinalSeal = false — 실패 or 1페이즈 종료]
        ///   코어 비활성
        ///   ForceRelease + 충격파
        ///   1페이즈 → 2페이즈 전환
        ///   Idle 복귀
        ///
        /// [isFinalSeal = true — 2페이즈 성공]
        ///   코어 비활성 (최종 봉인은 별도 처리)
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

            // 그로기 타이머 중단 (성공 경로)
            if (_groggyCoroutine != null)
            {
                StopCoroutine(_groggyCoroutine);
                _groggyCoroutine = null;
            }

            // 코어 게이지 비활성
            _gaugeManager?.ActivateCore(false);

            if (isFinalSeal)
            {
                // 2페이즈 딜페이즈 성공 → FinalSeal
                ActivateCore(false);
                EnterFinalSeal();
                return;
            }

            // 일반 종료 (실패 or 1페이즈)
            ActivateCore(false);

            // 모든 Part 봉인 해제 (저항 횟수 유지)
            _gaugeManager?.ReleaseAllParts(resetSealCount: false);

            OnDilPhaseExit?.Invoke();

            // 페이즈 전환 체크 (1 → 2페이즈)
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
        /// 코어 봉인도 100% 도달 (2페이즈) 시 ExitDilPhase(true) 에서 호출.
        ///
        /// [처리]
        ///   강한 슬로우 적용 (finalSealSlowTimeScale)
        ///   OnFinalSealReady 발행 → SealExecutionRunner 최종 봉인 대기
        /// </summary>
        private void EnterFinalSeal()
        {
            if (_state == SealBossState.Dead) return;

            SetState(SealBossState.FinalSeal);

            // 강한 슬로우 적용
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
        /// FinalSeal 중 코어 최종 집행 완료 시 HandleCoreSealCompleted 에서 호출.
        ///
        /// [처리]
        ///   Time.timeScale 복구
        ///   모든 코루틴 중단
        ///   물리 정지
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
        /// Groggy 진입 시 true / 종료 시 false.
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
        // 상태 설정 내부
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 상태 전환 + OnStateChanged 발행.
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
        /// BossWardenCore 에서 Initialize() 시 호출.
        /// </summary>
        public void Initialize(BossDataSO data)
        {
            _bossData = data;
        }

        /// <summary>
        /// 코어 오브젝트 외부 연결.
        /// Inspector 에서 미연결 시 BossWardenCore 에서 주입.
        /// </summary>
        public void ConnectCore(GameObject coreObject)
        {
            _coreObject = coreObject;

            // 코어 SealableComponent 이벤트 재구독
            if (_coreObject != null)
            {
                var coreSealable = _coreObject.GetComponent<SealableComponent>();
                if (coreSealable != null)
                {
                    coreSealable.OnSealCompleted -= HandleCoreSealCompleted;
                    coreSealable.OnSealCompleted += HandleCoreSealCompleted;
                    coreSealable.OnPhaseTargetReached -= HandlePhaseTargetReached;
                    coreSealable.OnPhaseTargetReached += HandlePhaseTargetReached;
                }
            }
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
        [ContextMenu("DEBUG — Groggy 강제 진입")]
        private void Debug_ForceGroggy()
        {
            if (!Application.isPlaying) return;
            EnterGroggy();
        }

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