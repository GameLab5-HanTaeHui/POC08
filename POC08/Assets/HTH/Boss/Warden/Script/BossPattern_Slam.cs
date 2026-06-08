// ============================================================
// BossPattern_Slam.cs  v3.3
// Boss_Warden 내려치기 패턴 — 팔 던지기 + 공략 타임
//
// [v3.3 — 색상 코드 제거 (BossWardenFeedback 위임)]
//   제거: _armLRenderer / _armColorTween / _armOriginColor
//   제거: Warning DOColor Pulse
//   제거: 공략 타임 DOColor Yoyo
//   제거: Recovery 팔 색상 복귀
//   제거: Interrupt 팔 색상 복귀
//   → 팔 색상은 SealableComponent + BossWardenFeedback 이 전담
//
//   Warning AttackRange 점멸 추가 (v1.2 AttackRange 연동)
//   → ShowSlamDisc() 후 StartSlamPulse() 호출
//   Active 진입 시 점멸 중단
//   → StopAllPulse() 호출
//
// [v3.2 유지 — 원복]
//   팔 Z축 회전 DOLocalRotate (+90f 오프셋) — 플레이어 방향 향함
//   InverseTransformDirection 로컬 좌표 변환
//   플레이어 위치 스냅 (_slamTarget0) 로직
//   SetParent(null/bossTransform) 팔 분리/재부착
//   공략 타임 SetSlamVuln 배율
//   Scale 캐싱/복구 (_armLOriginLocalScale)
//   DOPunchPosition 진동 연출
//   귀환 DOMove + 회전 초기화
//   2페이즈: 두 번째 내려치기 + 공략 타임 단축
//
// [연결 부위] 왼팔 (LeftArm)
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 내려치기 패턴 — 팔 던지기 + 공략 타임. (v3.3)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning : 플레이어 위치 스냅 → 디스크 배치 + 점멸 → 팔 백스윙 + 회전
    ///   Active  : StopAllPulse → 팔 분리 → 플레이어 위치 꽂기 → 공략 타임 → 귀환
    ///   Recovery: 팔 원위치 보정 → 충격 연출
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossPattern_Slam : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]

        [Tooltip("BossWardenAttackRange. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        [Tooltip("BossWardenAI. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAI _ai;

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 왼팔 Transform ──────────────────────")]

        /// <summary>
        /// 왼팔 Transform.
        /// Active 중 SetParent(null) 으로 분리 → DOMove 이동 → 재부착.
        /// 색상 제어 없음 (SealableComponent 전담).
        /// </summary>
        [Tooltip("왼팔 Transform. LeftArm 오브젝트 연결. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;

        [Header("── 레이어 ──────────────────────")]

        [Tooltip("플레이어 HurtBox 레이어. PlayerAttackHitBox 레이어 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 ──────────────────────")]

        /// <summary>Warning 백스윙 당기는 거리 (로컬 단위).</summary>
        [Tooltip("백스윙 당기는 거리. 권장: 0.5")]
        [Min(0f)]
        [SerializeField] private float _windupPullAmount = 0.5f;

        /// <summary>백스윙 들어올리는 높이 (로컬 Y 오프셋).</summary>
        [Tooltip("백스윙 들어올리는 높이. 권장: 0.3")]
        [Min(0f)]
        [SerializeField] private float _windupLiftAmount = 0.3f;

        /// <summary>팔이 목표 위치까지 이동하는 시간 (초).</summary>
        [Tooltip("내려치기 이동 시간 (초). 권장: 0.15")]
        [Min(0.05f)]
        [SerializeField] private float _slamMoveDuration = 0.15f;

        /// <summary>팔이 꽂혀있는 공략 타임 지속 시간 (초).</summary>
        [Tooltip("팔 공략 타임 지속 시간 (초). 권장: 2.0")]
        [Min(0.5f)]
        [SerializeField] private float _slamVulnDuration = 2.0f;

        /// <summary>공략 타임 중 봉인도 누적 배율.</summary>
        [Tooltip("공략 타임 봉인도 배율. 권장: 2.0")]
        [Min(1f)]
        [SerializeField] private float _slamVulnMultiplier = 2.0f;

        /// <summary>팔 귀환 이동 시간 (초).</summary>
        [Tooltip("팔 귀환 이동 시간 (초). 권장: 0.25")]
        [Min(0.05f)]
        [SerializeField] private float _returnDuration = 0.25f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>왼팔 원래 로컬 위치 (Awake 에서 캐싱).</summary>
        private Vector3 _armOriginLocalPos;

        /// <summary>왼팔 원래 로컬 스케일. SetParent 재부착 시 복구용.</summary>
        private Vector3 _armOriginLocalScale;

        /// <summary>보스 본체 Transform (팔 재부착 대상).</summary>
        private Transform _bossTransform;

        /// <summary>보스 Rigidbody2D (월드 위치 참조).</summary>
        private Rigidbody2D _rigid2D;

        /// <summary>Warning 시 스냅한 플레이어 월드 위치 (1번째).</summary>
        private Vector2 _slamTarget0;

        /// <summary>2페이즈 두 번째 내려치기 목표 위치.</summary>
        private Vector2 _slamTarget1;

        /// <summary>현재 2페이즈 여부.</summary>
        private bool _isPhase2;

        /// <summary>팔이 분리된 상태 여부. Interrupt 시 재부착 처리에 사용.</summary>
        private bool _isArmDetached;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            _rigid2D = GetComponentInParent<Rigidbody2D>();
            _bossTransform = _rigid2D != null ? _rigid2D.transform : transform.parent;

            if (_armLTransform != null)
            {
                _armOriginLocalPos = _armLTransform.localPosition;
                _armOriginLocalScale = _armLTransform.localScale;
            }

            _triggerGroggyOnRecovery = false;
        }

        private void OnDestroy()
        {
            ReattachArm();
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 플레이어 위치 스냅 + 디스크 점멸 + 팔 백스윙 + 회전
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            // ① 플레이어 현재 위치 스냅 (이후 고정)
            _slamTarget0 = GetPlayerPos();

            // ② 예고 디스크 배치 + 점멸 시작 (v3.3: StopAllPulse 는 Active 에서)
            _attackRange?.ShowSlamDisc(_slamTarget0, _data.slamHitRadius, 0);
            _attackRange?.StartSlamPulse(0);

            // ③ 보스→플레이어 방향 계산 (월드 기준)
            Vector2 bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            Vector2 toPlayer = (_slamTarget0 - bossPos).normalized;

            // ④ 팔 백스윙: 플레이어 반대 방향 + 들어올리기 (로컬 좌표 변환)
            Vector3 windupLocalDir = Vector3.zero;
            if (_bossTransform != null)
            {
                Vector3 worldBackDir = new Vector3(-toPlayer.x, -toPlayer.y, 0f);
                windupLocalDir = _bossTransform.InverseTransformDirection(worldBackDir);
            }
            else
            {
                windupLocalDir = new Vector3(-toPlayer.x, -toPlayer.y, 0f);
            }

            Vector3 windupOffset = windupLocalDir * _windupPullAmount
                                 + new Vector3(0f, _windupLiftAmount, 0f);

            if (_armLTransform != null)
            {
                // 위치: 플레이어 반대로 당기기
                _armLTransform
                    .DOLocalMove(_armOriginLocalPos + windupOffset, _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);

                // v3.2: 팔이 플레이어를 바라보도록 Z 회전
                // Vector.Down 이 플레이어 방향을 향함 → + 90f 오프셋
                float lookAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg + 90f;
                _armLTransform
                    .DOLocalRotate(new Vector3(0f, 0f, lookAngle), _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 팔 분리 → 꽂기 → 공략 타임 → 귀환
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_data == null || _armLTransform == null) yield break;

            // 점멸 중단 (v3.3)
            _attackRange?.StopAllPulse();

            // 첫 번째 내려치기
            yield return StartCoroutine(ExecuteThrow(_slamTarget0, 0));
            if (_isInterrupted) yield break;

            // 2페이즈: 귀환 직후 두 번째 내려치기
            if (_isPhase2)
            {
                _slamTarget1 = GetPlayerPos();
                _attackRange?.ShowSlamDisc(_slamTarget1, _data.slamHitRadius, 1);
                _attackRange?.StartSlamPulse(1);

                yield return StartCoroutine(WaitForPattern(0.2f));
                if (_isInterrupted) yield break;

                _attackRange?.StopAllPulse();
                yield return StartCoroutine(ExecuteThrow(_slamTarget1, 1));
            }
        }

        /// <summary>
        /// 단일 팔 던지기 실행 코루틴.
        ///
        /// [순서]
        ///   ① 팔 SetParent(null) 분리
        ///   ② DOMove → 목표 위치로 꽂기 + DOLocalRotate(+90f) 방향 회전
        ///   ③ 충격 OverlapCircle + 디스크 플래시
        ///   ④ 공략 타임 (봉인도 배율 활성) + DOPunchPosition 진동
        ///   ⑤ DOMove → 보스 위치로 귀환 + 회전 초기화
        ///   ⑥ SetParent(bossTransform) 재부착 + localPosition/Scale 보정
        /// </summary>
        private IEnumerator ExecuteThrow(Vector2 targetWorldPos, int discIndex)
        {
            if (_isInterrupted || _armLTransform == null) yield break;
            if (_data == null) yield break;

            // ① 팔 분리
            _armLTransform.SetParent(null, worldPositionStays: true);
            _isArmDetached = true;

            // ② 팔 → 목표 위치로 꽂기 DOMove + 방향 회전
            //    + 90f 오프셋: Vector.Down 이 목표 방향 (손바닥 아래 이미지 기준)
            Vector2 flyDir = (targetWorldPos - (Vector2)_armLTransform.position).normalized;
            float targetAngle = Mathf.Atan2(flyDir.y, flyDir.x) * Mathf.Rad2Deg + 90f;

            _armLTransform
                .DOMove(new Vector3(targetWorldPos.x, targetWorldPos.y, _armLTransform.position.z),
                        _slamMoveDuration)
                .SetEase(Ease.OutExpo);

            _armLTransform
                .DOLocalRotate(new Vector3(0f, 0f, targetAngle), _slamMoveDuration * 0.5f)
                .SetEase(Ease.OutQuart);

            yield return new WaitForSecondsRealtime(_slamMoveDuration);
            if (_isInterrupted) { ReattachArm(); yield break; }

            // ③ 충격 히트박스 + 디스크 플래시
            _attackRange?.FlashAndHideSlamDisc(discIndex);

            Collider2D hit = Physics2D.OverlapCircle(targetWorldPos, _data.slamHitRadius, _playerLayer);
            if (hit != null)
                Debug.Log($"[BossPattern_Slam] 내려치기 피격! | 목표:{targetWorldPos}");

            // ④ 공략 타임 — 팔이 꽂힌 채 대기 + 취약 배율 활성
            float vulnDuration = _isPhase2 ? _slamVulnDuration * 0.6f : _slamVulnDuration;

            _armLTransform
                .DOPunchPosition(
                    new Vector3(0.05f, 0.05f, 0f),
                    vulnDuration,
                    vibrato: 20,
                    elasticity: 0.5f)
                .SetUpdate(true);

            var armPart = _armLTransform.GetComponent<BossWardenArmPart>();
            armPart?.SetSlamVuln(true, _slamVulnMultiplier);

            yield return new WaitForSecondsRealtime(vulnDuration);

            armPart?.SetSlamVuln(false, 1f);

            if (_isInterrupted) { ReattachArm(); yield break; }

            // ⑤ 팔 귀환 — 보스 현재 위치로 DOMove + 회전 초기화
            Vector3 bossCurrentPos = _bossTransform != null
                ? _bossTransform.position
                : Vector3.zero;

            Vector3 returnWorldPos = bossCurrentPos + _armOriginLocalPos;

            _armLTransform
                .DOMove(returnWorldPos, _returnDuration)
                .SetEase(Ease.InBack);

            _armLTransform
                .DORotate(Vector3.zero, _returnDuration * 0.7f)
                .SetEase(Ease.OutQuart);

            yield return new WaitForSecondsRealtime(_returnDuration);

            // ⑥ 재부착
            ReattachArm();
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 보정 + 충격 연출
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 보정 (귀환 후 미세 오차 수정)
            if (_armLTransform != null && !_isArmDetached)
            {
                _armLTransform
                    .DOLocalMove(_armOriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);
            }

            // 본체 충격 흔들림
            transform.DOShakePosition(
                    duration: 0.3f,
                    strength: 0.2f,
                    vibrato: 10,
                    randomness: 90f)
                .SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            _attackRange?.StopAllPulse();
            _attackRange?.HideSlamDisc(0);
            _attackRange?.HideSlamDisc(1);

            ReattachArm();
            base.Interrupt();
        }

        // ══════════════════════════════════════════════════════
        // 팔 재부착 공용 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 분리된 팔을 보스에게 즉시 재부착한다.
        ///
        /// [worldPositionStays = true]
        ///   재부착 시 현재 월드 위치 유지 후 localPosition / localScale 강제 보정.
        /// </summary>
        private void ReattachArm()
        {
            if (!_isArmDetached || _armLTransform == null) return;
            if (_bossTransform == null) return;

            _armLTransform.DOKill();
            _armLTransform.SetParent(_bossTransform, worldPositionStays: true);
            _armLTransform.localPosition = _armOriginLocalPos;
            _armLTransform.localRotation = Quaternion.identity;
            _armLTransform.localScale = _armOriginLocalScale;

            var armPart = _armLTransform.GetComponent<BossWardenArmPart>();
            armPart?.SetSlamVuln(false, 1f);

            _isArmDetached = false;
            Debug.Log("[BossPattern_Slam] 팔 재부착 완료");
        }

        // ══════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════

        private Vector2 GetPlayerPos()
        {
            return (_ai != null && _ai.PlayerTransform != null)
                ? (Vector2)_ai.PlayerTransform.position
                : (_rigid2D != null ? _rigid2D.position : (Vector2)transform.position);
        }
    }
}