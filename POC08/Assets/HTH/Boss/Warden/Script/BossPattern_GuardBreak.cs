// ============================================================
// BossPattern_GuardBreak.cs  v3.0
// Boss_Warden 가드 → 강타 패턴 — 로컬 오프셋 정확화 + IsGuarding 연동 + WaitForSecondsRealtime
//
// [v3.0 변경]
//   기존(v2.0) 문제점:
//     🔴 GetBossWorldPos() 매 Active마다 GetComponentInParent 호출 → Awake 캐싱으로 수정
//     🔴 가드 자세 오프셋이 월드 dir 을 로컬에 그대로 적용 → 로컬 Y+ 고정 오프셋으로 수정
//     🔴 Active WaitForSeconds → TimeScale 영향 → WaitForSecondsRealtime 으로 교체
//     🟡 IsGuarding 정면 봉인도 무효 BossWardenArmPart 연동 추가
//     🟡 Recovery 본체 색상 중복 처리 (BossWardenFeedback 과 겹침) → 팔 색상만 복귀
//     🟡 Recovery 반동 DOShakePosition 추가
//
// [가드 오프셋 설계 — 로컬 Y+ 고정]
//   쿼터뷰 Hierarchy 구성 기준:
//   팔(LeftArm/RightArm) 이 Boss_Warden 의 자식이고
//   Boss_Warden 의 "정면" 이 로컬 Y+ 방향으로 세팅되어 있을 때:
//
//     가드 자세 (앞으로 내밀기) : Vector3(0, +guardForwardAmount, 0)
//     백스윙 (뒤로 당기기)      : Vector3(0, -windupPullAmount, 0)
//     찌르기 (앞으로 뻗기)      : Vector3(0, +thrustDistance, 0)
//
//   [Inspector에서 조정 가능]
//   _guardLocalForward 가 Inspector에서 조정 가능한 벡터.
//   기본값 = (0, 1, 0) = 로컬 Y+ 방향.
//   Hierarchy 구성에 따라 변경 가능.
//
// [IsGuarding 정면 봉인도 무효 연동]
//   BossWardenArmPart 에 _guardBreakPattern 필드 추가 필요.
//   현재: BossWardenArmPart.HandlePlayerHit() 에서 IsGuarding 체크.
//         정면 판단: AI.FacingDir 과 (플레이어→보스) 방향 Dot > 0.5f 이면 정면.
//
// [레이어]
//   _playerLayer = PlayerAttackHitBox 레이어
//
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
    /// Boss_Warden 가드 → 강타 패턴. (v3.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning 전반부: 양팔 로컬 Y+ 방향 가드 자세 + 흰 발광 + IsGuarding = true
    ///   Warning 후반부: 오른팔 로컬 Y- 백스윙 → 예고 디스크 → 주황 전환
    ///   Active:         오른팔 로컬 Y+ 빠른 찌르기 → OverlapBox → 타격 여운
    ///   Recovery:       팔 원위치 → DOShakePosition 반동 → 긴 후딜 → 그로기 유도
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

        [Tooltip("BossWardenAI.")]
        [SerializeField] private BossWardenAI _ai;

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 Transform / Renderer ──────────────────────")]

        /// <summary>
        /// 오른팔 Transform.
        /// 백스윙 + 찌르기 연출 주체.
        /// </summary>
        [Tooltip("오른팔 Transform.")]
        [SerializeField] private Transform _armRTransform;

        /// <summary>
        /// 왼팔 Transform.
        /// 가드 자세 시 앞으로 이동.
        /// </summary>
        [Tooltip("왼팔 Transform.")]
        [SerializeField] private Transform _armLTransform;

        /// <summary>오른팔 SpriteRenderer. 색상 연출용.</summary>
        [Tooltip("오른팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armRRenderer;

        /// <summary>왼팔 SpriteRenderer. 가드 색상 연출용.</summary>
        [Tooltip("왼팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armLRenderer;

        /// <summary>보스 본체 SpriteRenderer. 흰 발광 연출용.</summary>
        [Tooltip("보스 본체 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _bodyRenderer;

        [Header("── 레이어 ──────────────────────")]

        /// <summary>
        /// 플레이어 HurtBox 레이어 마스크.
        /// PlayerAttackHitBox 레이어 선택.
        /// </summary>
        [Tooltip("플레이어 HurtBox 레이어. PlayerAttackHitBox 레이어 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 로컬 오프셋 방향 ──────────────────────")]

        /// <summary>
        /// 팔 "정면" 로컬 방향 벡터.
        /// 기본값 = (0, 1, 0) = 로컬 Y+ 방향.
        /// Boss_Warden 의 정면이 Y+ 가 아닌 경우 Inspector 에서 조정.
        ///
        /// [설정 방법]
        ///   Boss_Warden 정면이 로컬 Y+ → (0, 1, 0) (기본)
        ///   Boss_Warden 정면이 로컬 X+ → (1, 0, 0) 으로 변경
        /// </summary>
        [Tooltip("팔 정면 로컬 방향. 기본 (0,1,0). Boss_Warden 정면 방향에 맞게 조정.")]
        [SerializeField] private Vector3 _guardLocalForward = new Vector3(0f, 1f, 0f);

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

        /// <summary>찌르기 이동 시간 (초). TimeScale 무관 (WaitForSecondsRealtime 사용).</summary>
        [Tooltip("찌르기 이동 시간 (초). 권장: 0.08")]
        [Min(0.03f)]
        [SerializeField] private float _thrustDuration = 0.08f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 오른팔 원래 로컬 위치. </summary>
        private Vector3 _armROriginLocalPos;

        /// <summary> 왼팔 원래 로컬 위치. </summary>
        private Vector3 _armLOriginLocalPos;

        /// <summary> 오른팔 원래 색상. </summary>
        private Color _armROriginColor;

        /// <summary> 왼팔 원래 색상. </summary>
        private Color _armLOriginColor;

        /// <summary>
        /// 보스 Rigidbody2D.
        /// Awake 에서 캐싱 (매 프레임 GetComponentInParent 방지).
        /// </summary>
        private Rigidbody2D _rigid2D;

        /// <summary> 2페이즈 여부. </summary>
        private bool _isPhase2;

        /// <summary>
        /// 가드 중 여부.
        /// true 시 정면에서 날아오는 플레이어 공격 봉인도 무효.
        /// BossWardenArmPart._guardBreakPattern 연동으로 체크.
        /// </summary>
        public bool IsGuarding { get; private set; }

        private Tweener _bodyColorTween;
        private Tweener _armRColorTween;
        private Tweener _armLColorTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInParent<SpriteRenderer>();

            // ✅ v3.0 수정: Awake 에서 캐싱 (매 프레임 GetComponentInParent 방지)
            _rigid2D = GetComponentInParent<Rigidbody2D>();

            if (_armRTransform != null) _armROriginLocalPos = _armRTransform.localPosition;
            if (_armLTransform != null) _armLOriginLocalPos = _armLTransform.localPosition;
            if (_armRRenderer != null) _armROriginColor = _armRRenderer.color;
            if (_armLRenderer != null) _armLOriginColor = _armLRenderer.color;

            _triggerGroggyOnRecovery = true;
        }

        private void OnDestroy()
        {
            _bodyColorTween?.Kill();
            _armRColorTween?.Kill();
            _armLColorTween?.Kill();
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 가드 자세 → 백스윙 → 예고
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            float guardDuration = _isPhase2 ? 0.5f : _data.guardBreakGuardDuration;
            float remainingWarning = _warningDuration - guardDuration;

            // ──────────────────────────────────────────
            // 전반부: 가드 자세 — IsGuarding = true
            // ──────────────────────────────────────────
            IsGuarding = true;

            // ✅ v3.0 수정: 로컬 Y+ 고정 오프셋 사용 (월드 dir 미적용)
            // 이유: 월드 방향을 로컬 오프셋에 그대로 더하면
            //       보스가 회전했을 때 의도와 다른 방향으로 팔이 이동함.
            Vector3 guardOffset = _guardLocalForward.normalized * _guardForwardAmount;

            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + guardOffset, guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);

            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos + guardOffset, guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);

            // 양팔 + 본체 흰 발광 (단단한 가드)
            if (_bodyRenderer != null)
            {
                _bodyColorTween?.Kill();
                _bodyColorTween = _bodyRenderer
                    .DOColor(Color.white, guardDuration * 0.3f)
                    .SetUpdate(true);
            }
            if (_armLRenderer != null)
            {
                _armLColorTween?.Kill();
                _armLColorTween = _armLRenderer
                    .DOColor(Color.white, guardDuration * 0.3f)
                    .SetUpdate(true);
            }
            if (_armRRenderer != null)
            {
                _armRColorTween?.Kill();
                _armRColorTween = _armRRenderer
                    .DOColor(Color.white, guardDuration * 0.3f)
                    .SetUpdate(true);
            }

            yield return StartCoroutine(WaitForPattern(guardDuration));

            if (_isInterrupted)
            {
                IsGuarding = false;
                yield break;
            }

            // ──────────────────────────────────────────
            // 후반부: 백스윙 + 예고 디스크
            // ──────────────────────────────────────────
            IsGuarding = false;

            // ✅ v3.0 수정: 로컬 Y- 백스윙 (로컬 정면 반대 방향)
            Vector3 backswingOffset = -_guardLocalForward.normalized * _windupPullAmount;

            // 오른팔만 뒤로 당김 (왼팔은 가드 유지)
            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + backswingOffset, 0.15f)
                    .SetEase(Ease.OutBack);

            // 왼팔은 원위치 (가드 해제)
            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos, 0.15f)
                    .SetEase(Ease.OutQuart);

            // 본체 + 오른팔 주황 전환
            if (_bodyRenderer != null)
            {
                _bodyColorTween?.Kill();
                _bodyColorTween = _bodyRenderer
                    .DOColor(_data.colorWarning, 0.1f)
                    .SetUpdate(true);
            }
            if (_armRRenderer != null)
            {
                _armRColorTween?.Kill();
                _armRColorTween = _armRRenderer
                    .DOColor(_data.colorWarning, 0.1f)
                    .SetUpdate(true);
            }
            if (_armLRenderer != null)
            {
                _armLColorTween?.Kill();
                _armLColorTween = _armLRenderer
                    .DOColor(_armLOriginColor, 0.1f)
                    .SetUpdate(true);
            }

            // 예고 디스크 표시 (보스 정면 방향)
            Vector2 bossWorldPos = _rigid2D != null
                ? _rigid2D.position
                : (Vector2)transform.position;

            Vector2 facingDir = _ai != null ? _ai.FacingDir : Vector2.up;
            Vector2 warningSize = _isPhase2
                ? new Vector2(1.8f, 1.0f)
                : _data.guardBreakWarningSize;

            _attackRange?.ShowGuardBreakDisc(bossWorldPos, facingDir, warningSize);

            yield return StartCoroutine(WaitForPattern(Mathf.Max(0f, remainingWarning)));
        }

        // ══════════════════════════════════════════════════════
        // Active — 오른팔 로컬 Y+ 빠른 찌르기 + 히트박스
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_data == null) yield break;

            _attackRange?.HideGuardBreakDisc();

            // 오른팔 순간 흰색 (공격 시작)
            if (_armRRenderer != null)
            {
                _armRColorTween?.Kill();
                _armRRenderer.color = Color.white;
            }

            // ✅ v3.0 수정: 로컬 Y+ 방향 찌르기 (_guardLocalForward 기준)
            // 이유: 보스가 회전해도 로컬 방향은 항상 정면을 가리킴.
            Vector3 thrustOffset = _guardLocalForward.normalized * _thrustDistance;

            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + thrustOffset, _thrustDuration)
                    .SetEase(Ease.OutExpo);
            }

            // ✅ v3.0 수정: WaitForSeconds → WaitForSecondsRealtime
            // 이유: 슬로우 모션 중 WaitForSeconds 는 늘어나서 찌르기 타이밍이 왜곡됨.
            yield return new WaitForSecondsRealtime(_thrustDuration);
            if (_isInterrupted) yield break;

            // OverlapBox 히트박스 — 보스 정면 방향 기준
            Vector2 bossWorldPos = _rigid2D != null
                ? _rigid2D.position
                : (Vector2)transform.position;

            Vector2 facingDir = _ai != null ? _ai.FacingDir : Vector2.up;
            float angle = Mathf.Atan2(facingDir.y, facingDir.x) * Mathf.Rad2Deg;

            Vector2 boxSize = _isPhase2
                ? new Vector2(1.2f, 0.8f)
                : _data.guardBreakHitboxSize;

            Vector2 boxCenter = bossWorldPos + facingDir * (boxSize.y * 0.5f);

            Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, angle, _playerLayer);
            if (hit != null)
                Debug.Log("[BossPattern_GuardBreak] 찌르기 피격!");

            // 타격 여운: 0.05초 유지 후 60% 위치로 복귀
            yield return new WaitForSecondsRealtime(0.05f);
            if (_isInterrupted) yield break;

            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + thrustOffset * 0.6f, 0.1f)
                    .SetEase(Ease.OutQuart);
            }
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 + 반동 + 긴 후딜 + 그로기 유도
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 복귀
            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.4f)
                    .SetEase(Ease.OutBack);

            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.4f)
                    .SetEase(Ease.OutBack);

            // 오른팔 색상 복귀 (팔만 — 본체는 BossWardenFeedback 이 담당)
            if (_armRRenderer != null)
            {
                _armRColorTween?.Kill();
                _armRColorTween = _armRRenderer
                    .DOColor(_armROriginColor, _data?.colorTransitionDuration ?? 0.1f)
                    .SetUpdate(true);
            }
            if (_armLRenderer != null)
            {
                _armLColorTween?.Kill();
                _armLColorTween = _armLRenderer
                    .DOColor(_armLOriginColor, _data?.colorTransitionDuration ?? 0.1f)
                    .SetUpdate(true);
            }

            // ✅ v3.0 추가: 찌르기 반동 DOShakePosition
            if (transform.parent != null)
            {
                transform.parent.DOShakePosition(
                    duration: 0.25f,
                    strength: 0.15f,
                    vibrato: 8,
                    randomness: 90f)
                    .SetUpdate(true);
            }

            // 긴 후딜 — recoveryVulnMultiplier 는 BossWardenAI 가 처리
            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            IsGuarding = false;
            _bodyColorTween?.Kill();
            _armRColorTween?.Kill();
            _armLColorTween?.Kill();
            _attackRange?.HideGuardBreakDisc();

            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armROriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armLOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);

            // 색상 즉시 복귀
            if (_armRRenderer != null)
                _armRRenderer.DOColor(_armROriginColor, 0.1f).SetUpdate(true);
            if (_armLRenderer != null)
                _armLRenderer.DOColor(_armLOriginColor, 0.1f).SetUpdate(true);

            base.Interrupt();
        }
    }
}