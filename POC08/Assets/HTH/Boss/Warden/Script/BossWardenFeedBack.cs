// ============================================================
// BossWardenFeedback.cs
// Boss_Warden 시각 피드백 컴포넌트
//
// [수정 내용]
//   팔(LeftArm/RightArm) SpriteRenderer 색상 제어 완전 제거
//   → 팔 색상은 SealableComponent 단독 담당
//   → 패턴이 임시로 바꾼 후 GetArmGaugeColor() 로 복귀
//
//   제거 항목:
//     _armLRenderer / _armRRenderer 필드 제거
//     _armLTween / _armRTween 핸들 제거
//     OnDilPhaseEnter() — 팔 노란 Pulse 제거
//     OnDilPhaseExit()  — 팔 Idle 복귀 DOColor 제거
//     OnPhaseChanged()  — 팔 붉은 점프 DOColor 제거
//     OnDead()          — 팔 검정 DOColor 제거
//     HandleArmLSealed / HandleArmRSealed — DOColor 파랑 제거
//                                           파티클 재생만 유지
//
//   유지 항목:
//     본체(_bodyRenderer) 모든 색상 연출
//     OnDilPhaseEnter() — 코어 흰 Pulse (코어는 DilPhase 연출 보조)
//     OnDilPhaseExit()  — 코어 Pulse 종료
//     OnFinalSealReady() — 코어 청백 강한 Pulse
//     HandleArmLSealed / HandleArmRSealed — 파티클 재생
//
// [색상 소유권 확정]
//   Boss_WardenBody   → 이 컴포넌트 단독
//   LeftArm / RightArm → SealableComponent 단독
//   Core              → SealableComponent 기본 + DilPhase Pulse 보조
//
// [namespace] SEAL
// ============================================================

using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 시각 피드백 컴포넌트.
    ///
    /// ────────────────────────────────────────────────────
    /// [색상 담당]
    ///   본체 SpriteRenderer → 이 컴포넌트 단독 (AI 상태 색상)
    ///   팔 SpriteRenderer   → SealableComponent 단독 (봉인도 색상)
    ///   코어 SpriteRenderer → SealableComponent 기본 + DilPhase Pulse 보조
    ///
    /// [BossWardenCore 브리지 — public 메서드]
    ///   Initialize(BossWardenDataSO)
    ///   OnDilPhaseEnter() / OnDilPhaseExit()
    ///   OnFinalSealReady()
    ///   OnPhaseChanged(int)
    ///   OnDead()
    ///   HandleStateChanged(WardenAIState, BossPatternBase)
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
        /// </summary>
        [Tooltip("BossWardenDataSO. BossWardenCore 에서 주입 or Inspector 연결.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── AI 연결 ──────────────────────")]

        /// <summary>BossWardenAI. OnStateChanged 이벤트 구독용. 미연결 시 자동 탐색.</summary>
        [Tooltip("BossWardenAI. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAI _ai;

        [Header("── 팔 부위 연결 (파티클용) ──────────────────────")]

        /// <summary>왼팔 BossWardenArmPart. SealableComponent 이벤트 구독용.</summary>
        [Tooltip("왼팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armLPart;

        /// <summary>오른팔 BossWardenArmPart.</summary>
        [Tooltip("오른팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armRPart;

        [Header("── SpriteRenderer ──────────────────────")]

        /// <summary>
        /// 본체 색상 제어 대상.
        /// 팔/코어는 SealableComponent 가 담당 — 이 컴포넌트에서 건드리지 않음.
        /// </summary>
        [Tooltip("본체 SpriteRenderer. 팔/코어는 SealableComponent 가 담당.")]
        [SerializeField] private SpriteRenderer _bodyRenderer;

        /// <summary>
        /// 코어 SpriteRenderer.
        /// DilPhase Pulse 보조 전용.
        /// 봉인도 색상은 SealableComponent 담당.
        /// </summary>
        [Tooltip("코어 SpriteRenderer. DilPhase Pulse 보조용.")]
        [SerializeField] private SpriteRenderer _coreRenderer;

        [Header("── 봉인 완료 파티클 ──────────────────────")]

        /// <summary>왼팔 봉인 완료 버스트 파티클.</summary>
        [Tooltip("왼팔 봉인 완료 버스트 파티클.")]
        [SerializeField] private ParticleSystem _armLSealParticle;

        /// <summary>오른팔 봉인 완료 버스트 파티클.</summary>
        [Tooltip("오른팔 봉인 완료 버스트 파티클.")]
        [SerializeField] private ParticleSystem _armRSealParticle;

        // ══════════════════════════════════════════════════════
        // DOTween 핸들 — 본체 + 코어만
        // (팔 트윈 핸들 없음 — SealableComponent 가 관리)
        // ══════════════════════════════════════════════════════

        /// <summary>본체 색상 트윈 핸들.</summary>
        private Tween _bodyTween;

        /// <summary>코어 색상 트윈 핸들. DilPhase Pulse 전용.</summary>
        private Tween _coreTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_bodyRenderer == null)
                _bodyRenderer = GetComponent<SpriteRenderer>();

            if (_ai == null)
                _ai = GetComponent<BossWardenAI>();
        }

        private void Start()
        {
            SubscribeAll();

            if (_data != null && _bodyRenderer != null)
                _bodyRenderer.color = _data.colorIdle;
        }

        private void OnDestroy()
        {
            _bodyTween?.Kill();
            _coreTween?.Kill();
            UnsubscribeAll();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독
        // ══════════════════════════════════════════════════════

        private void SubscribeAll()
        {
            if (_ai != null)
            {
                _ai.OnStateChanged -= HandleStateChanged;
                _ai.OnStateChanged += HandleStateChanged;
            }

            SubscribeArmSealable(_armLPart, isLeft: true);
            SubscribeArmSealable(_armRPart, isLeft: false);
        }

        private void UnsubscribeAll()
        {
            if (_ai != null)
                _ai.OnStateChanged -= HandleStateChanged;
        }

        /// <summary>
        /// 팔 SealableComponent 이벤트 구독.
        /// 파티클 재생만 처리.
        /// DOColor 처리 없음 — SealableComponent 가 색상 담당.
        /// </summary>
        private void SubscribeArmSealable(BossWardenArmPart armPart, bool isLeft)
        {
            if (armPart == null) return;
            var sealable = armPart.GetComponent<SealableComponent>();
            if (sealable == null) return;

            if (isLeft)
            {
                sealable.OnSealCompleted -= HandleArmLSealed;
                sealable.OnSealCompleted += HandleArmLSealed;
            }
            else
            {
                sealable.OnSealCompleted -= HandleArmRSealed;
                sealable.OnSealCompleted += HandleArmRSealed;
            }
        }

        // ══════════════════════════════════════════════════════
        // AI 상태 색상 핸들러 — 본체만
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// AI 상태에 따라 본체 색상 전환.
        /// 팔/코어 건드리지 않음.
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
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// DataSO 주입.
        /// BossWardenCore.InjectData() 에서 호출.
        /// </summary>
        public void Initialize(BossWardenDataSO data)
        {
            _data = data;
        }

        /// <summary>
        /// DilPhase 진입 연출.
        /// 본체 노란 Pulse + 코어 흰 Pulse.
        /// 팔 색상 건드리지 않음.
        /// </summary>
        public void OnDilPhaseEnter()
        {
            if (_data == null) return;

            float pulsePeriod = _data.ColorData?.sealReadyPulseDuration ?? 0.4f;

            // 본체 노란 Pulse
            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data.colorDilPhase, pulsePeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);

            // 코어 흰 Pulse
            _coreTween?.Kill();
            _coreTween = _coreRenderer?
                .DOColor(Color.white, pulsePeriod * 0.3f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>
        /// DilPhase 종료 연출.
        /// 본체 Idle 복귀 + 코어 Pulse 종료.
        /// 팔 색상 건드리지 않음.
        /// </summary>
        public void OnDilPhaseExit()
        {
            float lerpDuration = _data?.ColorData?.colorLerpDuration ?? 0.15f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data?.colorIdle ?? Color.gray, lerpDuration)
                .SetUpdate(true);

            _coreTween?.Kill();
        }

        /// <summary>
        /// FinalSeal 진입 연출.
        /// 본체 청백 Pulse + 코어 강한 청백 Pulse.
        /// </summary>
        public void OnFinalSealReady()
        {
            if (_data == null) return;

            float pulsePeriod = _data.ColorData?.sealReadyPulseDuration ?? 0.4f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data.ColorData.colorCoreFinalSeal, pulsePeriod * 0.3f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);

            _coreTween?.Kill();
            _coreTween = _coreRenderer?
                .DOColor(Color.cyan, pulsePeriod * 0.2f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>
        /// 페이즈 전환 연출.
        /// 본체 붉은 점프만. 팔 건드리지 않음.
        /// </summary>
        public void OnPhaseChanged(int newPhase)
        {
            if (_data == null || newPhase != 2) return;

            float lerpDuration = _data.ColorData?.colorLerpDuration ?? 0.15f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data.colorPhase2, 0.1f)
                .SetLoops(6, LoopType.Yoyo)
                .OnComplete(() =>
                {
                    _bodyTween = _bodyRenderer?
                        .DOColor(_data.colorIdle, lerpDuration)
                        .SetUpdate(true);
                })
                .SetUpdate(true);
        }

        /// <summary>
        /// 처치 연출.
        /// 본체 검정 + 본체 축소.
        /// 팔/코어 색상 건드리지 않음.
        /// </summary>
        public void OnDead()
        {
            _bodyTween?.Kill();
            _coreTween?.Kill();

            _bodyRenderer?.DOColor(Color.black, 0.5f).SetUpdate(true);

            transform.DOScale(Vector3.zero, 0.8f)
                .SetEase(Ease.InBack)
                .SetUpdate(true);
        }

        // ══════════════════════════════════════════════════════
        // 팔 봉인 완료 연출 — 파티클만
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 왼팔 봉인 완료.
        /// 파티클 재생만. DOColor 없음.
        /// SealableComponent.ExecuteSeal() 이 colorSealed 처리.
        /// </summary>
        private void HandleArmLSealed()
        {
            _armLSealParticle?.Play();
        }

        /// <summary>
        /// 오른팔 봉인 완료.
        /// 파티클 재생만.
        /// </summary>
        private void HandleArmRSealed()
        {
            _armRSealParticle?.Play();
        }
    }
}