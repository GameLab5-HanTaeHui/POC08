// ============================================================
// BossWardenFeedback.cs  v1.2
// Boss_Warden 상태별 DOTween 색상 연출 전담 컴포넌트
//
// [v1.2 변경 — 부위 봉인도 색상 실시간 보간 (DOColor 보정)]
//   기존: OnStageChanged(int) → 4단계(0/25/50/75/100%) 점프 방식
//         → 플레이어가 때릴 때마다 색상 변화 없고 단계 돌파 시에만 변경
//
//   변경: OnGaugeChanged(float UIPercent) → 매 AddGauge 호출마다 수신
//         → UIPercent 기준으로 colorArm0 ~ colorArm100 사이 Lerp 목표색 계산
//         → 현재 색상에서 목표색으로 DOColor(colorTransitionDuration) 부드럽게 보정
//         → 연속 공격 시 이전 DOColor Kill() 후 새 목표색으로 즉시 재시작
//         → 색상이 자연스럽게 누적되며 밝아지는 보정 효과
//
//   [OnSealReady 추가 구독]
//     봉인도 100% 도달 시 Pulse 시작 (colorArm100 빠른 Yoyo)
//     기존 OnStageChanged stage==4 에서 처리하던 것을 OnSealReady 로 이전
//
//   [_armStageColors 배열 제거]
//     단계별 색상 배열 불필요 — Lerp 로 연속 계산
//
// [v1.1 변경 — DefaultExecutionOrder(10) 추가]
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 상태별 DOTween 색상 연출 전담 컴포넌트. (v1.2)
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class BossWardenFeedback : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO (필수) ──────────────────────")]
        [Tooltip("BossWardenDataSO. 필수 연결.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 본체 렌더러 ──────────────────────")]
        [Tooltip("보스 본체 SpriteRenderer. 미연결 시 자동 탐색.")]
        [SerializeField] private SpriteRenderer _bodyRenderer;

        [Header("── 부위 렌더러 ──────────────────────")]
        [Tooltip("왼팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armLRenderer;

        [Tooltip("오른팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armRRenderer;

        [Tooltip("코어 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _coreRenderer;

        [Header("── 부위 컴포넌트 참조 ──────────────────────")]
        [Tooltip("왼팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armLPart;

        [Tooltip("오른팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armRPart;

        // ══════════════════════════════════════════════════════
        // 컴포넌트 참조
        // ══════════════════════════════════════════════════════
        private BossWardenAI _ai;
        private BossWardenCore _core;

        // ══════════════════════════════════════════════════════
        // DOTween 핸들
        // ══════════════════════════════════════════════════════
        private Tween _bodyTween;
        private Tween _armLTween;
        private Tween _armRTween;
        private Tween _coreTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_bodyRenderer == null)
                _bodyRenderer = GetComponent<SpriteRenderer>();

            _ai = GetComponent<BossWardenAI>();
            _core = GetComponent<BossWardenCore>();
        }

        private void Start()
        {
            SubscribeAll();
            SetBodyColorImmediate(_data != null ? _data.colorIdle : Color.gray);
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

            SubscribeArmGauge(_armLPart, isLeft: true);
            SubscribeArmGauge(_armRPart, isLeft: false);
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

            UnsubscribeArmGauge(_armLPart, isLeft: true);
            UnsubscribeArmGauge(_armRPart, isLeft: false);
        }

        /// <summary>
        /// 팔 봉인도 이벤트 구독.
        ///
        /// [v1.2 변경]
        ///   OnStageChanged 제거 → OnGaugeChanged 추가 구독
        ///   OnSealReady 추가 구독 → 100% 도달 시 Pulse 시작
        ///   OnSealed / OnReleased 유지
        /// </summary>
        private void SubscribeArmGauge(BossWardenArmPart armPart, bool isLeft)
        {
            if (armPart == null || armPart.SealGauge == null) return;
            var gauge = armPart.SealGauge;

            if (isLeft)
            {
                gauge.OnGaugeChanged -= HandleArmLGaugeChanged;
                gauge.OnSealReady -= HandleArmLSealReady;
                gauge.OnSealed -= HandleArmLSealed;
                gauge.OnReleased -= HandleArmLReleased;

                gauge.OnGaugeChanged += HandleArmLGaugeChanged;
                gauge.OnSealReady += HandleArmLSealReady;
                gauge.OnSealed += HandleArmLSealed;
                gauge.OnReleased += HandleArmLReleased;
            }
            else
            {
                gauge.OnGaugeChanged -= HandleArmRGaugeChanged;
                gauge.OnSealReady -= HandleArmRSealReady;
                gauge.OnSealed -= HandleArmRSealed;
                gauge.OnReleased -= HandleArmRReleased;

                gauge.OnGaugeChanged += HandleArmRGaugeChanged;
                gauge.OnSealReady += HandleArmRSealReady;
                gauge.OnSealed += HandleArmRSealed;
                gauge.OnReleased += HandleArmRReleased;
            }
        }

        private void UnsubscribeArmGauge(BossWardenArmPart armPart, bool isLeft)
        {
            if (armPart == null || armPart.SealGauge == null) return;
            var gauge = armPart.SealGauge;

            if (isLeft)
            {
                gauge.OnGaugeChanged -= HandleArmLGaugeChanged;
                gauge.OnSealReady -= HandleArmLSealReady;
                gauge.OnSealed -= HandleArmLSealed;
                gauge.OnReleased -= HandleArmLReleased;
            }
            else
            {
                gauge.OnGaugeChanged -= HandleArmRGaugeChanged;
                gauge.OnSealReady -= HandleArmRSealReady;
                gauge.OnSealed -= HandleArmRSealed;
                gauge.OnReleased -= HandleArmRReleased;
            }
        }

        // ══════════════════════════════════════════════════════
        // AI 상태 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleStateChanged(BossWardenAI.WardenAIState state, BossPatternBase pattern)
        {
            if (_data == null) return;

            Color target = state switch
            {
                BossWardenAI.WardenAIState.Idle => _data.colorIdle,
                BossWardenAI.WardenAIState.Chase => _data.colorIdle,
                BossWardenAI.WardenAIState.Warning => _data.colorWarning,
                BossWardenAI.WardenAIState.Active => _data.colorActive,
                BossWardenAI.WardenAIState.Recovery => _data.colorRecovery,
                _ => _data.colorIdle,
            };

            PlayBodyColor(target, _data.colorTransitionDuration);
        }

        // ══════════════════════════════════════════════════════
        // Core 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleGroggyEnter()
        {
            if (_data == null) return;

            // 본체 노란 빠른 Pulse
            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer
                .DOColor(_data.colorIdle, _data.pulsePeriod * 0.5f)
                .From(_data.colorGroggy)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);

            // 코어 노란 Pulse
            if (_coreRenderer != null)
            {
                _coreTween?.Kill();
                _coreTween = _coreRenderer
                    .DOColor(_data.colorIdle, _data.pulsePeriod * 0.5f)
                    .From(_data.colorCoreActive)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
        }

        private void HandleGroggyExit()
        {
            if (_data == null) return;
            PlayBodyColor(_data.colorIdle, _data.colorTransitionDuration);
            _coreTween?.Kill();
        }

        private void HandleDilPhaseEnter()
        {
            if (_data == null) return;

            // 본체 밝은 주황 Pulse
            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer
                .DOColor(_data.colorIdle, _data.pulsePeriod)
                .From(_data.colorDilPhase)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);

            // 코어 주황 → 빨강 보간은 BossWardenCoreSealGauge.UpdateCoreColor() 담당
            // 여기서는 초기 색상만 설정
            if (_coreRenderer != null)
            {
                _coreTween?.Kill();
                _coreRenderer.color = _data.colorCoreDilPhase;
            }
        }

        private void HandleDilPhaseExit()
        {
            if (_data == null) return;
            PlayBodyColor(_data.colorIdle, _data.colorTransitionDuration);
            _coreTween?.Kill();
        }

        private void HandlePhaseChanged(int newPhase)
        {
            if (_data == null || newPhase != 2) return;

            _bodyTween?.Kill();
            var seq = DOTween.Sequence();
            seq.Append(_bodyRenderer.DOColor(_data.colorPhase2, 0.05f).SetUpdate(true));
            seq.AppendInterval(0.3f);
            seq.Append(_bodyRenderer.DOColor(_data.colorIdle, _data.colorTransitionDuration).SetUpdate(true));
            seq.SetUpdate(true);
            _bodyTween = seq;
        }

        private void HandleDead()
        {
            if (_data == null) return;

            _bodyTween?.Kill();
            var seq = DOTween.Sequence();
            seq.Append(_bodyRenderer.DOColor(_data.colorDead, 0.3f).SetUpdate(true));
            seq.Join(transform.DOScale(0f, 0.5f).SetEase(Ease.InBack).SetUpdate(true));
            seq.SetUpdate(true);
            _bodyTween = seq;
        }

        // ══════════════════════════════════════════════════════
        // 팔 봉인도 이벤트 핸들러 — 왼팔
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// OnGaugeChanged(float UIPercent) 수신.
        /// AddGauge 호출마다 발행 → 실시간 색상 보간.
        /// </summary>
        private void HandleArmLGaugeChanged(float uiPercent)
            => ApplyArmGaugeColor(_armLRenderer, _armLPart, uiPercent, ref _armLTween);

        /// <summary>
        /// OnSealReady 수신 → 봉인도 100% → 연보라 빠른 Pulse 시작.
        /// </summary>
        private void HandleArmLSealReady()
            => ApplyArmSealReadyPulse(_armLRenderer, _armLPart, ref _armLTween);

        private void HandleArmLSealed()
            => ApplyArmSealedColor(_armLRenderer, _armLPart, ref _armLTween);

        private void HandleArmLReleased()
            => ApplyArmGaugeColor(_armLRenderer, _armLPart, uiPercent: 0f, ref _armLTween);

        // ══════════════════════════════════════════════════════
        // 팔 봉인도 이벤트 핸들러 — 오른팔
        // ══════════════════════════════════════════════════════

        private void HandleArmRGaugeChanged(float uiPercent)
            => ApplyArmGaugeColor(_armRRenderer, _armRPart, uiPercent, ref _armRTween);

        private void HandleArmRSealReady()
            => ApplyArmSealReadyPulse(_armRRenderer, _armRPart, ref _armRTween);

        private void HandleArmRSealed()
            => ApplyArmSealedColor(_armRRenderer, _armRPart, ref _armRTween);

        private void HandleArmRReleased()
            => ApplyArmGaugeColor(_armRRenderer, _armRPart, uiPercent: 0f, ref _armRTween);

        // ══════════════════════════════════════════════════════
        // 부위 색상 적용
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 UIPercent 기준으로 팔 색상을 부드럽게 보정.
        ///
        /// [v1.2 핵심 변경]
        ///   UIPercent(0~100) → t(0~1) 로 정규화
        ///   targetColor = Color.Lerp(colorArm0, colorArm100, t)
        ///   현재 색상 → targetColor 로 DOColor(colorTransitionDuration) 보정
        ///   연속 공격 시 이전 DOColor Kill() 후 새 목표색으로 재시작
        ///   → 플레이어가 때릴 때마다 색상이 자연스럽게 밝아지는 효과
        ///
        /// [보정 동작]
        ///   한 번 때릴 때마다 약간씩 밝아지고
        ///   colorTransitionDuration 안에 목표색에 도달
        ///   다시 때리면 더 밝은 목표색으로 부드럽게 이어짐
        /// </summary>
        private void ApplyArmGaugeColor(SpriteRenderer renderer, BossWardenArmPart armPart, float uiPercent, ref Tween tween)
        {
            if (renderer == null || _data == null) return;

            float t = Mathf.Clamp01(uiPercent / 100f);
            Color targetColor = Color.Lerp(_data.colorArm0, _data.colorArm100, t);

            // 히트 점멸 복귀 색상 동기화 — 먼저 갱신
            armPart?.UpdateBaseColor(targetColor);

            // 히트 점멸(hitFlashDuration) 완료 후 색상 보정 시작
            // → PlayHitFlash의 DOColor와 충돌 방지
            tween?.Kill();
            tween = DOVirtual.DelayedCall(_data.hitFlashDuration, () =>
            {
                if (renderer == null) return;
                renderer.DOColor(targetColor, _data.colorTransitionDuration)
                        .SetUpdate(true);
            }).SetUpdate(true);
        }

        /// <summary>
        /// 봉인도 100% 도달 시 Pulse 시작.
        /// colorArm100(연보라) 빠른 Yoyo — 집행 가능 상태 강조.
        /// </summary>
        private void ApplyArmSealReadyPulse(SpriteRenderer renderer, BossWardenArmPart armPart,
                                             ref Tween tween)
        {
            if (renderer == null || _data == null) return;

            tween?.Kill();
            tween = renderer
                .DOColor(_data.colorArm0, _data.pulsePeriod * 0.4f)
                .From(_data.colorArm100)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);

            armPart?.UpdateBaseColor(_data.colorArm100);
        }

        /// <summary>
        /// 봉인 집행 완료 색상 고정 (진한 보라 단색).
        /// Pulse 종료 후 colorArmSealed 로 고정.
        /// </summary>
        private void ApplyArmSealedColor(SpriteRenderer renderer, BossWardenArmPart armPart,
                                          ref Tween tween)
        {
            if (renderer == null || _data == null) return;

            tween?.Kill();
            tween = renderer
                .DOColor(_data.colorArmSealed, _data.colorTransitionDuration)
                .SetUpdate(true);

            armPart?.UpdateBaseColor(_data.colorArmSealed);
        }

        // ══════════════════════════════════════════════════════
        // 공통 DOTween 헬퍼
        // ══════════════════════════════════════════════════════

        private void PlayBodyColor(Color target, float duration)
        {
            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer
                .DOColor(target, duration)
                .SetUpdate(true);
        }

        private void SetBodyColorImmediate(Color color)
        {
            _bodyTween?.Kill();
            if (_bodyRenderer != null)
                _bodyRenderer.color = color;
        }
    }
}