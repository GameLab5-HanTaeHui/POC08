// ============================================================
// BossWardenPart.cs  v1.0
// Boss_Warden 부위 피격 처리 통합 컴포넌트
//
// [BossWardenArmPart 에서 통합]
//   기존: BossWardenArmPart (팔 전용) + BossWardenCorePart (코어 전용) 별도 파일
//   변경: BossWardenPart 하나로 통합
//         LeftArm / RightArm / Core 모두 이 컴포넌트 사용
//
// [부위 타입별 동작 차이]
//   PartType = LeftArm / RightArm
//     → _isRecoveryVuln / _isSlamVuln 배율 지원
//     → GuardBreak 정면 봉인도 무효 지원 (_guardBreakPattern 연결 시)
//     → 봉인 완료 시 HurtBox Collider 비활성 (재진입 피격 차단)
//     → 봉인 해제 시 HurtBox Collider 재활성
//
//   PartType = Core
//     → _isRecoveryVuln / _isSlamVuln 배율 없음
//     → DilPhase 외 차단은 SealableComponent._isDilPhaseOnly 에서 처리
//     → 봉인 완료 시 HurtBox Collider 비활성 (동일)
//
// [공통 동작]
//   PlayerAttackHitboxManager.OnHit 구독
//   → hitCol == _ownCollider 일 때만 처리
//   → SealableComponent.AddGauge() 호출
//   → SealableComponent.PlayHitFlash() 피격 점멸 위임
//   봉인 완료 → HurtBox Collider 비활성
//   봉인 해제 → HurtBox Collider 재활성
//
// [BossWardenCore 호환성]
//   Initialize(BossWardenDataSO) 서명 유지
//   IsSealed / Sealable 프로퍼티 유지
//   SetRecoveryVuln() / SetSlamVuln() 유지 (Core 에서 호출 시 무시됨)
//   PartType 프로퍼티는 WardenPartType 열거형 사용
//
// [기존 BossWardenArmPart 참조 교체]
//   BossWardenAI._armL / _armR → BossWardenPart 타입으로 변경 필요
//   BossWardenCore._armL / _armR → BossWardenPart 타입으로 변경 필요
//   BossPattern_Slam._armLTransform 등 Transform 참조는 변경 불필요
//
// [namespace] SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Warden 부위 타입.
    /// LeftArm / RightArm / Core 를 구분.
    /// </summary>
    public enum WardenPartType
    {
        /// <summary>왼팔. Recovery/Slam 배율 적용. GuardBreak 정면 무효 없음.</summary>
        LeftArm,
        /// <summary>오른팔. Recovery/Slam 배율 적용. GuardBreak 정면 무효 지원.</summary>
        RightArm,
        /// <summary>코어. 배율 없음. _isDilPhaseOnly 로 DilPhase 외 차단.</summary>
        Core,
    }

    /// <summary>
    /// Boss_Warden 부위 피격 처리 통합 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [공통 흐름]
    ///   PlayerAttackHitboxManager.OnHit
    ///     → hitCol == _ownCollider 확인
    ///     → 부위별 배율 / 가드 체크
    ///     → SealableComponent.AddGauge()
    ///     → SealableComponent.PlayHitFlash()
    ///
    ///   봉인 완료 → _ownCollider.enabled = false
    ///   봉인 해제 → _ownCollider.enabled = true
    ///
    /// [Inspector 설정]
    ///   _partType      → 부위 타입 선택
    ///   _ownCollider   → HurtBox Collider2D 연결
    ///   _data          → BossWardenDataSO (BossWardenCore 에서 주입)
    ///   _guardBreakPattern → RightArm 전용 (LeftArm/Core 는 null)
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(SealableComponent))]
    public class BossWardenPart : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 부위 설정 ──────────────────────")]

        /// <summary>
        /// 부위 타입.
        /// LeftArm / RightArm / Core 중 선택.
        /// </summary>
        [Tooltip("부위 타입. LeftArm / RightArm / Core 중 선택.")]
        [SerializeField] private WardenPartType _partType;

        [Header("── 컴포넌트 연결 ──────────────────────")]

        /// <summary>
        /// 이 부위의 HurtBox Collider2D.
        /// PlayerAttackHitboxManager.OnHit 의 hitCol 과 비교.
        ///   LeftArm  → LeftHurtBox Collider2D
        ///   RightArm → RightHurtBox Collider2D
        ///   Core     → CoreHurtBox Collider2D
        /// </summary>
        [Tooltip("HurtBox Collider2D. LeftHurtBox/RightHurtBox/CoreHurtBox 연결.")]
        [SerializeField] private Collider2D _ownCollider;

        /// <summary>
        /// BossWardenDataSO.
        /// BossWardenCore.InjectData() 에서 주입.
        /// Arm 배율 계산에 사용. Core 는 배율 없으므로 null 허용.
        /// </summary>
        [Tooltip("BossWardenDataSO. BossWardenCore 에서 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── GuardBreak 연동 (RightArm 전용) ──────────────────────")]

        /// <summary>
        /// BossPattern_GuardBreak 참조.
        /// IsGuarding = true + 플레이어 정면 → 봉인도 누적 차단.
        /// RightArm 만 연결. LeftArm / Core 는 null.
        /// </summary>
        [Tooltip("GuardBreak 패턴. RightArm 전용. LeftArm/Core 는 null.")]
        [SerializeField] private BossPattern_GuardBreak _guardBreakPattern;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>봉인도 관리. Awake 에서 GetComponent.</summary>
        private SealableComponent _sealable;

        /// <summary>PlayerAttackHitboxManager 참조. Start 에서 캐싱.</summary>
        private PlayerAttackHitboxManager _hitboxManager;

        /// <summary>플레이어 Transform. IsPlayerFacingFront() 에서 사용. Start 1회 캐싱.</summary>
        private Transform _playerTransform;

        /// <summary>보스 AI. 정면 방향 계산에 사용. Awake 에서 GetComponentInParent.</summary>
        private BossWardenAI _ai;

        // ══════════════════════════════════════════════════════
        // 내부 상태 — 팔 전용 배율 (Core 는 미사용)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Recovery 취약 구간 활성 여부.
        /// BossWardenAI 에서 SetRecoveryVuln() 으로 전환.
        /// Core 는 항상 false.
        /// </summary>
        private bool _isRecoveryVuln;

        /// <summary>
        /// 패턴 공략 타임 배율 활성 여부.
        /// Slam/Sweep 패턴에서 SetSlamVuln() 으로 전환.
        /// Core 는 항상 false.
        /// </summary>
        private bool _isSlamVuln;

        /// <summary>패턴 공략 타임 봉인도 배율. 기본 1.0.</summary>
        private float _slamVulnMultiplier = 1f;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>봉인 완료 여부. SealableComponent 위임.</summary>
        public bool IsSealed => _sealable != null && _sealable.IsSealed;

        /// <summary>부위 타입 식별자.</summary>
        public WardenPartType PartType => _partType;

        /// <summary>
        /// SealableComponent 직접 참조.
        /// SealGaugeManager / 패턴 너프 체크용.
        /// </summary>
        public SealableComponent Sealable => _sealable;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _sealable = GetComponent<SealableComponent>();
            _ai = GetComponentInParent<BossWardenAI>();

            if (_sealable == null)
                Debug.LogError($"[BossWardenPart] {gameObject.name} — SealableComponent 미부착.");
            if (_ownCollider == null)
                Debug.LogWarning($"[BossWardenPart] {gameObject.name} — _ownCollider 미연결.");
        }

        private void Start()
        {
            // PlayerAttackHitboxManager 구독
            var managers = FindObjectsByType<PlayerAttackHitboxManager>(FindObjectsSortMode.None);
            if (managers.Length > 0)
            {
                _hitboxManager = managers[0];
                _hitboxManager.OnHit += HandlePlayerHit;
                Debug.Log($"[BossWardenPart] {_partType} — OnHit 구독 완료");
            }
            else
                Debug.LogWarning($"[BossWardenPart] {_partType} — PlayerAttackHitboxManager 없음.");

            // 플레이어 Transform 1회 캐싱
            var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (players.Length > 0)
                _playerTransform = players[0].transform;

            // 봉인 완료 / 해제 이벤트 구독 → HurtBox Collider 제어
            if (_sealable != null)
            {
                _sealable.OnSealCompleted += HandleSealCompleted;
                _sealable.OnForceReleased += HandleForceReleased;
            }
        }

        private void OnDestroy()
        {
            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandlePlayerHit;

            if (_sealable != null)
            {
                _sealable.OnSealCompleted -= HandleSealCompleted;
                _sealable.OnForceReleased -= HandleForceReleased;
            }
        }

        // ══════════════════════════════════════════════════════
        // 초기화 (BossWardenCore 에서 호출)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenDataSO 주입 + SealableComponent 초기화.
        /// BossWardenCore.Start() → InjectData() 에서 호출.
        ///
        /// [BossDataSO 업캐스팅]
        ///   SealableComponent 는 범용 BossDataSO 만 참조.
        ///   BossWardenDataSO (BossDataSO 상속) 를 명시적 업캐스팅 후 전달.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
            _sealable?.Initialize((BossDataSO)data);
            Debug.Log($"[BossWardenPart] {_partType} 초기화 완료");
        }

        // ══════════════════════════════════════════════════════
        // 피격 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신.
        /// hitCol 이 _ownCollider 와 일치할 때만 봉인도 누적.
        ///
        /// [부위별 처리 분기]
        ///   Core
        ///     → 배율 없음
        ///     → SealableComponent._isDilPhaseOnly 가 DilPhase 외 차단 처리
        ///
        ///   LeftArm / RightArm
        ///     → GuardBreak 가드 중 정면 공격 봉인도 차단 (RightArm + _guardBreakPattern 연결 시)
        ///     → RecoveryVuln 배율 적용
        ///     → SlamVuln 배율 적용
        ///
        /// [피격 점멸]
        ///   SealableComponent.PlayHitFlash() 위임
        ///   colorHitFlash 로 점멸 후 현재 봉인도 색상으로 자동 복귀
        /// </summary>
        private void HandlePlayerHit(Collider2D hitCol, float sealAmount)
        {
            if (hitCol != _ownCollider) return;
            if (_sealable == null) return;
            if (IsSealed) return;

            float rawAmount = sealAmount;

            // ── 팔 전용 처리 ──────────────────────────────────
            if (_partType != WardenPartType.Core)
            {
                // GuardBreak 정면 봉인도 무효 처리 (RightArm + _guardBreakPattern 연결 시)
                if (_guardBreakPattern != null && _guardBreakPattern.IsGuarding)
                {
                    if (IsPlayerFacingFront())
                    {
                        // 막혔다는 시각 피드백 후 종료
                        _sealable.PlayHitFlash();
                        Debug.Log($"[BossWardenPart] {_partType} — 가드 중 정면 봉인도 무효");
                        return;
                    }
                }

                // Recovery 취약 구간 배율
                if (_isRecoveryVuln && _data != null)
                    rawAmount *= _data.recoveryVulnMultiplier;

                // 패턴 공략 타임 배율 (Slam/Sweep 팔 분리 구간)
                if (_isSlamVuln)
                    rawAmount *= _slamVulnMultiplier;
            }

            // 봉인도 누적
            _sealable.AddGauge(rawAmount);

            // 피격 점멸
            _sealable.PlayHitFlash();
        }

        // ══════════════════════════════════════════════════════
        // 봉인 상태 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// SealableComponent.OnSealCompleted 수신.
        /// HurtBox Collider 비활성화 → 봉인 완료 후 피격 차단.
        ///
        /// [이유]
        ///   봉인 완료 후에도 PlayerAttackHitboxManager 가 계속 hitCol 감지
        ///   → 파티클 낭비 + HandlePlayerHit IsSealed 체크로 봉인도는 막히지만
        ///     피격 판정 자체가 계속 발생 → Collider 비활성으로 근본 차단
        /// </summary>
        private void HandleSealCompleted()
        {
            if (_ownCollider != null)
                _ownCollider.enabled = false;

            Debug.Log($"[BossWardenPart] {_partType} 봉인 완료 → HurtBox Collider 비활성");
        }

        /// <summary>
        /// SealableComponent.OnForceReleased 수신.
        /// HurtBox Collider 재활성화 → 다음 봉인 사이클에서 피격 가능.
        /// </summary>
        private void HandleForceReleased()
        {
            if (_ownCollider != null)
                _ownCollider.enabled = true;

            Debug.Log($"[BossWardenPart] {_partType} 봉인 해제 → HurtBox Collider 재활성");
        }

        // ══════════════════════════════════════════════════════
        // 정면 판단
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 플레이어가 보스 정면 60도 이내에 있는지 체크.
        /// GuardBreak 봉인도 무효 판단에 사용.
        ///
        /// [계산 방법]
        ///   BossWardenAI.FacingDir 와 (부위→플레이어 방향) 내적
        ///   > cos(60°) = 0.5f → 정면 60도 이내 → true
        /// </summary>
        private bool IsPlayerFacingFront()
        {
            if (_ai == null || _playerTransform == null) return false;

            Vector2 toPlayer = ((Vector2)_playerTransform.position
                - (Vector2)transform.position).normalized;

            return Vector2.Dot(_ai.FacingDir, toPlayer) > 0.5f;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API — 패턴 / AI 에서 호출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Recovery 취약 구간 활성/비활성.
        /// BossWardenAI 에서 Recovery 상태 진입/종료 시 호출.
        /// Core 타입에서 호출 시 무시됨.
        /// </summary>
        public void SetRecoveryVuln(bool isVuln)
        {
            if (_partType == WardenPartType.Core) return;
            _isRecoveryVuln = isVuln;
        }

        /// <summary>
        /// 패턴 공략 타임 배율 설정.
        /// Slam / Sweep 패턴에서 팔 분리 구간 시작/종료 시 호출.
        /// Core 타입에서 호출 시 무시됨.
        ///
        /// [사용 예시]
        ///   _part.SetSlamVuln(true, 2.0f)   // 공략 타임 시작
        ///   _part.SetSlamVuln(false)          // 공략 타임 종료
        /// </summary>
        public void SetSlamVuln(bool isActive, float multiplier = 1f)
        {
            if (_partType == WardenPartType.Core) return;
            _isSlamVuln = isActive;
            _slamVulnMultiplier = multiplier;
        }

        /// <summary>
        /// 봉인 강제 해제.
        /// BossWardenCore / SealGaugeManager 에서 DilPhase 종료 시 호출.
        /// </summary>
        public void ForceRelease(bool resetSealCount = false)
        {
            _sealable?.ForceRelease(resetSealCount);
        }
    }
}