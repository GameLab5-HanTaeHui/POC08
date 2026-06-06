// ============================================================
// SealManager.cs  v1.0
// 봉인 흐름 규칙 정의 — 보스별 확장 베이스
//
// [역할]
//   SealGaugeManager + SealExecutionEvent + SealStateManager 를 연결하고
//   "어떤 조건에서 그로기/딜페이즈로 전환되는가" 라는
//   보스별 고유 게임 규칙을 정의하는 컴포넌트.
//
// [설계 원칙]
//   이 클래스의 기본 구현: "모든 Part 봉인 완료 → 그로기"
//   보스별 확장: 이 클래스를 상속하여 고유 규칙 오버라이드
//
//   예시:
//     WardenSealManager : SealManager
//       → 양팔(LeftArm + RightArm) 봉인 완료 → 그로기
//
//     DragonSealManager : SealManager
//       → 날개(LeftWing + RightWing) + 꼬리(Tail) 3개 봉인 → 그로기
//
// [범용 SealManager 기본 규칙]
//   RequiredSealCountForGroggy = 전체 Part 수
//   → 모든 Part 봉인 완료 시 그로기 진입 (SealGaugeManager.OnAllPartsSealed 구독)
//
//   보스별 규칙이 다른 경우:
//     SealManager 상속 후 SetupRules() 오버라이드
//     또는 Inspector 에서 _requiredSealCount 를 직접 설정
//
// [컴포넌트 연결]
//   같은 Boss_Root 오브젝트에
//   SealGaugeManager + SealStateManager + SealExecutionEvent 가 함께 있어야 함.
//
// [부착 위치]
//   Boss_Root 오브젝트에 부착. (보스 1개당 1개)
//   Warden 은 WardenSealManager : SealManager 상속 버전 사용.
//
// [namespace] SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 봉인 흐름 규칙 정의 베이스. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [기본 규칙]
    ///   모든 Part 봉인 완료 → 그로기 진입
    ///   SealGaugeManager.OnAllPartsSealed 이벤트 수신 시 동작.
    ///
    /// [보스별 확장]
    ///   이 클래스를 상속하여 SetupRules() 오버라이드.
    ///   또는 _requiredSealCount 를 Inspector 에서 직접 설정.
    ///
    /// [컴포넌트 의존성]
    ///   SealGaugeManager   → 봉인도 상태 조회
    ///   SealStateManager   → 상태 전환 요청
    ///   SealExecutionEvent → 집행 가능 목록 관리
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class SealManager : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 그로기 진입 규칙 ──────────────────────")]

        /// <summary>
        /// 그로기 진입에 필요한 Part 봉인 완료 수.
        /// 0 이면 자동 설정 (전체 Part 수 = SealGaugeManager.GetPartCount()).
        ///
        /// [Warden 기본값] 0 (자동 = 양팔 2개)
        /// [Dragon 예시]  3 (날개 2 + 꼬리 1)
        /// </summary>
        [Tooltip("그로기 진입 필요 Part 봉인 수. 0=자동(전체 Part 수).")]
        [Min(0)]
        [SerializeField] protected int _requiredSealCountForGroggy = 0;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>봉인도 전체 조율. 상태 조회 + 이벤트 구독.</summary>
        protected SealGaugeManager _gaugeManager;

        /// <summary>상태 총괄. 그로기/딜페이즈 전환 요청.</summary>
        protected SealStateManager _stateManager;

        /// <summary>집행 이벤트. 집행 가능 목록 관리.</summary>
        protected SealExecutionEvent _executionEvent;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        protected virtual void Awake()
        {
            _gaugeManager = GetComponent<SealGaugeManager>();
            _stateManager = GetComponent<SealStateManager>();
            _executionEvent = GetComponent<SealExecutionEvent>();

            if (_gaugeManager == null)
                Debug.LogError($"[SealManager] {gameObject.name} — SealGaugeManager 미연결.");
            if (_stateManager == null)
                Debug.LogError($"[SealManager] {gameObject.name} — SealStateManager 미연결.");
        }

        protected virtual void Start()
        {
            // 규칙 초기화 (서브클래스에서 오버라이드 가능)
            SetupRules();
        }

        protected virtual void OnDestroy()
        {
            TeardownRules();
        }

        // ══════════════════════════════════════════════════════
        // 규칙 설정 (오버라이드 진입점)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 규칙 초기화. Start() 에서 호출.
        /// 서브클래스에서 오버라이드하여 보스별 규칙 적용.
        ///
        /// [기본 구현]
        ///   _requiredSealCountForGroggy = 0 이면 → 전체 Part 수 자동 설정.
        ///   SealGaugeManager.OnAllPartsSealed 구독.
        ///
        /// [오버라이드 예시 — WardenSealManager]
        ///   override void SetupRules() {
        ///     base.SetupRules();
        ///     // 추가 규칙: 한쪽 팔 봉인 시 너프 적용 등
        ///   }
        /// </summary>
        protected virtual void SetupRules()
        {
            // _requiredSealCountForGroggy = 0 이면 전체 Part 수 자동 설정
            if (_requiredSealCountForGroggy == 0 && _gaugeManager != null)
                _requiredSealCountForGroggy = _gaugeManager.GetPartCount();

            // 봉인 완료 이벤트 구독
            if (_gaugeManager != null)
            {
                _gaugeManager.OnAllPartsSealed -= HandleAllPartsSealed;
                _gaugeManager.OnAllPartsSealed += HandleAllPartsSealed;
            }

            Debug.Log($"[SealManager] {gameObject.name} 규칙 설정 | " +
                      $"그로기 필요 봉인 수:{_requiredSealCountForGroggy}");
        }

        /// <summary>
        /// 봉인 규칙 해제. OnDestroy() 에서 호출.
        /// </summary>
        protected virtual void TeardownRules()
        {
            if (_gaugeManager != null)
                _gaugeManager.OnAllPartsSealed -= HandleAllPartsSealed;
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealGaugeManager.OnAllPartsSealed 수신.
        /// 봉인 완료 수가 요구치 이상 → 그로기 진입 조건 충족 알림.
        ///
        /// [기본 구현]
        ///   모든 Part 봉인 완료 시 CheckGroggyCondition() 호출.
        ///
        /// [서브클래스에서 오버라이드 가능]
        ///   특정 부위 조합만 봉인 완료 시 그로기 진입하는 규칙 등.
        /// </summary>
        protected virtual void HandleAllPartsSealed()
        {
            CheckGroggyCondition();
        }

        // ══════════════════════════════════════════════════════
        // 그로기 조건 체크
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 그로기 진입 조건 체크.
        /// 봉인 완료 수 >= _requiredSealCountForGroggy 이면 조건 충족.
        ///
        /// [서브클래스에서 오버라이드]
        ///   특정 부위(LeftArm + RightArm) 조합 체크 등.
        /// </summary>
        protected virtual void CheckGroggyCondition()
        {
            if (_gaugeManager == null || _stateManager == null) return;
            if (_stateManager.IsDead) return;

            int sealedCount = _gaugeManager.GetSealedCount(SealGrade.Part);

            if (sealedCount >= _requiredSealCountForGroggy)
            {
                Debug.Log($"[SealManager] ▶ 그로기 조건 충족 " +
                          $"({sealedCount}/{_requiredSealCountForGroggy}) → SealStateManager 통보");

                // SealStateManager 는 OnAllPartsSealed 이벤트로 직접 그로기 진입 처리.
                // SealManager 는 규칙 체크만 담당.
                // 실제 EnterGroggy 는 SealStateManager.HandleAllPartsSealed() 에서 실행.
            }
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossDataSO 주입. BossWardenCore.Initialize() 에서 호출.
        /// 서브클래스에서 오버라이드하여 추가 초기화 가능.
        /// </summary>
        public virtual void Initialize(BossDataSO data)
        {
            Debug.Log($"[SealManager] {gameObject.name} Initialize");
        }

        /// <summary>
        /// 현재 그로기 진입 가능 여부 반환.
        /// SealStateManager 또는 외부에서 조건 확인 시 사용.
        /// </summary>
        public virtual bool CanEnterGroggy()
        {
            if (_gaugeManager == null) return false;
            return _gaugeManager.GetSealedCount(SealGrade.Part) >= _requiredSealCountForGroggy;
        }
    }
}