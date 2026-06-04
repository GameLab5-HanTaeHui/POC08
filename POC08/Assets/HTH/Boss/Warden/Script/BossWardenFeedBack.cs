// ============================================================
// BossWardenFeedback.cs  v2.0
// Boss_Warden 시각 피드백 컴포넌트
//
// [v2.0 변경 — SealableComponent 이벤트 구독으로 교체]
//   제거:
//     SubscribeArmGauge() → SealGaugeComponent 이벤트 구독
//     UnsubscribeArmGauge() → SealGaugeComponent 이벤트 해제
//     armPart.SealGauge 참조 (SealGaugeComponent)
//
//   변경:
//     armPart.Sealable 참조 (SealableComponent) 로 교체
//     구독 이벤트:
//       OnGaugeChanged  → 유지 (SealableComponent 동일 이벤트)
//       OnStageChanged  → 유지 (SealableComponent 동일 이벤트)
//       OnSealCompleted → OnSealed 대체 (이름 변경)
//       OnForceReleased → OnReleased 대체 (이름 변경)
//       OnSealRequested → OnSealReady 대체 (이름 변경, 파라미터 있음)
//
// [v1.2 유지]
//   [DefaultExecutionOrder(10)] — Core(-10) → 기본(0) → Feedback(10)
//   DOTween 색상 전환 / Pulse / 처치 연출 전체 유지
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 시각 피드백 컴포넌트. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [역할]
    ///   BossWardenCore / BossWardenAI / SealableComponent 이벤트 수신
    ///   → SpriteRenderer 색상 전환 / Pulse / 처치 연출 담당
    /// ────────────────────────────────────────────────────
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class BossWardenFeedback : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenDataSO _data;
        [SerializeField] private BossWardenCore _core;
        [SerializeField] private BossWardenAI _ai;

        [Header("── 팔 부위 연결 ──────────────────────")]
        [SerializeField] private BossWardenArmPart _armLPart;
        [SerializeField] private BossWardenArmPart _armRPart;

        [Header("── SpriteRenderer ──────────────────────")]
        [SerializeField] private SpriteRenderer _bodyRenderer;
        [SerializeField] private SpriteRenderer _armLRenderer;
        [SerializeField] private SpriteRenderer _armRRenderer;
        [SerializeField] private SpriteRenderer _coreRenderer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private Tweener _bodyTween;
        private Tweener _armLTween;
        private Tweener _armRTween;
        private Tweener _coreTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Start()
        {
            if (_core == null) _core = GetComponent<BossWardenCore>();
            if (_ai == null) _ai = GetComponent<BossWardenAI>();

            if (_data != null && _bodyRenderer != null)
                _bodyRenderer.color = _data != null ? _data.colorIdle : Color.gray;

            SubscribeAll();
        }

        private void OnDestroy()
        {
            UnsubscribeAll();
            _bodyTween?.Kill();
            _armLTween?.Kill();
            _armRTween?.Kill();
            _coreTween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독 / 해제
        // ══════════════════════════════════════════════════════

        private void SubscribeAll()
        {
            if (_ai != null)
            {
                _ai.OnStateChanged -= HandleStateChanged;
                _ai.OnStateChanged += HandleStateChanged;
            }

            if (_core != null)
            {
                _core.OnGroggyEnter -= HandleGroggyEnter;
                _core.OnGroggyExit -= HandleGroggyExit;
                _core.OnDilPhaseEnter -= HandleDilPhaseEnter;
                _core.OnDilPhaseExit -= HandleDilPhaseExit;
                _core.OnPhaseChanged -= HandlePhaseChanged;
                _core.OnDead -= HandleDead;

                _core.OnGroggyEnter += HandleGroggyEnter;
                _core.OnGroggyExit += HandleGroggyExit;
                _core.OnDilPhaseEnter += HandleDilPhaseEnter;
                _core.OnDilPhaseExit += HandleDilPhaseExit;
                _core.OnPhaseChanged += HandlePhaseChanged;
                _core.OnDead += HandleDead;
            }

            // ✅ v2.0: SealableComponent 이벤트 구독
            SubscribeArmSealable(_armLPart, isLeft: true);
            SubscribeArmSealable(_armRPart, isLeft: false);
        }

        private void UnsubscribeAll()
        {
            if (_ai != null)
                _ai.OnStateChanged -= HandleStateChanged;

            if (_core != null)
            {
                _core.OnGroggyEnter -= HandleGroggyEnter;
                _core.OnGroggyExit -= HandleGroggyExit;
                _core.OnDilPhaseEnter -= HandleDilPhaseEnter;
                _core.OnDilPhaseExit -= HandleDilPhaseExit;
                _core.OnPhaseChanged -= HandlePhaseChanged;
                _core.OnDead -= HandleDead;
            }

            UnsubscribeArmSealable(_armLPart, isLeft: true);
            UnsubscribeArmSealable(_armRPart, isLeft: false);
        }

        /// <summary>
        /// 팔 SealableComponent 이벤트 구독.
        /// ✅ v2.0: SealGaugeComponent → SealableComponent 교체
        ///   OnSealReady  → OnSealRequested (파라미터: SealableComponent)
        ///   OnSealed     → OnSealCompleted
        ///   OnReleased   → OnForceReleased
        /// </summary>
        private void SubscribeArmSealable(BossWardenArmPart armPart, bool isLeft)
        {
            if (armPart == null || armPart.Sealable == null) return;
            var s = armPart.Sealable;

            if (isLeft)
            {
                s.OnGaugeChanged -= HandleArmLGaugeChanged;
                s.OnStageChanged -= HandleArmLStageChanged;
                s.OnSealRequested -= HandleArmLSealRequested;
                s.OnSealCompleted -= HandleArmLSealed;
                s.OnForceReleased -= HandleArmLReleased;

                s.OnGaugeChanged += HandleArmLGaugeChanged;
                s.OnStageChanged += HandleArmLStageChanged;
                s.OnSealRequested += HandleArmLSealRequested;
                s.OnSealCompleted += HandleArmLSealed;
                s.OnForceReleased += HandleArmLReleased;
            }
            else
            {
                s.OnGaugeChanged -= HandleArmRGaugeChanged;
                s.OnStageChanged -= HandleArmRStageChanged;
                s.OnSealRequested -= HandleArmRSealRequested;
                s.OnSealCompleted -= HandleArmRSealed;
                s.OnForceReleased -= HandleArmRReleased;

                s.OnGaugeChanged += HandleArmRGaugeChanged;
                s.OnStageChanged += HandleArmRStageChanged;
                s.OnSealRequested += HandleArmRSealRequested;
                s.OnSealCompleted += HandleArmRSealed;
                s.OnForceReleased += HandleArmRReleased;
            }
        }

        private void UnsubscribeArmSealable(BossWardenArmPart armPart, bool isLeft)
        {
            if (armPart == null || armPart.Sealable == null) return;
            var s = armPart.Sealable;

            if (isLeft)
            {
                s.OnGaugeChanged -= HandleArmLGaugeChanged;
                s.OnStageChanged -= HandleArmLStageChanged;
                s.OnSealRequested -= HandleArmLSealRequested;
                s.OnSealCompleted -= HandleArmLSealed;
                s.OnForceReleased -= HandleArmLReleased;
            }
            else
            {
                s.OnGaugeChanged -= HandleArmRGaugeChanged;
                s.OnStageChanged -= HandleArmRStageChanged;
                s.OnSealRequested -= HandleArmRSealRequested;
                s.OnSealCompleted -= HandleArmRSealed;
                s.OnForceReleased -= HandleArmRReleased;
            }
        }

        // ══════════════════════════════════════════════════════
        // AI 상태 변화 핸들러
        // ══════════════════════════════════════════════════════

        public void HandleStateChanged(string stateName)
        {
            if (_data == null || _bodyRenderer == null) return;

            _bodyTween?.Kill();
            switch (stateName)
            {
                case "Warning":
                    _bodyTween = _bodyRenderer
                        .DOColor(_data.colorWarning, _data.pulsePeriod * 0.4f)
                        .SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
                    break;
                case "Active":
                    _bodyRenderer.color = _data.colorActive;
                    break;
                default:
                    _bodyTween = _bodyRenderer
                        .DOColor(_data.colorIdle, _data.colorTransitionDuration)
                        .SetUpdate(true);
                    break;
            }
        }

        // ══════════════════════════════════════════════════════
        // Core 이벤트 핸들러 (public — BossWardenCore.SubscribeAll 에서 직접 구독)
        // ══════════════════════════════════════════════════════

        public void HandleGroggyEnter()
        {
            if (_data == null) return;
            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data.colorGroggy, _data.pulsePeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
        }

        public void HandleGroggyExit()
        {
            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data?.colorIdle ?? Color.white, _data?.colorTransitionDuration ?? 0.1f)
                .SetUpdate(true);
        }

        public void HandleDilPhaseEnter()
        {
            if (_data == null) return;
            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data.colorDilPhase, _data.pulsePeriod * 0.3f)
                .SetLoops(-1, LoopType.Yoyo).SetUpdate(true);

            _coreTween?.Kill();
            _coreTween = _coreRenderer?
                .DOColor(Color.white, _data.pulsePeriod * 0.3f)
                .SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
        }

        public void HandleDilPhaseExit()
        {
            _bodyTween?.Kill();
            _coreTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data?.colorIdle ?? Color.white, _data?.colorTransitionDuration ?? 0.1f)
                .SetUpdate(true);
        }

        public void HandlePhaseChanged(int phase)
        {
            if (_data == null) return;
            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data.colorPhase2, _data.colorTransitionDuration)
                .SetUpdate(true);
        }

        public void HandleDead()
        {
            _bodyTween?.Kill();
            _armLTween?.Kill();
            _armRTween?.Kill();
            _coreTween?.Kill();

            float dur = _data?.colorTransitionDuration ?? 0.3f;
            _bodyRenderer?.DOColor(Color.black, dur).SetUpdate(true);
            _armLRenderer?.DOColor(Color.black, dur).SetUpdate(true);
            _armRRenderer?.DOColor(Color.black, dur).SetUpdate(true);
        }

        // ══════════════════════════════════════════════════════
        // 왼팔 SealableComponent 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleArmLGaugeChanged(float percent)
        {
            // UI 게이지 업데이트 연결 예정
        }

        private void HandleArmLStageChanged(int stage)
        {
            if (_data == null || _armLRenderer == null) return;
            _armLTween?.Kill();
            Color targetColor = GetStageColor(stage);
            _armLTween = _armLRenderer
                .DOColor(targetColor, _data.colorTransitionDuration)
                .SetUpdate(true);
            _armLPart?.UpdateBaseColor(targetColor);
        }

        private void HandleArmLSealRequested(SealableComponent s)
        {
            if (_data == null || _armLRenderer == null) return;
            _armLTween?.Kill();
            _armLTween = _armLRenderer
                .DOColor(_data.colorArm100, _data.pulsePeriod * 0.3f)
                .SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
        }

        private void HandleArmLSealed()
        {
            _armLTween?.Kill();
            Color sealedColor = _data?.colorArmSealed ?? new Color(0.482f, 0.184f, 0.745f);
            _armLRenderer?.DOColor(sealedColor, _data?.colorTransitionDuration ?? 0.1f).SetUpdate(true);
            _armLPart?.UpdateBaseColor(sealedColor);
        }

        private void HandleArmLReleased()
        {
            _armLTween?.Kill();
            Color idleColor = _data?.colorArm0 ?? Color.gray;
            _armLRenderer?.DOColor(idleColor, _data?.colorTransitionDuration ?? 0.1f).SetUpdate(true);
            _armLPart?.UpdateBaseColor(idleColor);
        }

        // ══════════════════════════════════════════════════════
        // 오른팔 SealableComponent 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleArmRGaugeChanged(float percent) { }

        private void HandleArmRStageChanged(int stage)
        {
            if (_data == null || _armRRenderer == null) return;
            _armRTween?.Kill();
            Color targetColor = GetStageColor(stage);
            _armRTween = _armRRenderer
                .DOColor(targetColor, _data.colorTransitionDuration)
                .SetUpdate(true);
            _armRPart?.UpdateBaseColor(targetColor);
        }

        private void HandleArmRSealRequested(SealableComponent s)
        {
            if (_data == null || _armRRenderer == null) return;
            _armRTween?.Kill();
            _armRTween = _armRRenderer
                .DOColor(_data.colorArm100, _data.pulsePeriod * 0.3f)
                .SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
        }

        private void HandleArmRSealed()
        {
            _armRTween?.Kill();
            Color sealedColor = _data?.colorArmSealed ?? new Color(0.482f, 0.184f, 0.745f);
            _armRRenderer?.DOColor(sealedColor, _data?.colorTransitionDuration ?? 0.1f).SetUpdate(true);
            _armRPart?.UpdateBaseColor(sealedColor);
        }

        private void HandleArmRReleased()
        {
            _armRTween?.Kill();
            Color idleColor = _data?.colorArm0 ?? Color.gray;
            _armRRenderer?.DOColor(idleColor, _data?.colorTransitionDuration ?? 0.1f).SetUpdate(true);
            _armRPart?.UpdateBaseColor(idleColor);
        }

        // ══════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════

        private Color GetStageColor(int stage)
        {
            if (_data == null) return Color.gray;
            return stage switch
            {
                0 => _data.colorArm0,
                1 => _data.colorArm25,
                2 => _data.colorArm50,
                3 => _data.colorArm75,
                4 => _data.colorArm100,
                _ => _data.colorArm0
            };
        }
    }
}