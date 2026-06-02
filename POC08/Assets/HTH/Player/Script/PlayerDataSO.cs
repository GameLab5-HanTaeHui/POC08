// ============================================================
// PlayerTopViewDataSO.cs  v1.0
// 탑뷰 플레이어 이동 수치 ScriptableObject
//
// [POC07 참고 스크립트]
//   MovementSettings.cs (횡스크롤 수치 SO)
//   → 점프/중력/코요테타임 제거
//   → X/Y 평면 8방향 이동 전용으로 재설계
//
// [역할]
//   PlayerTopViewMover 에서 참조하는 이동 수치 보관.
//   Inspector 에서 조절 가능. 여러 캐릭터 Prefab 공유 가능.
//
// [생성 방법]
//   Project 창 우클릭 → Create → SEAL/Player → TopView Player Data
//
// [네임스페이스]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 탑뷰 플레이어 이동 전체 수치를 보관하는 ScriptableObject. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [사용법]
    ///   Project 창에서 Create → SEAL/Player → TopView Player Data 로 생성.
    ///   생성된 SO를 PlayerTopViewMover Inspector 의 _data 에 연결.
    ///
    /// [DataSO 설계 원칙]
    ///   수치와 설정만 보관한다.
    ///   런타임 로직을 포함하지 않는다.
    /// ────────────────────────────────────────────────────
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlayerTopViewData",
        menuName = "SEAL/Player/TopView Player Data",
        order = 0)]
    public class PlayerDataSO : ScriptableObject
    {
        // ──────────────────────────────────────────
        // 이동
        // ──────────────────────────────────────────

        [Header("── 이동 ──────────────────────")]

        /// <summary>
        /// 기본 이동 속도 (units/s).
        /// Rigidbody2D.linearVelocity = moveDir * MoveSpeed.
        /// 최솟값 1.0 권장.
        /// </summary>
        [Tooltip("기본 이동 속도 (units/s). 최솟값 1.0 권장.")]
        [Min(1f)]
        public float MoveSpeed = 5f;

        /// <summary>
        /// 이동 가속도 (units/s²).
        /// 0이면 즉시 최고속도 도달. 값이 클수록 빠르게 가속.
        /// 0으로 설정하면 가속 없이 즉시 이동 (탑뷰 액션에 권장).
        /// </summary>
        [Tooltip("이동 가속도. 0 = 즉시 최고속도. 높을수록 빠르게 가속.")]
        [Min(0f)]
        public float MoveAcceleration = 50f;

        /// <summary>
        /// 이동 감속도 (units/s²).
        /// 입력이 없을 때 속도를 줄이는 비율. 높을수록 빠르게 멈춤.
        /// </summary>
        [Tooltip("이동 감속도. 높을수록 입력 없을 때 빠르게 멈춤.")]
        [Min(0f)]
        public float MoveDeceleration = 80f;

        /// <summary>
        /// 대각선 이동 속도 유지 여부.
        /// true: 8방향 모두 동일 속도 (정규화).
        /// false: 대각선이 √2 배 빠름 (비정규화).
        /// 탑뷰 액션은 true 권장.
        /// </summary>
        [Tooltip("대각선 이동 시 속도 정규화. true = 8방향 동일 속도 (권장).")]
        public bool NormalizeMovement = true;

        // ──────────────────────────────────────────
        // 대시
        // ──────────────────────────────────────────

        [Header("── 대시 ──────────────────────")]

        /// <summary>
        /// 대시 속도 (units/s).
        /// 대시 방향으로 이 속도를 즉시 적용.
        /// </summary>
        [Tooltip("대시 속도 (units/s). 권장: 15~25.")]
        [Min(1f)]
        public float DashSpeed = 18f;

        /// <summary>
        /// 대시 지속 시간 (초).
        /// 이 시간 동안 DashSpeed 로 이동.
        /// </summary>
        [Tooltip("대시 지속 시간 (초). 권장: 0.12~0.2.")]
        [Range(0.05f, 0.5f)]
        public float DashDuration = 0.15f;

        /// <summary>
        /// 대시 쿨타임 (초).
        /// 대시 종료 후 이 시간이 지나야 다시 대시 가능.
        /// </summary>
        [Tooltip("대시 쿨타임 (초). 권장: 0.5~1.0.")]
        [Min(0f)]
        public float DashCooldown = 0.6f;

        /// <summary>
        /// 최대 대시 충전 횟수.
        /// 어빌리티 열쇠(대시 열쇠)로 증가 가능.
        /// </summary>
        [Tooltip("최대 대시 충전 횟수. 기본 1. 어빌리티로 증가 가능.")]
        [Min(1)]
        public int MaxDashCount = 1;

        /// <summary>
        /// 대시 무적 여부.
        /// true: 대시 중 피격 무효.
        /// </summary>
        [Tooltip("대시 중 무적 여부. true = 피격 무효.")]
        public bool DashInvincible = true;

        // ──────────────────────────────────────────
        // DOTween 이동 피드백
        // ──────────────────────────────────────────

        [Header("── DOTween 이동 피드백 ──────────────────────")]

        /// <summary>
        /// 대시 시작 시 스케일 펀치 강도.
        /// DOTween PunchScale 연출.
        /// 0이면 비활성.
        /// </summary>
        [Tooltip("대시 시작 스케일 펀치 강도. 0 = 비활성. 권장: 0.15~0.3.")]
        [Min(0f)]
        public float DashPunchScale = 0.2f;

        /// <summary>
        /// 대시 스케일 펀치 지속 시간 (초).
        /// </summary>
        [Tooltip("대시 스케일 펀치 지속 시간 (초).")]
        [Range(0.05f, 0.3f)]
        public float DashPunchDuration = 0.12f;

        /// <summary>
        /// 이동 중 스쿼시(squash) 연출 강도.
        /// 이동 방향으로 약간 늘어나는 효과. 0이면 비활성.
        /// </summary>
        [Tooltip("이동 중 방향 스쿼시 강도. 0 = 비활성.")]
        [Min(0f)]
        public float MoveSquashAmount = 0.08f;

        // ──────────────────────────────────────────
        // 레이어
        // ──────────────────────────────────────────

        [Header("── 레이어 ──────────────────────")]

        /// <summary>
        /// 벽/장애물 레이어.
        /// Rigidbody2D 충돌로 이동이 막힘 (Physics2D 설정 필요).
        /// </summary>
        [Tooltip("벽/장애물 레이어. Physics2D Layer Collision Matrix 설정 필요.")]
        public LayerMask WallLayer;

        /// <summary>
        /// 적 레이어. 대시 중 피격 판정에 사용.
        /// </summary>
        [Tooltip("적 레이어. 대시 중 피격 판정 등에 사용.")]
        public LayerMask EnemyLayer;
    }
}