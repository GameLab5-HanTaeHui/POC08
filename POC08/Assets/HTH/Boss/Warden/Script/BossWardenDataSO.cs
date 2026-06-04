// ============================================================
// BossWardenDataSO.cs  v1.2
// Boss_Warden 전용 수치 ScriptableObject
//
// [v1.2 수정 — 봉인 등급 2단계 필드 추가]
//   partSealSlowTimeScale : 0.5f  (부위 봉인 완료 시 짧은 슬로우 배율)
//   partSealSlowDuration  : 0.25f (부위 봉인 슬로우 지속 시간)
//   README v2 봉인 집행 등급 2단계 (부위 봉인) 반영
//
// [POC07 참고]
//   TestBossDataSO.cs (v1.1) 구조를 기반으로
//   탑뷰 봉인 시스템에 맞게 전면 재설계.
//
// [POC07과의 차이]
//   POC07: HP + 딜타임 중 데미지 + A키 홀드 처형 수치
//   POC08: HP 없음 (봉인도 누적 → 코어 봉인도 100% → 처치)
//          S키 봉인 집행 / 코어 해제 / 최종 봉인 수치 추가
//          부위 봉인도 내부 요구치 + 봉인 저항 배율 추가
//          2페이즈 강화 수치 추가
//          예고 범위 수치 추가 (히트박스보다 10~20% 넉넉)
//
// [생성 방법]
//   Assets 우클릭 → Create → SEAL/Boss → BossWardenDataSO
//
// [연결]
//   BossWardenCore._data 에 Inspector 연결.
//   BossWardenAI, BossWardenSealExecutor 등 모든 Warden 스크립트가 이 SO 를 참조.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 전용 수치 ScriptableObject. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [설계 원칙]
    ///   모든 Warden 관련 수치는 이 SO 하나에서만 관리한다.
    ///   런타임 중 수치 변경은 허용하지 않는다 (readonly 설계).
    ///   Inspector 에서 수치를 조정하여 게임 밸런스를 맞춘다.
    ///
    /// [봉인도 내부/UI 변환 공식]
    ///   UI% = (현재 내부 봉인도 / 최대 내부 요구치) × 100
    ///   UI 는 항상 0~100% 로 표시하지만 내부 요구치는 부위마다 다름.
    ///   → 겉으로는 같은 100% 이지만 실제 채우는 속도는 다름.
    /// ────────────────────────────────────────────────────
    /// </summary>
    [CreateAssetMenu(
        menuName = "SEAL/Boss/BossWardenDataSO",
        fileName = "BossWardenDataSO")]
    public class BossWardenDataSO : ScriptableObject
    {
        // ══════════════════════════════════════════════════════
        // 부위 봉인도 (SealGauge)
        // ══════════════════════════════════════════════════════

        [Header("── 부위 봉인도 ──────────────────────")]

        /// <summary>
        /// 팔 부위 봉인도 내부 최대 요구치.
        /// 플레이어 공격으로 이 수치가 쌓이면 봉인 집행 가능 상태.
        /// UI 는 항상 100% 로 표시.
        ///
        /// [권장값] 200 (기본 공격 +10 기준 20번 공격)
        /// </summary>
        [Tooltip("팔 부위 봉인도 내부 최대 요구치. UI는 항상 100%로 표시. 권장: 200.")]
        [Min(1f)]
        public float armSealGaugeMax = 200f;

        /// <summary>
        /// 봉인 저항 배율 배열.
        /// 인덱스 0 = 1회차 봉인, 1 = 2회차, 2 = 3회차, 3 = 4회차 이상.
        /// 같은 부위를 반복 봉인할수록 봉인도 누적 속도가 감소.
        ///
        /// [README #28 봉인 저항]
        ///   1회: 100% / 2회: 80% / 3회: 60% / 4회+: 40%
        /// </summary>
        [Tooltip("봉인 저항 배율. [0]=1회차, [1]=2회차, [2]=3회차, [3]=4회차+. 범위: 0~1.")]
        public float[] sealResistMultipliers = { 1.0f, 0.8f, 0.6f, 0.4f };

        // ══════════════════════════════════════════════════════
        // 코어 봉인도 (CoreSealGauge)
        // ══════════════════════════════════════════════════════

        [Header("── 코어 봉인도 ──────────────────────")]

        /// <summary>
        /// 코어 봉인도 내부 최대치.
        /// 딜 페이즈에서 코어를 공격하여 누적.
        /// UI 는 항상 100% 로 표시.
        ///
        /// [README #22 코어 봉인도]
        ///   코어 내부 요구치: 500
        ///   기본 공격 내부 누적: +25 / UI +5%
        ///   강공격 내부 누적: +75 / UI +15%
        /// </summary>
        [Tooltip("코어 봉인도 내부 최대치. UI는 항상 100%로 표시. 권장: 500.")]
        [Min(1f)]
        public float coreSealGaugeMax = 500f;

        /// <summary>
        /// 1페이즈 딜 페이즈 종료 코어 봉인도 목표 (내부).
        /// 이 수치에 도달하면 1페이즈 딜 페이즈 종료 → 2페이즈 전환.
        /// </summary>
        [Tooltip("1페이즈 종료 코어 봉인도 목표 (내부). 권장: coreSealGaugeMax의 50%.")]
        [Min(1f)]
        public float phase1CoreSealTarget = 250f;

        /// <summary>
        /// 2페이즈 딜 페이즈 종료 코어 봉인도 목표 (내부).
        /// 이 수치에 도달하면 최종 봉인 진입.
        /// coreSealGaugeMax 와 동일하게 설정 권장.
        /// </summary>
        [Tooltip("2페이즈 종료 코어 봉인도 목표 (내부). coreSealGaugeMax와 동일 권장.")]
        [Min(1f)]
        public float phase2CoreSealTarget = 500f;

        /// <summary>
        /// 딜 페이즈 중 기본 공격 코어 봉인도 누적량 (내부).
        /// UI 기준 +5% 가 되도록 설정.
        /// 기본값: 25 (내부 500 기준 5%)
        /// </summary>
        [Tooltip("딜 페이즈 중 기본 공격 코어 봉인도 누적량 (내부). 기본값: 25.")]
        [Min(1f)]
        public float coreBasicAttackGain = 25f;

        /// <summary>
        /// 딜 페이즈 중 강공격 코어 봉인도 누적량 (내부).
        /// UI 기준 +15% 가 되도록 설정.
        /// 기본값: 75
        /// </summary>
        [Tooltip("딜 페이즈 중 강공격 코어 봉인도 누적량 (내부). 기본값: 75.")]
        [Min(1f)]
        public float coreChargeAttackGain = 75f;

        // ══════════════════════════════════════════════════════
        // S키 봉인 집행 / 코어 해제 / 최종 봉인
        // ══════════════════════════════════════════════════════

        [Header("── S키 봉인 집행 ──────────────────────")]

        /// <summary>봉인 집행 가능 범위 반경 (units).</summary>
        [Tooltip("봉인 집행 가능 접근 범위 반경 (units). 권장: 1.5.")]
        [Min(0.1f)]
        public float sealExecutionRange = 1.5f;

        /// <summary>봉인 집행 S키 홀드 시간 (초).</summary>
        [Tooltip("봉인 집행 S키 홀드 시간 (초). 권장: 1.5.")]
        [Min(0.1f)]
        public float sealExecutionHoldTime = 1.5f;

        /// <summary>
        /// 2단계 부위 봉인 슬로우 배율. (README v2 봉인 등급 2단계)
        /// 집행 완료 직후 짧은 슬로우로 타격감 강조.
        /// 1.0 = 슬로우 없음 / 0.5 = 절반 속도.
        /// </summary>
        [Tooltip("부위 봉인 완료 시 짧은 슬로우 배율. 권장: 0.5 / 1.0 = 슬로우 없음.")]
        [Range(0.1f, 1f)]
        public float partSealSlowTimeScale = 0.5f;

        /// <summary>
        /// 2단계 부위 봉인 슬로우 지속 시간 (초, RealTime 기준).
        /// 이 시간 후 TimeScale 1.0 복구.
        /// 너무 길면 전투 템포를 해침 — 0.2~0.3초 권장.
        /// </summary>
        [Tooltip("부위 봉인 슬로우 지속 시간 (초). 권장: 0.25.")]
        [Min(0f)]
        public float partSealSlowDuration = 0.25f;

        [Header("── 코어 해제 ──────────────────────")]

        /// <summary>
        /// 코어 해제 가능 범위 반경 (units).
        /// 그로기 중 코어에 이 거리 이내로 접근해야 S키 코어 해제 가능.
        /// </summary>
        [Tooltip("코어 해제 접근 범위 반경 (units). 권장: 1.5.")]
        [Min(0.1f)]
        public float coreUnlockRange = 1.5f;

        /// <summary>
        /// 코어 해제 S키 홀드 시간 (초).
        /// 이 시간 동안 S키를 유지해야 딜 페이즈 진입.
        /// </summary>
        [Tooltip("코어 해제 S키 홀드 시간 (초). 권장: 3.0.")]
        [Min(0.1f)]
        public float coreUnlockHoldTime = 3.0f;

        /// <summary>
        /// 코어 해제 / 봉인 집행 중 슬로우 모션 배율.
        /// Time.timeScale 을 이 값으로 낮춰 연출 강조.
        /// 1.0 = 정상 속도 / 0.3 = 느린 모션
        /// </summary>
        [Tooltip("봉인 집행 / 코어 해제 슬로우 배율. 1.0=정상, 0.3=슬로우. 권장: 0.3.")]
        [Range(0.05f, 1.0f)]
        public float dilPhaseSlowTimeScale = 0.3f;

        [Header("── 최종 봉인 ──────────────────────")]

        /// <summary>
        /// 최종 봉인 S키 홀드 시간 (초).
        /// 코어 봉인도 100% 후 이 시간 동안 S키를 유지해야 Warden 처치.
        /// </summary>
        [Tooltip("최종 봉인 S키 홀드 시간 (초). 권장: 2.0.")]
        [Min(0.1f)]
        public float finalSealHoldTime = 2.0f;

        /// <summary>
        /// 최종 봉인 슬로우 모션 배율.
        /// dilPhaseSlowTimeScale 보다 더 강한 슬로우 권장.
        /// </summary>
        [Tooltip("최종 봉인 슬로우 배율. 권장: 0.1 (매우 느린 슬로우).")]
        [Range(0.05f, 1.0f)]
        public float finalSealSlowTimeScale = 0.1f;

        // ══════════════════════════════════════════════════════
        // 그로기 (Groggy)
        // ══════════════════════════════════════════════════════

        [Header("── 그로기 ──────────────────────")]

        /// <summary>
        /// 그로기 지속 시간 (초).
        /// 코어 해제에 실패하면 이 시간 후 그로기 종료 → 부위 해제 → 루프 재시작.
        /// </summary>
        [Tooltip("그로기 지속 시간 (초). 코어 해제 실패 시 이 시간 후 종료. 권장: 5.0.")]
        [Min(1f)]
        public float groggyDuration = 5.0f;

        // ══════════════════════════════════════════════════════
        // 딜 페이즈 (DilPhase)
        // ══════════════════════════════════════════════════════

        [Header("── 딜 페이즈 ──────────────────────")]

        /// <summary>
        /// 딜 페이즈 최대 지속 시간 (초).
        /// 이 시간 내에 코어 봉인도 목표 미달성 시 딜 페이즈 일반 종료 → 루프 재시작.
        /// </summary>
        [Tooltip("딜 페이즈 최대 지속 시간 (초). 권장: 10.0.")]
        [Min(1f)]
        public float dilPhaseDuration = 10.0f;

        // ══════════════════════════════════════════════════════
        // 충격파 (Shockwave)
        // ══════════════════════════════════════════════════════

        [Header("── 충격파 ──────────────────────")]

        /// <summary>
        /// 충격파 범위 반경 (units).
        /// 페이즈 전환 / 딜 페이즈 종료 시 이 범위 내 플레이어에 넉백 적용.
        /// </summary>
        [Tooltip("충격파 범위 반경 (units). 권장: 6.0.")]
        [Min(0.1f)]
        public float shockwaveRadius = 6.0f;

        /// <summary>
        /// 충격파 넉백 강도.
        /// Rigidbody2D 에 AddForce 또는 DOTween DOMove 방향 거리.
        /// </summary>
        [Tooltip("충격파 넉백 강도. 권장: 12.0.")]
        [Min(0.1f)]
        public float shockwaveKnockbackForce = 12.0f;

        /// <summary>
        /// 충격파 넉백 DOTween 지속 시간 (초).
        /// </summary>
        [Tooltip("충격파 넉백 DOTween 지속 시간 (초). 권장: 0.2.")]
        [Min(0.05f)]
        public float shockwaveKnockbackDuration = 0.2f;

        // ══════════════════════════════════════════════════════
        // AI 이동 (Movement)
        // ══════════════════════════════════════════════════════

        [Header("── AI 이동 ──────────────────────")]

        /// <summary>
        /// 1페이즈 Chase 이동 속도 (units/s).
        /// </summary>
        [Tooltip("1페이즈 이동 속도 (units/s). 권장: 3.5.")]
        [Min(0.1f)]
        public float moveSpeed = 3.5f;

        /// <summary>
        /// 2페이즈 Chase 이동 속도 (units/s).
        /// 1페이즈보다 빠르게 설정하여 압박감 증가.
        /// </summary>
        [Tooltip("2페이즈 이동 속도 (units/s). 권장: 4.5.")]
        [Min(0.1f)]
        public float phase2MoveSpeed = 4.5f;

        /// <summary>
        /// 패턴 발동 감지 범위 (units).
        /// 플레이어가 이 범위 내 진입 시 Idle → 패턴 선택.
        /// 이 범위 밖이면 Chase 이동.
        /// </summary>
        [Tooltip("패턴 발동 감지 범위 (units). 권장: 5.0.")]
        [Min(0.1f)]
        public float patternRange = 5.0f;

        /// <summary>
        /// 방향 전환 쿨타임 (초).
        /// 탑뷰에서 너무 자주 회전하는 것을 방지.
        /// </summary>
        [Tooltip("방향 전환 쿨타임 (초). 권장: 0.5.")]
        [Min(0f)]
        public float flipCooldown = 0.5f;

        // ══════════════════════════════════════════════════════
        // 패턴 — Charge (돌진)
        // ══════════════════════════════════════════════════════

        [Header("── 패턴: Charge (돌진) ──────────────────────")]

        /// <summary>
        /// 1페이즈 돌진 속도 (units/s).
        /// </summary>
        [Tooltip("1페이즈 돌진 속도. 권장: 12.0.")]
        [Min(0.1f)]
        public float chargeSpeed = 12.0f;

        /// <summary>
        /// 2페이즈 돌진 속도 (units/s).
        /// </summary>
        [Tooltip("2페이즈 돌진 속도. 권장: 16.0.")]
        [Min(0.1f)]
        public float phase2ChargeSpeed = 16.0f;

        /// <summary>
        /// 돌진 최대 거리 (units).
        /// 이 거리를 넘으면 돌진 종료 → Recovery.
        /// </summary>
        [Tooltip("돌진 최대 거리 (units). 권장: 8.0.")]
        [Min(0.1f)]
        public float chargeDistance = 8.0f;

        /// <summary>
        /// Charge 히트박스 실제 크기 (width × height).
        /// OverlapBox 에 사용.
        /// </summary>
        [Tooltip("Charge 히트박스 실제 크기 (width x height). 권장: (1.0, 8.0).")]
        public Vector2 chargeHitboxSize = new Vector2(1.0f, 8.0f);

        /// <summary>
        /// Charge 예고 범위 크기 (width × height).
        /// 실제 히트박스보다 20% 넉넉하게 표시.
        /// </summary>
        [Tooltip("Charge 예고 범위 크기. 실제 히트박스보다 20% 넉넉하게. 권장: (1.6, 9.6).")]
        public Vector2 chargeWarningSize = new Vector2(1.6f, 9.6f);

        // ══════════════════════════════════════════════════════
        // 패턴 — Slam (내려치기)
        // ══════════════════════════════════════════════════════

        [Header("── 패턴: Slam (내려치기) ──────────────────────")]

        /// <summary>
        /// Slam 히트박스 실제 반경 (units).
        /// OverlapCircle 에 사용.
        /// </summary>
        [Tooltip("Slam 히트박스 실제 반경 (units). 권장: 2.5.")]
        [Min(0.1f)]
        public float slamHitRadius = 2.5f;

        /// <summary>
        /// Slam 예고 범위 반경 (units).
        /// 실제 히트박스보다 20% 넉넉.
        /// </summary>
        [Tooltip("Slam 예고 범위 반경 (units). 실제보다 20% 넉넉. 권장: 3.0.")]
        [Min(0.1f)]
        public float slamWarningRadius = 3.0f;

        // ══════════════════════════════════════════════════════
        // 패턴 — Sweep (스윕)
        // ══════════════════════════════════════════════════════

        [Header("── 패턴: Sweep (스윕) ──────────────────────")]

        /// <summary>
        /// Sweep 히트박스 실제 반경 (units).
        /// </summary>
        [Tooltip("Sweep 히트박스 실제 반경 (units). 권장: 2.5.")]
        [Min(0.1f)]
        public float sweepHitRadius = 2.5f;

        /// <summary>
        /// Sweep 예고 범위 반경 (units).
        /// </summary>
        [Tooltip("Sweep 예고 범위 반경 (units). 실제보다 20% 넉넉. 권장: 3.0.")]
        [Min(0.1f)]
        public float sweepWarningRadius = 3.0f;

        /// <summary>
        /// 1페이즈 회전 속도 (도/초).
        /// </summary>
        [Tooltip("1페이즈 Sweep 회전 속도 (도/초). 권장: 180.")]
        [Min(1f)]
        public float sweepRotateSpeed = 180f;

        /// <summary>
        /// 2페이즈 회전 속도 (도/초).
        /// </summary>
        [Tooltip("2페이즈 Sweep 회전 속도 (도/초). 권장: 270.")]
        [Min(1f)]
        public float phase2SweepRotateSpeed = 270f;

        // ══════════════════════════════════════════════════════
        // 패턴 — GuardBreak (강타)
        // ══════════════════════════════════════════════════════

        [Header("── 패턴: GuardBreak (강타) ──────────────────────")]

        /// <summary>
        /// GuardBreak 히트박스 실제 크기.
        /// </summary>
        [Tooltip("GuardBreak 히트박스 실제 크기 (width x height). 권장: (1.0, 0.8).")]
        public Vector2 guardBreakHitboxSize = new Vector2(1.0f, 0.8f);

        /// <summary>
        /// GuardBreak 예고 범위 크기.
        /// </summary>
        [Tooltip("GuardBreak 예고 범위 크기. 권장: (1.5, 1.0).")]
        public Vector2 guardBreakWarningSize = new Vector2(1.5f, 1.0f);

        /// <summary>
        /// GuardBreak 가드 구간 지속 시간 (초).
        /// 이 구간 동안 정면 봉인도 누적 무효.
        /// Warning 전체 시간 중 이 시간만큼 먼저 가드 자세.
        /// </summary>
        [Tooltip("GuardBreak 가드 구간 지속 시간 (초). 권장: 0.8.")]
        [Min(0f)]
        public float guardBreakGuardDuration = 0.8f;

        // ══════════════════════════════════════════════════════
        // 패턴 — RageCharge (3연 돌진, 2페이즈 전용)
        // ══════════════════════════════════════════════════════

        [Header("── 패턴: RageCharge (3연 돌진, 2페이즈 전용) ──────────────────────")]

        /// <summary>
        /// RageCharge 돌진 속도 (units/s).
        /// </summary>
        [Tooltip("RageCharge 돌진 속도 (units/s). 권장: 18.0.")]
        [Min(0.1f)]
        public float rageChargeSpeed = 18.0f;

        /// <summary>
        /// RageCharge 돌진 횟수.
        /// </summary>
        [Tooltip("RageCharge 돌진 횟수. 권장: 3.")]
        [Min(1)]
        public int rageChargeCount = 3;

        /// <summary>
        /// RageCharge 각 돌진 사이 간격 (초).
        /// </summary>
        [Tooltip("RageCharge 각 돌진 사이 간격 (초). 권장: 0.2.")]
        [Min(0f)]
        public float rageChargeInterval = 0.2f;

        // ══════════════════════════════════════════════════════
        // Recovery 취약 구간
        // ══════════════════════════════════════════════════════

        [Header("── Recovery 취약 구간 ──────────────────────")]

        /// <summary>
        /// Recovery 구간 봉인도 누적 배율.
        /// 패턴 후딜 중 공격하면 봉인도가 더 많이 쌓임.
        /// 플레이어에게 공략 타이밍을 명확히 제공.
        /// </summary>
        [Tooltip("Recovery 구간 봉인도 누적 배율. 권장: 1.5 (50% 추가).")]
        [Min(1f)]
        public float recoveryVulnMultiplier = 1.5f;

        // ══════════════════════════════════════════════════════
        // DOTween 피드백 색상
        // ══════════════════════════════════════════════════════

        [Header("── 본체 색상 ──────────────────────")]

        /// <summary> Idle / Chase 기본 색상. </summary>
        [Tooltip("Idle / Chase 기본 색상.")]
        public Color colorIdle = new Color(0.533f, 0.533f, 0.533f); // #888888

        /// <summary> Warning (패턴 예고) 색상. </summary>
        [Tooltip("Warning 색상.")]
        public Color colorWarning = new Color(1.0f, 0.533f, 0.0f); // #FF8800

        /// <summary> Active (패턴 시전) 색상. </summary>
        [Tooltip("Active 색상.")]
        public Color colorActive = Color.white; // #FFFFFF

        /// <summary> Recovery (후딜) 색상. </summary>
        [Tooltip("Recovery 색상.")]
        public Color colorRecovery = new Color(0.8f, 0.133f, 0.0f); // #CC2200

        /// <summary> Groggy 색상. </summary>
        [Tooltip("Groggy 색상.")]
        public Color colorGroggy = new Color(1.0f, 0.933f, 0.0f); // #FFEE00

        /// <summary> DilPhase 색상. </summary>
        [Tooltip("DilPhase 색상.")]
        public Color colorDilPhase = new Color(1.0f, 0.4f, 0.0f); // #FF6600

        /// <summary> 2페이즈 전환 색상. </summary>
        [Tooltip("2페이즈 전환 색상.")]
        public Color colorPhase2 = new Color(0.6f, 0.0f, 0.0f); // #990000

        /// <summary> Dead 색상. </summary>
        [Tooltip("Dead 색상.")]
        public Color colorDead = new Color(0.067f, 0.067f, 0.067f); // #111111

        [Header("── 부위 색상 ──────────────────────")]

        /// <summary> 부위 봉인도 0% 색상. </summary>
        [Tooltip("부위 봉인도 0% 색상.")]
        public Color colorArm0 = new Color(0.667f, 0.667f, 0.667f); // #AAAAAA

        /// <summary> 부위 봉인도 25% 색상. </summary>
        [Tooltip("부위 봉인도 25% 색상.")]
        public Color colorArm25 = new Color(1.0f, 0.867f, 0.0f); // #FFDD00

        /// <summary> 부위 봉인도 50% 색상. </summary>
        [Tooltip("부위 봉인도 50% 색상.")]
        public Color colorArm50 = new Color(1.0f, 0.533f, 0.0f); // #FF8800

        /// <summary> 부위 봉인도 75% 색상. </summary>
        [Tooltip("부위 봉인도 75% 색상.")]
        public Color colorArm75 = new Color(1.0f, 0.133f, 0.0f); // #FF2200

        /// <summary> 부위 봉인도 100% (봉인 가능) 색상. </summary>
        [Tooltip("부위 봉인도 100% 색상. 파랑 빠른 Pulse.")]
        public Color colorArm100 = new Color(0.0f, 0.533f, 1.0f); // #0088FF

        /// <summary> 부위 봉인 완료 고정 색상. </summary>
        [Tooltip("부위 봉인 완료 고정 색상.")]
        public Color colorArmSealed = new Color(0.0f, 0.267f, 0.8f); // #0044CC

        [Header("── 코어 색상 ──────────────────────")]

        /// <summary> 코어 활성 (코어 해제 대기) 색상. </summary>
        [Tooltip("코어 활성 색상.")]
        public Color colorCoreActive = new Color(1.0f, 0.933f, 0.0f); // #FFEE00

        /// <summary> 딜 페이즈 중 코어 색상. </summary>
        [Tooltip("딜 페이즈 중 코어 색상.")]
        public Color colorCoreDilPhase = Color.white;

        /// <summary> 최종 봉인 가능 코어 색상. </summary>
        [Tooltip("최종 봉인 가능 코어 색상.")]
        public Color colorCoreFinalSeal = new Color(0.667f, 0.867f, 1.0f); // #AADDFF

        [Header("── 예고 범위 색상 ──────────────────────")]

        /// <summary>
        /// 공격 예고 범위 디스크 색상 (반투명 붉은색).
        /// Alpha 는 0.4 권장.
        /// </summary>
        [Tooltip("공격 예고 범위 색상. Alpha 0.4 권장.")]
        public Color colorWarningRange = new Color(1.0f, 0.0f, 0.0f, 0.4f);

        /// <summary>
        /// 봉인 집행 가능 범위 점선 색상.
        /// </summary>
        [Tooltip("봉인 집행 가능 범위 색상.")]
        public Color colorSealRange = new Color(0.0f, 0.533f, 1.0f); // #0088FF

        /// <summary>
        /// 코어 해제 가능 범위 점선 색상.
        /// </summary>
        [Tooltip("코어 해제 가능 범위 색상.")]
        public Color colorCoreRange = new Color(1.0f, 0.933f, 0.0f); // #FFEE00

        // ══════════════════════════════════════════════════════
        // DOTween 타이밍 수치
        // ══════════════════════════════════════════════════════

        [Header("── DOTween 타이밍 ──────────────────────")]

        /// <summary>
        /// 색상 전환 Duration (초). 빠른 전환 권장.
        /// </summary>
        [Tooltip("색상 전환 DOColor 기본 Duration. 권장: 0.1.")]
        [Min(0.01f)]
        public float colorTransitionDuration = 0.1f;

        /// <summary>
        /// Pulse 루프 한 주기 (초).
        /// DOColor Yoyo Loop 에 사용.
        /// </summary>
        [Tooltip("Pulse 한 주기 (초). 권장: 0.4.")]
        [Min(0.05f)]
        public float pulsePeriod = 0.4f;

        /// <summary>
        /// 히트 스탑 점멸 지속 시간 (초).
        /// 공격 적중 시 SpriteRenderer 흰색 점멸 시간.
        /// </summary>
        [Tooltip("히트 스탑 점멸 지속 시간 (초). 권장: 0.07.")]
        [Min(0.01f)]
        public float hitFlashDuration = 0.07f;

        // ══════════════════════════════════════════════════════
        // 유틸리티 메서드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 봉인 저항 배율을 반환한다.
        /// sealCount = 0-based (0 = 첫 번째 봉인).
        ///
        /// [사용 예시]
        ///   float mult = data.GetSealResistMultiplier(armSealCount);
        ///   float actualGain = rawGain * mult;
        /// </summary>
        /// <param name="sealCount">현재까지 봉인된 횟수 (0-based).</param>
        /// <returns>봉인도 누적 배율 (0~1).</returns>
        public float GetSealResistMultiplier(int sealCount)
        {
            if (sealResistMultipliers == null || sealResistMultipliers.Length == 0)
                return 1.0f;

            int idx = Mathf.Clamp(sealCount, 0, sealResistMultipliers.Length - 1);
            return sealResistMultipliers[idx];
        }

        /// <summary>
        /// 부위 봉인도를 UI 퍼센트로 변환한다.
        /// </summary>
        /// <param name="current">현재 내부 봉인도.</param>
        /// <returns>UI 퍼센트 (0~100).</returns>
        public float ArmGaugeToUIPercent(float current)
        {
            if (armSealGaugeMax <= 0f) return 0f;
            return Mathf.Clamp01(current / armSealGaugeMax) * 100f;
        }

        /// <summary>
        /// 코어 봉인도를 UI 퍼센트로 변환한다.
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