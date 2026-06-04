// ============================================================
// BossWardenCoreSealGauge.cs  v1.2
// Boss_Warden 코어 봉인도 누적 전담 컴포넌트
//
// [v1.2 변경 — SetActive=false 구독 누락 버그 수정 + 코어 색상 보간 추가]
//
//   🔴 버그 수정: Start() 에서 OnHit 구독 시 SetActive=false 상태면 실행 안 됨
//       기존: Start() 에서 FindObjectsByType → OnHit 구독
//             → 코어는 기본 SetActive=false → Start() 자체가 실행 안 됨
//             → 딜 페이즈에서 코어를 공격해도 봉인도 누적 불가
//       수정: BossWardenCore.EnterDilPhase() 에서 코어 SetActive(true) 후
//             SubscribeHitboxManager() 를 직접 호출하는 방식으로 변경.
//             OnEnable() 에서도 구독 시도 (SetActive=true 시 자동 구독).
//
//   🎨 코어 색상 보간 추가:
//       딜 페이즈 중 봉인도 0% → colorCoreDilPhase(주황)
//       봉인도 100% → colorCoreFinalSeal(빨강)
//       AddGauge 시 UIPercent 기준으로 Lerp → 색상 자동 보간
//       BossWardenFeedback 의 코어 색상 변경과 충돌 방지:
//         UpdateBaseColor() 로 점멸 복귀 색상만 갱신
//         실시간 Lerp 는 이 컴포넌트 내부에서만 처리
//
// [v1.1 수정]
//   _core 필드 미사용 확인 → 제거
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
    /// Boss_Warden 코어 봉인도 누적 전담 컴포넌트. (v1.2)
    /// </summary>
    public class BossWardenCoreSealGauge : MonoBehaviour
    {
        [Header("── DataSO ──────────────────────")]
        [Tooltip("BossWardenDataSO. BossWardenCore.Initialize() 에서 주입.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 코어 SpriteRenderer ──────────────────────")]
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
        private float _currentGauge;
        private bool _isActive;
        private bool _phase1Reached;
        private bool _phase2Reached;
        private Tweener _flashTween;
        private Color _baseColor;

        // ══════════════════════════════════════════════════════
        // 이벤트
        // ══════════════════════════════════════════════════════

        /// <summary>코어 봉인도 변화 시 발행. 파라미터: UI 퍼센트 (0~100).</summary>
        public event Action<float> OnCoreSealGaugeChanged;

        /// <summary>1페이즈 코어 봉인도 목표 도달 시 1회 발행.</summary>
        public event Action OnPhase1TargetReached;

        /// <summary>2페이즈 코어 봉인도 목표 도달 시 1회 발행.</summary>
        public event Action OnPhase2TargetReached;

        // ══════════════════════════════════════════════════════
        // 프로퍼티
        // ══════════════════════════════════════════════════════
        public float CurrentGauge => _currentGauge;

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

        /// <summary>
        /// SetActive(true) 시 자동 구독.
        /// v1.2: BossWardenCore.ActivateCore() → SetActive(true) → OnEnable() → 구독.
        /// </summary>
        private void OnEnable()
        {
            SubscribeHitboxManager();
        }

        private void OnDisable()
        {
            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandlePlayerHit;
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
        /// BossWardenCore 에서 DataSO 주입 후 초기화.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;

            if (_spriteRenderer != null)
                _baseColor = _spriteRenderer.color;

            Debug.Log("[BossWardenCoreSealGauge] 초기화 완료");
        }

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 구독.
        ///
        /// [v1.2 추가]
        ///   OnEnable() 에서 호출 — SetActive(true) 시 자동 구독.
        ///   BossWardenCore.EnterDilPhase() 에서 직접 호출도 가능.
        ///   중복 구독 방지: -= 먼저 후 +=.
        /// </summary>
        public void SubscribeHitboxManager()
        {
            if (_hitboxManager == null)
            {
                var managers = FindObjectsByType<PlayerAttackHitboxManager>(FindObjectsSortMode.None);
                if (managers.Length > 0)
                    _hitboxManager = managers[0];
                else
                {
                    Debug.LogWarning("[BossWardenCoreSealGauge] PlayerAttackHitboxManager 를 찾을 수 없습니다.");
                    return;
                }
            }

            _hitboxManager.OnHit -= HandlePlayerHit;
            _hitboxManager.OnHit += HandlePlayerHit;
            Debug.Log("[BossWardenCoreSealGauge] PlayerAttackHitboxManager.OnHit 구독 완료");
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

            // 딜 페이즈 진입 시 색상 초기화 (주황 → 빨강 보간 시작점)
            if (isActive && _data != null && _spriteRenderer != null)
            {
                _baseColor = _data.colorCoreDilPhase;
                _spriteRenderer.color = _baseColor;
            }

            Debug.Log($"[BossWardenCoreSealGauge] 게이지 활성 = {isActive}");
        }

        // ══════════════════════════════════════════════════════
        // 피격 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// PlayerAttackHitboxManager.OnHit 수신.
        /// hitCol 이 _ownCollider 와 일치할 때만 처리.
        /// </summary>
        private void HandlePlayerHit(Collider2D hitCol, float sealAmount)
        {
            if (hitCol != _ownCollider) return;
            if (!_isActive) return;
            if (_data == null) return;

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
        /// 코어 봉인도 누적 + 색상 보간 + 목표 도달 체크.
        ///
        /// [v1.2 추가 — 색상 보간]
        ///   UIPercent 기준으로 colorCoreDilPhase(주황) → colorCoreFinalSeal(빨강) 보간.
        ///   AddGauge 호출마다 SpriteRenderer 색상을 즉시 갱신.
        ///   점멸 중이면 _baseColor 만 갱신 (점멸 완료 후 반영).
        /// </summary>
        private void AddGauge(float amount)
        {
            if (!_isActive || amount <= 0f) return;
            if (_data == null) return;

            _currentGauge = Mathf.Min(_currentGauge + amount, _data.coreSealGaugeMax);

            // UI 이벤트
            OnCoreSealGaugeChanged?.Invoke(UIPercent);

            // [v1.2] 봉인도 기준 색상 보간 (주황 → 빨강)
            UpdateCoreColor();

            // 1페이즈 목표
            if (!_phase1Reached && _currentGauge >= _data.phase1CoreSealTarget)
            {
                _phase1Reached = true;
                OnPhase1TargetReached?.Invoke();
                Debug.Log("[BossWardenCoreSealGauge] 1페이즈 코어 봉인도 목표 도달!");
            }

            // 2페이즈 목표
            if (!_phase2Reached && _currentGauge >= _data.phase2CoreSealTarget)
            {
                _phase2Reached = true;
                OnPhase2TargetReached?.Invoke();
                Debug.Log("[BossWardenCoreSealGauge] 2페이즈 코어 봉인도 목표 도달 → 최종 봉인!");
            }
        }

        /// <summary>
        /// 봉인도 진행도에 따라 코어 색상 보간.
        ///
        /// [색상 기획]
        ///   0%   → colorCoreDilPhase  (#FF8C00 주황)
        ///   100% → colorCoreFinalSeal (#FF0000 빨강)
        ///   중간 → Lerp 보간
        ///
        /// [2페이즈 보간 기준]
        ///   1페이즈: 0 ~ phase1CoreSealTarget 기준
        ///   2페이즈: phase1CoreSealTarget ~ phase2CoreSealTarget 기준
        ///   각 페이즈 내에서 독립적으로 0~1 보간
        /// </summary>
        private void UpdateCoreColor()
        {
            if (_data == null || _spriteRenderer == null) return;

            float t;
            if (!_phase1Reached)
            {
                // 1페이즈: 0 ~ phase1CoreSealTarget
                t = Mathf.Clamp01(_currentGauge / _data.phase1CoreSealTarget);
            }
            else
            {
                // 2페이즈: phase1CoreSealTarget ~ phase2CoreSealTarget
                float range = _data.phase2CoreSealTarget - _data.phase1CoreSealTarget;
                float progress = _currentGauge - _data.phase1CoreSealTarget;
                t = (range > 0f) ? Mathf.Clamp01(progress / range) : 1f;
            }

            Color lerpColor = Color.Lerp(_data.colorCoreDilPhase, _data.colorCoreFinalSeal, t);

            // 점멸 중이면 _baseColor 만 갱신 (점멸 완료 후 복귀 색상에 반영)
            _baseColor = lerpColor;
            if (_flashTween == null || !_flashTween.IsActive() || !_flashTween.IsPlaying())
                _spriteRenderer.color = lerpColor;
        }

        // ══════════════════════════════════════════════════════
        // 히트 점멸
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

        /// <summary>기본 색상 업데이트. BossWardenFeedback 에서 호출.</summary>
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
                ? new Color(1f, 0.5f, 0f, 0.5f)
                : new Color(0.3f, 0.3f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }
}