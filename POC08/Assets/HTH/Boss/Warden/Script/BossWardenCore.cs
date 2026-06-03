// ============================================================
// BossWardenCore.cs  v1.1
// Boss_Warden 루트 통합 상태 관리 컴포넌트
//
// [v1.1 버그 수정]
//   🔴 버그4: Start() 실행 순서 미보장으로 초기화 타이밍 문제
//       원인: BossWardenFeedback.Start() 가 BossWardenCore.Start() 보다
//             먼저 실행되면 SubscribeArmGauge() 시점에
//             SealGaugeComponent 가 아직 Initialize() 되지 않은 상태
//             → SealGauge.MaxGauge 가 기본값(1f) 인 상태로 이벤트 구독
//       수정 1: [DefaultExecutionOrder(-10)] 추가
//               → BossWardenCore 가 다른 Warden 컴포넌트보다 먼저 Start() 실행
//       수정 2: 하위 컴포넌트 Initialize() 를 Start() 에서 Awake() 로 이동
//               → Awake() 는 씬 내 모든 Start() 보다 먼저 실행됨을 보장
//               → BossWardenFeedback.Start() 시점에는 반드시 초기화 완료 상태
//
// [POC07 참고]
//   TestBossCore.cs (v1.0) 전체 구조 계승.
//   HP 제거, 딜페이즈/페이즈/최종봉인 추가.
//
// [POC07 TestBossCore 와의 차이]
//   POC07: HP 있음 (딜타임 중 HP 감소 → 처치)
//   POC08: HP 없음 (코어 봉인도 100% → 최종 봉인 → 처치)
//
//   POC07: ArmPart.ReLock/ForceUnlock (이진 봉인 상태)
//   POC08: ArmPart.OnPartSealed / ForceRelease (봉인도 기반)
//          양팔 봉인 완료 → 그로기 → 코어 해제 → 딜 페이즈 → 페이즈 전환 or 최종 봉인
//
//   POC07: 단일 루프 (딜타임 → ForceUnlock → 반복)
//   POC08: 2페이즈 구조
//          1페이즈 딜 페이즈 → 충격파 + ForceRelease → 2페이즈 → 루프
//          2페이즈 딜 페이즈 → 코어 봉인도 100% → 최종 봉인 → 처치
//
// [핵심 플레이 루프 — POC08]
//   전투 시작 (1페이즈)
//     ↓ 패턴 분석 → 부위 봉인도 누적 → 봉인 집행
//   양팔 봉인 완료 → EnterGroggy()
//     ↓ 그로기 중 코어 활성 → S키 코어 해제 → EnterDilPhase()
//   딜 페이즈
//     ↓ 코어 공격 → 코어 봉인도 50% → ExitDilPhase(isPhase2=false)
//   충격파 + 팔 ForceRelease + 2페이즈 전환
//     ↓ (2페이즈) 동일 루프 반복 → 코어 봉인도 100% → OnFinalSealReady
//   최종 봉인 S키 → OnFinalSealCompleted → Die()
//
// [이벤트 목록]
//   OnGroggyEnter / OnGroggyExit
//   OnDilPhaseEnter / OnDilPhaseExit
//   OnPhaseChanged(int phase)
//   OnFinalSealReady
//   OnDead
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 루트 통합 상태 관리 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [이 스크립트가 하는 것]
    ///   - 그로기 상태 진입/종료/타이머
    ///   - 코어 오브젝트 SetActive 제어
    ///   - 딜 페이즈 진입/종료/타이머
    ///   - 페이즈 전환 (1→2페이즈) + 충격파 트리거
    ///   - 최종 봉인 준비 신호 (OnFinalSealReady)
    ///   - 보스 처치 (Die)
    ///   - 팔 부위 ForceRelease
    ///   - 모든 컴포넌트 초기화 (DataSO 주입)
    ///
    /// [이 스크립트가 하지 않는 것]
    ///   - 이동 / 패턴 실행 → BossWardenAI
    ///   - S키 입력 처리 → BossWardenSealExecutor
    ///   - 색상 / DOTween 연출 → BossWardenFeedback
    ///   - 충격파 판정 → BossWardenShockwave
    /// ────────────────────────────────────────────────────
    /// </summary>
    [DefaultExecutionOrder(-10)] // ✅ v1.1: 다른 Warden 컴포넌트보다 먼저 Awake/Start 실행 보장
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BossWardenAI))]
    [RequireComponent(typeof(BossWardenFeedback))]
    [RequireComponent(typeof(BossWardenSealExecutor))]
    [RequireComponent(typeof(BossWardenAttackRange))]
    public class BossWardenCore : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO (필수) ──────────────────────")]

        /// <summary>
        /// Warden 수치 ScriptableObject.
        /// 모든 하위 컴포넌트에 주입하는 단일 연결 지점.
        /// </summary>
        [Tooltip("BossWardenDataSO. 필수 연결. 모든 컴포넌트에 이 하나를 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 부위 연결 (필수) ──────────────────────")]

        [Tooltip("왼팔 BossWardenArmPart. 필수 연결.")]
        [SerializeField] private BossWardenArmPart _armL;

        [Tooltip("오른팔 BossWardenArmPart. 필수 연결.")]
        [SerializeField] private BossWardenArmPart _armR;

        [Header("── 코어 연결 (필수) ──────────────────────")]

        /// <summary>
        /// 코어 GameObject. 기본 SetActive = false.
        /// 그로기 진입 시 SetActive(true), 딜 페이즈 종료 시 SetActive(false).
        /// </summary>
        [Tooltip("코어 GameObject. 기본 SetActive=false 필요.")]
        [SerializeField] private GameObject _coreObject;

        /// <summary>
        /// 코어 봉인도 컴포넌트.
        /// 딜 페이즈 중 활성화.
        /// </summary>
        [Tooltip("코어 BossWardenCoreSealGauge. 필수 연결.")]
        [SerializeField] private BossWardenCoreSealGauge _coreSealGauge;

        [Header("── 충격파 (선택) ──────────────────────")]

        /// <summary>
        /// 충격파 컴포넌트.
        /// 딜 페이즈 종료 시 호출. 미연결 시 스킵.
        /// </summary>
        [Tooltip("BossWardenShockwave. 미연결 시 충격파 스킵.")]
        [SerializeField] private BossWardenShockwave _shockwave;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조 (Awake에서 GetComponent)
        // ══════════════════════════════════════════════════════

        private Rigidbody2D _rigid2D;
        private BossWardenAI _ai;
        private BossWardenFeedback _feedback;
        private BossWardenSealExecutor _executor;
        private BossWardenAttackRange _attackRange;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 현재 그로기 상태 여부. </summary>
        private bool _isGroggy;

        /// <summary> 현재 딜 페이즈 상태 여부. </summary>
        private bool _isDilPhase;

        /// <summary> 코어 활성 여부. </summary>
        private bool _isCoreActive;

        /// <summary> 최종 봉인 가능 상태 여부. </summary>
        private bool _isFinalSealReady;

        /// <summary> 사망 여부. </summary>
        private bool _isDead;

        /// <summary>
        /// 현재 페이즈 (1 or 2).
        /// 초기값 1.
        /// </summary>
        private int _currentPhase = 1;

        /// <summary>
        /// 봉인 완료된 팔 수.
        /// 양팔(2) 모두 봉인 시 그로기 진입.
        /// </summary>
        private int _sealedArmCount;

        // ══════════════════════════════════════════════════════
        // 코루틴 핸들
        // ══════════════════════════════════════════════════════

        private Coroutine _groggyCoroutine;
        private Coroutine _dilPhaseCoroutine;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 그로기 진입 시 발행.
        /// BossWardenAI / BossWardenFeedback / BossWardenSealExecutor 가 구독.
        /// </summary>
        public event Action OnGroggyEnter;

        /// <summary>
        /// 그로기 종료 시 발행 (코어 해제 실패 or 타이머 종료).
        /// BossWardenAI 가 구독 → Idle 복귀.
        /// </summary>
        public event Action OnGroggyExit;

        /// <summary>
        /// 딜 페이즈 진입 시 발행.
        /// BossWardenAI / BossWardenFeedback / BossWardenCoreSealGauge 가 구독.
        /// </summary>
        public event Action OnDilPhaseEnter;

        /// <summary>
        /// 딜 페이즈 종료 시 발행 (정상 종료 / 페이즈 전환).
        /// BossWardenAI / BossWardenFeedback / BossWardenSealExecutor 가 구독.
        /// </summary>
        public event Action OnDilPhaseExit;

        /// <summary>
        /// 페이즈 전환 시 발행.
        /// 파라미터: 새 페이즈 번호 (2).
        /// BossWardenAI 가 구독 → 2페이즈 패턴 강화.
        /// BossWardenFeedback 이 구독 → 전환 연출.
        /// </summary>
        public event Action<int> OnPhaseChanged;

        /// <summary>
        /// 코어 봉인도 100% 도달 (최종 봉인 가능) 시 발행.
        /// BossWardenSealExecutor 가 구독 → 최종 봉인 감지 활성.
        /// BossWardenFeedback 이 구독 → 코어 청백 Pulse 연출.
        /// </summary>
        public event Action OnFinalSealReady;

        /// <summary>
        /// 보스 처치 시 발행.
        /// BossWardenAI / BossWardenFeedback 이 구독.
        /// BattleManager 연결 예정.
        /// </summary>
        public event Action OnDead;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary> 현재 그로기 상태. </summary>
        public bool IsGroggy => _isGroggy;

        /// <summary> 현재 딜 페이즈 상태. </summary>
        public bool IsDilPhase => _isDilPhase;

        /// <summary> 코어 활성 여부. </summary>
        public bool IsCoreActive => _isCoreActive;

        /// <summary> 사망 여부. </summary>
        public bool IsDead => _isDead;

        /// <summary> 현재 페이즈 번호. </summary>
        public int CurrentPhase => _currentPhase;

        /// <summary> DataSO 참조 (외부 공개). </summary>
        public BossWardenDataSO Data => _data;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _rigid2D = GetComponent<Rigidbody2D>();
            _ai = GetComponent<BossWardenAI>();
            _feedback = GetComponent<BossWardenFeedback>();
            _executor = GetComponent<BossWardenSealExecutor>();
            _attackRange = GetComponent<BossWardenAttackRange>();

            // ✅ v1.1 버그4 수정: Initialize를 Start → Awake 로 이동
            // Awake 는 씬 내 모든 Start() 보다 먼저 실행됨을 보장.
            // BossWardenFeedback.Start() → SubscribeArmGauge() 시점에
            // SealGaugeComponent 가 반드시 초기화 완료 상태임을 보장.
            //
            // [주의] DataSO null 체크는 Start() 에서 수행 (Awake 시점 Inspector 연결 보장)
            //        Awake 에서 _data 가 null 이면 Initialize 스킵 → Start 에서 오류 출력.
            if (_data != null)
            {
                _armL?.Initialize(_data);
                _armR?.Initialize(_data);
                _coreSealGauge?.Initialize(_data);
                _ai?.Initialize(_data);
            }
        }

        private void Start()
        {
            if (_data == null)
            {
                Debug.LogError("[BossWardenCore] BossWardenDataSO 가 연결되지 않았습니다.");
                enabled = false;
                return;
            }

            // 코어 기본 비활성 보장
            if (_coreObject != null)
                _coreObject.SetActive(false);

            // 이벤트 구독
            SubscribeAll();

            Debug.Log("[BossWardenCore] 초기화 완료 — 전투 루프 준비");
        }

        private void OnDestroy()
        {
            UnsubscribeAll();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독 / 해제
        // ══════════════════════════════════════════════════════

        private void SubscribeAll()
        {
            // 팔 봉인 완료 이벤트 구독
            if (_armL != null)
            {
                _armL.OnPartSealed -= HandlePartSealed;
                _armL.OnPartSealed += HandlePartSealed;
            }
            if (_armR != null)
            {
                _armR.OnPartSealed -= HandlePartSealed;
                _armR.OnPartSealed += HandlePartSealed;
            }

            // SealExecutor 이벤트 구독
            if (_executor != null)
            {
                _executor.OnPartSealed -= HandleExecutorPartSealed;
                _executor.OnCoreUnlocked -= HandleCoreUnlocked;
                _executor.OnFinalSealCompleted -= HandleFinalSealCompleted;

                _executor.OnPartSealed += HandleExecutorPartSealed;
                _executor.OnCoreUnlocked += HandleCoreUnlocked;
                _executor.OnFinalSealCompleted += HandleFinalSealCompleted;
            }

            // CoreSealGauge 이벤트 구독
            if (_coreSealGauge != null)
            {
                _coreSealGauge.OnPhase1TargetReached -= HandlePhase1TargetReached;
                _coreSealGauge.OnPhase2TargetReached -= HandlePhase2TargetReached;

                _coreSealGauge.OnPhase1TargetReached += HandlePhase1TargetReached;
                _coreSealGauge.OnPhase2TargetReached += HandlePhase2TargetReached;
            }
        }

        private void UnsubscribeAll()
        {
            if (_armL != null) _armL.OnPartSealed -= HandlePartSealed;
            if (_armR != null) _armR.OnPartSealed -= HandlePartSealed;

            if (_executor != null)
            {
                _executor.OnPartSealed -= HandleExecutorPartSealed;
                _executor.OnCoreUnlocked -= HandleCoreUnlocked;
                _executor.OnFinalSealCompleted -= HandleFinalSealCompleted;
            }

            if (_coreSealGauge != null)
            {
                _coreSealGauge.OnPhase1TargetReached -= HandlePhase1TargetReached;
                _coreSealGauge.OnPhase2TargetReached -= HandlePhase2TargetReached;
            }
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러 — 팔 봉인 완료
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenArmPart.OnPartSealed 수신 (SealGaugeComponent.OnSealed 래핑).
        /// 그로기 조건 체크.
        ///
        /// [주의]
        ///   OnPartSealed 는 SealGaugeComponent.ExecuteSeal() 완료 시 발행.
        ///   BossWardenSealExecutor.OnPartSealed 와 별개이므로 중복 수신에 주의.
        ///   여기서는 카운트만 체크하고 실제 봉인 처리는 SealExecutor 에서.
        /// </summary>
        private void HandlePartSealed(WardenPartType partType)
        {
            if (_isGroggy || _isDilPhase || _isDead) return;

            _sealedArmCount++;
            Debug.Log($"[BossWardenCore] 봉인 완료: {partType} (총 {_sealedArmCount}/2)");

            CheckGroggyCondition();
        }

        /// <summary>
        /// BossWardenSealExecutor.OnPartSealed 수신.
        /// SealExecutor 가 ExecuteSeal() 호출 완료 후 발행하는 별도 이벤트.
        /// 중복 카운트 방지를 위해 이 핸들러에서는 카운트를 올리지 않는다.
        /// </summary>
        private void HandleExecutorPartSealed(WardenPartType partType)
        {
            Debug.Log($"[BossWardenCore] SealExecutor 봉인 집행 완료: {partType}");
        }

        // ══════════════════════════════════════════════════════
        // 그로기 조건 체크
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 그로기 진입 조건 체크.
        /// 양팔 봉인 완료 시 그로기 진입.
        /// </summary>
        private void CheckGroggyCondition()
        {
            bool armLSealed = _armL != null && _armL.IsSealed;
            bool armRSealed = _armR != null && _armR.IsSealed;

            if (armLSealed && armRSealed)
            {
                Debug.Log("[BossWardenCore] 양팔 봉인 완료 → 그로기 진입");
                EnterGroggy();
            }
        }

        // ══════════════════════════════════════════════════════
        // 그로기
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 그로기 진입.
        /// BossWardenAI.HandlePatternGroggy() 또는 CheckGroggyCondition() 에서 호출.
        ///
        /// [진입 조건 가드]
        ///   이미 그로기 / 딜 페이즈 / 사망 상태이면 무시.
        /// </summary>
        public void EnterGroggy()
        {
            if (_isGroggy || _isDilPhase || _isDead) return;

            _isGroggy = true;
            _rigid2D.linearVelocity = Vector2.zero;

            // 코어 활성화
            ActivateCore();

            // 이벤트 발행 (AI / Feedback / SealExecutor 가 수신)
            OnGroggyEnter?.Invoke();

            // 그로기 타이머 시작
            if (_groggyCoroutine != null) StopCoroutine(_groggyCoroutine);
            _groggyCoroutine = StartCoroutine(GroggyRoutine());

            Debug.Log($"[BossWardenCore] 그로기 진입 ({_data.groggyDuration:F1}초)");
        }

        /// <summary>
        /// 그로기 타이머 코루틴.
        /// groggyDuration 경과 시 코어 해제 실패 → 그로기 종료 + 루프 재시작.
        /// </summary>
        private IEnumerator GroggyRoutine()
        {
            yield return new WaitForSecondsRealtime(_data.groggyDuration);

            _groggyCoroutine = null;

            // 코어 해제 실패 처리 (타이머 만료)
            if (_isGroggy)
            {
                Debug.Log("[BossWardenCore] 그로기 타이머 만료 → 루프 재시작");
                ExitGroggyFailure();
            }
        }

        /// <summary>
        /// 그로기 실패 종료 (타이머 만료).
        /// 코어 비활성 + 팔 봉인 해제 + 봉인 카운트 초기화 + AI 재개.
        /// </summary>
        private void ExitGroggyFailure()
        {
            _isGroggy = false;
            _sealedArmCount = 0;

            // 코어 비활성
            DeactivateCore();

            // 팔 봉인 해제 (봉인도 초기화, 저항은 유지)
            _armL?.ForceRelease(resetSealCount: false);
            _armR?.ForceRelease(resetSealCount: false);

            OnGroggyExit?.Invoke();

            Debug.Log("[BossWardenCore] 그로기 실패 종료 → 팔 봉인 해제 + 루프 재시작");
        }

        // ══════════════════════════════════════════════════════
        // 코어 활성 / 비활성
        // ══════════════════════════════════════════════════════

        private void ActivateCore()
        {
            if (_isCoreActive) return;

            _isCoreActive = true;
            if (_coreObject != null) _coreObject.SetActive(true);

            Debug.Log("[BossWardenCore] 코어 활성화");
        }

        private void DeactivateCore()
        {
            if (!_isCoreActive) return;

            _isCoreActive = false;
            if (_coreObject != null) _coreObject.SetActive(false);

            Debug.Log("[BossWardenCore] 코어 비활성화");
        }

        // ══════════════════════════════════════════════════════
        // 코어 해제 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenSealExecutor.OnCoreUnlocked 수신.
        /// 딜 페이즈 진입.
        /// </summary>
        private void HandleCoreUnlocked()
        {
            if (_isDead) return;

            // 그로기 타이머 중단 (코어 해제 성공)
            if (_groggyCoroutine != null)
            {
                StopCoroutine(_groggyCoroutine);
                _groggyCoroutine = null;
            }

            _isGroggy = false;

            EnterDilPhase();
        }

        // ══════════════════════════════════════════════════════
        // 딜 페이즈
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 딜 페이즈 진입.
        /// 코어 해제 완료 시 HandleCoreUnlocked() 에서 호출.
        /// </summary>
        private void EnterDilPhase()
        {
            if (_isDilPhase || _isDead) return;

            _isDilPhase = true;

            // 코어 봉인도 활성화
            _coreSealGauge?.ActivateGauge(true);

            OnDilPhaseEnter?.Invoke();

            // 딜 페이즈 타이머 시작
            if (_dilPhaseCoroutine != null) StopCoroutine(_dilPhaseCoroutine);
            _dilPhaseCoroutine = StartCoroutine(DilPhaseRoutine());

            Debug.Log($"[BossWardenCore] 딜 페이즈 진입 ({_data.dilPhaseDuration:F1}초)");
        }

        /// <summary>
        /// 딜 페이즈 타이머 코루틴.
        /// dilPhaseDuration 경과 시 일반 종료.
        /// 코어 봉인도 목표 도달 시 이벤트로 조기 종료.
        /// </summary>
        private IEnumerator DilPhaseRoutine()
        {
            yield return new WaitForSecondsRealtime(_data.dilPhaseDuration);

            _dilPhaseCoroutine = null;

            if (_isDilPhase)
            {
                Debug.Log("[BossWardenCore] 딜 페이즈 타이머 만료 → 일반 종료");
                ExitDilPhase(isFinalSeal: false);
            }
        }

        /// <summary>
        /// 딜 페이즈 종료.
        ///
        /// [isFinalSeal = false (일반 종료)]
        ///   1. 코어 봉인도 비활성
        ///   2. 코어 비활성
        ///   3. 팔 봉인 해제 + 봉인도 초기화
        ///   4. 충격파 발동
        ///   5. 페이즈 전환 체크 (1→2페이즈)
        ///   6. OnDilPhaseExit 발행 → AI 재개
        ///
        /// [isFinalSeal = true (최종 봉인)]
        ///   1. 코어 봉인도 비활성
        ///   2. OnFinalSealReady 발행 → SealExecutor 최종 봉인 감지 활성
        /// </summary>
        private void ExitDilPhase(bool isFinalSeal)
        {
            if (!_isDilPhase) return;

            // 딜 페이즈 타이머 중단
            if (_dilPhaseCoroutine != null)
            {
                StopCoroutine(_dilPhaseCoroutine);
                _dilPhaseCoroutine = null;
            }

            // 코어 봉인도 비활성 (공격 누적 중단)
            _coreSealGauge?.ActivateGauge(false);

            if (isFinalSeal)
            {
                // 최종 봉인 진입
                _isDilPhase = false;
                _isFinalSealReady = true;

                OnFinalSealReady?.Invoke();
                Debug.Log("[BossWardenCore] 최종 봉인 진입 → SealExecutor 최종 봉인 감지 활성");
                return;
            }

            // 일반 종료
            _isDilPhase = false;
            _sealedArmCount = 0;

            // 코어 비활성
            DeactivateCore();

            // 팔 봉인 해제 (저항은 유지)
            _armL?.ForceRelease(resetSealCount: false);
            _armR?.ForceRelease(resetSealCount: false);

            // 충격파 발동
            if (_shockwave != null)
                _shockwave.Trigger(transform.position);
            else
                Debug.Log("[BossWardenCore] 충격파 스킵 (BossWardenShockwave 미연결)");

            OnDilPhaseExit?.Invoke();

            // 페이즈 전환 체크
            if (_currentPhase == 1)
            {
                _currentPhase = 2;
                OnPhaseChanged?.Invoke(2);
                Debug.Log("[BossWardenCore] 2페이즈 전환");
            }

            Debug.Log("[BossWardenCore] 딜 페이즈 종료 → 팔 봉인 해제 + 루프 재시작");
        }

        // ══════════════════════════════════════════════════════
        // 코어 봉인도 목표 도달 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenCoreSealGauge.OnPhase1TargetReached 수신.
        /// 1페이즈 딜 페이즈 종료 → 일반 종료.
        /// </summary>
        private void HandlePhase1TargetReached()
        {
            if (!_isDilPhase || _isDead) return;

            Debug.Log("[BossWardenCore] 1페이즈 코어 봉인도 목표 → 딜 페이즈 종료");
            ExitDilPhase(isFinalSeal: false);
        }

        /// <summary>
        /// BossWardenCoreSealGauge.OnPhase2TargetReached 수신.
        /// 2페이즈 딜 페이즈 종료 → 최종 봉인 진입.
        /// </summary>
        private void HandlePhase2TargetReached()
        {
            if (!_isDilPhase || _isDead) return;

            Debug.Log("[BossWardenCore] 2페이즈 코어 봉인도 100% → 최종 봉인 진입");
            ExitDilPhase(isFinalSeal: true);
        }

        // ══════════════════════════════════════════════════════
        // 최종 봉인 / 처치
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenSealExecutor.OnFinalSealCompleted 수신.
        /// 보스 처치.
        /// </summary>
        private void HandleFinalSealCompleted()
        {
            Die();
        }

        /// <summary>
        /// 보스 처치 처리.
        ///
        /// [처리 순서]
        ///   1. 이중 호출 방지
        ///   2. 모든 코루틴 중단
        ///   3. 물리 정지
        ///   4. 코어 / 팔 정리
        ///   5. OnDead 이벤트 발행
        /// </summary>
        private void Die()
        {
            if (_isDead) return;

            _isDead = true;
            _isGroggy = false;
            _isDilPhase = false;

            // 코루틴 전체 중단
            StopAllCoroutines();

            // 물리 정지
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 코어 정리
            DeactivateCore();
            _coreSealGauge?.ActivateGauge(false);

            // 예고 범위 정리
            _attackRange?.HideAll();

            OnDead?.Invoke();

            Debug.Log("[BossWardenCore] 보스 처치!");
        }

        // ══════════════════════════════════════════════════════
        // 디버그 / 테스트용 API
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        /// <summary>
        /// 그로기 강제 진입 (에디터 테스트용).
        /// Inspector ContextMenu 에서 호출.
        /// </summary>
        [ContextMenu("DEBUG: 그로기 강제 진입")]
        public void DEBUG_EnterGroggy() => EnterGroggy();

        /// <summary>
        /// 딜 페이즈 강제 진입 (에디터 테스트용).
        /// </summary>
        [ContextMenu("DEBUG: 딜 페이즈 강제 진입")]
        public void DEBUG_EnterDilPhase() => EnterDilPhase();

        /// <summary>
        /// 양팔 봉인도 즉시 100% (에디터 테스트용).
        /// </summary>
        [ContextMenu("DEBUG: 양팔 봉인도 100%")]
        public void DEBUG_FillArmGauge()
        {
            if (_armL != null) _armL.SealGauge?.AddGauge(9999f);
            if (_armR != null) _armR.SealGauge?.AddGauge(9999f);
        }
#endif

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            // 현재 상태 시각화
            if (_isGroggy)
                Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
            else if (_isDilPhase)
                Gizmos.color = new Color(1f, 0.4f, 0f, 0.4f);
            else
                Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.2f);

            Gizmos.DrawWireSphere(transform.position, 0.7f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.8f,
                $"Phase:{_currentPhase} | Groggy:{_isGroggy} | Dil:{_isDilPhase} | SealedArms:{_sealedArmCount}");
#endif
        }
    }
}