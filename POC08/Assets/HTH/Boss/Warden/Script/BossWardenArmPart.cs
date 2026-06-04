// ============================================================
// BossWardenArmPart.cs  v1.2
// Boss_Warden 팔 부위 컴포넌트
//
// [v1.2 변경 — _ownCollider Inspector 직접 연결 방식으로 변경]
//   기존: private Collider2D _ownCollider; → Awake() 에서 GetComponent<Collider2D>()
//         → LeftArm 본체 Collider2D (Layer 20) 를 가져옴
//         → PlayerAttackHitboxManager 가 감지하는 것은 LeftHurtBox (Layer 22)
//         → hitCol != _ownCollider → 항상 return → 봉인도 누적 안 됨
//
//   변경: [SerializeField] private Collider2D _ownCollider; (Inspector 직접 연결)
//         → Prefab 에서 LeftHurtBox / RightHurtBox 의 Collider2D 를 직접 연결
//         → Awake() 에서 GetComponent 제거
//
//   [Prefab 에서 설정]
//     LeftArm.BossWardenArmPart._ownCollider  → LeftHurtBox 의 Collider2D
//     RightArm.BossWardenArmPart._ownCollider → RightHurtBox 의 Collider2D
//
// [v1.1 수정]
//   ① Initialize() 이벤트 중복 구독 방지
//   ② OnTriggerEnter2D → PlayerAttackHitboxManager.OnHit 구독 방식으로 전환
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 팔 부위 컴포넌트. (v1.2)
    ///
    /// ────────────────────────────────────────────────────
    /// [봉인 흐름]
    ///   PlayerAttackHitboxManager.OnHit(col, sealAmount)
    ///     → HandlePlayerHit() : col 이 _ownCollider 와 일치하면
    ///     → SealGaugeComponent.AddGauge(sealAmount * recoveryMult)
    ///     → 히트 스탑 점멸
    ///     → SealGaugeComponent.OnSealReady → BossWardenSealExecutor 처리
    ///     → SealGaugeComponent.OnSealed → BossWardenCore.CheckGroggyCondition()
    ///
    /// [_ownCollider 연결]
    ///   Inspector 에서 직접 연결 필수.
    ///   LeftArm  → LeftHurtBox 의 Collider2D
    ///   RightArm → RightHurtBox 의 Collider2D
    /// ────────────────────────────────────────────────────
    /// </summary>
    [RequireComponent(typeof(SealGaugeComponent))]
    public class BossWardenArmPart : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 부위 식별 ──────────────────────")]

        /// <summary>
        /// 팔 타입 (LeftArm / RightArm).
        /// BossWardenCore 에서 양팔 봉인 완료 여부 체크에 사용.
        /// </summary>
        [Tooltip("팔 타입. LeftArm 또는 RightArm 설정.")]
        [SerializeField] private WardenPartType _partType = WardenPartType.LeftArm;

        [Header("── 컴포넌트 연결 ──────────────────────")]

        /// <summary>
        /// 부위 SpriteRenderer.
        /// 히트 스탑 점멸 + 초기 색상 캐싱에 사용.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("SpriteRenderer. 미연결 시 자동 탐색.")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        /// <summary>
        /// 피격 감지 대상 Collider2D.
        /// PlayerAttackHitboxManager.OnHit 수신 시 hitCol 과 대조에 사용.
        ///
        /// [v1.2 변경 — Inspector 직접 연결]
        ///   기존: Awake() 에서 GetComponent → LeftArm 본체 콜라이더 (Layer 20)
        ///         → PlayerAttackHitboxManager 가 감지하는 LeftHurtBox (Layer 22) 와 불일치
        ///         → 봉인도 누적 안 됨
        ///
        ///   변경: Inspector 에서 직접 연결
        ///         LeftArm  → LeftHurtBox 의 Collider2D (Layer 22)
        ///         RightArm → RightHurtBox 의 Collider2D (Layer 22)
        /// </summary>
        [Tooltip("피격 감지 콜라이더. LeftHurtBox / RightHurtBox 의 Collider2D 를 직접 연결.")]
        [SerializeField] private Collider2D _ownCollider;

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// BossWardenDataSO.
        /// 히트 점멸 시간 / 색상 참조.
        /// BossWardenCore.Initialize() 에서 주입.
        /// </summary>
        [Tooltip("BossWardenDataSO. BossWardenCore.Initialize 에서 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        /// <summary>봉인도 관리 컴포넌트. Awake 에서 GetComponent.</summary>
        private SealGaugeComponent _sealGauge;

        /// <summary>씬 내 PlayerAttackHitboxManager. Start() 에서 탐색.</summary>
        private PlayerAttackHitboxManager _hitboxManager;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Recovery 취약 구간 활성 여부.
        /// true → AddGauge 시 recoveryVulnMultiplier 적용.
        /// </summary>
        private bool _isRecoveryVuln;

        /// <summary>
        /// 패턴 공략 타임 (팔 분리 구간) 활성 여부.
        /// BossPattern_Slam / Sweep 에서 SetSlamVuln(true, mult) 호출 시 활성.
        /// </summary>
        private bool _isSlamVuln;

        /// <summary>공략 타임 봉인도 배율.</summary>
        private float _slamVulnMultiplier = 1.0f;

        /// <summary>기본 색상 캐시. 히트 점멸 후 복귀 색상.</summary>
        private Color _baseColor;

        /// <summary>현재 히트 점멸 Tween 핸들.</summary>
        private Tweener _flashTween;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>봉인 집행 완료 시 발행. BossWardenCore 가 구독.</summary>
        public event Action<WardenPartType> OnPartSealed;

        /// <summary>봉인 강제 해제 시 발행. BossWardenCore 가 구독.</summary>
        public event Action<WardenPartType> OnPartReleased;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        public WardenPartType PartType => _partType;
        public bool IsSealed => _sealGauge != null && _sealGauge.IsSealed;
        public bool IsSealReady => _sealGauge != null && _sealGauge.IsSealReady;
        public SealGaugeComponent SealGauge => _sealGauge;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _sealGauge = GetComponent<SealGaugeComponent>();

            // [v1.2] _ownCollider 는 Inspector 에서 직접 연결 — GetComponent 제거
            if (_ownCollider == null)
                Debug.LogWarning($"[BossWardenArmPart] {gameObject.name} — _ownCollider 미연결. " +
                                 "Inspector 에서 LeftHurtBox / RightHurtBox 의 Collider2D 를 연결하세요.");

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
            if (_sealGauge != null)
            {
                _sealGauge.OnSealed -= HandleSealed;
                _sealGauge.OnReleased -= HandleReleased;
            }
            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandlePlayerHit;

            _flashTween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// DataSO 주입 + SealGaugeComponent 초기화 + 이벤트 연결.
        /// BossWardenCore.Start() 에서 호출.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;

            if (_sealGauge != null)
                _sealGauge.Initialize(data, data.armSealGaugeMax);

            if (_sealGauge != null)
            {
                _sealGauge.OnSealed -= HandleSealed;
                _sealGauge.OnReleased -= HandleReleased;
                _sealGauge.OnSealed += HandleSealed;
                _sealGauge.OnReleased += HandleReleased;
            }

            Debug.Log($"[BossWardenArmPart] {_partType} 초기화 완료");
        }

        // ══════════════════════════════════════════════════════
        // 피격 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신.
        /// hitCol 이 _ownCollider 와 일치할 때만 봉인도 누적.
        /// </summary>
        private void HandlePlayerHit(Collider2D hitCol, float sealAmount)
        {
            if (hitCol != _ownCollider) return;
            if (IsSealed) return;

            float rawAmount = sealAmount;

            if (_isRecoveryVuln && _data != null)
                rawAmount *= _data.recoveryVulnMultiplier;

            if (_isSlamVuln)
                rawAmount *= _slamVulnMultiplier;

            if (_sealGauge != null)
                _sealGauge.AddGauge(rawAmount);

            PlayHitFlash();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleSealed()
        {
            OnPartSealed?.Invoke(_partType);
            Debug.Log($"[BossWardenArmPart] {_partType} 봉인 완료 이벤트 발행");
        }

        private void HandleReleased()
        {
            OnPartReleased?.Invoke(_partType);
            Debug.Log($"[BossWardenArmPart] {_partType} 봉인 해제 이벤트 발행");
        }

        // ══════════════════════════════════════════════════════
        // 히트 스탑 점멸
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
        // 외부 API
        // ══════════════════════════════════════════════════════

        public void SetRecoveryVuln(bool isVuln) => _isRecoveryVuln = isVuln;
        public void SetSlamVuln(bool isVuln, float multiplier = 1f)
        {
            _isSlamVuln = isVuln;
            _slamVulnMultiplier = multiplier;
        }
        public void ForceRelease(bool resetSealCount = false) => _sealGauge?.ForceRelease(resetSealCount);
        public void UpdateBaseColor(Color newColor) => _baseColor = newColor;

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            if (IsSealed)
                Gizmos.color = new Color(0.482f, 0.184f, 0.745f, 0.5f);  // 봉인 완료 — 보라
            else if (IsSealReady)
                Gizmos.color = new Color(0.780f, 0.490f, 1.000f, 0.5f);  // 집행 가능 — 연보라
            else
                Gizmos.color = new Color(0.420f, 0.208f, 0.722f, 0.3f);  // 진행 중 — 중간 보라

            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }
    // ══════════════════════════════════════════════════════
    // 팔 타입 열거형
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// Warden 부위 타입.
    /// BossWardenCore 에서 L/R 구분 + 그로기 조건 체크에 사용.
    /// </summary>
    public enum WardenPartType
    {
        /// <summary> 왼팔. Slam / Sweep 연결. </summary>
        LeftArm,

        /// <summary> 오른팔. Charge / GuardBreak 연결. </summary>
        RightArm,

        /// <summary> 코어. 양팔 봉인 후 활성. </summary>
        Core,
    }
}