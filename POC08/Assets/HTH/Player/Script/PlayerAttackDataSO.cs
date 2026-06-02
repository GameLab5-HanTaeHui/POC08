// ============================================================
// PlayerAttackDataSO.cs  v2.1
// 플레이어 공격 수치 ScriptableObject
//
// [v2.1 변경 — 콤보/강공격 회전 방향 bool 추가]
//   각 콤보와 강공격에 SwingClockwise(bool) 필드 추가.
//   true  = 시계 방향(CW)  : BackRot → AttackRot 을 시계로 회전
//   false = 반시계 방향(CCW): BackRot → AttackRot 을 반시계로 회전
//
//   [회전 방향 제어 원리]
//     DOTween RotateMode.Fast 는 최단 경로를 자동 선택 → 방향 예측 불가.
//     PlayerWeaponSwingController 에서 이 bool 을 읽어
//     RotateMode.LocalAxisAdd 방식으로 "회전량(delta)" 을 직접 계산:
//
//       delta = AttackRot - BackRot  (백스윙 → 타격 사이 각도 차)
//
//       CW  (시계)  : delta > 0 이면 delta -= 360 (강제 시계)
//                     delta == 0 이면 delta = -360 (한 바퀴 시계)
//       CCW (반시계): delta < 0 이면 delta += 360 (강제 반시계)
//                     delta == 0 이면 delta = +360 (한 바퀴 반시계)
//
//     LocalAxisAdd 는 현재 각도에 delta 를 더하는 방식.
//     부호가 곧 회전 방향 → CW = 음수 delta / CCW = 양수 delta.
//
// [SwingClockwise 설정 가이드]
//   Combo1 횡베기  : 위에서 아래로 내리치는 스윙 → true (시계)
//   Combo2 내리찍기: 위에서 아래로 수직 찍기    → true (시계)
//   Combo3 찌르기  : 직선 → 회전 없음 (bool 무시)
//   Charge 강타    : 큰 원호 스윙 → 원하는 방향 선택
//
// [v2.0 변경 — POC07 KeyDataSO 구조 참고, 탑뷰 재설계]
//   기존: 단순 방향 거리값만 존재 → 무기가 슬라이딩만 함
//   변경: 콤보별 백스윙/타격 절대 위치(Vector2) + Z축 회전값 추가
//         탑뷰 8방향 대응 위해 "공격 방향 기준 로컬 좌표" 방식 적용
//
// [탑뷰 좌표계 설계]
//   POC07은 횡스크롤(오른쪽=X, 위=Y) 고정.
//   탑뷰는 공격 방향이 8방향으로 바뀌므로 무기 피벗 오브젝트를
//   공격 방향으로 회전시킨 뒤, 그 로컬 좌표계 기준으로 무기를 이동.
//   따라서 좌표 정의는 "오른쪽(+X)이 공격 방향"으로 통일.
//
// [무기 피벗 구조]
//   PlayerRoot
//   └─ WeaponPivot      ← 이 오브젝트를 공격 방향으로 Z회전
//      └─ Weapon        ← 이 오브젝트를 DOLocalMove + DOLocalRotate
//
// [Combo별 궤적 설계 (오른쪽 공격 기준)]
//   Combo1 - 횡베기   : 뒤에서 앞으로 수평 휩쓸기
//   Combo2 - 내리찍기 : 위에서 아래로 크게 내려찍기
//   Combo3 - 찌르기   : 직선으로 빠르게 뻗었다 튕겨 복귀
//   Charge - 회전 강타: 뒤로 충전 후 크게 원호 스윙
//
// [Z회전 직관 규칙 (Weapon 오브젝트 기준)]
//   0°   = 무기가 오른쪽(공격 방향) 수평
//   +90° = 무기 날이 위를 향함
//   -90° = 무기 날이 아래를 향함
//
// [생성 방법]
//   Project 창 우클릭 → Create → SEAL/Player → Player Attack Data
//
// [네임스페이스]
//   namespace : SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 플레이어 공격 수치 ScriptableObject. (v2.1)
    ///
    /// ────────────────────────────────────────────────────
    /// [무기 피벗 구조]
    ///   WeaponPivot (공격 방향으로 Z회전)
    ///     └─ Weapon (DOLocalMove + DOLocalRotate 연출 대상)
    ///
    /// [좌표 기준]
    ///   모든 위치값 = WeaponPivot 로컬 좌표.
    ///   +X = 공격 방향 전방 / -X = 후방
    ///   +Y = 좌측 / -Y = 우측 (탑뷰 로컬 기준)
    ///
    /// [SwingClockwise 회전 방향 원리]
    ///   true  = 시계(CW)   → delta 가 음수가 되도록 보정
    ///   false = 반시계(CCW) → delta 가 양수가 되도록 보정
    ///   PlayerWeaponSwingController.CalcSwingDelta() 에서 사용.
    /// ────────────────────────────────────────────────────
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlayerAttackData",
        menuName = "SEAL/Player/Player Attack Data",
        order = 1)]
    public class PlayerAttackDataSO : ScriptableObject
    {
        // ──────────────────────────────────────────
        // 공통 타이밍
        // ──────────────────────────────────────────

        [Header("── 공통 타이밍 ──────────────────────")]

        /// <summary>
        /// 백스윙 이동 시간 (초).
        /// 짧을수록 빠른 예비동작. 권장: 0.06~0.10.
        /// </summary>
        [Tooltip("백스윙 이동 시간 (초). 권장: 0.06~0.10.")]
        [Range(0.01f, 0.3f)]
        public float BackswingDuration = 0.15f;

        /// <summary>
        /// 타격 이동 시간 (초).
        /// 짧을수록 날카로운 타격감. 권장: 0.06~0.10.
        /// </summary>
        [Tooltip("타격 이동 시간 (초). 권장: 0.06~0.10.")]
        [Range(0.01f, 0.3f)]
        public float AttackDuration = 0.2f;

        /// <summary>
        /// 복귀 이동 시간 (초).
        /// 타격 후 원점으로 돌아오는 시간. 권장: 0.12~0.20.
        /// </summary>
        [Tooltip("복귀 이동 시간 (초). 권장: 0.12~0.20.")]
        [Range(0.01f, 0.5f)]
        public float ReturnDuration = 0.4f;

        /// <summary>
        /// 최대 연속 콤보 횟수.
        /// </summary>
        [Tooltip("최대 연속 콤보 횟수.")]
        [Min(1)]
        public int MaxComboCount = 3;

        /// <summary>
        /// 콤보 입력 허용 시작 비율.
        /// AttackDuration 의 이 비율 이후부터 다음 콤보 입력 수신.
        /// </summary>
        [Tooltip("콤보 윈도우 시작 비율. 0.5 = 타격 모션 50% 이후 입력 허용.")]
        [Range(0.1f, 0.9f)]
        public float ComboWindowStartRatio = 0.8f;

        // ──────────────────────────────────────────
        // Combo1 — 횡베기 (좌→우 수평 스윙)
        // ──────────────────────────────────────────

        [Header("── Combo1 — 횡베기 ──────────────────────")]

        /// <summary>
        /// Combo1 백스윙 위치.
        /// 공격 방향 후방 + 측면으로 당기는 지점.
        /// X 음수 = 후방 / Y 양수 = 측면.
        /// </summary>
        [Tooltip("Combo1 백스윙 위치. (−X=후방, ±Y=측면).")]
        public Vector2 Combo1BackPos = new Vector2(0.4f, -0.5f);

        /// <summary>
        /// Combo1 타격 위치.
        /// 전방으로 크게 휩쓰는 종착점.
        /// </summary>
        [Tooltip("Combo1 타격 위치. (+X=전방).")]
        public Vector2 Combo1AttackPos = new Vector2(3f, 1f);

        /// <summary>
        /// Combo1 백스윙 Z회전 (도).
        /// 무기를 측면 위로 기울여 횡베기 준비 자세.
        /// </summary>
        [Tooltip("Combo1 백스윙 Z회전. 무기를 옆으로 기울이는 각도.")]
        [Range(-180f, 180f)]
        public float Combo1RotBack = -150f;

        /// <summary>
        /// Combo1 타격 Z회전 (도).
        /// 스윙 완료 후 무기 각도.
        /// </summary>
        [Tooltip("Combo1 타격 Z회전. 스윙 후 무기 각도.")]
        [Range(-180f, 180f)]
        public float Combo1RotAtk = 25f;

        /// <summary>
        /// Combo1 회전 방향.
        /// true  = 시계 방향(CW)  : Rot Back → Rot Atk 를 시계로 회전.
        /// false = 반시계 방향(CCW): Rot Back → Rot Atk 를 반시계로 회전.
        ///
        /// [예시]
        ///   RotBack = -150, RotAtk = 25, Clockwise = true
        ///   delta = 25 - (-150) = 175 → CW 이므로 175 - 360 = -185 (시계 강제)
        /// </summary>
        [Tooltip("Combo1 회전 방향. true=시계(CW) / false=반시계(CCW).")]
        public bool Combo1Clockwise = true;

        /// <summary>
        /// Combo1 봉인도 누적량.
        /// </summary>
        [Tooltip("Combo1 봉인도 누적량.")]
        [Min(1f)]
        public float Combo1SealAmount = 10f;

        // ──────────────────────────────────────────
        // Combo2 — 내리찍기 (위→아래 수직 스윙)
        // ──────────────────────────────────────────

        [Header("── Combo2 — 내리찍기 ──────────────────────")]

        /// <summary>
        /// Combo2 백스윙 위치.
        /// 무기를 머리 위로 들어올리는 지점.
        /// Y 크게 양수.
        /// </summary>
        [Tooltip("Combo2 백스윙 위치. 머리 위로 들어올리는 지점.")]
        public Vector2 Combo2BackPos = new Vector2(1f, 1f);

        /// <summary>
        /// Combo2 타격 위치.
        /// 전방 발 아래 방향으로 찍는 지점.
        /// </summary>
        [Tooltip("Combo2 타격 위치. 전방 아래로 내리찍는 지점.")]
        public Vector2 Combo2AttackPos = new Vector2(3.0f, -1.5f);

        /// <summary>
        /// Combo2 백스윙 Z회전.
        /// 무기를 하늘로 세우는 각도.
        /// </summary>
        [Tooltip("Combo2 백스윙 Z회전. 무기를 위로 세우는 각도.")]
        [Range(-180f, 180f)]
        public float Combo2RotBack = 120f;

        /// <summary>
        /// Combo2 타격 Z회전.
        /// 내리찍힌 후 무기 각도.
        /// </summary>
        [Tooltip("Combo2 타격 Z회전. 내리찍힌 후 각도.")]
        [Range(-180f, 180f)]
        public float Combo2RotAtk = -40f;

        /// <summary>
        /// Combo2 회전 방향.
        /// true  = 시계 방향(CW)  : 위에서 아래로 내려찍는 방향.
        /// false = 반시계 방향(CCW).
        ///
        /// [내리찍기 권장]
        ///   RotBack = 120 (위로 세움), RotAtk = -40 (아래로 찍힘)
        ///   delta = -40 - 120 = -160 → CCW 이므로 -160 + 360 = 200
        ///   하지만 실제로 위 → 아래 내리찍기는 CW(시계) 가 자연스러움.
        ///   Clockwise = true → delta -160, 이미 음수이므로 그대로 사용.
        /// </summary>
        [Tooltip("Combo2 회전 방향. true=시계(CW) / false=반시계(CCW).")]
        public bool Combo2Clockwise = true;

        /// <summary>
        /// Combo2 봉인도 누적량.
        /// </summary>
        [Tooltip("Combo2 봉인도 누적량.")]
        [Min(1f)]
        public float Combo2SealAmount = 12f;

        // ──────────────────────────────────────────
        // Combo3 — 찌르기 피니셔 (직선 돌진)
        // ──────────────────────────────────────────

        [Header("── Combo3 — 찌르기 피니셔 ──────────────────────")]

        /// <summary>
        /// Combo3 백스윙 위치.
        /// 무기를 몸 뒤로 당기는 지점.
        /// </summary>
        [Tooltip("Combo3 백스윙 위치. 몸 뒤로 당기는 지점.")]
        public Vector2 Combo3BackPos = new Vector2(-1.2f, 0.0f);

        /// <summary>
        /// Combo3 타격 위치.
        /// 전방 최대 사거리 찌르기 지점.
        /// </summary>
        [Tooltip("Combo3 타격 위치. 전방 최대 찌르기.")]
        public Vector2 Combo3AttackPos = new Vector2(2.8f, 0.0f);

        // ─ Combo3 은 직선 찌르기이므로 Z회전과 회전 방향 bool 없음 ─

        /// <summary>
        /// Combo3 봉인도 누적량 (피니셔 — 가장 높음).
        /// </summary>
        [Tooltip("Combo3 봉인도 누적량 (피니셔). 가장 높게.")]
        [Min(1f)]
        public float Combo3SealAmount = 18f;

        /// <summary>
        /// Combo3 히트스톱 지속 시간 (실시간 초).
        /// 피니셔는 더 강한 히트스톱.
        /// </summary>
        [Tooltip("Combo3(피니셔) 히트스톱 시간. 권장: 0.08~0.12.")]
        [Range(0f, 0.3f)]
        public float Combo3HitStopDuration = 0.10f;

        // ──────────────────────────────────────────
        // 강공격 — 회전 강타
        // ──────────────────────────────────────────

        [Header("── 강공격 — 회전 강타 ──────────────────────")]

        /// <summary>
        /// 강공격으로 인정되는 최소 홀드 시간 (초).
        /// 이 시간 미만 홀드 시 기본 공격 처리.
        /// </summary>
        [Tooltip("강공격 최소 홀드 시간 (초). 권장: 0.3~0.5.")]
        [Range(0.1f, 1f)]
        public float ChargeMinHoldTime = 0.5f;

        /// <summary>
        /// 강공격 백스윙 위치.
        /// 무기를 몸 뒤 멀리 당기는 지점.
        /// </summary>
        [Tooltip("강공격 백스윙 위치. 뒤로 크게 당기는 지점.")]
        public Vector2 ChargeBackPos = new Vector2(-0.5f, 0.5f);

        /// <summary>
        /// 강공격 타격 위치.
        /// 전방 최대 거리로 크게 휩쓰는 지점.
        /// </summary>
        [Tooltip("강공격 타격 위치. 전방 최대 스윙.")]
        public Vector2 ChargeAttackPos = new Vector2(2f, -1f);

        /// <summary>
        /// 강공격 백스윙 Z회전.
        /// 크게 틀어 충전 자세.
        /// </summary>
        [Tooltip("강공격 백스윙 Z회전. 크게 틀어 충전.")]
        [Range(-270f, 180f)]
        public float ChargeRotBack = -120f;

        /// <summary>
        /// 강공격 타격 Z회전.
        /// 크게 휩쓴 후 무기 각도.
        /// </summary>
        [Tooltip("강공격 타격 Z회전. 크게 휩쓴 후 각도.")]
        [Range(-180f, 180f)]
        public float ChargeRotAtk = 0f;

        /// <summary>
        /// 강공격 회전 방향.
        /// true  = 시계 방향(CW)  : BackRot → AttackRot 을 시계로 회전.
        /// false = 반시계 방향(CCW): BackRot → AttackRot 을 반시계로 회전.
        ///
        /// [예시]
        ///   RotBack = -120, RotAtk = 0, Clockwise = false
        ///   delta = 0 - (-120) = 120 → CCW 이고 이미 양수 → 그대로 반시계 120°
        ///
        ///   RotBack = -120, RotAtk = 0, Clockwise = true
        ///   delta = 120 → CW 이므로 120 - 360 = -240 (시계 방향 240° 회전)
        /// </summary>
        [Tooltip("강공격 회전 방향. true=시계(CW) / false=반시계(CCW).")]
        public bool ChargeClockwise = false;

        /// <summary>
        /// 강공격 봉인도 누적량 (콤보 3단계보다 높게).
        /// </summary>
        [Tooltip("강공격 봉인도 누적량. 높게 설정.")]
        [Min(1f)]
        public float ChargeSealAmount = 30f;

        /// <summary>
        /// 강공격 히트스톱 지속 시간 (실시간 초).
        /// </summary>
        [Tooltip("강공격 히트스톱 시간. 권장: 0.10~0.15.")]
        [Range(0f, 0.3f)]
        public float ChargeHitStopDuration = 0.12f;

        /// <summary>
        /// 강공격 히트박스 크기 배율.
        /// 기본 히트박스보다 넓게.
        /// </summary>
        [Tooltip("강공격 히트박스 크기 배율. 권장: 1.5~2.0.")]
        [Min(1f)]
        public float ChargeHitboxScale = 1.8f;

        /// <summary>
        /// 강공격 홀드 중 무기 맥동(Pulse) 강도.
        /// 홀드 중 무기가 커졌다 작아지는 충전 연출.
        /// </summary>
        [Tooltip("강공격 홀드 중 맥동 강도. 0=비활성. 권장: 0.1~0.2.")]
        [Min(0f)]
        public float ChargePulseScale = 0.15f;

        /// <summary>
        /// 강공격 홀드 맥동 주기 (초).
        /// </summary>
        [Tooltip("강공격 홀드 맥동 주기 (초). 권장: 0.2~0.35.")]
        [Range(0.1f, 1f)]
        public float ChargePulsePeriod = 0.25f;

        // ──────────────────────────────────────────
        // 히트박스
        // ──────────────────────────────────────────

        [Header("── 히트박스 ──────────────────────")]

        /// <summary>
        /// 히트박스 감지 반경 (units).
        /// WeaponPivot 기준 전방 공격 위치 주변을 감지.
        /// </summary>
        [Tooltip("히트박스 감지 반경 (units). 권장: 0.6~1.0.")]
        [Min(0.1f)]
        public float HitboxRadius = 0.8f;

        /// <summary>
        /// 히트박스 중심 오프셋.
        /// 플레이어에서 공격 방향으로 이 거리만큼 앞에 판정 생성.
        /// </summary>
        [Tooltip("히트박스 중심 오프셋. 플레이어 앞 이 거리. 권장: 0.8~1.2.")]
        [Min(0f)]
        public float HitboxOffset = 1.0f;

        /// <summary>
        /// 공격 가능 레이어마스크. Enemy 레이어 등록 필요.
        /// </summary>
        [Tooltip("공격 가능 레이어마스크. Enemy 레이어 등록 필요.")]
        public LayerMask HitLayer;

        // ──────────────────────────────────────────
        // 히트스톱 (기본)
        // ──────────────────────────────────────────

        [Header("── 히트스톱 ──────────────────────")]

        /// <summary>
        /// 기본 공격 적중 히트스톱 지속 시간 (실시간 초).
        /// 0이면 비활성.
        /// </summary>
        [Tooltip("기본 공격 히트스톱 시간 (실시간). 0=비활성. 권장: 0.04~0.06.")]
        [Range(0f, 0.3f)]
        public float HitStopDuration = 0.05f;

        /// <summary>
        /// 히트스톱 중 TimeScale. 0=완전 정지.
        /// </summary>
        [Tooltip("히트스톱 중 TimeScale. 0=완전 정지. 권장: 0.0~0.05.")]
        [Range(0f, 0.2f)]
        public float HitStopTimeScale = 0.02f;

        // ──────────────────────────────────────────
        // 플레이어 전진 (Lunge)
        // ──────────────────────────────────────────

        [Header("── 공격 전진 (Lunge) ──────────────────────")]

        /// <summary>
        /// 공격 시 플레이어 Visual 이 공격 방향으로 소량 전진하는 거리.
        /// Rigidbody 가 아닌 Visual Transform 만 이동 (물리계와 분리).
        /// </summary>
        [Tooltip("공격 시 Visual 전진 거리. 0=비활성. 권장: 0.15~0.3.")]
        [Min(0f)]
        public float LungeDistance = 0.2f;

        /// <summary>
        /// 공격 전진 지속 시간 (초).
        /// </summary>
        [Tooltip("공격 전진 지속 시간 (초). 권장: 0.06~0.10.")]
        [Range(0.01f, 0.3f)]
        public float LungeDuration = 0.07f;

        // ══════════════════════════════════════════════════════
        // 유틸리티 메서드 — SwingController 에서 사용
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 백스윙 → 타격 사이의 Z회전 델타를 회전 방향 bool 에 맞게 계산한다.
        ///
        /// [원리]
        ///   DOTween RotateMode.LocalAxisAdd 방식에서
        ///   양수 delta = 반시계(CCW) / 음수 delta = 시계(CW)
        ///
        ///   delta = rotAtk - rotBack  (각도 차)
        ///
        ///   CW (시계) 강제:
        ///     delta > 0 → delta -= 360  (양수이면 시계 방향으로 돌아감)
        ///     delta = 0 → delta = -360  (같은 각도면 시계 한 바퀴)
        ///
        ///   CCW (반시계) 강제:
        ///     delta &lt; 0 → delta += 360  (음수이면 반시계로 돌아감)
        ///     delta = 0 → delta = +360  (같은 각도면 반시계 한 바퀴)
        ///
        /// [PlayerWeaponSwingController 사용 예시]
        ///   float delta = _data.CalcSwingDelta(rotBack, rotAtk, clockwise);
        ///   _weapon.DOLocalRotate(new Vector3(0,0,delta), dur, RotateMode.LocalAxisAdd);
        /// </summary>
        /// <param name="rotBack">백스윙 Z각도 (도).</param>
        /// <param name="rotAtk">타격 Z각도 (도).</param>
        /// <param name="clockwise">true=시계(CW) / false=반시계(CCW).</param>
        /// <returns>LocalAxisAdd 에 전달할 Z회전 델타 (도).</returns>
        public static float CalcSwingDelta(float rotBack, float rotAtk, bool clockwise)
        {
            float delta = rotAtk - rotBack;

            if (clockwise)
            {
                // 시계(CW) 강제 → delta 가 반드시 음수여야 함
                if (delta >= 0f)
                    delta -= 360f;
            }
            else
            {
                // 반시계(CCW) 강제 → delta 가 반드시 양수여야 함
                if (delta <= 0f)
                    delta += 360f;
            }

            return delta;
        }
    }
}