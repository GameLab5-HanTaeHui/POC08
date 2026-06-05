// ============================================================
// PlayerAttackDataSO.cs  v3.0
// 플레이어 공격 수치 ScriptableObject
//
// [v3.0 변경 — 강공격 관련 전부 제거]
//   제거 항목:
//     ChargeMinHoldTime / ChargeBackPos / ChargeAttackPos
//     ChargeRotBack / ChargeRotAtk / ChargeClockwise
//     ChargeSealAmount / ChargeHitStopDuration / ChargeHitboxScale
//     ChargePulseScale / ChargePulsePeriod / ChargeArcHeight
//
//   이유:
//     pressed/released 로 기본/강공격을 분리하는 구조가
//     공격 메커니즘에 혼란을 유발함.
//     좌클릭 = 항상 기본 콤보 공격으로 단일화.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 플레이어 공격 수치 ScriptableObject. (v3.0)
    /// 강공격 제거 — 좌클릭 = 기본 콤보 공격으로 단일화.
    /// </summary>
    [CreateAssetMenu(
        menuName = "SEAL/Player/PlayerAttackDataSO",
        fileName = "PlayerAttackDataSO")]
    public class PlayerAttackDataSO : ScriptableObject
    {
        // ══════════════════════════════════════════════════════
        // 공격 이동
        // ══════════════════════════════════════════════════════

        [Header("── 공격 이동 ──────────────────────")]

        /// <summary>
        /// 공격 중 이동 속도.
        /// PlayerController.AttackMoveRoutine 에서 사용.
        /// </summary>
        [Tooltip("공격 중 이동 속도. 권장: 2~4.")]
        [Min(0f)]
        public float AttackMoveSpeed = 3f;

        /// <summary>
        /// 콤보 리셋 시간 (초).
        /// 이 시간 내 입력 없으면 1콤보로 초기화.
        /// </summary>
        [Tooltip("콤보 리셋 시간. 권장: 1.5~2.0초.")]
        [Min(0.1f)]
        public float ComboResetTime = 1.5f;

        // ══════════════════════════════════════════════════════
        // 타이밍
        // ══════════════════════════════════════════════════════

        [Header("── 타이밍 ──────────────────────")]

        /// <summary>백스윙 지속 시간 (초).</summary>
        [Tooltip("백스윙 지속 시간. 권장: 0.10~0.20.")]
        [Range(0.01f, 0.5f)]
        public float BackswingDuration = 0.15f;

        /// <summary>타격 이동 지속 시간 (초).</summary>
        [Tooltip("타격 이동 지속 시간. 권장: 0.10~0.20.")]
        [Range(0.01f, 0.5f)]
        public float AttackDuration = 0.2f;

        /// <summary>복귀 지속 시간 (초).</summary>
        [Tooltip("복귀 지속 시간. 권장: 0.20~0.40.")]
        [Range(0.01f, 0.5f)]
        public float ReturnDuration = 0.4f;

        /// <summary>최대 연속 콤보 횟수.</summary>
        [Tooltip("최대 연속 콤보 횟수.")]
        [Min(1)]
        public int MaxComboCount = 3;

        /// <summary>
        /// 콤보 입력 허용 시작 비율.
        /// AttackDuration 의 이 비율 이후부터 다음 콤보 입력 수신.
        /// </summary>
        [Tooltip("콤보 윈도우 시작 비율. 0.8 = 타격 80% 이후 입력 허용.")]
        [Range(0.1f, 0.9f)]
        public float ComboWindowStartRatio = 0.8f;

        // ══════════════════════════════════════════════════════
        // Combo1 — 횡베기
        // ══════════════════════════════════════════════════════

        [Header("── Combo1 — 횡베기 ──────────────────────")]

        [Tooltip("Combo1 백스윙 위치.")]
        public Vector2 Combo1BackPos = new Vector2(0.5f, -1.5f);

        [Tooltip("Combo1 타격 위치.")]
        public Vector2 Combo1AttackPos = new Vector2(0.5f, 1.5f);

        [Tooltip("Combo1 백스윙 Z회전.")]
        [Range(-180f, 180f)]
        public float Combo1RotBack = -180f;

        [Tooltip("Combo1 타격 Z회전.")]
        [Range(-180f, 180f)]
        public float Combo1RotAtk = 90f;

        [Tooltip("Combo1 회전 방향. true=시계 / false=반시계.")]
        public bool Combo1Clockwise = false;

        [Tooltip("Combo1 봉인도 누적량.")]
        [Min(1f)]
        public float Combo1SealAmount = 10f;

        [Tooltip("Combo1 타격 호 높이. 0=직선 / +위볼록 / -아래볼록.")]
        [Range(-3f, 3f)]
        public float Combo1ArcHeight = 0.8f;

        // ══════════════════════════════════════════════════════
        // Combo2 — 내리찍기
        // ══════════════════════════════════════════════════════

        [Header("── Combo2 — 내리찍기 ──────────────────────")]

        [Tooltip("Combo2 백스윙 위치.")]
        public Vector2 Combo2BackPos = new Vector2(0.5f, 1.5f);

        [Tooltip("Combo2 타격 위치.")]
        public Vector2 Combo2AttackPos = new Vector2(0.5f, -1.5f);

        [Tooltip("Combo2 백스윙 Z회전.")]
        [Range(-180f, 180f)]
        public float Combo2RotBack = 180f;

        [Tooltip("Combo2 타격 Z회전.")]
        [Range(-180f, 180f)]
        public float Combo2RotAtk = -90f;

        [Tooltip("Combo2 회전 방향. true=시계 / false=반시계.")]
        public bool Combo2Clockwise = true;

        [Tooltip("Combo2 봉인도 누적량.")]
        [Min(1f)]
        public float Combo2SealAmount = 12f;

        [Tooltip("Combo2 타격 호 높이.")]
        [Range(-3f, 3f)]
        public float Combo2ArcHeight = -0.6f;

        // ══════════════════════════════════════════════════════
        // Combo3 — 찌르기 피니셔
        // ══════════════════════════════════════════════════════

        [Header("── Combo3 — 찌르기 피니셔 ──────────────────────")]

        [Tooltip("Combo3 백스윙 위치.")]
        public Vector2 Combo3BackPos = new Vector2(-1.2f, 0f);

        [Tooltip("Combo3 타격 위치.")]
        public Vector2 Combo3AttackPos = new Vector2(2.8f, 0f);

        [Tooltip("Combo3 봉인도 누적량 (피니셔 — 가장 높음).")]
        [Min(1f)]
        public float Combo3SealAmount = 18f;

        [Tooltip("Combo3 히트스톱 시간.")]
        [Range(0f, 0.3f)]
        public float Combo3HitStopDuration = 0.1f;

        // ══════════════════════════════════════════════════════
        // 히트박스
        // ══════════════════════════════════════════════════════

        [Header("── 히트박스 ──────────────────────")]

        [Tooltip("히트박스 감지 반경. 권장: 0.6~1.0.")]
        [Min(0.1f)]
        public float HitboxRadius = 1f;

        [Tooltip("히트박스 중심 오프셋. 권장: 0.8~1.2.")]
        [Min(0f)]
        public float HitboxOffset = 1f;

        [Tooltip("적 레이어 마스크.")]
        public LayerMask HitLayer;

        // ══════════════════════════════════════════════════════
        // 히트스톱
        // ══════════════════════════════════════════════════════

        [Header("── 히트스톱 ──────────────────────")]

        [Tooltip("기본 히트스톱 지속 시간. 권장: 0.04~0.07.")]
        [Range(0f, 0.3f)]
        public float HitStopDuration = 0.05f;

        [Tooltip("히트스톱 중 TimeScale. 권장: 0.02~0.1.")]
        [Range(0f, 1f)]
        public float HitStopTimeScale = 0.02f;
    }
}