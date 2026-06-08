// ============================================================
// BossWardenDataSO.cs  v2.1
// Boss_Warden 전용 수치 ScriptableObject — PatternDataSO 분리
//
// [v2.0 — BossDataSO 상속 리팩토링]
//
//   [변경 내용]
//     BossDataSO 상속 추가
//       → SealDataSO / SealColorDataSO 는 BossDataSO 에서 관리
//       → 범용 봉인 컴포넌트는 BossDataSO 만 참조
//
//   [제거된 필드 — SealDataSO 로 이동]
//     armSealGaugeMax / sealResistMultipliers
//     coreSealGaugeMax / phase1CoreSealTarget / phase2CoreSealTarget
//     coreBasicAttackGain / coreChargeAttackGain
//     sealExecutionRange / sealExecutionHoldTime
//     coreUnlockRange / coreUnlockHoldTime
//     finalSealHoldTime / groggyDuration
//     partSealSlowTimeScale / partSealSlowDuration
//     dilPhaseSlowTimeScale / finalSealSlowTimeScale
//
//   [제거된 필드 — SealColorDataSO 로 이동]
//     colorArm0~100 / colorArmSealed
//     colorCoreActive / colorCoreDilPhase / colorCoreFinalSeal
//     colorSealRange / colorCoreRange
//     colorTransitionDuration / pulsePeriod / hitFlashDuration
//
//   [유지된 필드 — Warden 전용]
//     본체 AI 색상  (colorIdle ~ colorDead)
//     이동 수치     (moveSpeed, phase2MoveSpeed, patternRange, flipCooldown)
//     패턴 수치     BossWardenPatternDataSO 로 분리
//     페이즈 수치   (dilPhaseDuration, 충격파, recoveryVulnMultiplier)
//     예고 범위     (colorWarningRange)
//
//   [추가된 필드]
//     WardenMovementDataSO movementData  ← 미래 분할 준비 (현재 인라인 유지)
//
// [v1.2 이전 변경 이력]
//   v1.2: partSealSlowTimeScale / partSealSlowDuration 추가
//   v1.1: 색상 값 어두운 보라색 계열로 변경
//   v1.0: 최초 작성
//
// [생성 방법]
//   Assets 우클릭 → Create → SEAL/Boss/BossWardenDataSO
//
// [연결]
//   BossWardenCore._data 에 Inspector 연결.
//   범용 컴포넌트(SealableComponent 등) 는 BossDataSO 캐스팅 또는
//   BossWardenCore.Initialize() 시 BossDataSO 주입.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 전용 수치 ScriptableObject — PatternDataSO 분리. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [상속 구조]
    ///   BossDataSO (범용 봉인 수치 — SealDataSO + SealColorDataSO)
    ///     ↑
    ///   BossWardenDataSO (Warden 전용 수치)
    ///
    /// [Inspector 연결 체크리스트]
    ///   □ sealData     → SealDataSO 에셋 (BossDataSO 상속 필드)
    ///   □ colorData    → SealColorDataSO 에셋 (BossDataSO 상속 필드)
    ///   □ 이하 Warden 전용 수치는 이 파일에서 인라인 설정
    ///
    /// [참조 방식]
    ///   범용 컴포넌트  → _bossData.SealData.xxx
    ///   Warden 컴포넌트 → _data.moveSpeed (직접 접근)
    /// ────────────────────────────────────────────────────
    /// </summary>
    [CreateAssetMenu(
        menuName = "SEAL/Boss/BossWardenDataSOO",
        fileName = "BossWardenDataSOO")]
    public class BossWardenDataSO : BossDataSO
    {
        // ══════════════════════════════════════════════════════
        // 본체 AI 상태 색상 (Warden 전용)
        // ══════════════════════════════════════════════════════

        [Header("── 본체 AI 상태 색상 ──────────────────────")]

        /// <summary>Idle / Chase 상태 색상. 회색 계열.</summary>
        [Tooltip("Idle/Chase 색상. 권장: 회색 #888888.")]
        public Color colorIdle = new Color(0.533f, 0.533f, 0.533f);

        /// <summary>Warning (패턴 예고) 상태 색상. 주황 계열.</summary>
        [Tooltip("Warning 색상. 권장: 주황 #FF8800.")]
        public Color colorWarning = new Color(1.0f, 0.533f, 0.0f);

        /// <summary>Active (패턴 시전) 상태 색상. 흰색.</summary>
        [Tooltip("Active 색상. 권장: 흰색 #FFFFFF.")]
        public Color colorActive = Color.white;

        /// <summary>Recovery (후딜 취약) 상태 색상. 붉은색.</summary>
        [Tooltip("Recovery 색상. 권장: 붉은색 #CC2200.")]
        public Color colorRecovery = new Color(0.8f, 0.133f, 0.0f);

        /// <summary>Groggy (그로기) 상태 색상. 노란색.</summary>
        [Tooltip("Groggy 색상. 권장: 노란색 #FFEE00.")]
        public Color colorGroggy = new Color(1.0f, 0.933f, 0.0f);

        /// <summary>DilPhase (딜 페이즈) 상태 색상. 밝은 주황.</summary>
        [Tooltip("DilPhase 색상. 권장: 밝은 주황 #FF6600.")]
        public Color colorDilPhase = new Color(1.0f, 0.4f, 0.0f);

        /// <summary>2페이즈 전환 색상. 진한 붉은색.</summary>
        [Tooltip("2페이즈 전환 색상. 권장: 진한 붉은색 #990000.")]
        public Color colorPhase2 = new Color(0.6f, 0.0f, 0.0f);

        /// <summary>Dead 상태 색상. 거의 검정.</summary>
        [Tooltip("Dead 색상. 권장: 검정 #111111.")]
        public Color colorDead = new Color(0.067f, 0.067f, 0.067f);

        // ══════════════════════════════════════════════════════
        // 예고 범위 색상 (Warden 전용 — 패턴 예고 디스크)
        // ══════════════════════════════════════════════════════

        [Header("── 예고 범위 색상 ──────────────────────")]

        /// <summary>
        /// 공격 예고 범위 디스크 색상.
        /// Alpha 0.4 반투명 권장.
        /// BossWardenAttackRange 에서 사용.
        /// </summary>
        [Tooltip("공격 예고 범위 색상. Alpha 0.4 권장.")]
        public Color colorWarningRange = new Color(1.0f, 0.0f, 0.0f, 0.4f);

        // ══════════════════════════════════════════════════════
        // 공격 패턴 DataSO (Warden 전용 — Step 1 분리)
        // ══════════════════════════════════════════════════════

        [Header("── 공격 패턴 DataSO ──────────────────────")]

        /// <summary>
        /// Boss_Warden 공격 패턴 전용 DataSO.
        /// Charge / Slam / Sweep / GuardBreak / RageCharge 수치를 이 SO에서 관리한다.
        ///
        /// [Step 1 호환성]
        ///   기존 코드가 _data.chargeSpeed 처럼 접근하던 부분은
        ///   아래 호환 프로퍼티를 통해 PatternDataSO 값을 반환한다.
        /// </summary>
        [Tooltip("Boss_Warden 공격 패턴 전용 DataSO. 패턴 수치는 이 에셋에서 관리.")]
        [SerializeField] private BossWardenPatternDataSO _patternData;

        public BossWardenPatternDataSO PatternData => _patternData;

        // ══════════════════════════════════════════════════════
        // 이동 수치 (Warden 전용)
        // ══════════════════════════════════════════════════════

        [Header("── 이동 수치 ──────────────────────")]

        /// <summary>
        /// 1페이즈 이동 속도 (units/s).
        /// BossWardenAI 추적 이동 속도.
        ///
        /// [권장값] 3.5
        /// </summary>
        [Tooltip("1페이즈 이동 속도. 권장: 3.5.")]
        [Min(0f)]
        public float moveSpeed = 3.5f;

        /// <summary>
        /// 2페이즈 이동 속도 (units/s).
        /// 2페이즈 전환 후 적용.
        ///
        /// [권장값] 4.5
        /// </summary>
        [Tooltip("2페이즈 이동 속도. 권장: 4.5.")]
        [Min(0f)]
        public float phase2MoveSpeed = 4.5f;

        /// <summary>
        /// 패턴 감지 범위 (units).
        /// 플레이어가 이 범위 이내에 있어야 패턴 시작.
        ///
        /// [권장값] 10.0
        /// </summary>
        [Tooltip("패턴 감지 범위 (units). 권장: 10.0.")]
        [Min(0f)]
        public float patternRange = 10f;

        /// <summary>
        /// 좌우 방향 전환 쿨타임 (초).
        /// 너무 빠른 방향 전환 방지.
        ///
        /// [권장값] 0.5
        /// </summary>
        [Tooltip("방향 전환 쿨타임 (초). 권장: 0.5.")]
        [Min(0f)]
        public float flipCooldown = 0.5f;

        // ══════════════════════════════════════════════════════
        // 딜 페이즈 타이머 (Warden 전용)
        // ══════════════════════════════════════════════════════

        [Header("── 딜 페이즈 타이머 ──────────────────────")]

        /// <summary>
        /// 딜 페이즈 제한 시간 (초, RealTime 기준).
        /// 이 시간 내 코어 봉인도 목표 미달 시 딜 페이즈 강제 종료.
        /// groggyDuration 은 SealDataSO 에서 관리.
        ///
        /// [권장값] 10.0
        /// </summary>
        [Tooltip("딜 페이즈 제한 시간 (초). 권장: 10.0.")]
        [Min(1f)]
        public float dilPhaseDuration = 10f;

        // ══════════════════════════════════════════════════════
        // 충격파 수치 (Warden 전용)
        // ══════════════════════════════════════════════════════

        [Header("── 충격파 수치 ──────────────────────")]

        /// <summary>
        /// 충격파 감지 반경 (units).
        /// 이 범위 내 플레이어에게 넉백 적용.
        ///
        /// [권장값] 30.0
        /// </summary>
        [Tooltip("충격파 감지 반경. 권장: 30.0.")]
        [Min(0f)]
        public float shockwaveRadius = 30f;

        /// <summary>
        /// 충격파 넉백 강도 (units/s).
        ///
        /// [권장값] 12.0
        /// </summary>
        [Tooltip("충격파 넉백 강도. 권장: 12.0.")]
        [Min(0f)]
        public float shockwaveKnockbackForce = 12f;

        /// <summary>
        /// 충격파 넉백 지속 시간 (초, RealTime 기준).
        ///
        /// [권장값] 0.2
        /// </summary>
        [Tooltip("충격파 넉백 지속 시간 (초). 권장: 0.2.")]
        [Min(0f)]
        public float shockwaveKnockbackDuration = 0.2f;

        // ══════════════════════════════════════════════════════
        // Recovery 취약 배율 (Warden 전용)
        // ══════════════════════════════════════════════════════

        [Header("── Recovery 취약 배율 ──────────────────────")]

        /// <summary>
        /// Recovery(후딜) 구간 봉인도 누적 배율.
        /// 패턴 후딜 중 공격 시 봉인도가 이 배율만큼 추가 누적.
        ///
        /// [권장값] 1.5 (50% 추가)
        /// </summary>
        [Tooltip("Recovery 구간 봉인도 추가 누적 배율. 권장: 1.5.")]
        [Min(1f)]
        public float recoveryVulnMultiplier = 1.5f;

        // ══════════════════════════════════════════════════════
        // 패턴 수치 호환 프로퍼티 — 기존 코드 원형 유지용
        // ══════════════════════════════════════════════════════

        private BossWardenPatternDataSO.ChargeSettings ChargePattern
            => _patternData != null ? _patternData.Charge : BossWardenPatternDataSO.DefaultCharge;

        private BossWardenPatternDataSO.SlamSettings SlamPattern
            => _patternData != null ? _patternData.Slam : BossWardenPatternDataSO.DefaultSlam;

        private BossWardenPatternDataSO.SweepSettings SweepPattern
            => _patternData != null ? _patternData.Sweep : BossWardenPatternDataSO.DefaultSweep;

        private BossWardenPatternDataSO.GuardBreakSettings GuardBreakPattern
            => _patternData != null ? _patternData.GuardBreak : BossWardenPatternDataSO.DefaultGuardBreak;

        private BossWardenPatternDataSO.RageChargeSettings RageChargePattern
            => _patternData != null ? _patternData.RageCharge : BossWardenPatternDataSO.DefaultRageCharge;

        public float chargeSpeed => ChargePattern.speed;
        public float phase2ChargeSpeed => ChargePattern.phase2Speed;
        public float chargeDistance => ChargePattern.distance;
        public Vector2 chargeHitboxSize => ChargePattern.hitboxSize;
        public Vector2 chargeWarningSize => ChargePattern.warningSize;

        public float slamHitRadius => SlamPattern.hitRadius;
        public float slamWarningRadius => SlamPattern.warningRadius;

        public float sweepHitRadius => SweepPattern.hitRadius;
        public float sweepWarningRadius => SweepPattern.warningRadius;
        public float sweepRotateSpeed => SweepPattern.rotateSpeed;
        public float phase2SweepRotateSpeed => SweepPattern.phase2RotateSpeed;

        public Vector2 guardBreakHitboxSize => GuardBreakPattern.hitboxSize;
        public Vector2 guardBreakWarningSize => GuardBreakPattern.warningSize;
        public float guardBreakGuardDuration => GuardBreakPattern.guardDuration;

        public float rageChargeSpeed => RageChargePattern.speed;
        public int rageChargeCount => RageChargePattern.count;
        public float rageChargeInterval => RageChargePattern.interval;

        // ══════════════════════════════════════════════════════
        // 유틸리티 메서드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 현재 페이즈에 맞는 이동 속도를 반환한다.
        /// </summary>
        /// <param name="phase">현재 페이즈 (1 or 2).</param>
        /// <returns>이동 속도 (units/s).</returns>
        public float GetMoveSpeed(int phase)
            => phase >= 2 ? phase2MoveSpeed : moveSpeed;

        /// <summary>
        /// 현재 페이즈에 맞는 Charge 돌진 속도를 반환한다.
        /// </summary>
        /// <param name="phase">현재 페이즈 (1 or 2).</param>
        /// <returns>Charge 속도 (units/s).</returns>
        public float GetChargeSpeed(int phase)
            => ChargePattern.GetSpeed(phase);

        /// <summary>
        /// 현재 페이즈에 맞는 Sweep 회전 속도를 반환한다.
        /// </summary>
        /// <param name="phase">현재 페이즈 (1 or 2).</param>
        /// <returns>Sweep 회전 속도 (°/s).</returns>
        public float GetSweepRotateSpeed(int phase)
            => SweepPattern.GetRotateSpeed(phase);

        /// <summary>
        /// Warden 전용 필수 SO 연결 여부를 검사한다.
        /// BossDataSO.IsValid() + PatternDataSO 연결까지 확인한다.
        /// </summary>
        public new bool IsValid()
        {
            if (!base.IsValid())
                return false;

            if (_patternData == null)
            {
                Debug.LogError($"[BossWardenDataSO] {name} — BossWardenPatternDataSO 미연결.");
                return false;
            }

            return _patternData.IsValid();
        }
    }
}