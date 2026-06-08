// ============================================================
// BossPattern_GuardBreak.cs  v3.3
// Boss_Warden 가드 → 강타 패턴
//
// [v3.3 — 색상 코드 제거 (BossWardenFeedback 위임)]
//   제거: _armRRenderer / _armLRenderer / _bodyRenderer 색상
//   제거: _armRColorTween / _armLColorTween / _bodyColorTween
//   제거: _armROriginColor / _armLOriginColor
//   제거: Warning 가드 흰 발광 + 백스윙 주황 전환
//   제거: Recovery 팔 색상 복귀
//   제거: Interrupt 색상 즉시 복귀
//   → 색상은 SealableComponent + BossWardenFeedback 이 전담
//
//   Warning AttackRange 점멸 추가 (v1.2 AttackRange 연동)
//   → ShowGuardBreakDisc() 후 StartGuardBreakPulse() 호출
//   Active 진입 시 점멸 중단
//   → StopAllPulse() 호출
//
// [v3.2 유지 — 원복]
//   팔 Z축 회전 DOLocalRotate (+90f 오프셋)
//   InverseTransformDirection 로컬 좌표 변환
//   _facingDir / _thrustLocalOff 멤버 필드 (코루틴 간 공유)
//   가드 자세 양팔 위치 이동
//   백스윙 오른팔 위치 이동 + 왼팔 원위치 복귀
//   왼팔 DOLocalRotate(Vector3.zero) 회전 복귀
//   Recovery 양팔 위치 복귀 + DOLocalRotate(Vector3.zero)
//   Recovery DOShakePosition 반동
//   Interrupt 양팔 DOLocalMove + DOLocalRotate 원위치
//
// [IsGuarding]
//   BossWardenArmPart._guardBreakPattern 연동으로
//   정면 봉인도 무효 체크에 사용.
//
// [레이어]
//   _playerLayer = PlayerAttackHitBox 레이어
// [연결 부위] 오른팔 (RightArm)
// [그로기 유발] Recovery 완료 시 (_triggerGroggyOnRecovery = true)
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 가드 → 강타 패턴. (v3.3)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning 전반부: 양팔 가드 자세 + IsGuarding = true
    ///   Warning 후반부: 오른팔 백스윙 → 예고 디스크 점멸
    ///   Active:         StopAllPulse → 오른팔 찌르기 → OverlapBox
    ///   Recovery:       팔 원위치 → 반동 → 긴 후딜
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossPattern_GuardBreak : BossPatternBase
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

        [Header("── 팔 Transform ──────────────────────")]

        /// <summary>
        /// 오른팔 Transform. 백스윙 + 찌르기 연출 주체.
        /// 색상 제어 없음 (SealableComponent 전담).
        /// </summary>
        [Tooltip("오른팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armRTransform;

        /// <summary>왼팔 Transform. 가드 자세 시 앞으로 이동. 색상 제어 없음.</summary>
        [Tooltip("왼팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;

        [Header("── 레이어 ──────────────────────")]

        [Tooltip("플레이어 HurtBox 레이어. PlayerAttackHitBox 레이어 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 ──────────────────────")]

        /// <summary>가드 자세 시 팔이 앞으로 이동하는 거리 (로컬 기준).</summary>
        [Tooltip("가드 자세 앞으로 나오는 거리. 권장: 0.4")]
        [Min(0f)]
        [SerializeField] private float _guardForwardAmount = 0.4f;

        /// <summary>백스윙 뒤로 당기는 거리 (로컬 기준).</summary>
        [Tooltip("백스윙 당기는 거리. 권장: 0.5")]
        [Min(0f)]
        [SerializeField] private float _windupPullAmount = 0.5f;

        /// <summary>찌르기 뻗는 거리 (로컬 기준).</summary>
        [Tooltip("찌르기 뻗는 거리. 권장: 0.8")]
        [Min(0f)]
        [SerializeField] private float _thrustDistance = 0.8f;

        /// <summary>찌르기 이동 시간 (초).</summary>
        [Tooltip("찌르기 이동 시간 (초). 권장: 0.08")]
        [Min(0.03f)]
        [SerializeField] private float _thrustDuration = 0.08f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>오른팔 원래 로컬 위치.</summary>
        private Vector3 _armROriginLocalPos;

        /// <summary>왼팔 원래 로컬 위치.</summary>
        private Vector3 _armLOriginLocalPos;

        /// <summary>보스 Rigidbody2D. Awake 에서 캐싱.</summary>
        private Rigidbody2D _rigid2D;

        /// <summary>2페이즈 여부.</summary>
        private bool _isPhase2;

        /// <summary>
        /// 가드 중 여부.
        /// BossWardenArmPart._guardBreakPattern 연동으로 정면 봉인도 무효 체크.
        /// </summary>
        public bool IsGuarding { get; private set; }

        /// <summary>
        /// Warning 시 계산한 보스 정면 방향 (월드 기준).
        /// Active 에서 OverlapBox 방향 계산에 재사용.
        /// OnWarning() → OnActive() 코루틴 간 공유용 멤버 필드.
        /// </summary>
        private Vector2 _facingDir;

        /// <summary>
        /// Warning 시 계산한 찌르기 로컬 오프셋.
        /// InverseTransformDirection 적용된 값.
        /// OnWarning() → OnActive() 코루틴 간 공유용 멤버 필드.
        /// </summary>
        private Vector3 _thrustLocalOff;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            _rigid2D = GetComponentInParent<Rigidbody2D>();

            if (_armRTransform != null) _armROriginLocalPos = _armRTransform.localPosition;
            if (_armLTransform != null) _armLOriginLocalPos = _armLTransform.localPosition;

            _triggerGroggyOnRecovery = true;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 가드 자세 → 백스윙 → 예고 디스크 점멸
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            float guardDuration = _isPhase2 ? 0.5f : _data.guardBreakGuardDuration;
            float remainingWarning = _warningDuration - guardDuration;

            // ── 전반부: 가드 자세 ──
            IsGuarding = true;

            // v3.2: _facingDir / _thrustLocalOff 멤버 필드에 저장 → OnActive 재사용
            _facingDir = _ai != null ? _ai.FacingDir : Vector2.up;

            Vector3 worldFacingDir = new Vector3(_facingDir.x, _facingDir.y, 0f);
            Vector3 localFacingDir = _rigid2D != null
                ? _rigid2D.transform.InverseTransformDirection(worldFacingDir)
                : worldFacingDir;

            Vector3 guardOffset = localFacingDir.normalized * _guardForwardAmount;
            Vector3 backswingOff = -localFacingDir.normalized * _windupPullAmount;
            _thrustLocalOff = localFacingDir.normalized * _thrustDistance;

            // v3.2: 가드 자세 시 팔 Z 회전 (+90f: Vector.Down 이 플레이어 방향)
            float lookAngle = Mathf.Atan2(_facingDir.y, _facingDir.x) * Mathf.Rad2Deg + 90f;

            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + guardOffset, guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);
                _armRTransform
                    .DOLocalRotate(new Vector3(0f, 0f, lookAngle), guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            if (_armLTransform != null)
            {
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos + guardOffset, guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);
                _armLTransform
                    .DOLocalRotate(new Vector3(0f, 0f, lookAngle), guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            yield return StartCoroutine(WaitForPattern(guardDuration));

            if (_isInterrupted)
            {
                IsGuarding = false;
                yield break;
            }

            // ── 후반부: 백스윙 + 예고 디스크 점멸 ──
            IsGuarding = false;

            // 오른팔만 뒤로 당김 (lookAngle 유지)
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + backswingOff, 0.15f)
                    .SetEase(Ease.OutBack);
            }

            // 왼팔 원위치 복귀 + 회전 초기화
            if (_armLTransform != null)
            {
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos, 0.15f)
                    .SetEase(Ease.OutQuart);
                _armLTransform
                    .DOLocalRotate(Vector3.zero, 0.15f)
                    .SetEase(Ease.OutQuart);
            }

            // 예고 디스크 표시 + 점멸 (v3.3)
            Vector2 bossWorldPos = _rigid2D != null
                ? _rigid2D.position : (Vector2)transform.position;
            Vector2 warningSize = _isPhase2
                ? new Vector2(1.8f, 1.0f) : _data.guardBreakWarningSize;
            float discAngle = Mathf.Atan2(_facingDir.y, _facingDir.x) * Mathf.Rad2Deg;
            Vector2 discCenter = bossWorldPos + _facingDir * (warningSize.y * 0.5f);

            _attackRange?.ShowGuardBreakDisc(discCenter, warningSize, discAngle);
            _attackRange?.StartGuardBreakPulse();

            yield return StartCoroutine(WaitForPattern(Mathf.Max(0f, remainingWarning)));
        }

        // ══════════════════════════════════════════════════════
        // Active — 오른팔 찌르기 + 히트박스
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_data == null) yield break;

            // 점멸 중단 (v3.3)
            _attackRange?.StopAllPulse();
            _attackRange?.HideGuardBreakDisc();

            // v3.2: _thrustLocalOff 는 OnWarning 에서 계산한 멤버 필드 재사용
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + _thrustLocalOff, _thrustDuration)
                    .SetEase(Ease.OutExpo);
            }

            yield return new WaitForSecondsRealtime(_thrustDuration);
            if (_isInterrupted) yield break;

            // OverlapBox 히트박스 (_facingDir 멤버 필드 재사용)
            Vector2 bossWorldPos = _rigid2D != null
                ? _rigid2D.position : (Vector2)transform.position;
            float angle = Mathf.Atan2(_facingDir.y, _facingDir.x) * Mathf.Rad2Deg;

            Vector2 boxSize = _isPhase2
                ? new Vector2(1.2f, 0.8f) : _data.guardBreakHitboxSize;
            Vector2 boxCenter = bossWorldPos + _facingDir * (boxSize.y * 0.5f);

            Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, angle, _playerLayer);
            if (hit != null)
                Debug.Log("[BossPattern_GuardBreak] 찌르기 피격!");

            yield return new WaitForSecondsRealtime(0.05f);
            if (_isInterrupted) yield break;

            // 팔 살짝 복귀
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + _thrustLocalOff * 0.6f, 0.1f)
                    .SetEase(Ease.OutQuart);
            }
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 + 반동 + 긴 후딜
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 복귀 (위치 + 회전)
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.4f)
                    .SetEase(Ease.OutBack);
                // v3.2: 찌르기/가드 회전 복귀
                _armRTransform
                    .DOLocalRotate(Vector3.zero, _recoveryDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            if (_armLTransform != null)
            {
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.4f)
                    .SetEase(Ease.OutBack);
                _armLTransform
                    .DOLocalRotate(Vector3.zero, _recoveryDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            // 찌르기 반동 DOShakePosition
            if (transform.parent != null)
            {
                transform.parent.DOShakePosition(
                    duration: 0.25f,
                    strength: 0.15f,
                    vibrato: 8,
                    randomness: 90f)
                    .SetUpdate(true);
            }

            IsGuarding = false;
            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            IsGuarding = false;
            _attackRange?.StopAllPulse();
            _attackRange?.HideGuardBreakDisc();

            // v3.2: 팔 위치 + 회전 즉시 원위치
            if (_armRTransform != null)
            {
                _armRTransform.DOLocalMove(_armROriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
                _armRTransform.DOLocalRotate(Vector3.zero, 0.1f).SetEase(Ease.OutQuart);
            }
            if (_armLTransform != null)
            {
                _armLTransform.DOLocalMove(_armLOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
                _armLTransform.DOLocalRotate(Vector3.zero, 0.1f).SetEase(Ease.OutQuart);
            }

            base.Interrupt();
        }
    }
}