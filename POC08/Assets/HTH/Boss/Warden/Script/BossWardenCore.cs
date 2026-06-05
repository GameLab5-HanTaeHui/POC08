// ============================================================
// BossWardenCore.cs  v2.0
// Boss_Warden 루트 통합 상태 관리 컴포넌트
//
// [v2.0 변경 — SealableComponent / SealExecutor 통합]
//
//   제거:
//     BossWardenCoreSealGauge _coreSealGauge → SealableComponent 로 교체
//     BossWardenSealExecutor _executor       → SealExecutor 로 교체
//     _armL.OnPartSealed / _armR.OnPartSealed 구독 → SealableComponent.OnSealCompleted 로 통합
//     _executor.OnPartSealed / OnCoreUnlocked / OnFinalSealCompleted 구독 → 제거
//     _coreSealGauge.OnPhase1/2TargetReached 구독 → OnPhaseTargetReached 단일 이벤트로 통합
//     HandleExecutorPartSealed / HandleCoreUnlocked / HandleFinalSealCompleted → 제거
//
//   추가:
//     SealExecutor _sealExecutor             → 신규 연결
//     SealableComponent _armLSealable / _armRSealable / _coreSealable → 신규 연결
//     _armLSealable.OnSealCompleted 구독 → HandleArmLSealed()
//     _armRSealable.OnSealCompleted 구독 → HandleArmRSealed()
//     _coreSealable.OnSealCompleted 구독 → HandleCoreExecuted()
//     _coreSealable.OnPhaseTargetReached 구독 → HandlePhaseTargetReached()
//
//   [버그 수정]
//     HandleCoreExecuted():
//       기존 HandleFinalSealed() 는 항상 Die() → 코어 해제와 최종 봉인 구별 불가
//       수정: _isGroggy = true  → EnterDilPhase() (코어 해제)
//             _isDilPhase = true → Die()           (최종 봉인)
//
// [v1.1 유지]
//   [DefaultExecutionOrder(-10)] / Awake 초기화 순서 보장
//   그로기 / 딜 페이즈 / 페이즈 전환 / 처치 전체 흐름 유지
//
// [namespace] SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 루트 통합 상태 관리 컴포넌트. (v2.0)
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class BossWardenCore : MonoBehaviour, IBossCore
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO (필수) ──────────────────────")]
        [Tooltip("BossWardenDataSO. 필수 연결.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 부위 연결 (필수) ──────────────────────")]
        [Tooltip("왼팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armL;

        [Tooltip("오른팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armR;

        [Header("── SealableComponent 연결 (필수) ──────────────────────")]

        /// <summary>
        /// 왼팔 SealableComponent.
        /// LeftArm 오브젝트에 부착된 SealableComponent 연결.
        /// OnSealCompleted → HandleArmSealed() 구독.
        /// </summary>
        [Tooltip("LeftArm 의 SealableComponent.")]
        [SerializeField] private SealableComponent _armLSealable;

        /// <summary>오른팔 SealableComponent.</summary>
        [Tooltip("RightArm 의 SealableComponent.")]
        [SerializeField] private SealableComponent _armRSealable;

        /// <summary>
        /// 코어 SealableComponent.
        /// Core 오브젝트에 부착된 SealableComponent 연결.
        /// grade = Core / isDilPhaseOnly = true.
        /// OnSealCompleted → HandleCoreExecuted() (그로기 중: EnterDilPhase / 딜페이즈 중: Die).
        /// OnPhaseTargetReached → HandlePhaseTargetReached().
        /// </summary>
        [Tooltip("Core 의 SealableComponent.")]
        [SerializeField] private SealableComponent _coreSealable;

        [Header("── SealExecutor 연결 (필수) ──────────────────────")]

        /// <summary>
        /// SealExecutor. Boss_Warden 전용 집행 관리자.
        /// Start() 에서 Initialize(_data) 호출.
        /// </summary>
        [Tooltip("Boss_Warden SealExecutor. 같은 오브젝트 또는 자식에 부착.")]
        [SerializeField] private SealExecutor _sealExecutor;

        [Header("── 코어 오브젝트 ──────────────────────")]
        [Tooltip("코어 GameObject. 기본 SetActive=false.")]
        [SerializeField] private GameObject _coreObject;

        [Header("── 충격파 (선택) ──────────────────────")]
        [Tooltip("BossWardenShockwave. 미연결 시 충격파 스킵.")]
        [SerializeField] private BossWardenShockwave _shockwave;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        private Rigidbody2D _rigid2D;
        private BossWardenAI _ai;
        private BossWardenFeedback _feedback;
        private BossWardenAttackRange _attackRange;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private bool _isGroggy;
        private bool _isDilPhase;
        private bool _isCoreActive;
        private bool _isFinalSealReady;
        private bool _isDead;
        private int _currentPhase = 1;

        /// <summary>
        /// 봉인 완료된 팔 수.
        /// 양팔(2) 모두 봉인 시 그로기 진입.
        /// </summary>
        private int _sealedArmCount;

        private Coroutine _groggyCoroutine;
        private Coroutine _dilPhaseCoroutine;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        public event Action OnGroggyEnter;
        public event Action OnGroggyExit;
        public event Action OnDilPhaseEnter;
        public event Action OnDilPhaseExit;
        public event Action<int> OnPhaseChanged;
        public event Action OnFinalSealReady;
        public event Action OnDead;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        public bool IsGroggy => _isGroggy;
        public bool IsDilPhase => _isDilPhase;
        public bool IsCoreActive => _isCoreActive;
        public bool IsDead => _isDead;
        public int CurrentPhase => _currentPhase;
        public BossWardenDataSO Data => _data;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _rigid2D = GetComponent<Rigidbody2D>();
            _ai = GetComponent<BossWardenAI>();
            _feedback = GetComponent<BossWardenFeedback>();
            _attackRange = GetComponent<BossWardenAttackRange>();

            if (_data != null)
            {
                _armL?.Initialize(_data);
                _armR?.Initialize(_data);
                _coreSealable?.Initialize(_data);
                _ai?.Initialize(_data);
                _sealExecutor?.Initialize(_data);
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

            if (_coreObject != null)
                _coreObject.SetActive(false);

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
            // ✅ v2.0: SealableComponent.OnSealCompleted 직접 구독
            // 기존 OnPartSealed (BossWardenArmPart 이벤트) 제거
            if (_armLSealable != null)
            {
                _armLSealable.OnSealCompleted -= HandleArmLSealed;
                _armLSealable.OnSealCompleted += HandleArmLSealed;
            }
            if (_armRSealable != null)
            {
                _armRSealable.OnSealCompleted -= HandleArmRSealed;
                _armRSealable.OnSealCompleted += HandleArmRSealed;
            }

            // ✅ v2.0: Core SealableComponent 이벤트 구독
            // 기존 BossWardenCoreSealGauge 이벤트 구독 제거
            if (_coreSealable != null)
            {
                _coreSealable.OnSealCompleted -= HandleCoreExecuted;
                _coreSealable.OnPhaseTargetReached -= HandlePhaseTargetReached;

                _coreSealable.OnSealCompleted += HandleCoreExecuted;
                _coreSealable.OnPhaseTargetReached += HandlePhaseTargetReached;
            }

            // AI / Feedback 이벤트 구독 (기존 유지)
            if (_ai != null)
            {
                OnGroggyEnter -= _ai.HandleGroggyEnter;
                OnGroggyExit -= _ai.HandleGroggyExit;
                OnDilPhaseEnter -= _ai.HandleDilPhaseEnter;
                OnDilPhaseExit -= _ai.HandleDilPhaseExit;
                OnPhaseChanged -= _ai.HandlePhaseChanged;
                OnDead -= _ai.HandleDead;

                OnGroggyEnter += _ai.HandleGroggyEnter;
                OnGroggyExit += _ai.HandleGroggyExit;
                OnDilPhaseEnter += _ai.HandleDilPhaseEnter;
                OnDilPhaseExit += _ai.HandleDilPhaseExit;
                OnPhaseChanged += _ai.HandlePhaseChanged;
                OnDead += _ai.HandleDead;
            }

            if (_feedback != null)
            {
                OnGroggyEnter -= _feedback.HandleGroggyEnter;
                OnGroggyExit -= _feedback.HandleGroggyExit;
                OnDilPhaseEnter -= _feedback.HandleDilPhaseEnter;
                OnDilPhaseExit -= _feedback.HandleDilPhaseExit;
                OnPhaseChanged -= _feedback.HandlePhaseChanged;
                OnDead -= _feedback.HandleDead;

                OnGroggyEnter += _feedback.HandleGroggyEnter;
                OnGroggyExit += _feedback.HandleGroggyExit;
                OnDilPhaseEnter += _feedback.HandleDilPhaseEnter;
                OnDilPhaseExit += _feedback.HandleDilPhaseExit;
                OnPhaseChanged += _feedback.HandlePhaseChanged;
                OnDead += _feedback.HandleDead;
            }
        }

        private void UnsubscribeAll()
        {
            if (_armLSealable != null) _armLSealable.OnSealCompleted -= HandleArmLSealed;
            if (_armRSealable != null) _armRSealable.OnSealCompleted -= HandleArmRSealed;

            if (_coreSealable != null)
            {
                _coreSealable.OnSealCompleted -= HandleCoreExecuted;
                _coreSealable.OnPhaseTargetReached -= HandlePhaseTargetReached;
            }

            if (_ai != null)
            {
                OnGroggyEnter -= _ai.HandleGroggyEnter;
                OnGroggyExit -= _ai.HandleGroggyExit;
                OnDilPhaseEnter -= _ai.HandleDilPhaseEnter;
                OnDilPhaseExit -= _ai.HandleDilPhaseExit;
                OnPhaseChanged -= _ai.HandlePhaseChanged;
                OnDead -= _ai.HandleDead;
            }

            if (_feedback != null)
            {
                OnGroggyEnter -= _feedback.HandleGroggyEnter;
                OnGroggyExit -= _feedback.HandleGroggyExit;
                OnDilPhaseEnter -= _feedback.HandleDilPhaseEnter;
                OnDilPhaseExit -= _feedback.HandleDilPhaseExit;
                OnPhaseChanged -= _feedback.HandlePhaseChanged;
                OnDead -= _feedback.HandleDead;
            }
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러 — 팔 봉인 완료
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// LeftArm SealableComponent.OnSealCompleted 수신.
        /// </summary>
        private void HandleArmLSealed()
        {
            HandleArmSealed(WardenPartType.LeftArm);
        }

        /// <summary>
        /// RightArm SealableComponent.OnSealCompleted 수신.
        /// </summary>
        private void HandleArmRSealed()
        {
            HandleArmSealed(WardenPartType.RightArm);
        }

        private void HandleArmSealed(WardenPartType partType)
        {
            if (_isGroggy || _isDilPhase || _isDead) return;

            _sealedArmCount++;
            Debug.Log($"[BossWardenCore] 봉인 완료: {partType} (총 {_sealedArmCount}/2)");

            CheckGroggyCondition();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러 — 코어 봉인도 페이즈 목표
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Core SealableComponent.OnPhaseTargetReached 수신.
        /// 딜 페이즈 종료 처리.
        /// </summary>
        private void HandlePhaseTargetReached()
        {
            if (!_isDilPhase || _isDead) return;

            bool isFinalSeal = _currentPhase >= 2;
            Debug.Log($"[BossWardenCore] 코어 봉인도 목표 도달 | 페이즈:{_currentPhase} 최종봉인:{isFinalSeal}");
            ExitDilPhase(isFinalSeal);
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러 — 코어 집행 완료
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Core SealableComponent.OnSealCompleted 수신.
        ///
        /// [버그2, 3 수정]
        ///   기존: HandleFinalSealed() → 항상 Die() 호출
        ///         코어 해제(그로기 중)와 최종 봉인(딜 페이즈 중)을 구별 못함
        ///
        ///   수정: BossWardenCore 현재 상태로 분기
        ///     _isGroggy = true  → 코어 해제 → EnterDilPhase()
        ///     _isDilPhase = true → 최종 봉인 → Die()
        ///     그 외             → 경고 로그 + 무시
        ///
        /// [코어 집행 시점별 상태]
        ///   그로기 진입 → 코어 활성 → 플레이어 S키 집행
        ///     → 이 시점 _isGroggy = true, _isDilPhase = false
        ///     → EnterDilPhase() 호출
        ///
        ///   딜 페이즈 진입 → 코어 봉인도 100% → OnSealRequested → 플레이어 S키 집행
        ///     → 이 시점 _isGroggy = false, _isDilPhase = true
        ///     → Die() 호출
        /// </summary>
        private void HandleCoreExecuted()
        {
            if (_isDead) return;

            if (_isGroggy && !_isDilPhase)
            {
                Debug.Log("[BossWardenCore] 코어 해제 집행 완료 → 딜 페이즈 진입");
                EnterDilPhase();
            }
            else if (_isDilPhase)
            {
                Debug.Log("[BossWardenCore] 최종 봉인 집행 완료 → 처치");
                Die();
            }
            else
            {
                Debug.LogWarning("[BossWardenCore] HandleCoreExecuted — 예상치 못한 상태. 무시.");
            }
        }

        // ══════════════════════════════════════════════════════
        // 그로기 조건 체크
        // ══════════════════════════════════════════════════════

        private void CheckGroggyCondition()
        {
            bool armLSealed = _armLSealable != null && _armLSealable.IsSealed;
            bool armRSealed = _armRSealable != null && _armRSealable.IsSealed;

            if (armLSealed && armRSealed)
            {
                Debug.Log("[BossWardenCore] 양팔 봉인 완료 → 그로기 진입");
                EnterGroggy();
            }
        }

        // ══════════════════════════════════════════════════════
        // 그로기
        // ══════════════════════════════════════════════════════

        public void EnterGroggy()
        {
            if (_isGroggy || _isDilPhase || _isDead) return;

            _isGroggy = true;
            if (_rigid2D != null) _rigid2D.linearVelocity = Vector2.zero;

            ActivateCore();
            OnGroggyEnter?.Invoke();

            if (_groggyCoroutine != null) StopCoroutine(_groggyCoroutine);
            _groggyCoroutine = StartCoroutine(GroggyRoutine());

            Debug.Log($"[BossWardenCore] 그로기 진입 ({_data.groggyDuration:F1}초)");
        }

        private IEnumerator GroggyRoutine()
        {
            yield return new WaitForSecondsRealtime(_data.groggyDuration);
            _groggyCoroutine = null;

            if (_isGroggy)
            {
                Debug.Log("[BossWardenCore] 그로기 타이머 만료 → 실패 종료");
                ExitGroggyFailure();
            }
        }

        private void ExitGroggyFailure()
        {
            _isGroggy = false;
            _sealedArmCount = 0;

            DeactivateCore();

            // ✅ v2.0: SealableComponent.ForceRelease() 직접 호출
            _armLSealable?.ForceRelease(false);
            _armRSealable?.ForceRelease(false);

            OnGroggyExit?.Invoke();
            Debug.Log("[BossWardenCore] 그로기 실패 종료 → AI 재개");
        }

        // ══════════════════════════════════════════════════════
        // 딜 페이즈
        // ══════════════════════════════════════════════════════

        public void EnterDilPhase()
        {
            if (_isDilPhase || _isDead) return;

            _isGroggy = false;
            _isDilPhase = true;

            if (_groggyCoroutine != null)
            {
                StopCoroutine(_groggyCoroutine);
                _groggyCoroutine = null;
            }

            // ✅ v2.0: SealableComponent.ActivateGauge(true)
            _coreSealable?.ActivateGauge(true);

            OnDilPhaseEnter?.Invoke();

            if (_dilPhaseCoroutine != null) StopCoroutine(_dilPhaseCoroutine);
            _dilPhaseCoroutine = StartCoroutine(DilPhaseRoutine());

            Debug.Log($"[BossWardenCore] 딜 페이즈 진입 ({_data.dilPhaseDuration:F1}초)");
        }

        private IEnumerator DilPhaseRoutine()
        {
            yield return new WaitForSecondsRealtime(_data.dilPhaseDuration);
            _dilPhaseCoroutine = null;

            if (_isDilPhase)
            {
                Debug.Log("[BossWardenCore] 딜 페이즈 타이머 만료 → 종료");
                ExitDilPhase(isFinalSeal: false);
            }
        }

        private void ExitDilPhase(bool isFinalSeal)
        {
            _isDilPhase = false;

            if (_dilPhaseCoroutine != null)
            {
                StopCoroutine(_dilPhaseCoroutine);
                _dilPhaseCoroutine = null;
            }

            // ✅ v2.0: SealableComponent.ActivateGauge(false)
            _coreSealable?.ActivateGauge(false);

            if (isFinalSeal)
            {
                // 최종 봉인 진입
                _isFinalSealReady = true;
                OnFinalSealReady?.Invoke();
                Debug.Log("[BossWardenCore] 최종 봉인 준비");
                return;
            }

            // 일반 딜 페이즈 종료
            DeactivateCore();
            _sealedArmCount = 0;

            // ✅ v2.0: SealableComponent.ForceRelease()
            _armLSealable?.ForceRelease(false);
            _armRSealable?.ForceRelease(false);

            _shockwave?.Trigger(transform.position);

            OnDilPhaseExit?.Invoke();

            if (_currentPhase == 1)
            {
                _currentPhase = 2;
                OnPhaseChanged?.Invoke(2);
                Debug.Log("[BossWardenCore] 2페이즈 전환");
            }
        }

        // ══════════════════════════════════════════════════════
        // 코어 활성/비활성
        // ══════════════════════════════════════════════════════

        private void ActivateCore()
        {
            if (_coreObject != null) _coreObject.SetActive(true);
            _isCoreActive = true;
            Debug.Log("[BossWardenCore] 코어 활성");
        }

        private void DeactivateCore()
        {
            if (_coreObject != null) _coreObject.SetActive(false);
            _isCoreActive = false;
            Debug.Log("[BossWardenCore] 코어 비활성");
        }

        // ══════════════════════════════════════════════════════
        // 처치
        // ══════════════════════════════════════════════════════

        private void Die()
        {
            if (_isDead) return;

            _isDead = true;
            _isGroggy = false;
            _isDilPhase = false;

            StopAllCoroutines();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            DeactivateCore();
            _coreSealable?.ActivateGauge(false);
            _attackRange?.HideAll();

            OnDead?.Invoke();
            Debug.Log("[BossWardenCore] 보스 처치!");
        }

        // ══════════════════════════════════════════════════════
        // 디버그
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        [ContextMenu("DEBUG: 그로기 강제 진입")]
        public void DEBUG_EnterGroggy() => EnterGroggy();

        [ContextMenu("DEBUG: 딜 페이즈 강제 진입")]
        public void DEBUG_EnterDilPhase() => EnterDilPhase();
#endif
    }
}