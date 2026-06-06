// ============================================================
// SealColorDataSO.cs  v1.0
// 범용 봉인 색상 / 연출 수치 ScriptableObject
//
// [역할]
//   봉인도 변화에 따른 색상 / DOTween 연출 수치 보관.
//   SealDataSO 와 함께 BossDataSO 에 포함.
//   어떤 보스에게도 재사용 가능한 범용 SO.
//
// [색상 변화 설계 — 점진적 보간]
//   봉인도 0~100% 구간을 colorBase → colorFull 로 선형 보간.
//   갑자기 바뀌는(띡띡) 방식이 아닌 서서히 물들어가는 방식.
//
//   AddGauge() 호출마다:
//     Color target = Color.Lerp(colorBase, colorFull, percent / 100f)
//     SpriteRenderer.DOColor(target, colorLerpDuration)
//
//   [패턴 사용 후 색상 초기화 방지]
//     피격 점멸(HitFlash) 은 흰색 아닌 colorHitFlash 사용
//     점멸 종료 후 → 현재 봉인도 비율 색상으로 반드시 복귀
//     패턴 내부에서 DOColor(white) 절대 금지
//
// [봉인 완료 색상]
//   ExecuteSeal() 완료 시 colorSealed 고정
//   ForceRelease() 시 colorBase 복귀 (DOTween 보간)
//
// [집행 가능 상태 맥동]
//   봉인도 100% 도달 → sealReadyPulse 시작
//   colorFull ↔ colorSealReadyPulse 반복 (DOTween Yoyo Loop)
//   집행 완료 or ForceRelease 시 맥동 종료
//
// [범위 표시 색상]
//   봉인 집행 가능 원형 범위 → colorSealRange
//   코어 범위 → colorCoreRange
//   패턴 예고 범위 → colorWarningRange (Warden 전용 SO 에서 관리)
//
// [생성 방법]
//   Assets 우클릭 → Create → SEAL/System/SealColorDataSO
//
// [연결]
//   BossDataSO._sealColorData 에 연결.
//   SealableComponent, SealExecutionEffect, SealEffectManager 등이 참조.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 범용 봉인 색상 / 연출 수치 ScriptableObject. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [색상 보간 공식]
    ///   Color.Lerp(colorBase, colorFull, gaugePercent / 100f)
    ///   → 봉인도가 오를수록 colorBase → colorFull 로 서서히 변화
    ///   → DOTween DOColor 로 부드럽게 보간
    ///
    /// [피격 점멸 색상 분리]
    ///   colorHitFlash 사용 (흰색 아님)
    ///   점멸 종료 후 현재 봉인도 비율 색상으로 복귀 필수
    ///
    /// [봉인 완료 Loop 파티클]
    ///   sealedParticlePrefab — Loop 파티클 프리팹
    ///   부위 오브젝트에 Instantiate 후 SetActive(true) 유지
    ///   ForceRelease 시 SetActive(false) or Destroy
    /// ────────────────────────────────────────────────────
    /// </summary>
    [CreateAssetMenu(
        menuName = "SEAL/System/SealColorDataSO",
        fileName = "SealColorDataSO")]
    public class SealColorDataSO : ScriptableObject
    {
        // ══════════════════════════════════════════════════════
        // 부위 봉인도 색상 (점진적 보간)
        // ══════════════════════════════════════════════════════

        [Header("── 부위 봉인도 색상 (점진적 보간) ──────────────────────")]

        /// <summary>
        /// 봉인도 0% 기본 색상.
        /// 봉인도 누적 전 / ForceRelease 후 복귀 색상.
        ///
        /// [권장값] 흰색 계열 (1, 1, 1, 1)
        /// </summary>
        [Tooltip("봉인도 0% 기본 색상. ForceRelease 후 복귀 색상. 권장: 흰색.")]
        public Color colorBase = Color.white;

        /// <summary>
        /// 봉인도 100% 도달 색상 (집행 가능 상태).
        /// colorBase 에서 이 색상으로 서서히 물들어 감.
        ///
        /// [권장값] 어두운 보라색 (0.78, 0.49, 1.0, 1.0)
        /// </summary>
        [Tooltip("봉인도 100% 도달 색상. colorBase 에서 이 색상으로 서서히 변화. 권장: 어두운 보라색.")]
        public Color colorFull = new Color(0.78f, 0.49f, 1.0f, 1.0f);

        /// <summary>
        /// 봉인 집행 완료 고정 색상.
        /// ExecuteSeal() 완료 후 이 색상으로 고정.
        /// ForceRelease 시 colorBase 로 복귀.
        ///
        /// [권장값] 진한 보라색 (0.48, 0.18, 0.75, 1.0)
        /// </summary>
        [Tooltip("봉인 완료 고정 색상. 권장: 진한 보라색.")]
        public Color colorSealed = new Color(0.48f, 0.18f, 0.75f, 1.0f);

        /// <summary>
        /// 피격 점멸 색상.
        /// 흰색(white) 대신 이 색상으로 점멸.
        /// 점멸 종료 후 반드시 현재 봉인도 비율 색상으로 복귀.
        ///
        /// [패턴 사용 후 색상 초기화 방지 핵심]
        ///   흰색 점멸을 사용하면 점멸 후 봉인도 색상 정보를 잃음.
        ///   이 색상은 colorBase 와 colorFull 사이 밝은 색 권장.
        ///
        /// [권장값] 밝은 보라색 (0.9, 0.8, 1.0, 1.0)
        /// </summary>
        [Tooltip("피격 점멸 색상. 흰색 대신 이 색상 사용. 권장: 밝은 보라색.")]
        public Color colorHitFlash = new Color(0.9f, 0.8f, 1.0f, 1.0f);

        // ══════════════════════════════════════════════════════
        // 집행 가능 상태 맥동 색상
        // ══════════════════════════════════════════════════════

        [Header("── 집행 가능 상태 맥동 ──────────────────────")]

        /// <summary>
        /// 봉인도 100% 도달 시 맥동 색상.
        /// colorFull ↔ colorSealReadyPulse 반복 (DOTween Yoyo Loop).
        /// 플레이어에게 "집행 가능" 을 시각적으로 알림.
        ///
        /// [권장값] 밝은 파란보라 (0.6, 0.8, 1.0, 1.0)
        /// </summary>
        [Tooltip("집행 가능 상태 맥동 색상. colorFull 과 교대로 맥동. 권장: 밝은 파란보라.")]
        public Color colorSealReadyPulse = new Color(0.6f, 0.8f, 1.0f, 1.0f);

        /// <summary>
        /// 맥동 한 주기 (초).
        /// colorFull → colorSealReadyPulse → colorFull 1회 시간.
        ///
        /// [권장값] 0.4
        /// </summary>
        [Tooltip("맥동 한 주기 (초). colorFull → pulse → colorFull 1회. 권장: 0.4.")]
        [Min(0.05f)]
        public float sealReadyPulseDuration = 0.4f;

        // ══════════════════════════════════════════════════════
        // 코어 색상
        // ══════════════════════════════════════════════════════

        [Header("── 코어 색상 ──────────────────────")]

        /// <summary>
        /// 코어 기본 색상 (그로기 진입 시 활성화).
        ///
        /// [권장값] 노란색 (1.0, 0.93, 0.0, 1.0)
        /// </summary>
        [Tooltip("코어 기본 색상 (그로기 진입 시). 권장: 노란색.")]
        public Color colorCoreIdle = new Color(1.0f, 0.93f, 0.0f, 1.0f);

        /// <summary>
        /// 코어 딜 페이즈 색상.
        ///
        /// [권장값] 밝은 주황색 (1.0, 0.55, 0.0, 1.0)
        /// </summary>
        [Tooltip("코어 딜 페이즈 색상. 권장: 밝은 주황색.")]
        public Color colorCoreDilPhase = new Color(1.0f, 0.55f, 0.0f, 1.0f);

        /// <summary>
        /// 코어 최종 봉인 가능 색상 (코어 봉인도 100% 도달).
        ///
        /// [권장값] 빨간색 (1.0, 0.0, 0.0, 1.0)
        /// </summary>
        [Tooltip("코어 최종 봉인 가능 색상 (봉인도 100%). 권장: 빨간색.")]
        public Color colorCoreFinalSeal = new Color(1.0f, 0.0f, 0.0f, 1.0f);

        // ══════════════════════════════════════════════════════
        // 범위 표시 색상
        // ══════════════════════════════════════════════════════

        [Header("── 범위 표시 색상 ──────────────────────")]

        /// <summary>
        /// 부위 봉인 집행 가능 원형 범위 색상.
        /// 봉인도 100% 도달 시 해당 부위 주변에 표시.
        ///
        /// [권장값] 파란색 반투명 (0.0, 0.53, 1.0, 1.0)
        /// </summary>
        [Tooltip("부위 봉인 집행 가능 범위 색상. 권장: 파란색.")]
        public Color colorSealRange = new Color(0.0f, 0.53f, 1.0f, 1.0f);

        /// <summary>
        /// 코어 범위 색상.
        /// 그로기 진입 시 코어 주변에 표시.
        ///
        /// [권장값] 노란색 (1.0, 0.93, 0.0, 1.0)
        /// </summary>
        [Tooltip("코어 범위 색상. 권장: 노란색.")]
        public Color colorCoreRange = new Color(1.0f, 0.93f, 0.0f, 1.0f);

        // ══════════════════════════════════════════════════════
        // DOTween 수치
        // ══════════════════════════════════════════════════════

        [Header("── DOTween 수치 ──────────────────────")]

        /// <summary>
        /// 봉인도 변화 시 색상 보간 Duration.
        /// AddGauge() 호출마다 DOColor 에 사용.
        /// 너무 길면 색상이 수치를 따라가지 못함.
        ///
        /// [권장값] 0.15
        /// </summary>
        [Tooltip("봉인도 변화 시 색상 보간 Duration. 권장: 0.15.")]
        [Min(0.01f)]
        public float colorLerpDuration = 0.15f;

        /// <summary>
        /// 피격 점멸 지속 시간 (초).
        /// colorHitFlash → 현재 봉인도 색상 복귀까지.
        ///
        /// [권장값] 0.07
        /// </summary>
        [Tooltip("피격 점멸 지속 시간 (초). 권장: 0.07.")]
        [Min(0.01f)]
        public float hitFlashDuration = 0.07f;

        /// <summary>
        /// 봉인 완료 / ForceRelease 색상 전환 Duration.
        /// 봉인 완료 → colorSealed / ForceRelease → colorBase 전환 시.
        ///
        /// [권장값] 0.3
        /// </summary>
        [Tooltip("봉인 완료 / ForceRelease 색상 전환 Duration. 권장: 0.3.")]
        [Min(0.01f)]
        public float sealTransitionDuration = 0.3f;

        // ══════════════════════════════════════════════════════
        // 봉인 완료 Loop 파티클
        // ══════════════════════════════════════════════════════

        [Header("── 봉인 완료 Loop 파티클 ──────────────────────")]

        /// <summary>
        /// 봉인 완료 시 Loop 파티클 프리팹.
        /// ExecuteSeal() 완료 시 부위 Transform 에 Instantiate.
        /// 부위가 이동해도 자동으로 따라가도록 자식으로 붙임.
        /// ForceRelease 시 SetActive(false) or Destroy.
        ///
        /// [용도]
        ///   봉인된 부위에 지속적인 시각 효과 제공.
        ///   "이 부위가 봉인됐다" 는 것을 플레이어에게 명확히 표시.
        ///
        /// [설정 주의]
        ///   ParticleSystem 의 Looping = true 필수.
        ///   Play On Awake = true 권장.
        /// </summary>
        [Tooltip("봉인 완료 Loop 파티클 프리팹. Looping=true 필수. 부위에 자식으로 붙음.")]
        public GameObject sealedParticlePrefab;

        // ══════════════════════════════════════════════════════
        // 유틸리티 메서드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인도 퍼센트에 따른 현재 부위 색상을 반환한다.
        /// colorBase → colorFull 선형 보간.
        ///
        /// [사용 예시]
        ///   Color c = _colorData.GetPartColor(gaugePercent);
        ///   _spriteRenderer.DOColor(c, _colorData.colorLerpDuration);
        /// </summary>
        /// <param name="gaugePercent">봉인도 퍼센트 (0~100).</param>
        /// <returns>현재 봉인도에 해당하는 색상.</returns>
        public Color GetPartColor(float gaugePercent)
        {
            float t = Mathf.Clamp01(gaugePercent / 100f);
            return Color.Lerp(colorBase, colorFull, t);
        }
    }
}