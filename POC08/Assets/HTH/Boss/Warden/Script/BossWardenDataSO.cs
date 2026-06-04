// ============================================================
// BossWardenDataSO.cs  v1.1
// Boss_Warden 전용 수치 ScriptableObject
//
// [v1.1 변경 — 색상 기획 변경]
//   부위 봉인도 단계 색상: 기존 노랑→주황→빨강→파랑 → 어두운 보라색 계열
//   코어 색상: 기존 노랑/흰/청백 → 노랑(그로기) / 주황(딜 시작) / 빨강(최종)
//
//   [부위 색상 변경 이유]
//     파랑 계열이 '봉인'이라는 키워드와 맞지 않음.
//     어두운 보라색은 신비/봉인 분위기에 더 적합.
//     Stage 0~4 까지 자연스럽게 밝아지는 보라색 그라데이션.
//
//   [코어 색상 변경 이유]
//     흰색 → 시각적으로 특색 없음.
//     청백 → 부위 봉인 완료(파랑)와 혼동 가능.
//     노랑→주황→빨강 보간: 코어가 점점 위험해지는 느낌 직관적.
//     BossWardenCoreSealGauge.UpdateCoreColor() 에서
//     colorCoreDilPhase → colorCoreFinalSeal 을 UIPercent 기준으로 Lerp.
//
// [v1.0 기준]
//   POC07 TestBossDataSO.cs 구조 기반으로 탑뷰 봉인 시스템 전면 재설계.
//   HP 개념 제거. S키 봉인 집행 / 코어 해제 / 최종 봉인 수치 추가.
//   봉인 저항 배율 배열. 2페이즈 강화 수치. 예고 범위 수치.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 전용 수치 ScriptableObject. (v1.1)
    /// </summary>
    [CreateAssetMenu(
        menuName = "SEAL/Boss/BossWardenDataSO",
        fileName = "BossWardenDataSO")]
    public class BossWardenDataSO : ScriptableObject
    {
        // ══════════════════════════════════════════════════════
        // 부위 봉인도
        // ══════════════════════════════════════════════════════

        [Header("── 부위 봉인도 ──────────────────────")]

        [Tooltip("팔 부위 봉인도 내부 최대 요구치. 권장: 200.")]
        [Min(1f)]
        public float armSealGaugeMax = 200f;

        [Tooltip("봉인 저항 배율. [0]=1회차, [1]=2회차, [2]=3회차, [3]=4회차+.")]
        public float[] sealResistMultipliers = { 1.0f, 0.8f, 0.6f, 0.4f };

        // ══════════════════════════════════════════════════════
        // 코어 봉인도
        // ══════════════════════════════════════════════════════

        [Header("── 코어 봉인도 ──────────────────────")]

        [Tooltip("코어 봉인도 내부 최대치. 권장: 500.")]
        [Min(1f)]
        public float coreSealGaugeMax = 500f;

        [Tooltip("1페이즈 종료 코어 봉인도 목표. 권장: 250.")]
        [Min(1f)]
        public float phase1CoreSealTarget = 250f;

        [Tooltip("2페이즈 종료 코어 봉인도 목표. coreSealGaugeMax 와 동일 권장.")]
        [Min(1f)]
        public float phase2CoreSealTarget = 500f;

        [Tooltip("딜 페이즈 기본 공격 코어 봉인도 누적량. 기본값: 25.")]
        [Min(1f)]
        public float coreBasicAttackGain = 25f;

        [Tooltip("딜 페이즈 강공격 코어 봉인도 누적량. 기본값: 75.")]
        [Min(1f)]
        public float coreChargeAttackGain = 75f;

        // ══════════════════════════════════════════════════════
        // S키 봉인 집행 / 코어 해제 / 최종 봉인
        // ══════════════════════════════════════════════════════

        [Header("── S키 봉인 집행 ──────────────────────")]

        [Tooltip("봉인 집행 가능 접근 범위 반경 (units). 권장: 1.5.")]
        [Min(0.1f)]
        public float sealExecutionRange = 1.5f;

        [Tooltip("봉인 집행 S키 홀드 시간 (초). 권장: 1.5.")]
        [Min(0.1f)]
        public float sealExecutionHoldTime = 1.5f;

        [Header("── 코어 해제 ──────────────────────")]

        [Tooltip("코어 해제 접근 범위 반경 (units). 권장: 1.5.")]
        [Min(0.1f)]
        public float coreUnlockRange = 1.5f;

        [Tooltip("코어 해제 S키 홀드 시간 (초). 권장: 3.0.")]
        [Min(0.1f)]
        public float coreUnlockHoldTime = 3.0f;

        [Tooltip("코어 해제 슬로우 TimeScale. 권장: 0.3.")]
        [Range(0.01f, 1f)]
        public float dilPhaseSlowTimeScale = 0.3f;

        [Header("── 최종 봉인 ──────────────────────")]

        [Tooltip("최종 봉인 S키 홀드 시간 (초). 권장: 2.0.")]
        [Min(0.1f)]
        public float finalSealHoldTime = 2.0f;

        [Tooltip("최종 봉인 슬로우 TimeScale. 권장: 0.1.")]
        [Range(0.01f, 1f)]
        public float finalSealSlowTimeScale = 0.1f;

        // ══════════════════════════════════════════════════════
        // 그로기 / 딜 페이즈
        // ══════════════════════════════════════════════════════

        [Header("── 그로기 / 딜 페이즈 ──────────────────────")]

        [Tooltip("그로기 지속 시간 (초). 권장: 5.0.")]
        [Min(0.1f)]
        public float groggyDuration = 5.0f;

        [Tooltip("딜 페이즈 최대 지속 시간 (초). 권장: 10.0.")]
        [Min(0.1f)]
        public float dilPhaseDuration = 10.0f;

        // ══════════════════════════════════════════════════════
        // 충격파
        // ══════════════════════════════════════════════════════

        [Header("── 충격파 ──────────────────────")]

        [Tooltip("충격파 감지 반경 (units). 권장: 30.")]
        [Min(0.1f)]
        public float shockwaveRadius = 30f;

        [Tooltip("충격파 넉백 강도. 권장: 12.")]
        [Min(0f)]
        public float shockwaveKnockbackForce = 12f;

        [Tooltip("충격파 넉백 지속 시간 (초). 권장: 0.2.")]
        [Min(0f)]
        public float shockwaveKnockbackDuration = 0.2f;

        // ══════════════════════════════════════════════════════
        // AI 이동
        // ══════════════════════════════════════════════════════

        [Header("── AI 이동 ──────────────────────")]

        [Tooltip("1페이즈 이동 속도. 권장: 3.5.")]
        [Min(0f)]
        public float moveSpeed = 3.5f;

        [Tooltip("2페이즈 이동 속도. 권장: 4.5.")]
        [Min(0f)]
        public float phase2MoveSpeed = 4.5f;

        [Tooltip("패턴 발동 감지 범위 (units). 권장: 10.")]
        [Min(0f)]
        public float patternRange = 10f;

        [Tooltip("좌우 반전 쿨타임 (초). 권장: 0.5.")]
        [Min(0f)]
        public float flipCooldown = 0.5f;

        // ══════════════════════════════════════════════════════
        // 패턴 수치
        // ══════════════════════════════════════════════════════

        [Header("── Charge 패턴 ──────────────────────")]
        public float chargeSpeed = 12f;
        public float phase2ChargeSpeed = 16f;
        public float chargeDistance = 15f;
        public Vector2 chargeHitboxSize = new Vector2(1f, 8f);
        public Vector2 chargeWarningSize = new Vector2(1.6f, 9.6f);

        [Header("── Slam 패턴 ──────────────────────")]
        public float slamHitRadius = 2.5f;
        public float slamWarningRadius = 3f;

        [Header("── Sweep 패턴 ──────────────────────")]
        public float sweepHitRadius = 5f;
        public float sweepWarningRadius = 10f;
        public float sweepRotateSpeed = 180f;
        public float phase2SweepRotateSpeed = 270f;

        [Header("── GuardBreak 패턴 ──────────────────────")]
        public Vector2 guardBreakHitboxSize = new Vector2(1f, 0.8f);
        public Vector2 guardBreakWarningSize = new Vector2(1.5f, 1f);
        public float guardBreakGuardDuration = 0.8f;

        [Header("── RageCharge 패턴 (2페이즈 전용) ──────────────────────")]
        public float rageChargeSpeed = 18f;
        public int rageChargeCount = 3;
        public float rageChargeInterval = 0.2f;

        // ══════════════════════════════════════════════════════
        // Recovery 취약 구간
        // ══════════════════════════════════════════════════════

        [Header("── Recovery 취약 구간 ──────────────────────")]

        [Tooltip("Recovery 구간 봉인도 누적 배율. 권장: 1.5.")]
        [Min(1f)]
        public float recoveryVulnMultiplier = 1.5f;

        // ══════════════════════════════════════════════════════
        // 색상 — 본체
        // ══════════════════════════════════════════════════════

        [Header("── 본체 색상 ──────────────────────")]

        public Color colorIdle = new Color(0.533f, 0.533f, 0.533f); // #888888
        public Color colorWarning = new Color(1.0f, 0.533f, 0.0f);     // #FF8800
        public Color colorActive = Color.white;                         // #FFFFFF
        public Color colorRecovery = new Color(0.8f, 0.133f, 0.0f);     // #CC2200
        public Color colorGroggy = new Color(1.0f, 0.933f, 0.0f);     // #FFEE00
        public Color colorDilPhase = new Color(1.0f, 0.4f, 0.0f);       // #FF6600
        public Color colorPhase2 = new Color(0.6f, 0.0f, 0.0f);       // #990000
        public Color colorDead = new Color(0.067f, 0.067f, 0.067f); // #111111

        // ══════════════════════════════════════════════════════
        // 색상 — 부위 봉인도 (v1.1: 어두운 보라색 계열)
        // ══════════════════════════════════════════════════════

        [Header("── 부위 색상 (봉인도 단계별) ──────────────────────")]

        /// <summary>
        /// 부위 봉인도 0% 색상.
        /// v1.1: 매우 어두운 보라 (#1A0A2E)
        /// </summary>
        [Tooltip("부위 봉인도 0% 색상. v1.1: 매우 어두운 보라 #1A0A2E.")]
        public Color colorArm0 = new Color(0.102f, 0.039f, 0.180f); // #1A0A2E

        /// <summary>
        /// 부위 봉인도 25% 색상.
        /// v1.1: 어두운 보라 (#3D1F6E)
        /// </summary>
        [Tooltip("부위 봉인도 25% 색상. v1.1: 어두운 보라 #3D1F6E.")]
        public Color colorArm25 = new Color(0.239f, 0.122f, 0.431f); // #3D1F6E

        /// <summary>
        /// 부위 봉인도 50% 색상.
        /// v1.1: 중간 보라 (#6B35B8)
        /// </summary>
        [Tooltip("부위 봉인도 50% 색상. v1.1: 중간 보라 #6B35B8.")]
        public Color colorArm50 = new Color(0.420f, 0.208f, 0.722f); // #6B35B8

        /// <summary>
        /// 부위 봉인도 75% 색상.
        /// v1.1: 밝은 보라 (#9B59D0)
        /// </summary>
        [Tooltip("부위 봉인도 75% 색상. v1.1: 밝은 보라 #9B59D0.")]
        public Color colorArm75 = new Color(0.608f, 0.349f, 0.816f); // #9B59D0

        /// <summary>
        /// 부위 봉인도 100% (봉인 가능) 색상. 빠른 Pulse.
        /// v1.1: 연보라 (#C77DFF)
        /// </summary>
        [Tooltip("부위 봉인도 100% 색상. 연보라 빠른 Pulse. #C77DFF.")]
        public Color colorArm100 = new Color(0.780f, 0.490f, 1.000f); // #C77DFF

        /// <summary>
        /// 부위 봉인 완료 고정 색상.
        /// v1.1: 진한 보라 (#7B2FBE)
        /// </summary>
        [Tooltip("부위 봉인 완료 고정 색상. 진한 보라 #7B2FBE.")]
        public Color colorArmSealed = new Color(0.482f, 0.184f, 0.745f); // #7B2FBE

        // ══════════════════════════════════════════════════════
        // 색상 — 코어 (v1.1: 노란색 → 붉은색)
        // ══════════════════════════════════════════════════════

        [Header("── 코어 색상 (노란색 → 붉은색) ──────────────────────")]

        /// <summary>
        /// 코어 활성 (그로기 진입 시) 색상. 밝은 노랑 Pulse.
        /// v1.1: #FFE600
        /// </summary>
        [Tooltip("코어 활성 색상. 그로기 진입 시 밝은 노랑 Pulse. #FFE600.")]
        public Color colorCoreActive = new Color(1.000f, 0.902f, 0.000f); // #FFE600

        /// <summary>
        /// 딜 페이즈 시작 코어 색상 (봉인도 0% 기준).
        /// BossWardenCoreSealGauge.UpdateCoreColor() 에서
        /// colorCoreFinalSeal 방향으로 UIPercent 기준 Lerp 보간.
        /// v1.1: #FF8C00 주황
        /// </summary>
        [Tooltip("딜 페이즈 시작 코어 색상 (0%). 주황 #FF8C00. 봉인도 증가 시 빨강으로 보간.")]
        public Color colorCoreDilPhase = new Color(1.000f, 0.549f, 0.000f); // #FF8C00

        /// <summary>
        /// 코어 봉인도 100% (최종 봉인 가능) 색상. 강한 빨강 Pulse.
        /// v1.1: #FF0000
        /// </summary>
        [Tooltip("최종 봉인 가능 코어 색상. 선명한 빨강 강한 Pulse. #FF0000.")]
        public Color colorCoreFinalSeal = new Color(1.000f, 0.000f, 0.000f); // #FF0000

        // ══════════════════════════════════════════════════════
        // 색상 — 예고 범위
        // ══════════════════════════════════════════════════════

        [Header("── 예고 범위 색상 ──────────────────────")]

        [Tooltip("공격 예고 범위 색상. Alpha 0.4 권장.")]
        public Color colorWarningRange = new Color(1.0f, 0.0f, 0.0f, 0.4f);

        [Tooltip("봉인 집행 가능 범위 색상.")]
        public Color colorSealRange = new Color(0.0f, 0.533f, 1.0f); // #0088FF

        [Tooltip("코어 해제 가능 범위 색상.")]
        public Color colorCoreRange = new Color(1.0f, 0.933f, 0.0f); // #FFEE00

        // ══════════════════════════════════════════════════════
        // DOTween 타이밍 수치
        // ══════════════════════════════════════════════════════

        [Header("── DOTween 타이밍 ──────────────────────")]

        [Tooltip("색상 전환 DOColor 기본 Duration. 권장: 0.1.")]
        [Min(0.01f)]
        public float colorTransitionDuration = 0.1f;

        [Tooltip("Pulse 한 주기 (초). 권장: 0.4.")]
        [Min(0.05f)]
        public float pulsePeriod = 0.4f;

        [Tooltip("히트 스탑 점멸 지속 시간 (초). 권장: 0.07.")]
        [Min(0.01f)]
        public float hitFlashDuration = 0.07f;

        // ══════════════════════════════════════════════════════
        // 유틸리티 메서드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 저항 배율 반환.
        /// sealCount = 0-based (0 = 첫 번째 봉인).
        /// </summary>
        public float GetSealResistMultiplier(int sealCount)
        {
            if (sealResistMultipliers == null || sealResistMultipliers.Length == 0)
                return 1.0f;
            int idx = Mathf.Clamp(sealCount, 0, sealResistMultipliers.Length - 1);
            return sealResistMultipliers[idx];
        }

        /// <summary>부위 봉인도 → UI 퍼센트 변환.</summary>
        public float ArmGaugeToUIPercent(float current)
        {
            if (armSealGaugeMax <= 0f) return 0f;
            return Mathf.Clamp01(current / armSealGaugeMax) * 100f;
        }

        /// <summary>코어 봉인도 → UI 퍼센트 변환.</summary>
        public float CoreGaugeToUIPercent(float current)
        {
            if (coreSealGaugeMax <= 0f) return 0f;
            return Mathf.Clamp01(current / coreSealGaugeMax) * 100f;
        }
    }
}