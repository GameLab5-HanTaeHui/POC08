// ============================================================
// BossWardenArmPart.cs  v2.0
// Boss_Warden 팔 부위 피격 + 봉인도 누적 컴포넌트
//
// [v2.0 변경 — SealableComponent 통합]
//   기존:
//     SealGaugeComponent _sealGauge  → 봉인도 관리
//     OnPartSealed 이벤트            → BossWardenCore 구독
//     OnPartReleased 이벤트          → BossWardenCore 구독
//     HandleSealed() / HandleReleased() 내부 핸들러
//
//   변경:
//     SealableComponent _sealable    → 봉인도 관리 (SealGaugeComponent 대체)
//     OnPartSealed / OnPartReleased  → 제거 (SealableComponent 이벤트로 통합)
//     BossWardenCore 가 SealableComponent.OnSealCompleted 직접 구독
//     HandlePlayerHit → _sealable.AddGauge() 호출
//
//   유지:
//     PlayerAttackHitboxManager.OnHit 구독 방식
//     _isRecoveryVuln / _isSlamVuln 배율 처리
//     SetSlamVuln() / SetRecoveryVuln() API
//     DOTween 피격 점멸
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 팔 부위 피격 + 봉인도 누적 컴포넌트. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [역할]
    ///   PlayerAttackHitboxManager.OnHit 구독
    ///   → _ownCollider 일치 시 배율 적용 후 SealableComponent.AddGauge() 호출
    ///   봉인도 관리는 SealableComponent 에 위임
    ///   피격 점멸 연출 담당
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
        /// BossWardenCore 에서 봉인 완료 부위 식별에 사용.
        /// </summary>
        [Tooltip("팔 타입. LeftArm 또는 RightArm.")]
        [SerializeField] private WardenPartType _partType;

        [Header("── 컴포넌트 연결 ──────────────────────")]

        /// <summary>
        /// 이 팔의 HurtBox Collider2D.
        /// PlayerAttackHitboxManager.OnHit 의 hitCol 과 비교.
        /// Inspector 에서 LeftHurtBox / RightHurtBox 의 Collider2D 연결.
        /// </summary>
        [Tooltip("팔 HurtBox Collider2D. LeftHurtBox/RightHurtBox 연결.")]
        [SerializeField] private Collider2D _ownCollider;

        [Tooltip("팔 SpriteRenderer. 피격 점멸 연출용.")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Tooltip("BossWardenDataSO. BossWardenCore.Initialize() 에서 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        // ══════════════════════════════════════════════════════
        // 내부 컴포넌트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 관리 컴포넌트.
        /// SealGaugeComponent 를 대체. Awake 에서 GetComponent 탐색.
        /// </summary>
        private SealableComponent _sealable;

        private PlayerAttackHitboxManager _hitboxManager;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Recovery 취약 구간 활성 여부.
        /// true → AddGauge 시 recoveryVulnMultiplier 배율 적용.
        /// BossWardenAI 가 Recovery 상태 진입/종료 시 SetRecoveryVuln() 호출.
        /// </summary>
        private bool _isRecoveryVuln;

        /// <summary>
        /// 패턴 공략 타임 (팔 분리 구간) 활성 여부.
        /// BossPattern_Slam / Sweep 에서 SetSlamVuln(true, mult) 호출 시 활성.
        /// </summary>
        private bool _isSlamVuln;

        /// <summary>
        /// 공략 타임 봉인도 배율.
        /// Slam 공략 타임 = 2.0 / Sweep 날리기 타임 = 1.5 / 기본 = 1.0.
        /// </summary>
        private float _slamVulnMultiplier = 1.0f;

        /// <summary> 기본 색상 (피격 점멸 복귀 기준). </summary>
        private Color _baseColor;

        /// <summary> 피격 점멸 Tween 핸들. </summary>
        private Tweener _flashTween;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        public WardenPartType PartType => _partType;

        /// <summary>
        /// 봉인 완료 여부.
        /// SealableComponent 위임.
        /// </summary>
        public bool IsSealed => _sealable != null && _sealable.IsSealed;

        /// <summary>
        /// 봉인 가능 상태 여부.
        /// SealableComponent 위임.
        /// </summary>
        public bool IsSealReady => _sealable != null && _sealable.IsSealReady;

        /// <summary>
        /// SealableComponent 참조.
        /// BossWardenCore / SealExecutor 에서 직접 접근 시 사용.
        /// </summary>
        public SealableComponent Sealable => _sealable;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // ✅ v2.0: SealGaugeComponent → SealableComponent 교체
            _sealable = GetComponent<SealableComponent>();

            if (_ownCollider == null)
                Debug.LogWarning($"[BossWardenArmPart] {gameObject.name} — _ownCollider 미연결.");

            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();

            if (_spriteRenderer != null)
                _baseColor = _spriteRenderer.color;
        }

        private void Start()
        {
            var managers = FindObjectsByType<PlayerAttackHitboxManager>(FindObjectsSortMode.None);
            if (managers.Length > 0)
            {
                _hitboxManager = managers[0];
                _hitboxManager.OnHit += HandlePlayerHit;
                Debug.Log($"[BossWardenArmPart] {_partType} — PlayerAttackHitboxManager.OnHit 구독 완료");
            }
            else
                Debug.LogWarning($"[BossWardenArmPart] {_partType} — PlayerAttackHitboxManager 없음.");
        }

        private void OnDestroy()
        {
            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandlePlayerHit;

            _flashTween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 초기화 (BossWardenCore 에서 호출)
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// DataSO 주입 + SealableComponent 초기화.
        /// BossWardenCore.Awake() 에서 호출.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;

            // ✅ v2.0: SealableComponent.Initialize() 호출
            _sealable?.Initialize(data);

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
        ///   1. RecoveryVuln: Recovery 구간 배율 (DataSO.recoveryVulnMultiplier)
        ///   2. SlamVuln: 패턴 공략 타임 배율 (패턴별 자유 설정)
        ///   두 배율은 독립 적용 (중복 시 모두 곱함).
        /// </summary>
        private void HandlePlayerHit(Collider2D hitCol, float sealAmount)
        {
            if (hitCol != _ownCollider) return;
            if (IsSealed) return;

            float rawAmount = sealAmount;

            // Recovery 취약 구간 배율
            if (_isRecoveryVuln && _data != null)
                rawAmount *= _data.recoveryVulnMultiplier;

            // 패턴 공략 타임 배율 (Slam/Sweep 팔 분리 구간)
            if (_isSlamVuln)
                rawAmount *= _slamVulnMultiplier;

            // ✅ v2.0: _sealGauge.AddGauge → _sealable.AddGauge
            _sealable?.AddGauge(rawAmount);

            PlayHitFlash();
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>Recovery 취약 구간 활성/비활성.</summary>
        public void SetRecoveryVuln(bool isVuln)
        {
            _isRecoveryVuln = isVuln;
        }

        /// <summary>
        /// 패턴 공략 타임 전용 봉인도 배율 설정.
        /// BossPattern_Slam / Sweep 에서 팔 분리 구간 동안 호출.
        /// </summary>
        public void SetSlamVuln(bool isActive, float multiplier)
        {
            _isSlamVuln = isActive;
            _slamVulnMultiplier = multiplier;
        }

        /// <summary>봉인 강제 해제. BossWardenCore 에서 딜 페이즈 종료 시 호출.</summary>
        public void ForceRelease(bool resetSealCount = false)
        {
            _sealable?.ForceRelease(resetSealCount);
        }

        /// <summary>기본 색상 업데이트. BossWardenFeedback 에서 색상 전환 시 호출.</summary>
        public void UpdateBaseColor(Color newColor)
        {
            _baseColor = newColor;
            _sealable?.UpdateBaseColor(newColor);
        }

        // ══════════════════════════════════════════════════════
        // 피격 점멸
        // ══════════════════════════════════════════════════════

        private void PlayHitFlash()
        {
            if (_spriteRenderer == null || _data == null) return;

            _flashTween?.Kill();
            _spriteRenderer.color = Color.white;
            _flashTween = _spriteRenderer
                .DOColor(_baseColor, _data.hitFlashDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            if (IsSealed)
                Gizmos.color = new Color(0.482f, 0.184f, 0.745f, 0.5f);
            else if (IsSealReady)
                Gizmos.color = new Color(0.780f, 0.490f, 1.000f, 0.5f);
            else
                Gizmos.color = new Color(0.420f, 0.208f, 0.722f, 0.3f);

            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }

    /// <summary>Warden 부위 타입.</summary>
    public enum WardenPartType { LeftArm, RightArm, Core }
}