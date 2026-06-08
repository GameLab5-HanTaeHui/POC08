// ============================================================
// BossWardenPatternDataSO.cs  v1.0
// Boss_Warden 공격 패턴 전용 ScriptableObject
//
// [역할]
//   Boss_Warden의 모든 공격 패턴 수치를 한곳에서 관리한다.
//   BossWardenDataSO 내부에 흩어져 있던 Charge / Slam / Sweep /
//   GuardBreak / RageCharge 수치를 분리한다.
//
// [구조]
//   1. Common      — 모든 패턴의 공통 생애주기 기본값
//   2. Charge      — 돌진 패턴 수치
//   3. Slam        — 내려치기 패턴 수치
//   4. Sweep       — 회전 스윕 패턴 수치
//   5. GuardBreak  — 가드브레이크 패턴 수치
//   6. RageCharge  — 3연 돌진 패턴 수치
//
// [원칙]
//   이 SO는 수치만 담당한다.
//   Transform / Rigidbody2D / LineRenderer / AI / VFX 참조는 담당하지 않는다.
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 공격 패턴 전용 DataSO.
    /// </summary>
    [CreateAssetMenu(
        menuName = "SEAL/Boss/Warden/BossWardenPatternDataSO",
        fileName = "BossWardenPatternDataSO")]
    public class BossWardenPatternDataSO : ScriptableObject
    {
        // ══════════════════════════════════════════════════════
        // Static fallback — PatternDataSO 미연결 시 기존 코드 보호용
        // ══════════════════════════════════════════════════════

        private static readonly CommonSettings s_DefaultCommon = new CommonSettings();
        private static readonly ChargeSettings s_DefaultCharge = new ChargeSettings();
        private static readonly SlamSettings s_DefaultSlam = new SlamSettings();
        private static readonly SweepSettings s_DefaultSweep = new SweepSettings();
        private static readonly GuardBreakSettings s_DefaultGuardBreak = new GuardBreakSettings();
        private static readonly RageChargeSettings s_DefaultRageCharge = new RageChargeSettings();

        public static CommonSettings DefaultCommon => s_DefaultCommon;
        public static ChargeSettings DefaultCharge => s_DefaultCharge;
        public static SlamSettings DefaultSlam => s_DefaultSlam;
        public static SweepSettings DefaultSweep => s_DefaultSweep;
        public static GuardBreakSettings DefaultGuardBreak => s_DefaultGuardBreak;
        public static RageChargeSettings DefaultRageCharge => s_DefaultRageCharge;

        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 공통 패턴 설정 ──────────────────────")]
        public CommonSettings common = new CommonSettings();

        [Header("── 패턴: Charge (돌진) ──────────────────────")]
        public ChargeSettings charge = new ChargeSettings();

        [Header("── 패턴: Slam (내려치기) ──────────────────────")]
        public SlamSettings slam = new SlamSettings();

        [Header("── 패턴: Sweep (회전 스윕) ──────────────────────")]
        public SweepSettings sweep = new SweepSettings();

        [Header("── 패턴: GuardBreak (가드브레이크) ──────────────────────")]
        public GuardBreakSettings guardBreak = new GuardBreakSettings();

        [Header("── 패턴: RageCharge (3연 돌진) ──────────────────────")]
        public RageChargeSettings rageCharge = new RageChargeSettings();

        // ══════════════════════════════════════════════════════
        // Safe Accessors
        // ══════════════════════════════════════════════════════

        public CommonSettings Common => common ?? DefaultCommon;
        public ChargeSettings Charge => charge ?? DefaultCharge;
        public SlamSettings Slam => slam ?? DefaultSlam;
        public SweepSettings Sweep => sweep ?? DefaultSweep;
        public GuardBreakSettings GuardBreak => guardBreak ?? DefaultGuardBreak;
        public RageChargeSettings RageCharge => rageCharge ?? DefaultRageCharge;

        public bool IsValid()
        {
            if (common == null)
            {
                Debug.LogError($"[BossWardenPatternDataSO] {name} — Common 설정 누락.");
                return false;
            }

            if (charge == null || slam == null || sweep == null || guardBreak == null || rageCharge == null)
            {
                Debug.LogError($"[BossWardenPatternDataSO] {name} — 일부 패턴 설정 누락.");
                return false;
            }

            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            common?.Clamp();
            charge?.Clamp();
            slam?.Clamp();
            sweep?.Clamp();
            guardBreak?.Clamp();
            rageCharge?.Clamp();
        }
#endif

        // ══════════════════════════════════════════════════════
        // Common
        // ══════════════════════════════════════════════════════

        [Serializable]
        public class CommonSettings
        {
            [Tooltip("기본 패턴 쿨타임. 개별 패턴이 Common Timing을 사용할 때 적용.")]
            [Min(0f)] public float defaultCooldown = 5.0f;

            [Tooltip("기본 Warning 시간. 개별 패턴이 Common Timing을 사용할 때 적용.")]
            [Min(0f)] public float defaultWarningDuration = 1.0f;

            [Tooltip("기본 Recovery 시간. 개별 패턴이 Common Timing을 사용할 때 적용.")]
            [Min(0f)] public float defaultRecoveryDuration = 0.8f;

            [Tooltip("2페이즈에서 공통적으로 속도를 보정하고 싶을 때 사용하는 배율. 현재는 선택 사용.")]
            [Min(0f)] public float phase2GlobalSpeedMultiplier = 1.0f;

            [Tooltip("Warning 범위 점멸 주기. AttackRangeView로 이전할 수 있는 공통 VFX 수치.")]
            [Min(0.05f)] public float warningPulsePeriod = 0.35f;

            [Tooltip("Warning 점멸 최소 Alpha.")]
            [Range(0f, 0.5f)] public float warningPulseMinAlpha = 0.05f;

            [Tooltip("Warning 점멸 최대 Alpha.")]
            [Range(0.3f, 1f)] public float warningPulseMaxAlpha = 0.55f;

            public void Clamp()
            {
                defaultCooldown = Mathf.Max(0f, defaultCooldown);
                defaultWarningDuration = Mathf.Max(0f, defaultWarningDuration);
                defaultRecoveryDuration = Mathf.Max(0f, defaultRecoveryDuration);
                phase2GlobalSpeedMultiplier = Mathf.Max(0f, phase2GlobalSpeedMultiplier);
                warningPulsePeriod = Mathf.Max(0.05f, warningPulsePeriod);
            }
        }

        [Serializable]
        public class LifecycleSettings
        {
            [Tooltip("true면 Common의 기본 Cooldown/Warning/Recovery를 사용한다.")]
            public bool useCommonTiming = true;

            [Tooltip("개별 패턴 쿨타임. useCommonTiming=false일 때 사용.")]
            [Min(0f)] public float cooldown = 5.0f;

            [Tooltip("개별 Warning 시간. useCommonTiming=false일 때 사용.")]
            [Min(0f)] public float warningDuration = 1.0f;

            [Tooltip("개별 Recovery 시간. useCommonTiming=false일 때 사용.")]
            [Min(0f)] public float recoveryDuration = 0.8f;

            [Tooltip("2페이즈 전용 패턴 여부.")]
            public bool phase2Only = false;

            public float GetCooldown(CommonSettings common)
                => useCommonTiming && common != null ? common.defaultCooldown : cooldown;

            public float GetWarningDuration(CommonSettings common)
                => useCommonTiming && common != null ? common.defaultWarningDuration : warningDuration;

            public float GetRecoveryDuration(CommonSettings common)
                => useCommonTiming && common != null ? common.defaultRecoveryDuration : recoveryDuration;

            public void Clamp()
            {
                cooldown = Mathf.Max(0f, cooldown);
                warningDuration = Mathf.Max(0f, warningDuration);
                recoveryDuration = Mathf.Max(0f, recoveryDuration);
            }
        }

        // ══════════════════════════════════════════════════════
        // Charge
        // ══════════════════════════════════════════════════════

        [Serializable]
        public class ChargeSettings
        {
            public LifecycleSettings lifecycle = new LifecycleSettings();

            [Tooltip("1페이즈 Charge 돌진 속도.")]
            [Min(0f)] public float speed = 12f;

            [Tooltip("2페이즈 Charge 돌진 속도.")]
            [Min(0f)] public float phase2Speed = 16f;

            [Tooltip("Charge 최대 돌진 거리.")]
            [Min(0f)] public float distance = 15f;

            [Tooltip("Charge 실제 히트박스 크기.")]
            public Vector2 hitboxSize = new Vector2(1f, 8f);

            [Tooltip("Charge 예고 범위 크기. 실제 히트박스보다 넉넉하게.")]
            public Vector2 warningSize = new Vector2(1.6f, 9.6f);

            [Tooltip("Warning 중 팔을 뒤로 당기는 거리.")]
            [Min(0f)] public float windupPullAmount = 0.5f;

            [Tooltip("Active 시작 시 팔을 앞으로 뻗는 거리.")]
            [Min(0f)] public float thrustAmount = 0.4f;

            public float GetSpeed(int phase) => phase >= 2 ? phase2Speed : speed;
            public float GetSpeed(bool isPhase2) => isPhase2 ? phase2Speed : speed;

            public void Clamp()
            {
                lifecycle?.Clamp();
                speed = Mathf.Max(0f, speed);
                phase2Speed = Mathf.Max(0f, phase2Speed);
                distance = Mathf.Max(0f, distance);
                windupPullAmount = Mathf.Max(0f, windupPullAmount);
                thrustAmount = Mathf.Max(0f, thrustAmount);
            }
        }

        // ══════════════════════════════════════════════════════
        // Slam
        // ══════════════════════════════════════════════════════

        [Serializable]
        public class SlamSettings
        {
            public LifecycleSettings lifecycle = new LifecycleSettings();

            [Tooltip("Slam 실제 히트 반경.")]
            [Min(0f)] public float hitRadius = 2.5f;

            [Tooltip("Slam 예고 범위 반경.")]
            [Min(0f)] public float warningRadius = 3f;

            [Tooltip("Warning 중 팔을 뒤로 당기는 거리.")]
            [Min(0f)] public float windupPullAmount = 0.5f;

            [Tooltip("Warning 중 팔을 들어 올리는 높이.")]
            [Min(0f)] public float windupLiftAmount = 0.3f;

            [Tooltip("팔이 목표 위치까지 이동하는 시간.")]
            [Min(0.05f)] public float moveDuration = 0.15f;

            [Tooltip("팔이 꽂혀있는 공략 타임 지속 시간.")]
            [Min(0.5f)] public float vulnerableDuration = 2.0f;

            [Tooltip("공략 타임 중 봉인도 누적 배율.")]
            [Min(1f)] public float vulnerableMultiplier = 2.0f;

            [Tooltip("팔 귀환 이동 시간.")]
            [Min(0.05f)] public float returnDuration = 0.25f;

            [Tooltip("2페이즈에서 두 번째 내려치기를 사용할지 여부.")]
            public bool phase2SecondSlam = true;

            [Tooltip("2페이즈 두 번째 내려치기 전 대기 시간.")]
            [Min(0f)] public float phase2SecondDelay = 0.2f;

            [Tooltip("2페이즈 공략 타임 배율. 0.6이면 60%로 단축.")]
            [Min(0f)] public float phase2VulnerableDurationMultiplier = 0.6f;

            public float GetVulnerableDuration(bool isPhase2)
                => isPhase2 ? vulnerableDuration * phase2VulnerableDurationMultiplier : vulnerableDuration;

            public void Clamp()
            {
                lifecycle?.Clamp();
                hitRadius = Mathf.Max(0f, hitRadius);
                warningRadius = Mathf.Max(0f, warningRadius);
                windupPullAmount = Mathf.Max(0f, windupPullAmount);
                windupLiftAmount = Mathf.Max(0f, windupLiftAmount);
                moveDuration = Mathf.Max(0.05f, moveDuration);
                vulnerableDuration = Mathf.Max(0.5f, vulnerableDuration);
                vulnerableMultiplier = Mathf.Max(1f, vulnerableMultiplier);
                returnDuration = Mathf.Max(0.05f, returnDuration);
                phase2SecondDelay = Mathf.Max(0f, phase2SecondDelay);
                phase2VulnerableDurationMultiplier = Mathf.Max(0f, phase2VulnerableDurationMultiplier);
            }
        }

        // ══════════════════════════════════════════════════════
        // Sweep
        // ══════════════════════════════════════════════════════

        [Serializable]
        public class SweepSettings
        {
            public LifecycleSettings lifecycle = new LifecycleSettings();

            [Tooltip("Sweep 실제 히트 반경.")]
            [Min(0f)] public float hitRadius = 5f;

            [Tooltip("Sweep 예고 범위 반경.")]
            [Min(0f)] public float warningRadius = 10f;

            [Tooltip("1페이즈 Sweep 회전 속도.")]
            [Min(0f)] public float rotateSpeed = 180f;

            [Tooltip("2페이즈 Sweep 회전 속도.")]
            [Min(0f)] public float phase2RotateSpeed = 270f;

            [Tooltip("Warning 중 팔이 좌우로 벌어지는 거리.")]
            [Min(0f)] public float armSpreadAmount = 0.6f;

            [Tooltip("회전 완료 후 팔이 날아가는 거리.")]
            [Min(0.5f)] public float flyDistance = 1.5f;

            [Tooltip("2페이즈 팔 날아가는 거리 배율.")]
            [Min(0f)] public float phase2FlyDistanceMultiplier = 1.5f;

            [Tooltip("팔이 날아가는 데 걸리는 시간.")]
            [Min(0.05f)] public float flyDuration = 0.2f;

            [Tooltip("팔이 날아간 후 공략 타임 지속 시간.")]
            [Min(0.5f)] public float vulnerableDuration = 1.5f;

            [Tooltip("팔 귀환 시간.")]
            [Min(0.05f)] public float returnDuration = 0.3f;

            [Tooltip("2페이즈 히트 반경 추가값.")]
            [Min(0f)] public float phase2HitRadiusBonus = 0.5f;

            public float GetRotateSpeed(int phase) => phase >= 2 ? phase2RotateSpeed : rotateSpeed;
            public float GetRotateSpeed(bool isPhase2) => isPhase2 ? phase2RotateSpeed : rotateSpeed;
            public float GetHitRadius(bool isPhase2) => isPhase2 ? hitRadius + phase2HitRadiusBonus : hitRadius;
            public float GetFlyDistance(bool isPhase2) => isPhase2 ? flyDistance * phase2FlyDistanceMultiplier : flyDistance;

            public void Clamp()
            {
                lifecycle?.Clamp();
                hitRadius = Mathf.Max(0f, hitRadius);
                warningRadius = Mathf.Max(0f, warningRadius);
                rotateSpeed = Mathf.Max(0f, rotateSpeed);
                phase2RotateSpeed = Mathf.Max(0f, phase2RotateSpeed);
                armSpreadAmount = Mathf.Max(0f, armSpreadAmount);
                flyDistance = Mathf.Max(0.5f, flyDistance);
                phase2FlyDistanceMultiplier = Mathf.Max(0f, phase2FlyDistanceMultiplier);
                flyDuration = Mathf.Max(0.05f, flyDuration);
                vulnerableDuration = Mathf.Max(0.5f, vulnerableDuration);
                returnDuration = Mathf.Max(0.05f, returnDuration);
                phase2HitRadiusBonus = Mathf.Max(0f, phase2HitRadiusBonus);
            }
        }

        // ══════════════════════════════════════════════════════
        // GuardBreak
        // ══════════════════════════════════════════════════════

        [Serializable]
        public class GuardBreakSettings
        {
            public LifecycleSettings lifecycle = new LifecycleSettings();

            [Tooltip("GuardBreak 실제 히트박스 크기.")]
            public Vector2 hitboxSize = new Vector2(1f, 0.8f);

            [Tooltip("2페이즈 GuardBreak 실제 히트박스 크기.")]
            public Vector2 phase2HitboxSize = new Vector2(1.2f, 0.8f);

            [Tooltip("GuardBreak 예고 범위 크기.")]
            public Vector2 warningSize = new Vector2(1.5f, 1f);

            [Tooltip("2페이즈 GuardBreak 예고 범위 크기.")]
            public Vector2 phase2WarningSize = new Vector2(1.8f, 1.0f);

            [Tooltip("GuardBreak 가드 지속 시간.")]
            [Min(0f)] public float guardDuration = 0.8f;

            [Tooltip("2페이즈 GuardBreak 가드 지속 시간.")]
            [Min(0f)] public float phase2GuardDuration = 0.5f;

            [Tooltip("가드 자세에서 앞으로 나오는 거리.")]
            [Min(0f)] public float guardForwardAmount = 0.4f;

            [Tooltip("백스윙으로 뒤로 당기는 거리.")]
            [Min(0f)] public float windupPullAmount = 0.5f;

            [Tooltip("강타 찌르기 거리.")]
            [Min(0f)] public float thrustDistance = 0.8f;

            [Tooltip("강타 찌르기 시간.")]
            [Min(0.01f)] public float thrustDuration = 0.08f;

            public float GetGuardDuration(bool isPhase2) => isPhase2 ? phase2GuardDuration : guardDuration;
            public Vector2 GetWarningSize(bool isPhase2) => isPhase2 ? phase2WarningSize : warningSize;
            public Vector2 GetHitboxSize(bool isPhase2) => isPhase2 ? phase2HitboxSize : hitboxSize;

            public void Clamp()
            {
                lifecycle?.Clamp();
                guardDuration = Mathf.Max(0f, guardDuration);
                phase2GuardDuration = Mathf.Max(0f, phase2GuardDuration);
                guardForwardAmount = Mathf.Max(0f, guardForwardAmount);
                windupPullAmount = Mathf.Max(0f, windupPullAmount);
                thrustDistance = Mathf.Max(0f, thrustDistance);
                thrustDuration = Mathf.Max(0.01f, thrustDuration);
            }
        }

        // ══════════════════════════════════════════════════════
        // RageCharge
        // ══════════════════════════════════════════════════════

        [Serializable]
        public class RageChargeSettings
        {
            public LifecycleSettings lifecycle = new LifecycleSettings
            {
                useCommonTiming = true,
                phase2Only = true,
            };

            [Tooltip("RageCharge 개별 돌진 속도.")]
            [Min(0f)] public float speed = 18f;

            [Tooltip("RageCharge 최대 돌진 거리.")]
            [Min(0f)] public float distance = 15f;

            [Tooltip("RageCharge 실제 히트박스 크기.")]
            public Vector2 hitboxSize = new Vector2(1f, 8f);

            [Tooltip("예고선 길이 = distance × warningLineLengthMultiplier.")]
            [Min(0f)] public float warningLineLengthMultiplier = 1.2f;

            [Tooltip("연속 돌진 횟수.")]
            [Min(1)] public int count = 3;

            [Tooltip("돌진 사이 간격.")]
            [Min(0f)] public float interval = 0.2f;

            [Tooltip("좌/우 추가 돌진 각도.")]
            [Min(0f)] public float sideAngle = 20f;

            [Tooltip("Warning 중 양팔을 뒤로 당기는 거리.")]
            [Min(0f)] public float windupPullAmount = 0.4f;

            [Tooltip("각 돌진 직전 양팔을 앞으로 뻗는 거리.")]
            [Min(0f)] public float thrustAmount = 0.3f;

            public float WarningLineLength => distance * warningLineLengthMultiplier;

            public void Clamp()
            {
                lifecycle?.Clamp();
                speed = Mathf.Max(0f, speed);
                distance = Mathf.Max(0f, distance);
                warningLineLengthMultiplier = Mathf.Max(0f, warningLineLengthMultiplier);
                count = Mathf.Max(1, count);
                interval = Mathf.Max(0f, interval);
                sideAngle = Mathf.Max(0f, sideAngle);
                windupPullAmount = Mathf.Max(0f, windupPullAmount);
                thrustAmount = Mathf.Max(0f, thrustAmount);
            }
        }
    }
}
