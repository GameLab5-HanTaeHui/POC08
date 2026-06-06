// ============================================================
// BossWardenFeedback.cs  v4.0
// Boss_Warden 시각 피드백 컴포넌트
//
// [v4.0 변경 — Groggy 연출 제거, DilPhase로 통합]
//   제거:
//     OnGroggyEnter() public 브리지 메서드 (노란 Pulse)
//     OnGroggyExit()  public 브리지 메서드 (Idle 색상 복귀)
//
//   변경:
//     OnDilPhaseEnter() → 딜페이즈 색상 연출 전담
//                         (기존 OnGroggyEnter 노란 Pulse 흡수)
//     OnDilPhaseExit()  → Idle 색상 복귀 전담
//                         (기존 OnGroggyExit 역할 흡수)
//
// [BossWardenCore 브리지 메서드 — public]
//   Initialize(BossWardenDataSO)
//   OnDilPhaseEnter()
//   OnDilPhaseExit()
//   OnFinalSealReady()
//   OnPhaseChanged(int)
//   OnDead()
//   HandleStateChanged(WardenAIState, BossPatternBase)
//
// [namespace] SEAL
// ============================================================

using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 시각 피드백 컴포넌트. (v4.0)
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

        /// <summary>BossWardenAI. OnStateChanged 이벤트 구독용. 미연결 시 자동 탐색.</summary>
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
        [Tooltip("왼팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armLRenderer;

        /// <summary>오른팔 색상 제어 대상.</summary>
        [Tooltip("오른팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armRRenderer;

        /// <summary>코어 색상 제어 대상. 딜 페이즈 Pulse 연출용.</summary>
        [Tooltip("코어 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _coreRenderer;

        [Header("── 봉인 완료 파티클 ──────────────────────")]

        /// <summary>왼팔 봉인 완료 버스트 파티클.</summary>
        [Tooltip("왼팔 봉인 완료 버스트 파티클.")]
        [SerializeField] private ParticleSystem _armLSealParticle;

        /// <summary>오른팔 봉인 완료 버스트 파티클.</summary>
        [Tooltip("오른팔 봉인 완료 버스트 파티클.")]
        [SerializeField] private ParticleSystem _armRSealParticle;

        // ══════════════════════════════════════════════════════
        // DOTween 핸들
        // ══════════════════════════════════════════════════════

        /// <summary>본체 색상 트윈 핸들. Kill() 전용.</summary>
        private Tween _bodyTween;

        /// <summary>왼팔 색상 트윈 핸들.</summary>
        private Tween _armLTween;

        /// <summary>오른팔 색상 트윈 핸들.</summary>
        private Tween _armRTween;

        /// <summary>코어 색상 트윈 핸들.</summary>
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

            // 초기 색상 설정
            if (_data != null && _bodyRenderer != null)
                _bodyRenderer.color = _data.colorIdle;
        }

        private void OnDestroy()
        {
            _bodyTween?.Kill();
            _armLTween?.Kill();
            _armRTween?.Kill();
            _coreTween?.Kill();
            UnsubscribeAll();
        }

        // ══════════════════════════════════════════════════════
        // 이벤트 구독
        // ══════════════════════════════════════════════════════

        private void SubscribeAll()
        {
            // AI 상태 변화 구독
            if (_ai != null)
            {
                _ai.OnStateChanged -= HandleStateChanged;
                _ai.OnStateChanged += HandleStateChanged;
            }

            // 팔 봉인 이벤트 구독
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
        /// 봉인 완료 / 해제 색상 연출 + 파티클.
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

        // ══════════════════════════════════════════════════════
        // AI 상태 색상 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// AI 상태에 따라 본체 색상 전환.
        /// Warning / Active / Recovery / Idle 색상 사용.
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
        /// 본체 노란-주황 Pulse + 코어 흰색 Pulse.
        /// (v4.0: 기존 OnGroggyEnter 노란 Pulse 흡수)
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

            // 코어 흰색 Pulse
            _coreTween?.Kill();
            _coreTween = _coreRenderer?
                .DOColor(Color.white, pulsePeriod * 0.3f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>
        /// DilPhase 종료 연출.
        /// 본체 + 코어 Idle 색상 복귀.
        /// (v4.0: 기존 OnGroggyExit + OnDilPhaseExit 통합)
        /// </summary>
        public void OnDilPhaseExit()
        {
            float lerpDuration = _data?.ColorData?.colorLerpDuration ?? 0.15f;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer?
                .DOColor(_data?.colorIdle ?? Color.gray, lerpDuration)
                .SetUpdate(true);

            _coreTween?.Kill();
            if (_coreRenderer != null)
                _coreRenderer.color = Color.yellow;
        }

        /// <summary>
        /// FinalSeal 진입 연출.
        /// 코어 청백 강한 Pulse.
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
        /// 본체 빠른 붉은 Pulse 후 Idle 복귀.
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
        /// 본체 검정 + 축소.
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
        // 팔 봉인 완료 / 해제 연출
        // ══════════════════════════════════════════════════════

        private void HandleArmLSealed()
        {
            _armLTween?.Kill();
            _armLTween = _armLRenderer?
                .DOColor(Color.blue, 0.15f)
                .SetUpdate(true);
            _armLSealParticle?.Play();
        }

        private void HandleArmLReleased()
        {
            _armLTween?.Kill();
            _armLTween = _armLRenderer?
                .DOColor(_data?.colorIdle ?? Color.gray, 0.15f)
                .SetUpdate(true);
        }

        private void HandleArmRSealed()
        {
            _armRTween?.Kill();
            _armRTween = _armRRenderer?
                .DOColor(Color.blue, 0.15f)
                .SetUpdate(true);
            _armRSealParticle?.Play();
        }

        private void HandleArmRReleased()
        {
            _armRTween?.Kill();
            _armRTween = _armRRenderer?
                .DOColor(_data?.colorIdle ?? Color.gray, 0.15f)
                .SetUpdate(true);
        }
    }
}