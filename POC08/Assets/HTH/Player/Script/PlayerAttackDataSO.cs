// ============================================================
// PlayerAttackDataSO.cs  v1.0
// 플레이어 공격 수치 ScriptableObject
//
// [역할]
//   PlayerAttackController 에서 참조하는 공격 수치 보관.
//   기본 공격(A) / 강공격(A 홀드 릴리즈) / 히트스톱 수치 포함.
//   DOTween 무기 연출 수치 포함.
//
// [생성 방법]
//   Project 창 우클릭 → Create → SEAL/Player → Player Attack Data
//
// [설계 원칙]
//   DataSO 는 수치와 설정만 보관한다.
//   런타임 로직을 포함하지 않는다.
//
// [네임스페이스]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 플레이어 공격 전체 수치를 보관하는 ScriptableObject. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [사용법]
    ///   Create → SEAL/Player → Player Attack Data 로 생성.
    ///   PlayerAttackController._data 에 연결.
    /// ────────────────────────────────────────────────────
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlayerAttackData",
        menuName = "SEAL/Player/Player Attack Data",
        order = 1)]
    public class PlayerAttackDataSO : ScriptableObject
    {
        // ──────────────────────────────────────────
        // 기본 공격 (A 탭)
        // ──────────────────────────────────────────

        [Header("── 기본 공격 ──────────────────────")]

        /// <summary>
        /// 기본 공격 봉인도 누적량.
        /// 적중 1회당 대상 봉인도에 더해지는 값.
        /// README: 기본 공격 적중 시 봉인도 누적.
        /// </summary>
        [Tooltip("기본 공격 1회 적중 시 봉인도 누적량.")]
        [Min(1f)]
        public float BasicSealGaugeAmount = 10f;

        /// <summary>
        /// 기본 공격 히트박스 활성 시간 (초).
        /// Swing DOTween 이 타격 지점에 도달한 이후 유지 시간.
        /// </summary>
        [Tooltip("기본 공격 히트박스 활성 유지 시간 (초). 권장: 0.08~0.15.")]
        [Range(0.05f, 0.5f)]
        public float BasicHitboxDuration = 0.1f;

        /// <summary>
        /// 기본 공격 쿨타임 (초).
        /// 공격 완료 후 이 시간이 지나야 다음 공격 가능.
        /// 0이면 연타 가능 (콤보 윈도우로 제어).
        /// </summary>
        [Tooltip("기본 공격 쿨타임 (초). 0 = 콤보 윈도우로만 제어.")]
        [Min(0f)]
        public float BasicAttackCooldown = 0f;

        /// <summary>
        /// 기본 공격 전체 지속 시간 (초).
        /// 이 시간 동안 재입력 없으면 콤보 초기화.
        /// </summary>
        [Tooltip("기본 공격 한 회 전체 지속 시간 (초). 권장: 0.3~0.5.")]
        [Range(0.1f, 1f)]
        public float BasicAttackDuration = 0.35f;

        /// <summary>
        /// 최대 연속 콤보 횟수.
        /// 이 횟수까지 연속 입력 시 콤보 배율 적용.
        /// </summary>
        [Tooltip("최대 연속 콤보 횟수. 권장: 3.")]
        [Min(1)]
        public int MaxComboCount = 3;

        /// <summary>
        /// 콤보 입력 허용 시작 비율.
        /// BasicAttackDuration 의 이 비율 이후부터 다음 공격 입력 받음.
        /// 예: 0.5 = 모션의 절반 지점부터 콤보 입력 허용.
        /// </summary>
        [Tooltip("콤보 입력 허용 시작 비율. 0.5 = 모션 50% 이후부터 입력 받음.")]
        [Range(0.1f, 0.9f)]
        public float ComboWindowStartRatio = 0.5f;

        /// <summary>
        /// 콤보 단계별 봉인도 배율.
        /// 인덱스 0 = 1콤보, 1 = 2콤보, 2 = 3콤보.
        /// </summary>
        [Tooltip("콤보 단계별 봉인도 배율. 인덱스 0=1콤보, 1=2콤보, 2=3콤보.")]
        public float[] ComboSealMultipliers = { 1.0f, 1.2f, 1.5f };

        // ──────────────────────────────────────────
        // 강공격 (A 홀드 후 릴리즈)
        // ──────────────────────────────────────────

        [Header("── 강공격 (A 홀드 후 릴리즈) ──────────────────────")]

        /// <summary>
        /// 강공격으로 인정되는 최소 홀드 시간 (초).
        /// 이 시간보다 짧게 누르면 기본 공격으로 처리.
        /// </summary>
        [Tooltip("강공격 최소 홀드 시간 (초). 권장: 0.3~0.5.")]
        [Range(0.1f, 1f)]
        public float ChargeMinHoldTime = 0.35f;

        /// <summary>
        /// 강공격 봉인도 누적량.
        /// 기본 공격보다 높게 설정. README: 적중 시 높은 봉인도 누적.
        /// </summary>
        [Tooltip("강공격 1회 적중 시 봉인도 누적량.")]
        [Min(1f)]
        public float ChargeSealGaugeAmount = 30f;

        /// <summary>
        /// 강공격 히트박스 활성 시간 (초).
        /// 기본 공격보다 넓고 오래 활성.
        /// </summary>
        [Tooltip("강공격 히트박스 활성 유지 시간 (초). 권장: 0.15~0.25.")]
        [Range(0.05f, 0.5f)]
        public float ChargeHitboxDuration = 0.2f;

        /// <summary>
        /// 강공격 히트박스 크기 배율.
        /// 기본 히트박스 크기에 이 값을 곱함.
        /// </summary>
        [Tooltip("강공격 히트박스 크기 배율. 기본 히트박스 대비 크기.")]
        [Min(1f)]
        public float ChargeHitboxScale = 1.5f;

        // ──────────────────────────────────────────
        // 히트박스
        // ──────────────────────────────────────────

        [Header("── 히트박스 ──────────────────────")]

        /// <summary>
        /// 히트박스 감지 반경 (units).
        /// 공격 방향으로 이 거리 안의 적을 감지.
        /// OverlapCircle 방식 사용.
        /// </summary>
        [Tooltip("히트박스 감지 반경 (units). 권장: 0.8~1.2.")]
        [Min(0.1f)]
        public float HitboxRadius = 1.0f;

        /// <summary>
        /// 히트박스 중심 오프셋 (공격 방향 기준).
        /// 플레이어 위치에서 공격 방향으로 이 거리만큼 앞에 히트박스 생성.
        /// </summary>
        [Tooltip("히트박스 중심 오프셋. 플레이어 앞 이 거리에 판정 생성. 권장: 0.8~1.2.")]
        [Min(0f)]
        public float HitboxOffset = 1.0f;

        /// <summary>
        /// 공격 가능한 레이어마스크.
        /// Enemy 레이어 등록 필요.
        /// </summary>
        [Tooltip("공격 가능 레이어마스크. Enemy 레이어 등록 필요.")]
        public LayerMask HitLayer;

        // ──────────────────────────────────────────
        // 히트스톱
        // ──────────────────────────────────────────

        [Header("── 히트스톱 ──────────────────────")]

        /// <summary>
        /// 공격 적중 시 히트스톱 지속 시간 (실시간 초).
        /// Time.timeScale 을 낮춰 일시 정지 효과.
        /// 0이면 비활성.
        /// README: 히트스톱 발생.
        /// </summary>
        [Tooltip("공격 적중 시 히트스톱 시간 (실시간). 0 = 비활성. 권장: 0.04~0.08.")]
        [Range(0f, 0.3f)]
        public float HitStopDuration = 0.05f;

        /// <summary>
        /// 히트스톱 중 TimeScale 값.
        /// 0 = 완전 정지. 권장: 0.0~0.05.
        /// </summary>
        [Tooltip("히트스톱 중 TimeScale. 0 = 완전 정지. 권장: 0.0~0.05.")]
        [Range(0f, 0.2f)]
        public float HitStopTimeScale = 0.02f;

        /// <summary>
        /// 강공격 적중 시 히트스톱 지속 시간.
        /// 기본 공격보다 길게 설정.
        /// </summary>
        [Tooltip("강공격 적중 시 히트스톱 시간. 기본 공격보다 길게.")]
        [Range(0f, 0.3f)]
        public float ChargeHitStopDuration = 0.1f;

        // ──────────────────────────────────────────
        // DOTween 무기 연출
        // ──────────────────────────────────────────

        [Header("── DOTween 무기 연출 ──────────────────────")]

        /// <summary>
        /// 백스윙 이동 시간 (초).
        /// 공격 전 무기를 뒤로 당기는 모션 시간.
        /// </summary>
        [Tooltip("백스윙 이동 시간 (초). 권장: 0.05~0.1.")]
        [Range(0.01f, 0.3f)]
        public float BackswingDuration = 0.07f;

        /// <summary>
        /// 타격 이동 시간 (초).
        /// 백스윙 후 전방으로 휩쓰는 시간.
        /// </summary>
        [Tooltip("타격 이동 시간 (초). 권장: 0.06~0.12.")]
        [Range(0.01f, 0.3f)]
        public float SwingDuration = 0.09f;

        /// <summary>
        /// 복귀 이동 시간 (초).
        /// 타격 후 원래 위치로 돌아오는 시간.
        /// </summary>
        [Tooltip("복귀 이동 시간 (초). 권장: 0.1~0.2.")]
        [Range(0.01f, 0.5f)]
        public float RecoverDuration = 0.15f;

        /// <summary>
        /// 백스윙 당기기 거리 (units).
        /// 공격 방향 반대로 이 거리만큼 무기를 당김.
        /// </summary>
        [Tooltip("백스윙 당기기 거리 (units). 권장: 0.2~0.4.")]
        [Min(0f)]
        public float BackswingDistance = 0.3f;

        /// <summary>
        /// 타격 전진 거리 (units).
        /// 공격 방향으로 이 거리만큼 무기가 뻗어나감.
        /// </summary>
        [Tooltip("타격 전진 거리 (units). 권장: 0.6~1.0.")]
        [Min(0f)]
        public float SwingDistance = 0.8f;

        /// <summary>
        /// 타격 시 무기 스케일 펀치 강도.
        /// DOTween PunchScale 로 타격감 연출.
        /// 0이면 비활성.
        /// </summary>
        [Tooltip("타격 스케일 펀치 강도. 0 = 비활성. 권장: 0.2~0.4.")]
        [Min(0f)]
        public float SwingPunchScale = 0.3f;

        /// <summary>
        /// 강공격 전진 거리 배율.
        /// SwingDistance 에 이 값을 곱해 강공격은 더 멀리 뻗어나감.
        /// </summary>
        [Tooltip("강공격 전진 거리 배율. SwingDistance 에 곱함. 권장: 1.5~2.0.")]
        [Min(1f)]
        public float ChargeSwingDistanceMultiplier = 1.8f;

        /// <summary>
        /// 강공격 홀드 중 무기 충전 맥동(Pulse) 강도.
        /// 홀드 중 무기 오브젝트가 커졌다 작아지는 효과.
        /// 0이면 비활성.
        /// </summary>
        [Tooltip("강공격 홀드 중 맥동 강도. 0 = 비활성. 권장: 0.1~0.2.")]
        [Min(0f)]
        public float ChargePulseScale = 0.15f;

        /// <summary>
        /// 강공격 홀드 중 맥동 주기 (초).
        /// </summary>
        [Tooltip("강공격 홀드 맥동 주기 (초). 권장: 0.2~0.4.")]
        [Range(0.1f, 1f)]
        public float ChargePulsePeriod = 0.25f;

        // ──────────────────────────────────────────
        // 플레이어 넉포워드 (공격 시 소량 전진)
        // ──────────────────────────────────────────

        [Header("── 공격 전진 (Knock Forward) ──────────────────────")]

        /// <summary>
        /// 공격 시 플레이어가 공격 방향으로 소량 전진하는 거리.
        /// 탑뷰 근접 공격의 타격감 강화.
        /// 0이면 비활성.
        /// </summary>
        [Tooltip("공격 시 플레이어 전진 거리 (units). 0 = 비활성. 권장: 0.1~0.3.")]
        [Min(0f)]
        public float AttackLungeDistance = 0.2f;

        /// <summary>
        /// 공격 전진 지속 시간 (초).
        /// DOTween 으로 이 시간 동안 전진.
        /// </summary>
        [Tooltip("공격 전진 지속 시간 (초). 권장: 0.06~0.1.")]
        [Range(0.01f, 0.3f)]
        public float AttackLungeDuration = 0.07f;
    }
}