// ============================================================
// SealDataSO.cs  v1.0
// 범용 봉인도 수치 ScriptableObject
//
// [역할]
//   봉인 시스템의 모든 수치를 보관하는 범용 베이스 SO.
//   Warden, Dragon 등 어떤 보스에게도 재사용 가능.
//   BossDataSO 에 포함되어 사용.
//
// [포함 수치 카테고리]
//   1. 부위 봉인도  — 팔 등 부위 봉인도 최대치 / 저항 배율
//   2. 코어 봉인도  — 코어 봉인도 최대치 / 페이즈별 목표치 / 공격 누적량
//   3. 집행 수치    — 즉시 집행/일섬 접근 범위
//   4. 슬로우 수치  — 등급별 슬로우 배율 / 지속 시간
//   5. 너프 수치    — 한쪽 부위 봉인 시 패턴 너프 배율
//
// [범용 설계 원칙]
//   이 SO 는 Warden 전용 수치를 포함하지 않는다.
//   패턴 수치 / 이동 수치 / AI 수치는 각 보스 전용 SO 에서 관리.
//   어떤 보스든 SealDataSO 하나로 봉인 시스템 전체를 구동 가능.
//
// [생성 방법]
//   Assets 우클릭 → Create → SEAL/System/SealDataSO
//
// [연결]
//   BossDataSO._sealData 에 연결.
//   SealableComponent, SealExecutionRunner, SealStateManager 등
//   범용 봉인 컴포넌트가 BossDataSO 를 통해 이 SO 를 참조.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 범용 봉인도 수치 ScriptableObject. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [봉인도 내부/UI 변환 공식]
    ///   UI% = (현재 내부 봉인도 / 최대 내부 요구치) × 100
    ///   UI 는 항상 0~100% 로 표시.
    ///   내부 요구치는 부위마다 다르게 설정 가능.
    ///   → 겉으로는 같은 100% 이지만 실제 채우는 속도가 다름.
    ///
    /// [봉인 저항 배열]
    ///   같은 부위를 반복 봉인할수록 봉인도 누적 속도 감소.
    ///   ForceRelease 시 저항 카운트 유지 (딜페이즈 종료 루프 재시작용).
    ///   씬 완전 초기화 시에만 resetSealCount = true 사용.
    ///
    /// [너프 배율]
    ///   한쪽 부위 봉인 시 양팔 패턴 속도/추적 속도 너프.
    ///   BossPatternBase.IsHalfSealed() → 너프 여부 판단.
    ///   각 패턴이 이 배율을 읽어서 수치 분기.
    /// ────────────────────────────────────────────────────
    /// </summary>
    [CreateAssetMenu(
        menuName = "SEAL/System/SealDataSO",
        fileName = "SealDataSO")]
    public class SealDataSO : ScriptableObject
    {
        // ══════════════════════════════════════════════════════
        // 부위 봉인도
        // ══════════════════════════════════════════════════════

        [Header("── 부위 봉인도 ──────────────────────")]

        /// <summary>
        /// 부위 봉인도 내부 최대 요구치.
        /// 플레이어 공격으로 이 수치가 쌓이면 집행 가능 상태.
        /// UI 는 항상 0~100% 로 표시.
        ///
        /// [권장값] 200 (기본 공격 +10 기준 20번 공격)
        /// </summary>
        [Tooltip("부위 봉인도 내부 최대 요구치. UI는 0~100%로 표시. 권장: 200.")]
        [Min(1f)]
        public float partSealGaugeMax = 200f;

        /// <summary>
        /// 봉인 저항 배율 배열.
        /// 인덱스 0 = 1회차 봉인, 1 = 2회차, 2 = 3회차, 3 = 4회차 이상.
        /// 같은 부위를 반복 봉인할수록 봉인도 누적 속도 감소.
        ///
        /// [권장값]
        ///   1회: 1.0 (100%) / 2회: 0.8 (80%)
        ///   3회: 0.6 (60%)  / 4회+: 0.4 (40%)
        /// </summary>
        [Tooltip("봉인 저항 배율. [0]=1회차 ~ [N]=N+1회차. 범위 0~1. 클수록 누적 빠름.")]
        public float[] sealResistMultipliers = { 1.0f, 0.8f, 0.6f, 0.4f };

        // ══════════════════════════════════════════════════════
        // 코어 봉인도
        // ══════════════════════════════════════════════════════

        [Header("── 코어 봉인도 ──────────────────────")]

        /// <summary>
        /// 코어 봉인도 내부 최대치.
        /// 딜 페이즈에서 코어를 공격하여 누적.
        /// UI 는 항상 0~100% 로 표시.
        ///
        /// [권장값] 500
        /// </summary>
        [Tooltip("코어 봉인도 내부 최대치. UI는 0~100%로 표시. 권장: 500.")]
        [Min(1f)]
        public float coreSealGaugeMax = 500f;

        /// <summary>
        /// 1페이즈 딜 페이즈 종료 코어 봉인도 목표 (내부 수치).
        /// 이 수치 도달 시 1페이즈 딜 페이즈 종료 → 2페이즈 전환.
        ///
        /// [권장값] coreSealGaugeMax 의 50% (기본: 250)
        /// </summary>
        [Tooltip("1페이즈 딜 페이즈 종료 코어 봉인도 목표. 권장: coreSealGaugeMax 의 50%.")]
        [Min(1f)]
        public float phase1CoreSealTarget = 250f;

        /// <summary>
        /// 2페이즈 딜 페이즈 종료 코어 봉인도 목표 (내부 수치).
        /// 이 수치 도달 시 그로기 무한 고정 → 최종 봉인 가능 상태.
        /// coreSealGaugeMax 와 동일하게 설정 권장.
        /// </summary>
        [Tooltip("2페이즈 딜 페이즈 종료 코어 봉인도 목표. coreSealGaugeMax 와 동일 권장.")]
        [Min(1f)]
        public float phase2CoreSealTarget = 500f;

        /// <summary>
        /// 딜 페이즈 중 기본 공격 코어 봉인도 누적량 (내부).
        /// UI 기준 +5% 가 되도록 설정.
        ///
        /// [권장값] 25 (내부 500 기준 5%)
        /// </summary>
        [Tooltip("딜 페이즈 중 기본 공격 코어 봉인도 누적량. UI +5% 기준: 25.")]
        [Min(1f)]
        public float coreBasicAttackGain = 25f;

        /// <summary>
        /// 딜 페이즈 중 강공격 코어 봉인도 누적량 (내부).
        /// UI 기준 +15% 가 되도록 설정.
        ///
        /// [권장값] 75 (내부 500 기준 15%)
        /// </summary>
        [Tooltip("딜 페이즈 중 강공격 코어 봉인도 누적량. UI +15% 기준: 75.")]
        [Min(1f)]
        public float coreChargeAttackGain = 75f;

        // ══════════════════════════════════════════════════════
        // 봉인 집행 수치
        // ══════════════════════════════════════════════════════

        [Header("── 봉인 집행 수치 ──────────────────────")]

        /// <summary>
        /// 부위 봉인 집행 가능 접근 범위 반경 (units).
        /// 플레이어가 이 범위 이내에 있으면 즉시 집행/일섬 대상으로 선택 가능.
        ///
        /// [권장값] 1.5
        /// </summary>
        [Tooltip("부위 봉인 집행 접근 범위 반경. 권장: 1.5.")]
        [Min(0.1f)]
        public float sealExecutionRange = 1.5f;

        // ══════════════════════════════════════════════════════
        // 슬로우 수치
        // ══════════════════════════════════════════════════════

        [Header("── 슬로우 수치 ──────────────────────")]

        /// <summary>
        /// 부위 봉인 완료 시 짧은 슬로우 배율.
        /// 집행 완료 직후 타격감 강조용.
        /// 1.0 = 슬로우 없음 / 0.5 = 절반 속도.
        ///
        /// [권장값] 0.5
        /// </summary>
        [Tooltip("부위 봉인 완료 시 슬로우 배율. 1.0=없음 / 0.5=절반. 권장: 0.5.")]
        [Range(0.1f, 1f)]
        public float partSealSlowTimeScale = 0.5f;

        /// <summary>
        /// 부위 봉인 완료 슬로우 지속 시간 (초, RealTime 기준).
        ///
        /// [권장값] 0.25
        /// </summary>
        [Tooltip("부위 봉인 완료 슬로우 지속 시간 (초). 권장: 0.25.")]
        [Min(0f)]
        public float partSealSlowDuration = 0.25f;

        /// <summary>
        /// 최종 봉인 집행 중 슬로우 배율.
        /// 코어 봉인도 100% 후 최종 집행 순간에 강한 슬로우.
        /// 1.0 = 슬로우 없음.
        ///
        /// [권장값] 0.1 (매우 강한 슬로우)
        /// </summary>
        [Tooltip("최종 봉인 집행 중 슬로우 배율. 권장: 0.1.")]
        [Range(0.01f, 1f)]
        public float finalSealSlowTimeScale = 0.1f;

        // ══════════════════════════════════════════════════════
        // 그로기 / 딜 페이즈 타이머
        // ══════════════════════════════════════════════════════

        [Header("── 그로기 / 딜 페이즈 타이머 ──────────────────────")]

        /// <summary>
        /// 그로기 + 딜 페이즈 제한 시간 (초, RealTime 기준).
        /// 양팔 봉인 완료 → 그로기 + 딜 페이즈 자동 시작.
        /// 이 시간 내 코어 봉인도 100% 미달 시 그로기 해제 + ForceRelease + 루프.
        ///
        /// [권장값] 10~15초
        /// </summary>
        [Tooltip("그로기 + 딜 페이즈 제한 시간 (초). 초과 시 그로기 해제 + 루프. 권장: 10~15.")]
        [Min(1f)]
        public float groggyDuration = 10f;

        // ══════════════════════════════════════════════════════
        // 패턴 너프 수치 (한쪽 부위 봉인 시)
        // ══════════════════════════════════════════════════════

        [Header("── 패턴 너프 수치 (한쪽 부위 봉인) ──────────────────────")]

        /// <summary>
        /// 한쪽 부위 봉인 시 양팔 패턴 속도 너프 배율.
        /// 차징 속도 / 패턴 실행 속도에 적용.
        /// 1.0 = 너프 없음 / 0.6 = 40% 느려짐.
        ///
        /// [사용처]
        ///   BossPatternBase.IsHalfSealed() = true 시 패턴이 이 배율 적용.
        ///
        /// [권장값] 0.6
        /// </summary>
        [Tooltip("한쪽 봉인 시 패턴 속도 너프 배율. 1.0=없음 / 0.6=40% 느려짐. 권장: 0.6.")]
        [Range(0.1f, 1f)]
        public float halfSealPatternSpeedMultiplier = 0.6f;

        /// <summary>
        /// 한쪽 부위 봉인 시 추적형 패턴 추적 속도 너프 배율.
        /// 플레이어 추적 속도에 적용.
        /// 1.0 = 너프 없음 / 0.5 = 50% 느려짐.
        ///
        /// [권장값] 0.5
        /// </summary>
        [Tooltip("한쪽 봉인 시 추적 속도 너프 배율. 1.0=없음 / 0.5=50% 느려짐. 권장: 0.5.")]
        [Range(0.1f, 1f)]
        public float halfSealTrackingMultiplier = 0.5f;

        // ══════════════════════════════════════════════════════
        // 유틸리티 메서드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 저항 배율을 반환한다.
        ///
        /// [파라미터]
        ///   sealCount = 현재까지 봉인된 횟수 (0-based).
        ///   0 = 첫 번째 봉인 (저항 없음, 1.0 반환).
        ///
        /// [사용 예시]
        ///   float mult = _sealData.GetSealResistMultiplier(_sealCount);
        ///   float actual = rawGain * mult;
        /// </summary>
        /// <param name="sealCount">현재 봉인 횟수 (0-based).</param>
        /// <returns>봉인도 누적 배율 (0~1).</returns>
        public float GetSealResistMultiplier(int sealCount)
        {
            if (sealResistMultipliers == null || sealResistMultipliers.Length == 0)
                return 1.0f;

            int idx = Mathf.Clamp(sealCount, 0, sealResistMultipliers.Length - 1);
            return sealResistMultipliers[idx];
        }

        /// <summary>
        /// 부위 봉인도를 UI 퍼센트 (0~100) 로 변환한다.
        /// </summary>
        /// <param name="current">현재 내부 봉인도.</param>
        /// <returns>UI 퍼센트 (0~100).</returns>
        public float PartGaugeToUIPercent(float current)
        {
            if (partSealGaugeMax <= 0f) return 0f;
            return Mathf.Clamp01(current / partSealGaugeMax) * 100f;
        }

        /// <summary>
        /// 코어 봉인도를 UI 퍼센트 (0~100) 로 변환한다.
        /// </summary>
        /// <param name="current">현재 내부 코어 봉인도.</param>
        /// <returns>UI 퍼센트 (0~100).</returns>
        public float CoreGaugeToUIPercent(float current)
        {
            if (coreSealGaugeMax <= 0f) return 0f;
            return Mathf.Clamp01(current / coreSealGaugeMax) * 100f;
        }
    }
}