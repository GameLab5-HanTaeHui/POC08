// ============================================================
// BossWardenFeedback.cs  v1.0
// Boss_Warden 상태별 DOTween 색상 연출 전담 컴포넌트
//
// [POC07 참고]
//   TestBossFeedBack.cs 구조를 기반으로 탑뷰 재설계.
//   DOTween Sequence / Tween 핸들 관리 방식 계승.
//
// [POC07과의 차이]
//   POC07: Animator 대체 목적의 flipX 기반 방향 처리 포함
//   POC08: 방향 처리 제거 (탑뷰에서 flipX 는 ObjectDirectionController 담당)
//          Animator 없음 → DOTween 으로 모든 동작감 표현
//
// [역할]
//   ① BossWardenAI.OnStateChanged 구독 → 상태별 색상 전환
//   ② SealGaugeComponent.OnStageChanged 구독 → 부위 봉인도 단계 색상 전환
//   ③ BossWardenCore 이벤트 구독 → 그로기/딜페이즈/처치 연출
//   ④ 히트 스탑 점멸 (본체 기준)
//   ⑤ Pulse 루프 (Groggy / DilPhase 중 지속 발광)
//
// [DOTween 원칙]
//   - 새 상태 진입 시 반드시 이전 Tween/Sequence 를 Kill() 후 새로 생성
//   - SetUpdate(true) : 슬로우 모션(Time.timeScale 감소) 중에도 연출 유지
//   - DOColor 는 SpriteRenderer 에 직접 호출 (Material 변경 없음)
//   - Pulse = DOColor Yoyo Loop 로 구현 (InfiteLoop = -1)
//   - 새 색상으로 바뀔 때 UpdateBaseColor() 로 ArmPart 기본 색상 동기화
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
    /// Boss_Warden 상태별 DOTween 색상 연출 전담 컴포넌트. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [색상 우선순위]
    ///   Dead > Groggy > DilPhase > Warning > Active > Recovery > Chase > Idle
    ///   상위 상태 진입 시 하위 상태 Tween 을 Kill() 하고 새 Tween 시작.
    ///
    /// [구독 대상]
    ///   BossWardenAI.OnStateChanged
    ///   BossWardenCore.OnGroggyEnter / OnGroggyExit
    ///   BossWardenCore.OnDilPhaseEnter / OnDilPhaseExit
    ///   BossWardenCore.OnPhaseChanged
    ///   BossWardenCore.OnDead
    ///   LeftArm.SealGaugeComponent.OnStageChanged
    ///   RightArm.SealGaugeComponent.OnStageChanged
    ///   LeftArm / RightArm SealGaugeComponent.OnSealed / OnReleased
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossWardenFeedback : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── DataSO (필수) ──────────────────────")]

        /// <summary>
        /// BossWardenDataSO.
        /// 모든 색상 / DOTween 타이밍 수치 참조.
        /// </summary>
        [Tooltip("BossWardenDataSO. 필수 연결.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 본체 렌더러 ──────────────────────")]

        /// <summary>
        /// 보스 본체 SpriteRenderer.
        /// 미연결 시 GetComponent 자동 탐색.
        /// </summary>
        [Tooltip("보스 본체 SpriteRenderer. 미연결 시 자동 탐색.")]
        [SerializeField] private SpriteRenderer _bodyRenderer;

        [Header("── 부위 렌더러 ──────────────────────")]

        /// <summary>
        /// 왼팔 SpriteRenderer.
        /// 봉인도 단계 색상 전환 대상.
        /// </summary>
        [Tooltip("왼팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armLRenderer;

        /// <summary>
        /// 오른팔 SpriteRenderer.
        /// </summary>
        [Tooltip("오른팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armRRenderer;

        /// <summary>
        /// 코어 SpriteRenderer.
        /// 그로기 진입 시 색상 변화.
        /// </summary>
        [Tooltip("코어 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _coreRenderer;

        [Header("── 부위 컴포넌트 참조 ──────────────────────")]

        /// <summary>
        /// 왼팔 BossWardenArmPart.
        /// 봉인 완료 / 해제 이벤트 구독 + UpdateBaseColor() 호출 대상.
        /// </summary>
        [Tooltip("왼팔 BossWardenArmPart.")]
        [SerializeField] private BossWardenArmPart _armLPart;

        /// <summary>
        /// 오른팔 BossWardenArmPart.
        /// </summary>
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

        /// <summary> 본체 현재 Tween 핸들. 새 상태 진입 시 Kill() 후 교체. </summary>
        private Tween _bodyTween;

        /// <summary> 왼팔 현재 Tween 핸들. </summary>
        private Tween _armLTween;

        /// <summary> 오른팔 현재 Tween 핸들. </summary>
        private Tween _armRTween;

        /// <summary> 코어 현재 Tween 핸들. </summary>
        private Tween _coreTween;

        // ══════════════════════════════════════════════════════
        // 봉인도 단계별 색상 테이블
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 단계(0~4) 별 색상 배열.
        /// DataSO 의 색상 필드를 순서대로 참조.
        /// index 0 = 0% / 1 = 25% / 2 = 50% / 3 = 75% / 4 = 100%
        /// </summary>
        private Color[] _armStageColors;

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
            // 색상 테이블 초기화
            BuildArmStageColors();

            // 이벤트 구독
            SubscribeAll();

            // 초기 색상 설정
            SetBodyColorImmediate(_data != null ? _data.colorIdle : Color.gray);
        }

        private void OnDestroy()
        {
            UnsubscribeAll();

            // 모든 Tween 정리
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
            // AI 상태 이벤트
            if (_ai != null)
            {
                _ai.OnStateChanged -= HandleStateChanged;
                _ai.OnStateChanged += HandleStateChanged;
            }

            // Core 이벤트
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

            // 팔 봉인도 단계 이벤트
            SubscribeArmGauge(_armLPart, _armLRenderer, isLeft: true);
            SubscribeArmGauge(_armRPart, _armRRenderer, isLeft: false);
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
        /// 팔 부위 봉인도 / 봉인 완료 / 봉인 해제 이벤트 구독.
        /// </summary>
        private void SubscribeArmGauge(BossWardenArmPart armPart, SpriteRenderer renderer, bool isLeft)
        {
            if (armPart == null || armPart.SealGauge == null) return;

            var gauge = armPart.SealGauge;

            if (isLeft)
            {
                gauge.OnStageChanged -= HandleArmLStageChanged;
                gauge.OnSealed -= HandleArmLSealed;
                gauge.OnReleased -= HandleArmLReleased;

                gauge.OnStageChanged += HandleArmLStageChanged;
                gauge.OnSealed += HandleArmLSealed;
                gauge.OnReleased += HandleArmLReleased;
            }
            else
            {
                gauge.OnStageChanged -= HandleArmRStageChanged;
                gauge.OnSealed -= HandleArmRSealed;
                gauge.OnReleased -= HandleArmRReleased;

                gauge.OnStageChanged += HandleArmRStageChanged;
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
                gauge.OnStageChanged -= HandleArmLStageChanged;
                gauge.OnSealed -= HandleArmLSealed;
                gauge.OnReleased -= HandleArmLReleased;
            }
            else
            {
                gauge.OnStageChanged -= HandleArmRStageChanged;
                gauge.OnSealed -= HandleArmRSealed;
                gauge.OnReleased -= HandleArmRReleased;
            }
        }

        // ══════════════════════════════════════════════════════
        // 색상 테이블 초기화
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// DataSO 에서 봉인도 단계 색상 배열을 구성한다.
        /// </summary>
        private void BuildArmStageColors()
        {
            if (_data == null)
            {
                _armStageColors = new Color[] { Color.white, Color.yellow, new Color(1f, 0.5f, 0f), Color.red, Color.blue };
                return;
            }

            _armStageColors = new Color[]
            {
                _data.colorArm0,    // Stage 0 : 0%
                _data.colorArm25,   // Stage 1 : 25%
                _data.colorArm50,   // Stage 2 : 50%
                _data.colorArm75,   // Stage 3 : 75%
                _data.colorArm100,  // Stage 4 : 100%
            };
        }

        // ══════════════════════════════════════════════════════
        // AI 상태 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// BossWardenAI.OnStateChanged 수신.
        /// 상태별 본체 색상 전환 시작.
        /// </summary>
        private void HandleStateChanged(BossWardenAI.WardenAIState newState, BossPatternBase pattern)
        {
            if (_data == null || _bodyRenderer == null) return;

            switch (newState)
            {
                case BossWardenAI.WardenAIState.Idle:
                case BossWardenAI.WardenAIState.Chase:
                    PlayBodyColor(_data.colorIdle, _data.colorTransitionDuration);
                    break;

                case BossWardenAI.WardenAIState.Warning:
                    // 주황 빠른 Pulse — 긴장감 연출
                    PlayBodyPulse(_data.colorWarning, _data.colorIdle, _data.pulsePeriod);
                    break;

                case BossWardenAI.WardenAIState.Active:
                    // 흰색 순간 전환 — 공격 강조
                    PlayBodyColor(_data.colorActive, 0.05f);
                    break;

                case BossWardenAI.WardenAIState.Recovery:
                    // 붉은 페이드 — 취약 구간 강조
                    PlayBodyColor(_data.colorRecovery, _data.colorTransitionDuration);
                    break;
            }
        }

        // ══════════════════════════════════════════════════════
        // Core 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 그로기 진입.
        /// 본체 노란 빠른 Pulse + 코어 노란 Pulse 시작.
        /// </summary>
        private void HandleGroggyEnter()
        {
            if (_data == null) return;

            // 본체 노란 빠른 Pulse
            PlayBodyPulse(_data.colorGroggy, _data.colorIdle, _data.pulsePeriod * 0.6f);

            // 코어 노란 Pulse (코어 활성화 시점과 연동)
            if (_coreRenderer != null)
                PlayCorePulse(_data.colorCoreActive, new Color(1f, 1f, 0.5f), _data.pulsePeriod * 0.5f);
        }

        /// <summary>
        /// 그로기 종료.
        /// 본체 Idle 색상 복귀.
        /// </summary>
        private void HandleGroggyExit()
        {
            if (_data == null) return;
            PlayBodyColor(_data.colorIdle, _data.colorTransitionDuration);
        }

        /// <summary>
        /// 딜 페이즈 진입.
        /// 본체 밝은 주황 Pulse + 코어 흰 빠른 Pulse.
        /// </summary>
        private void HandleDilPhaseEnter()
        {
            if (_data == null) return;

            PlayBodyPulse(_data.colorDilPhase, _data.colorIdle, _data.pulsePeriod);

            if (_coreRenderer != null)
                PlayCorePulse(_data.colorCoreDilPhase, _data.colorCoreActive, _data.pulsePeriod * 0.4f);
        }

        /// <summary>
        /// 딜 페이즈 종료.
        /// 본체 Idle 복귀.
        /// </summary>
        private void HandleDilPhaseExit()
        {
            if (_data == null) return;
            PlayBodyColor(_data.colorIdle, _data.colorTransitionDuration);

            // 코어 Pulse 정리
            _coreTween?.Kill();
        }

        /// <summary>
        /// 페이즈 전환.
        /// 2페이즈 진입 시 본체 진한 붉은 전환 강조.
        /// </summary>
        private void HandlePhaseChanged(int newPhase)
        {
            if (_data == null || newPhase != 2) return;

            // 진한 붉은 순간 → DOShake → Idle 복귀 Sequence
            _bodyTween?.Kill();
            var seq = DOTween.Sequence();
            seq.Append(_bodyRenderer.DOColor(_data.colorPhase2, 0.05f).SetUpdate(true));
            seq.AppendInterval(0.3f);
            seq.Append(_bodyRenderer.DOColor(_data.colorIdle, _data.colorTransitionDuration).SetUpdate(true));
            seq.SetUpdate(true);
            _bodyTween = seq;

            Debug.Log("[BossWardenFeedback] 2페이즈 전환 연출");
        }

        /// <summary>
        /// 처치.
        /// 본체 검정 페이드 → Scale 0 축소.
        /// </summary>
        private void HandleDead()
        {
            if (_data == null) return;

            _bodyTween?.Kill();

            var seq = DOTween.Sequence();
            seq.Append(_bodyRenderer.DOColor(_data.colorDead, 0.3f).SetUpdate(true));
            seq.Join(transform.DOScale(0f, 0.5f).SetEase(Ease.InBack).SetUpdate(true));
            seq.SetUpdate(true);
            _bodyTween = seq;

            Debug.Log("[BossWardenFeedback] 처치 연출 시작");
        }

        // ══════════════════════════════════════════════════════
        // 팔 봉인도 이벤트 핸들러 — 왼팔
        // ══════════════════════════════════════════════════════

        private void HandleArmLStageChanged(int stage)
            => ApplyArmStageColor(_armLRenderer, _armLPart, stage, isLoop: stage == 4);

        private void HandleArmLSealed()
            => ApplyArmSealedColor(_armLRenderer, _armLPart);

        private void HandleArmLReleased()
            => ApplyArmStageColor(_armLRenderer, _armLPart, stage: 0, isLoop: false);

        // ══════════════════════════════════════════════════════
        // 팔 봉인도 이벤트 핸들러 — 오른팔
        // ══════════════════════════════════════════════════════

        private void HandleArmRStageChanged(int stage)
            => ApplyArmStageColor(_armRRenderer, _armRPart, stage, isLoop: stage == 4);

        private void HandleArmRSealed()
            => ApplyArmSealedColor(_armRRenderer, _armRPart);

        private void HandleArmRReleased()
            => ApplyArmStageColor(_armRRenderer, _armRPart, stage: 0, isLoop: false);

        // ══════════════════════════════════════════════════════
        // 부위 색상 적용
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 단계에 따른 팔 색상 전환.
        ///
        /// [단계별 연출]
        ///   Stage 0~3 : DOColor 단순 전환
        ///   Stage 4   : 파랑 빠른 Yoyo Pulse (집행 가능 상태 강조)
        ///
        /// [UpdateBaseColor 동기화]
        ///   색상이 바뀔 때 BossWardenArmPart.UpdateBaseColor() 를 호출하여
        ///   히트 점멸 후 복귀 색상을 현재 단계 색상으로 유지.
        /// </summary>
        private void ApplyArmStageColor(SpriteRenderer renderer, BossWardenArmPart armPart, int stage, bool isLoop)
        {
            if (renderer == null || _data == null) return;
            if (stage < 0 || stage >= _armStageColors.Length) return;

            Color targetColor = _armStageColors[stage];

            // 진행 중인 Tween 정리 후 새 Tween 시작
            if (renderer == _armLRenderer)
            {
                _armLTween?.Kill();

                if (isLoop)
                    _armLTween = renderer.DOColor(_data.colorIdle, _data.pulsePeriod)
                        .From(targetColor).SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
                else
                    _armLTween = renderer.DOColor(targetColor, _data.colorTransitionDuration).SetUpdate(true);
            }
            else
            {
                _armRTween?.Kill();

                if (isLoop)
                    _armRTween = renderer.DOColor(_data.colorIdle, _data.pulsePeriod)
                        .From(targetColor).SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
                else
                    _armRTween = renderer.DOColor(targetColor, _data.colorTransitionDuration).SetUpdate(true);
            }

            // 히트 점멸 복귀 색상 동기화
            armPart?.UpdateBaseColor(targetColor);
        }

        /// <summary>
        /// 봉인 집행 완료 색상 (파랑 고정) 적용.
        /// Pulse 종료 후 단색 고정.
        /// </summary>
        private void ApplyArmSealedColor(SpriteRenderer renderer, BossWardenArmPart armPart)
        {
            if (renderer == null || _data == null) return;

            Color sealedColor = _data.colorArmSealed;

            if (renderer == _armLRenderer)
            {
                _armLTween?.Kill();
                _armLTween = renderer.DOColor(sealedColor, _data.colorTransitionDuration).SetUpdate(true);
            }
            else
            {
                _armRTween?.Kill();
                _armRTween = renderer.DOColor(sealedColor, _data.colorTransitionDuration).SetUpdate(true);
            }

            armPart?.UpdateBaseColor(sealedColor);
        }

        // ══════════════════════════════════════════════════════
        // 공통 DOTween 헬퍼
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 본체를 지정 색상으로 전환한다.
        /// 이전 Tween 을 Kill() 후 새 DOColor 시작.
        /// SetUpdate(true) 로 timeScale 영향 없이 동작.
        /// </summary>
        private void PlayBodyColor(Color targetColor, float duration)
        {
            if (_bodyRenderer == null) return;

            _bodyTween?.Kill();
            _bodyTween = _bodyRenderer
                .DOColor(targetColor, duration)
                .SetUpdate(true);
        }

        /// <summary>
        /// 본체를 두 색상 사이로 반복 Pulse 시킨다.
        /// colorA(밝음) ↔ colorB(어두움) Yoyo 무한 루프.
        /// </summary>
        private void PlayBodyPulse(Color colorA, Color colorB, float period)
        {
            if (_bodyRenderer == null) return;

            _bodyTween?.Kill();
            _bodyRenderer.color = colorA;
            _bodyTween = _bodyRenderer
                .DOColor(colorB, period)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>
        /// 코어를 두 색상 사이로 반복 Pulse 시킨다.
        /// </summary>
        private void PlayCorePulse(Color colorA, Color colorB, float period)
        {
            if (_coreRenderer == null) return;

            _coreTween?.Kill();
            _coreRenderer.color = colorA;
            _coreTween = _coreRenderer
                .DOColor(colorB, period)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        /// <summary>
        /// 본체를 순간 흰색 점멸 후 현재 색상으로 복귀.
        /// 공격 피격 시 BossWardenCore 에서 호출.
        /// </summary>
        public void PlayBodyHitFlash()
        {
            if (_bodyRenderer == null || _data == null) return;

            Color currentColor = _bodyRenderer.color;
            _bodyTween?.Kill();
            _bodyRenderer.color = Color.white;
            _bodyTween = _bodyRenderer
                .DOColor(currentColor, _data.hitFlashDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        // ══════════════════════════════════════════════════════
        // 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 본체 색상을 즉시 설정한다 (Tween 없음).
        /// 초기화 시에만 사용.
        /// </summary>
        private void SetBodyColorImmediate(Color color)
        {
            if (_bodyRenderer == null) return;
            _bodyTween?.Kill();
            _bodyRenderer.color = color;
        }
    }
}