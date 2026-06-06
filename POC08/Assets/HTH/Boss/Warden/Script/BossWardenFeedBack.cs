// ============================================================
// BossWardenFeedback.cs  v3.0
// Boss_Warden 시각 피드백 컴포넌트
//
// [v3.0 — BossWardenCore 직접 구독 제거 + BossDataSO 색상 참조 교체]
//
//   [변경 1] BossWardenCore 이벤트 직접 구독 제거
//     기존: Start() → _core.OnGroggyEnter += HandleGroggyEnter 등 직접 구독
//           → BossWardenFeedback 이 BossWardenCore 에 직접 의존
//
//     변경: BossWardenCore v3.0 브리지 방식
//           BossWardenCore 가 SealStateManager 이벤트 수신 후
//           _feedback.OnGroggyEnter() 등을 직접 호출
//           → Feedback 은 BossWardenCore / SealStateManager 둘 다 모름
//           → 메서드 이름 public 으로 변경 (HandleXxx → OnXxx)
//
//   [변경 2] 색상 수치 참조 교체
//     기존: _data.pulsePeriod / _data.colorTransitionDuration
//           → BossWardenDataSO 인라인 필드 (구버전)
//
//     변경: _data.ColorData.sealReadyPulseDuration / colorLerpDuration
//           → SealColorDataSO 범용 필드 (신버전)
//           BossWardenDataSO 색상 필드는 Warden 본체 AI 상태색만 사용
//           (colorIdle / colorWarning / colorActive / colorRecovery
//            colorGroggy / colorDilPhase / colorPhase2 / colorDead)
//
//   [변경 3] Initialize(BossWardenDataSO) 추가
//     기존: Inspector 에서 _data 직접 연결
//     변경: BossWardenCore.InjectData() 에서 Initialize(_data) 주입
//           Inspector 연결도 유지 (둘 다 지원)
//
//   [v2.1 유지]
//     HandleStateChanged(WardenAIState, BossPatternBase) — AI 상태 색상
//     SubscribeArmSealable() — 팔 SealableComponent 이벤트 구독
//       OnGaugeChanged → 팔 색상 보간 (SealColorDataSO.GetPartColor)
//       OnSealCompleted → 팔 봉인 완료 연출
//       OnForceReleased → 팔 색상 복귀
//     봉인 완료 파티클 연동 (PlaySealParticle / StopSealLoopParticle)
//     ProcessingOrder(10) 유지
//
//   [SealableComponent 팔 색상 처리 — 이중 처리 주의]
//     SealableComponent v2.0 은 자체적으로 DOColor 색상 보간 처리
//     BossWardenFeedback 도 OnGaugeChanged 수신 시 팔 색상 처리
//     → 중복 방지: BossWardenFeedback 은 OnStageChanged(단계별 이벤트)만 처리
//                  OnGaugeChanged 는 SealableComponent 에 위임 (구독 제거)
//
// [namespace] SEAL
// ============================================================

using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 시각 피드백 컴포넌트. (v3.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [역할]
    ///   BossWardenCore 브리지 메서드 수신
    ///   → 본체 SpriteRenderer 색상 / Pulse 연출
    ///   → 처치 연출 (검정 + 축소)
    ///   BossWardenAI.OnStateChanged 수신
    ///   → Warning / Active / Recovery / Idle 색상 전환
    ///   SealableComponent 이벤트 수신
    ///   → 팔 봉인 완료 연출 + 파티클
    ///
    /// [BossWardenCore 브리지 메서드 — public]
    ///   OnGroggyEnter()
    ///   OnGroggyExit()
    ///   OnDilPhaseEnter()
    ///   OnDilPhaseExit()
    ///   OnFinalSealReady()
    ///   OnPhaseChanged(int)
    ///   OnDead()
    ///   Initialize(BossWardenDataSO)
    /// ────────────────────────────────────────────────────
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class BossWardenFeedback : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO ──────────────────────")]

        /// <summary>
        /// BossWardenDataSO.
        /// BossWardenCore.Initialize() 에서 주입 or Inspector 직접 연결.
        /// 본체 AI 상태 색상 참조.
        /// </summary>
        [Tooltip("BossWardenDataSO. BossWardenCore 에서 주입 or Inspector 연결.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── AI 연결 ──────────────────────")]

        /// <summary>
        /// BossWardenAI. OnStateChanged 이벤트 구독용.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("BossWardenAI. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAI _ai;

        [Header("── 팔 부위 연결 ──────────────────────")]

        /// <summary>왼팔 BossWardenArmPart. SealableComponent 이벤트 구독용.</summary>
        [Tooltip("왼팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armLPart;

        /// <summary>오른팔 BossWardenArmPart.</summary>
        [Tooltip("오른팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armRPart;

        [Header("── SpriteRenderer ──────────────────────")]

        /// <summary>본체 색상 제어 대상.</summary>
        [Tooltip("본체 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _bodyRenderer;

        /// <summary>왼팔 색상 제어 대상. 봉인 완료 연출용.</summary>
        [Tooltip("왼팔 SpriteRenderer. 봉인 완료 연출용.")]
        [SerializeField] private SpriteRenderer _armLRenderer;

        /// <summary>오른팔 색상 제어 대상.</summary>
        [Tooltip("오른팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armRRenderer;

        /// <summary>코어 색상 제어 대상. 딜 페이즈 Pulse 연출용.</summary>
        [Tooltip("코어 SpriteRenderer. 딜 페이즈 Pulse 연출용.")]
        [SerializeField] private SpriteRenderer _coreRenderer;

        [Header("── 봉인 완료 파티클 ──────────────────────")]

        /// <summary>왼팔 봉인 완료 버스트 파티클 (1회 재생).</summary>
        [Tooltip("왼팔 봉인 완료 버스트 파티클.")]
        [SerializeField] private ParticleSystem _armLSealBurst;

        /// <summary>왼팔 봉인 완료 루프 파티클 (지속 재생).</summary>
        [Tooltip("왼팔 봉인 완료 루프 파티클.")]
        [SerializeField] private ParticleSystem _armLSealLoop;

        /// <summary>오른팔 봉인 완료 버스트 파티클.</summary>
        [Tooltip("오른팔 봉인 완료 버스트 파티클.")]
        [SerializeField] private ParticleSystem _armRSealBurst;

        /// <summary>오른팔 봉인 완료 루프 파티클.</summary>
        [Tooltip("오른팔 봉인 완료 루프 파티클.")]
        [SerializeField] private ParticleSystem _armRSealLoop;

        // ══════════════════════════════════════════════════════
        // 내부 Tween 참조
        // ══════════════════════════════════════════════════════

        /// <summary>본체 색상 Tween.</summary>
        private Tweener _bodyTween;

        /// <summary>왼팔 색상 Tween.</summary>
        private Tweener _armLTween;

        /// <summary>오른팔 색상 Tween.</summary>
        private Tweener _armRTween;

        /// <summary>코어 색상 Tween.</summary>
        private Tweener _coreTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_ai == null) _ai = GetComponent<BossWardenAI>();
            if (_bodyRenderer == null) _bodyRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            // AI 상태 이벤트 구독 (BossWardenAI 직접 — 변경 없음)
            if (_ai != null)
            {
                _ai.OnStateChanged -= HandleStateChanged;
                _ai.OnStateChanged += HandleStateChanged;
            }

            // 팔 SealableComponent 이벤트 구독
            SubscribeArmSealable(_armLPart, isLeft: true);
            SubscribeArmSealable(_armRPart, isLeft: false);

            // 초기 색상 설정
            if (_bodyRenderer != null && _data != null)
                _bodyRenderer.color = _data.colorIdle;
        }

        private void OnDestroy()
        {
            if (_ai != null)
                _ai.OnStateChanged -= HandleStateChanged;

            UnsubscribeArmSealable(_armLPart, isLeft: true);
            UnsubscribeArmSealable(_armRPart, isLeft: false);

            _bodyTween?.Kill();
            _armLTween?.Kill();
            _armRTween?.Kill();
            _coreTween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // 초기화 — BossWardenCore 에서 주입
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenDataSO 주입.
        /// BossWardenCore.InjectData() 에서 호출.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
        }

        // ══════════════════════════════════════════════════════
        // 팔 SealableComponent 이벤트 구독
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 팔 SealableComponent 이벤트 구독.
        ///
        /// [구독 이벤트]
        ///   OnStageChanged  → 단계별 팔 색상 반영 (SealableComponent 자체 처리와 중복 방지)
        ///   OnSealCompleted → 봉인 완료 연출 + 파티클
        ///   OnForceReleased → 봉인 해제 연출 (파티클 정지)
        ///
        /// [OnGaugeChanged 미구독 이유 — v3.0]
        ///   SealableComponent v2.0 이 AddGauge() 마다 DOColor 직접 처리
        ///   → Feedback 이 중복 처리하면 Tween 충돌 발생
        ///   → OnGaugeChanged 구독 제거, SealableComponent 에 완전 위임
        /// </summary>
        private void SubscribeArmSealable(BossWardenArmPart armPart, bool isLeft)
        {
            if (armPart == null) return;
            var sealable = armPart.Sealable;
            if (sealable == null) return;

            if (isLeft)
            {
                sealable.OnSealCompleted -= HandleArmLSealed;
                sealable.OnSealCompleted += HandleArmLSealed;
                sealable.OnForceReleased -= HandleArmLReleased;
                sealable.OnForceReleased += HandleArmLReleased;
            }
            else
            {
                sealable.OnSealCompleted -= HandleArmRSealed;
                sealable.OnSealCompleted += HandleArmRSealed;
                sealable.OnForceReleased -= HandleArmRReleased;
                sealable.OnForceReleased += HandleArmRReleased;
            }
        }

        private void UnsubscribeArmSealable(BossWardenArmPart armPart, bool isLeft)
        {
            if (armPart == null) return;
            var sealable = armPart.Sealable;
            if (sealable == null) return;

            if (isLeft)
            {
                sealable.OnSealCompleted -= HandleArmLSealed;
                sealable.OnForceReleased -= HandleArmLReleased;
            }
            else
            {
                sealable.OnSealCompleted -= HandleArmRSealed;
                sealable.OnForceReleased -= HandleArmRReleased;
            }
        }

        // ══════════════════════════════════════════════════════
        // AI 상태 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenAI.OnStateChanged 수신.
        /// AI 상태에 따라 본체 색상 전환.
        ///
        /// [색상 참조]
        ///   BossWardenDataSO Warden 전용 색상 필드 사용
        ///   (colorIdle / colorWarning / colorActive / colorRecovery)
        ///
        /// [pulsePeriod → ColorData.sealReadyPulseDuration]
        ///   Warning / Recovery 맥동 주기는 SealColorDataSO.sealReadyPulseDuration 사용
        /// </summary>
        public void HandleStateChanged(BossWardenAI.WardenAIState state, BossPatternBase pattern)
        {
            if (_data == null || _bodyRenderer == null) return;

            float pulsePeriod = _data.ColorData?.sealReadyPulseDuration ?? 0.4f;
            float lerpDuration = _data.ColorData?.colorLerpDuration ?? 0.15f;

            _bodyTween?.Kill();

            switch (state)
            {
                case BossWardenAI.WardenAIState.Warning:
                    _bodyTween = _bodyRenderer
                        .DOColor(_data.colorWarning, pulsePeriod * 0.4f)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetUpdate(true);
                    break;

                case BossWardenAI.WardenAIState.Active:
                    _bodyRenderer.color = _data.colorActive;
                    break;

                case BossWardenAI.WardenAIState.Recovery:
                    _bodyTween = _bodyRenderer
                        .DOColor(_data.colorRecovery, pulsePeriod * 0.5f)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetUpdate(true);
                    break;

                default: // Idle / Chase
                    _bodyTween = _bodyRenderer
                        .DOColor(_data.colorIdle, lerpDuration)
                        .SetUpdate(true);
                    break;
            }
        }

        // ══════════════════════════════════════════════════════
        // BossWardenCore 브리지 메서드 — public
        // BossWardenCore v3.0 이 직접 호출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Groggy 진입 연출.
        /// 본체 노란 Pulse.
        /// </summary>
        public void OnGroggyEnter()
        {
            if (_data == null) return;

            float pulsePeriod = _data.ColorData?.sealReadyPulseDuration ?? 0.4f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data.colorGroggy, pulsePeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>
        /// Groggy 실패 종료 연출.
        /// 본체 Idle 색상 복귀.
        /// </summary>
        public void OnGroggyExit()
        {
            float lerpDuration = _data?.ColorData?.colorLerpDuration ?? 0.15f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data?.colorIdle ?? Color.gray, lerpDuration)
                .SetUpdate(true);
        }

        /// <summary>
        /// DilPhase 진입 연출.
        /// 본체 밝은 주황 Pulse + 코어 흰색 Pulse.
        /// </summary>
        public void OnDilPhaseEnter()
        {
            if (_data == null) return;

            float pulsePeriod = _data.ColorData?.sealReadyPulseDuration ?? 0.4f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data.colorDilPhase, pulsePeriod * 0.3f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);

            // 코어 흰색 빠른 Pulse
            _coreTween?.Kill();
            _coreTween = _coreRenderer?
                .DOColor(Color.white, pulsePeriod * 0.3f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>
        /// DilPhase 종료 연출.
        /// 본체 + 코어 Tween 종료 → Idle 복귀.
        /// </summary>
        public void OnDilPhaseExit()
        {
            float lerpDuration = _data?.ColorData?.colorLerpDuration ?? 0.15f;

            _bodyTween?.Kill();
            _coreTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data?.colorIdle ?? Color.gray, lerpDuration)
                .SetUpdate(true);
        }

        /// <summary>
        /// FinalSeal 준비 연출.
        /// 코어 최종봉인 색상 강한 Pulse.
        /// </summary>
        public void OnFinalSealReady()
        {
            if (_data?.ColorData == null) return;

            float pulsePeriod = _data.ColorData.sealReadyPulseDuration * 0.5f;
            Color finalColor = _data.ColorData.colorCoreFinalSeal;

            _coreTween?.Kill();
            _coreTween = _coreRenderer?
                .DOColor(finalColor, pulsePeriod)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>
        /// 페이즈 전환 연출.
        /// 2페이즈 → 본체 진한 붉은색 전환.
        /// </summary>
        public void OnPhaseChanged(int newPhase)
        {
            if (_data == null || _bodyRenderer == null) return;

            float lerpDuration = _data.ColorData?.sealTransitionDuration ?? 0.3f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer
                .DOColor(_data.colorPhase2, lerpDuration)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    // 전환 후 Idle 색상으로 서서히 복귀
                    _bodyTween = _bodyRenderer
                        .DOColor(_data.colorIdle, lerpDuration * 2f)
                        .SetDelay(0.3f)
                        .SetUpdate(true);
                });

            Debug.Log($"[BossWardenFeedback] 페이즈 {newPhase} 전환 연출");
        }

        /// <summary>
        /// Dead 연출.
        /// 본체 검정 DOColor + DOScale 0 소멸.
        /// </summary>
        public void OnDead()
        {
            float lerpDuration = _data?.ColorData?.sealTransitionDuration ?? 0.3f;

            _bodyTween?.Kill();
            _armLTween?.Kill();
            _armRTween?.Kill();
            _coreTween?.Kill();

            // 본체 검정 전환
            _bodyRenderer?
                .DOColor(_data?.colorDead ?? Color.black, lerpDuration)
                .SetUpdate(true);

            // 본체 축소 소멸
            transform
                .DOScale(Vector3.zero, lerpDuration * 2f)
                .SetEase(Ease.InBack)
                .SetDelay(lerpDuration * 0.5f)
                .SetUpdate(true);

            // 파티클 전체 정지
            StopAllSealParticles();
        }

        // ══════════════════════════════════════════════════════
        // 팔 이벤트 핸들러 — SealableComponent 이벤트 수신
        // ══════════════════════════════════════════════════════

        private void HandleArmLSealed()
        {
            if (_data?.ColorData == null) return;

            float transitionDur = _data.ColorData.sealTransitionDuration;

            // 팔 봉인 완료 색상 고정 (SealableComponent 도 처리하지만 Feedback 연출 추가)
            _armLTween?.Kill();
            _armLTween = _armLRenderer?
                .DOColor(_data.ColorData.colorSealed, transitionDur)
                .SetUpdate(true);

            // 파티클 재생
            PlaySealParticle(_armLSealBurst, _armLSealLoop);

            Debug.Log("[BossWardenFeedback] LeftArm 봉인 완료 연출");
        }

        private void HandleArmRSealed()
        {
            if (_data?.ColorData == null) return;

            float transitionDur = _data.ColorData.sealTransitionDuration;

            _armRTween?.Kill();
            _armRTween = _armRRenderer?
                .DOColor(_data.ColorData.colorSealed, transitionDur)
                .SetUpdate(true);

            PlaySealParticle(_armRSealBurst, _armRSealLoop);

            Debug.Log("[BossWardenFeedback] RightArm 봉인 완료 연출");
        }

        private void HandleArmLReleased()
        {
            if (_data?.ColorData == null) return;

            float transitionDur = _data.ColorData.sealTransitionDuration;

            _armLTween?.Kill();
            _armLTween = _armLRenderer?
                .DOColor(_data.ColorData.colorBase, transitionDur)
                .SetUpdate(true);

            StopSealLoopParticle(_armLSealLoop);

            Debug.Log("[BossWardenFeedback] LeftArm 봉인 해제 연출");
        }

        private void HandleArmRReleased()
        {
            if (_data?.ColorData == null) return;

            float transitionDur = _data.ColorData.sealTransitionDuration;

            _armRTween?.Kill();
            _armRTween = _armRRenderer?
                .DOColor(_data.ColorData.colorBase, transitionDur)
                .SetUpdate(true);

            StopSealLoopParticle(_armRSealLoop);

            Debug.Log("[BossWardenFeedback] RightArm 봉인 해제 연출");
        }

        // ══════════════════════════════════════════════════════
        // 파티클 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 버스트(1회) + 루프(지속) 파티클 재생.
        /// </summary>
        private void PlaySealParticle(ParticleSystem burst, ParticleSystem loop)
        {
            if (burst != null)
            {
                burst.Stop();
                burst.Play();
            }
            if (loop != null)
            {
                loop.Stop();
                loop.Play();
            }
        }

        /// <summary>
        /// 루프 파티클만 정지.
        /// ForceRelease 시 호출.
        /// </summary>
        private void StopSealLoopParticle(ParticleSystem loop)
        {
            loop?.Stop();
        }

        /// <summary>
        /// 모든 봉인 파티클 즉시 정지.
        /// Dead 연출 시 호출.
        /// </summary>
        private void StopAllSealParticles()
        {
            _armLSealBurst?.Stop();
            _armLSealLoop?.Stop();
            _armRSealBurst?.Stop();
            _armRSealLoop?.Stop();
        }
    }
}