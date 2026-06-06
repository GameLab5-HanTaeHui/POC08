// ============================================================
// BossWardenArmPart.cs  v3.0
// Boss_Warden 팔 부위 피격 + 봉인도 누적 컴포넌트
//
// [v3.0 — BossDataSO 주입 교체 + 피격 점멸 위임]
//
//   [변경 1] Initialize 파라미터 교체
//     기존: Initialize(BossWardenDataSO data)
//           → _sealable.Initialize(data) 로 BossWardenDataSO 전달
//             BossWardenDataSO 는 BossDataSO 를 상속하지만
//             SealableComponent 내부에서 BossDataSO 로 캐스팅하는 구조였음
//
//     변경: Initialize(BossWardenDataSO data) 서명 유지
//           내부에서 BossDataSO 로 명시적 업캐스팅 후 전달
//           → _sealable.Initialize((BossDataSO)data)
//           SealableComponent 는 BossDataSO 만 알면 됨 (범용 원칙 준수)
//
//   [변경 2] 피격 점멸 SealableComponent 위임
//     기존: PlayHitFlash() 직접 SpriteRenderer DOTween 처리
//           Color.white 점멸 → 봉인도 색상 정보 유실 위험
//
//     변경: _sealable.PlayHitFlash() 위임
//           SealableComponent v2.0 이 colorHitFlash 로 점멸 처리
//           점멸 종료 후 현재 봉인도 비율 색상으로 자동 복귀
//           → 봉인도 색상 초기화 문제 완전 해결
//
//     [SpriteRenderer 직접 점멸 제거]
//       _spriteRenderer / _baseColor / _flashTween 필드 제거
//       색상 관련 로직 전부 SealableComponent 에 위임
//       BossWardenArmPart 는 피격 감지 + 봉인도 전달만 담당
//
//   [v2.1 유지]
//     PlayerAttackHitboxManager.OnHit 구독 방식
//     GuardBreak IsGuarding 정면 봉인도 무효 처리
//     IsPlayerFacingFront() 체크
//     _isRecoveryVuln / _isSlamVuln 배율 처리
//     SetSlamVuln() / SetRecoveryVuln() 외부 API
//     IsSealed 프로퍼티 (SealableComponent 위임)
//     PartType / Sealable 프로퍼티
//
// [namespace] SEAL
// ============================================================

using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    public enum WardenPartType
    {
        LeftArm,
        RightArm,
    }
    /// <summary>
    /// Boss_Warden 팔 부위 피격 + 봉인도 누적 컴포넌트. (v3.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [역할]
    ///   PlayerAttackHitboxManager.OnHit 구독
    ///   → 배율 적용 후 SealableComponent.AddGauge() 호출
    ///   봉인도 관리 + 색상 점멸 → SealableComponent 에 완전 위임
    ///   GuardBreak 가드 중 정면 공격 봉인도 차단
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossWardenArmPart : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 부위 설정 ──────────────────────")]

        /// <summary>
        /// 팔 타입. LeftArm / RightArm 구분.
        /// BossWardenCore / 패턴에서 부위 식별에 사용.
        /// </summary>
        [Tooltip("팔 타입. LeftArm 또는 RightArm.")]
        [SerializeField] private WardenPartType _partType;

        [Header("── 컴포넌트 연결 ──────────────────────")]

        /// <summary>
        /// 이 팔의 HurtBox Collider2D.
        /// PlayerAttackHitboxManager.OnHit 의 hitCol 과 비교.
        /// LeftHurtBox / RightHurtBox 의 Collider2D 연결.
        /// </summary>
        [Tooltip("팔 HurtBox Collider2D. LeftHurtBox/RightHurtBox 연결.")]
        [SerializeField] private Collider2D _ownCollider;

        /// <summary>
        /// BossWardenDataSO.
        /// BossWardenCore.Initialize() 에서 주입.
        /// recoveryVulnMultiplier 참조에 사용.
        /// </summary>
        [Tooltip("BossWardenDataSO. BossWardenCore.Initialize() 에서 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── GuardBreak 연동 (RightArm 전용) ──────────────────────")]

        /// <summary>
        /// BossPattern_GuardBreak 참조.
        /// IsGuarding = true + 플레이어 정면 → 봉인도 누적 차단.
        /// LeftArm 은 null 로 두기 (체크 자동 스킵).
        /// </summary>
        [Tooltip("GuardBreak 패턴. RightArm 전용. LeftArm 은 null.")]
        [SerializeField] private BossPattern_GuardBreak _guardBreakPattern;

        // ══════════════════════════════════════════════════════
        // 내부 컴포넌트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 관리 컴포넌트.
        /// Awake 에서 GetComponent 자동 탐색.
        /// AddGauge / PlayHitFlash / IsSealed 모두 여기에 위임.
        /// </summary>
        private SealableComponent _sealable;

        /// <summary>PlayerAttackHitboxManager 참조. Start 에서 캐싱.</summary>
        private PlayerAttackHitboxManager _hitboxManager;

        /// <summary>BossWardenAI 참조. IsPlayerFacingFront() 에서 FacingDir 조회.</summary>
        private BossWardenAI _ai;

        /// <summary>플레이어 Transform. Start 1회 캐싱 (매 피격 탐색 방지).</summary>
        private Transform _playerTransform;

        // ══════════════════════════════════════════════════════
        // 내부 상태 — 배율 플래그
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Recovery 취약 구간 활성 여부.
        /// BossWardenAI 에서 Recovery 진입/종료 시 SetRecoveryVuln() 으로 전환.
        /// </summary>
        private bool _isRecoveryVuln;

        /// <summary>
        /// Slam/Sweep 패턴 공략 타임 취약 배율 활성 여부.
        /// 패턴에서 SetSlamVuln() 으로 전환.
        /// </summary>
        private bool _isSlamVuln;

        /// <summary>
        /// 패턴 공략 타임 취약 배율.
        /// SetSlamVuln(true, multiplier) 로 설정.
        /// </summary>
        private float _slamVulnMultiplier = 1f;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary>봉인 완료 여부. SealableComponent.IsSealed 위임.</summary>
        public bool IsSealed => _sealable != null && _sealable.IsSealed;

        /// <summary>팔 타입 식별자.</summary>
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
                Debug.LogError($"[BossWardenArmPart] {gameObject.name} — SealableComponent 미부착.");
            if (_ownCollider == null)
                Debug.LogWarning($"[BossWardenArmPart] {gameObject.name} — _ownCollider 미연결.");
        }

        private void Start()
        {
            // PlayerAttackHitboxManager 구독
            var managers = FindObjectsByType<PlayerAttackHitboxManager>(FindObjectsSortMode.None);
            if (managers.Length > 0)
            {
                _hitboxManager = managers[0];
                _hitboxManager.OnHit += HandlePlayerHit;
                Debug.Log($"[BossWardenArmPart] {_partType} — OnHit 구독 완료");
            }
            else
                Debug.LogWarning($"[BossWardenArmPart] {_partType} — PlayerAttackHitboxManager 없음.");

            // 플레이어 Transform 1회 캐싱
            var player = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (player.Length > 0)
                _playerTransform = player[0].transform;
        }

        private void OnDestroy()
        {
            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandlePlayerHit;
        }

        // ══════════════════════════════════════════════════════
        // 초기화 (BossWardenCore 에서 호출)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenDataSO 주입 + SealableComponent 초기화.
        /// BossWardenCore.Start() → InjectData() 에서 호출.
        ///
        /// [BossDataSO 업캐스팅]
        ///   BossWardenDataSO 는 BossDataSO 상속.
        ///   SealableComponent 는 BossDataSO 만 참조 (범용 원칙).
        ///   명시적 업캐스팅 후 전달.
        /// </summary>
        /// <param name="data">BossWardenDataSO.</param>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;

            // BossDataSO 로 업캐스팅 후 SealableComponent 에 주입
            // SealableComponent 는 범용이므로 BossDataSO 만 받음
            _sealable?.Initialize((BossDataSO)data);

            Debug.Log($"[BossWardenArmPart] {_partType} 초기화 완료");
        }

        // ══════════════════════════════════════════════════════
        // 피격 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신.
        /// hitCol 이 _ownCollider 와 일치할 때만 봉인도 누적.
        ///
        /// [배율 적용 우선순위]
        ///   1. GuardBreak 가드 중 정면 → 봉인도 누적 완전 차단
        ///   2. RecoveryVuln : Recovery 구간 배율 (recoveryVulnMultiplier)
        ///   3. SlamVuln     : 패턴 공략 타임 배율 (패턴별 자유 설정)
        ///   2 + 3 은 독립 적용 (중복 시 모두 곱함)
        ///
        /// [피격 점멸 — v3.0 변경]
        ///   기존 직접 DOTween 처리 제거
        ///   → _sealable.PlayHitFlash() 위임
        ///   SealableComponent 가 colorHitFlash 로 점멸 + 봉인도 색상 자동 복귀
        /// </summary>
        private void HandlePlayerHit(Collider2D hitCol, float sealAmount)
        {
            if (hitCol != _ownCollider) return;
            if (IsSealed) return;

            // GuardBreak 정면 봉인도 무효 처리 (RightArm 전용)
            if (_guardBreakPattern != null && _guardBreakPattern.IsGuarding)
            {
                if (IsPlayerFacingFront())
                {
                    Debug.Log($"[BossWardenArmPart] {_partType} — 가드 중 정면 공격 봉인도 무효");
                    // 피격 점멸은 표시 (막혔다는 시각 피드백)
                    _sealable?.PlayHitFlash();
                    return;
                }
            }

            float rawAmount = sealAmount;

            // Recovery 취약 구간 배율
            if (_isRecoveryVuln && _data != null)
                rawAmount *= _data.recoveryVulnMultiplier;

            // 패턴 공략 타임 배율 (Slam/Sweep 팔 분리 구간)
            if (_isSlamVuln)
                rawAmount *= _slamVulnMultiplier;

            // 봉인도 누적 (SealableComponent 위임)
            _sealable?.AddGauge(rawAmount);

            // 피격 점멸 (SealableComponent 위임 — v3.0)
            _sealable?.PlayHitFlash();
        }

        /// <summary>
        /// 플레이어가 보스 정면 60도 이내에 있는지 체크.
        /// GuardBreak 봉인도 무효 판단에 사용.
        ///
        /// [계산 방법]
        ///   AI.FacingDir 과 (보스→플레이어 방향) 내적
        ///   > 0.5f (cos 60°) = 정면 60도 이내 = true
        /// </summary>
        private bool IsPlayerFacingFront()
        {
            if (_ai == null || _playerTransform == null) return false;

            Vector2 toPlayer = ((Vector2)_playerTransform.position
                - (Vector2)transform.position).normalized;

            return Vector2.Dot(_ai.FacingDir, toPlayer) > 0.5f;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API — 패턴에서 호출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Recovery 취약 구간 배율 설정.
        /// BossWardenAI 에서 Recovery 상태 진입/종료 시 호출.
        /// </summary>
        public void SetRecoveryVuln(bool isVuln)
        {
            _isRecoveryVuln = isVuln;
        }

        /// <summary>
        /// 패턴 공략 타임 취약 배율 설정.
        /// Slam / Sweep 패턴에서 팔 분리 시 호출.
        ///
        /// [사용 예시]
        ///   _armPart.SetSlamVuln(true, 2.0f)   // 공략 타임 시작
        ///   _armPart.SetSlamVuln(false)          // 공략 타임 종료
        /// </summary>
        /// <param name="isVuln">활성 여부.</param>
        /// <param name="multiplier">봉인도 배율. 기본 1.0.</param>
        public void SetSlamVuln(bool isVuln, float multiplier = 1f)
        {
            _isSlamVuln = isVuln;
            _slamVulnMultiplier = multiplier;
        }
    }
}