// ============================================================
// BossWardenCoreSealGauge.cs  v1.0
// Boss_Warden 코어 봉인도 누적 전담 컴포넌트
//
// [역할]
//   딜 페이즈 중 플레이어가 코어를 공격하면 코어 봉인도를 누적한다.
//   부위 SealGaugeComponent 와 동일한 이벤트 구조를 가지지만
//   봉인 저항 없음 / 딜 페이즈 전용이라는 점이 다르다.
//
// [피격 감지 방식]
//   PlayerAttackHitboxManager.OnHit 이벤트 구독.
//   hitCol 이 _ownCollider 와 일치할 때만 처리.
//   (BossWardenArmPart 와 동일한 방식)
//
// [딜 페이즈 전용]
//   BossWardenCore.OnDilPhaseEnter → SetActive(true) + ActivateGauge(true)
//   BossWardenCore.OnDilPhaseExit  → ActivateGauge(false)
//   딜 페이즈 외에는 AddGauge 무시.
//
// [페이즈별 목표치]
//   1페이즈: 0 → phase1CoreSealTarget (250)
//   2페이즈: 250 → phase2CoreSealTarget (500)
//   코어 봉인도는 페이즈 전환 후에도 초기화되지 않는다.
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
    /// Boss_Warden 코어 봉인도 누적 전담 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [봉인도 흐름]
    ///   딜 페이즈 진입 → ActivateGauge(true)
    ///   플레이어 코어 공격 → OnHit → AddGauge(sealAmount)
    ///   코어 봉인도 누적
    ///   → 1페이즈 목표(250) 도달 → OnPhase1TargetReached
    ///   → 2페이즈 목표(500) 도달 → OnPhase2TargetReached
    ///   딜 페이즈 종료 → ActivateGauge(false)
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossWardenCoreSealGauge : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO ──────────────────────")]

        [Tooltip("BossWardenDataSO. BossWardenCore.Initialize() 에서 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 코어 SpriteRenderer ──────────────────────")]

        /// <summary>
        /// 코어 SpriteRenderer.
        /// 피격 점멸 연출에 사용.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("코어 SpriteRenderer. 미연결 시 자동 탐색.")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════

        private Collider2D _ownCollider;
        private PlayerAttackHitboxManager _hitboxManager;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 현재 코어 봉인도 (내부 수치, 0 ~ coreSealGaugeMax). </summary>
        private float _currentGauge;

        /// <summary>
        /// 딜 페이즈 활성 여부.
        /// false 시 AddGauge 완전 무시.
        /// </summary>
        private bool _isActive;

        /// <summary> 1페이즈 목표 도달 이벤트 발행 완료 여부. </summary>
        private bool _phase1Reached;

        /// <summary> 2페이즈 목표 도달 이벤트 발행 완료 여부. </summary>
        private bool _phase2Reached;

        /// <summary> 현재 피격 점멸 Tween. </summary>
        private Tweener _flashTween;

        /// <summary> 기본 색상 캐시. </summary>
        private Color _baseColor;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 코어 봉인도 변화 시 발행.
        /// 파라미터: UI 퍼센트 (0~100).
        /// BossWardenCore / UI 가 구독.
        /// </summary>
        public event Action<float> OnCoreSealGaugeChanged;

        /// <summary>
        /// 1페이즈 코어 봉인도 목표 도달 시 1회 발행.
        /// BossWardenCore 가 구독 → 딜 페이즈 종료 / 2페이즈 전환.
        /// </summary>
        public event Action OnPhase1TargetReached;

        /// <summary>
        /// 2페이즈 코어 봉인도 목표 도달 시 1회 발행.
        /// BossWardenCore 가 구독 → 최종 봉인 진입 신호.
        /// </summary>
        public event Action OnPhase2TargetReached;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════

        /// <summary> 현재 코어 봉인도 내부 수치. </summary>
        public float CurrentGauge => _currentGauge;

        /// <summary>
        /// 코어 봉인도 UI 퍼센트 (0~100).
        /// coreSealGaugeMax 기준.
        /// </summary>
        public float UIPercent => (_data != null && _data.coreSealGaugeMax > 0f)
            ? Mathf.Clamp01(_currentGauge / _data.coreSealGaugeMax) * 100f
            : 0f;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _ownCollider = GetComponent<Collider2D>();

            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();

            if (_spriteRenderer != null)
                _baseColor = _spriteRenderer.color;
        }

        private void Start()
        {
            // PlayerAttackHitboxManager 탐색 (1회)
            var managers = FindObjectsByType<PlayerAttackHitboxManager>(FindObjectsSortMode.None);
            if (managers.Length > 0)
            {
                _hitboxManager = managers[0];
                _hitboxManager.OnHit -= HandlePlayerHit;
                _hitboxManager.OnHit += HandlePlayerHit;
            }
            else
            {
                Debug.LogWarning("[BossWardenCoreSealGauge] PlayerAttackHitboxManager 를 찾을 수 없습니다.");
            }
        }

        private void OnDestroy()
        {
            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandlePlayerHit;

            _flashTween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenCore.Start() 에서 DataSO 주입 후 초기화.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;

            if (_spriteRenderer != null)
                _baseColor = _spriteRenderer.color;

            Debug.Log("[BossWardenCoreSealGauge] 초기화 완료");
        }

        // ══════════════════════════════════════════════════════
        // 딜 페이즈 활성 제어
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 딜 페이즈 활성/비활성 전환.
        /// BossWardenCore.OnDilPhaseEnter / OnDilPhaseExit 에서 호출.
        /// </summary>
        public void ActivateGauge(bool isActive)
        {
            _isActive = isActive;
            Debug.Log($"[BossWardenCoreSealGauge] 게이지 활성 = {isActive}");
        }

        // ══════════════════════════════════════════════════════
        // 피격 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신.
        /// hitCol 이 _ownCollider 와 일치할 때만 처리.
        ///
        /// [누적량 결정]
        ///   PlayerAttackHitboxManager.CurrentSealAmount 를 코어 봉인도 누적량으로 변환.
        ///   기본 공격 봉인도 = coreBasicAttackGain / 강공격 = coreChargeAttackGain.
        ///   현재는 sealAmount 크기 기준으로 강/약 구분 (추후 PlayerAttackController 연동).
        /// </summary>
        private void HandlePlayerHit(Collider2D hitCol, float sealAmount)
        {
            if (hitCol != _ownCollider) return;
            if (!_isActive) return;
            if (_data == null) return;

            // 강공격 여부 판단 (기준값 이상이면 강공격으로 간주)
            float coreGain = sealAmount >= 25f
                ? _data.coreChargeAttackGain
                : _data.coreBasicAttackGain;

            AddGauge(coreGain);
            PlayHitFlash();
        }

        // ══════════════════════════════════════════════════════
        // 봉인도 누적
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 코어 봉인도를 누적한다.
        ///
        /// [페이즈별 목표 체크]
        ///   _currentGauge >= phase1CoreSealTarget → OnPhase1TargetReached (1회)
        ///   _currentGauge >= phase2CoreSealTarget → OnPhase2TargetReached (1회)
        /// </summary>
        private void AddGauge(float amount)
        {
            if (!_isActive || amount <= 0f) return;
            if (_data == null) return;

            _currentGauge = Mathf.Min(_currentGauge + amount, _data.coreSealGaugeMax);

            // UI 이벤트 발행
            OnCoreSealGaugeChanged?.Invoke(UIPercent);

            // 1페이즈 목표 도달
            if (!_phase1Reached && _currentGauge >= _data.phase1CoreSealTarget)
            {
                _phase1Reached = true;
                OnPhase1TargetReached?.Invoke();
                Debug.Log("[BossWardenCoreSealGauge] 1페이즈 코어 봉인도 목표 도달!");
            }

            // 2페이즈 목표 도달
            if (!_phase2Reached && _currentGauge >= _data.phase2CoreSealTarget)
            {
                _phase2Reached = true;
                OnPhase2TargetReached?.Invoke();
                Debug.Log("[BossWardenCoreSealGauge] 2페이즈 코어 봉인도 목표 도달 → 최종 봉인!");
            }
        }

        // ══════════════════════════════════════════════════════
        // 히트 점멸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 코어 피격 시 흰색 점멸 연출.
        /// SetUpdate(true) — TimeScale 슬로우 중에도 정상 복귀.
        /// </summary>
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

        /// <summary>
        /// 기본 색상 업데이트.
        /// BossWardenFeedback 이 딜 페이즈 색상 변경 시 호출.
        /// </summary>
        public void UpdateBaseColor(Color newColor)
        {
            _baseColor = newColor;
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = _isActive
                ? new Color(1f, 1f, 0f, 0.5f)
                : new Color(0.4f, 0.4f, 0.4f, 0.2f);

            Gizmos.DrawWireSphere(transform.position, 0.35f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.5f,
                $"Core: {UIPercent:F0}% [{(_isActive ? "Active" : "Inactive")}]");
#endif
        }
    }
}