// ============================================================
// BossPattern_GuardBreak.cs  v2.0
// Boss_Warden 가드 → 강타 패턴 — 전면 재작성
//
// [v2.0 디테일링]
//   기존: 팔이 앞으로 조금 이동하는 것이 전부
//   변경:
//     Warning 전반부 (가드 자세):
//       ① 양팔을 앞으로 모아 가드 자세 DOLocalMove
//       ② 본체 흰 발광 (단단한 가드 느낌)
//       ③ IsGuarding = true
//
//     Warning 후반부 (공격 예고):
//       ① 오른팔을 더 뒤로 당김 (백스윙 준비)
//       ② 예고 디스크를 플레이어 방향에 표시
//       ③ 색상 주황으로 전환
//
//     Active (찌르기):
//       ① 오른팔을 플레이어 방향으로 빠르게 뻗음 (OutExpo)
//       ② 뻗은 시점에 OverlapBox 히트박스 활성화
//       ③ 0.1초 유지 후 팔 약간 복귀 (타격 후 잔상)
//
//     Recovery:
//       ① 양팔 원위치 복귀
//       ② 긴 후딜 (취약 구간)
//       ③ OnPatternGroggy 발행
//
// [레이어]
//   _playerLayer = EnemyAttackHitBox 레이어
//
// [연결 부위] 오른팔 (RightArm)
// [그로기 유발] Recovery 완료 시
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 가드 → 강타 패턴. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning 전반부: 양팔 앞으로 → 가드 자세 + 흰 발광
    ///   Warning 후반부: 오른팔 뒤로 백스윙 → 플레이어 방향 예고 디스크
    ///   Active: 오른팔 플레이어 방향 빠른 찌르기 → 히트박스
    ///   Recovery: 팔 복귀 → 긴 후딜 → 그로기 유도
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossPattern_GuardBreak : BossPatternBase
    {
        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private BossWardenAI _ai;
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 Transform / Renderer ──────────────────────")]

        /// <summary>오른팔 Transform. 찌르기 연출 주체.</summary>
        [Tooltip("오른팔 Transform.")]
        [SerializeField] private Transform _armRTransform;

        /// <summary>왼팔 Transform. 가드 자세 시 앞으로 이동.</summary>
        [Tooltip("왼팔 Transform.")]
        [SerializeField] private Transform _armLTransform;

        [SerializeField] private SpriteRenderer _armRRenderer;
        [SerializeField] private SpriteRenderer _bodyRenderer;

        [Header("── 레이어 ──────────────────────")]

        /// <summary>
        /// 플레이어 HurtBox 레이어 마스크.
        /// EnemyAttackHitBox 레이어 선택.
        /// </summary>
        [Tooltip("플레이어 HurtBox 레이어. EnemyAttackHitBox 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 ──────────────────────")]

        /// <summary>가드 자세 시 팔이 앞으로 이동하는 거리.</summary>
        [Tooltip("가드 자세 팔 전진 거리. 권장: 0.4")]
        [Min(0f)]
        [SerializeField] private float _guardForwardAmount = 0.4f;

        /// <summary>찌르기 도달 거리 (로컬 기준 추가 이동량).</summary>
        [Tooltip("찌르기 뻗는 거리. 권장: 1.0")]
        [Min(0f)]
        [SerializeField] private float _thrustDistance = 1.0f;

        /// <summary>찌르기 이동 시간 (초). 짧을수록 빠름.</summary>
        [Tooltip("찌르기 이동 시간. 권장: 0.08")]
        [Min(0.03f)]
        [SerializeField] private float _thrustDuration = 0.08f;

        // ── 내부 상태 ──
        private Vector3 _armROriginLocalPos;
        private Vector3 _armLOriginLocalPos;
        private Color _armROriginColor;
        private bool _isPhase2;
        private Tweener _bodyColorTween;
        private Tweener _armColorTween;

        /// <summary>가드 중 여부. BossWardenArmPart 에서 참조.</summary>
        public bool IsGuarding { get; private set; }

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInParent<SpriteRenderer>();

            if (_armRTransform != null) _armROriginLocalPos = _armRTransform.localPosition;
            if (_armLTransform != null) _armLOriginLocalPos = _armLTransform.localPosition;
            if (_armRRenderer != null) _armROriginColor = _armRRenderer.color;

            _triggerGroggyOnRecovery = true;
        }

        private void OnDestroy()
        {
            _bodyColorTween?.Kill();
            _armColorTween?.Kill();
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
            float totalWarning = _warningDuration;
            Vector2 dir = _ai != null ? _ai.FacingDir : Vector2.right;

            // ──── 전반부: 가드 자세 ────────────────────────────
            IsGuarding = true;

            // 양팔을 보스 정면 방향으로 앞으로 모음
            Vector3 guardOffset = new Vector3(dir.x * _guardForwardAmount, dir.y * _guardForwardAmount, 0f);
            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armROriginLocalPos + guardOffset, guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armLOriginLocalPos + guardOffset, guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);

            // 본체 흰 발광 (단단한 가드)
            if (_bodyRenderer != null)
            {
                _bodyColorTween?.Kill();
                _bodyColorTween = _bodyRenderer
                    .DOColor(Color.white, guardDuration * 0.3f)
                    .SetUpdate(true);
            }

            yield return StartCoroutine(WaitForPattern(guardDuration));
            if (_isInterrupted) { IsGuarding = false; yield break; }

            // ──── 후반부: 백스윙 + 예고 디스크 ────────────────
            IsGuarding = false;

            // 방향 갱신 (플레이어가 이동했을 수 있음)
            dir = _ai != null ? _ai.FacingDir : Vector2.right;

            // 오른팔 뒤로 당김 (백스윙 — 찌르기 준비)
            Vector3 backswingOffset = new Vector3(-dir.x * 0.6f, -dir.y * 0.6f, 0f);
            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armROriginLocalPos + backswingOffset, 0.15f)
                    .SetEase(Ease.OutBack);

            // 왼팔은 약간 뒤로 (자연스럽게)
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armLOriginLocalPos, 0.15f).SetEase(Ease.OutQuart);

            // 본체 주황 전환
            if (_bodyRenderer != null)
            {
                _bodyColorTween?.Kill();
                _bodyColorTween = _bodyRenderer
                    .DOColor(_data.colorWarning, 0.1f)
                    .SetUpdate(true);
            }

            // 오른팔 주황
            if (_armRRenderer != null)
            {
                _armColorTween?.Kill();
                _armColorTween = _armRRenderer
                    .DOColor(_data.colorWarning, 0.1f)
                    .SetUpdate(true);
            }

            // 예고 디스크 표시 (보스 → 플레이어 방향)
            Vector2 warningSize = _isPhase2 ? new Vector2(1.8f, 1.0f) : _data.guardBreakWarningSize;
            _attackRange?.ShowGuardBreakDisc(GetBossWorldPos(), dir, warningSize);

            float remainingWarning = totalWarning - guardDuration;
            yield return StartCoroutine(WaitForPattern(Mathf.Max(0f, remainingWarning)));
        }

        // ══════════════════════════════════════════════════════
        // Active — 오른팔 플레이어 방향 빠른 찌르기 + 히트박스
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_data == null) yield break;

            _attackRange?.HideGuardBreakDisc();

            Vector2 dir = _ai != null ? _ai.FacingDir : Vector2.right;

            // 오른팔 흰색 (공격 시작)
            if (_armRRenderer != null)
            {
                _armColorTween?.Kill();
                _armColorTween = _armRRenderer
                    .DOColor(Color.white, 0.03f)
                    .SetUpdate(true);
            }

            // 오른팔 찌르기: 보스 정면 방향으로 _thrustDistance 만큼 빠르게 뻗음
            Vector3 thrustOffset = new Vector3(dir.x * _thrustDistance, dir.y * _thrustDistance, 0f);
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + thrustOffset, _thrustDuration)
                    .SetEase(Ease.OutExpo);
            }

            // 찌르기 완료 대기
            yield return new WaitForSeconds(_thrustDuration);
            if (_isInterrupted) yield break;

            // 히트박스 활성화 — EnemyAttackHitBox 레이어 감지
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Vector2 boxSize = _isPhase2 ? new Vector2(1.2f, 0.8f) : _data.guardBreakHitboxSize;
            Vector2 boxCenter = GetBossWorldPos() + dir * (boxSize.y * 0.5f);

            Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, angle, _playerLayer);
            if (hit != null)
                Debug.Log("[BossPattern_GuardBreak] 찌르기 피격!");

            // 짧은 유지 후 팔 약간 복귀 (타격 여운)
            yield return new WaitForSeconds(0.05f);
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + thrustOffset * 0.6f, 0.1f)
                    .SetEase(Ease.OutQuart);
            }
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 + 긴 후딜 + 그로기 유도
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 복귀
            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.4f).SetEase(Ease.OutBack);
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.4f).SetEase(Ease.OutBack);

            // 색상 복귀
            if (_armRRenderer != null)
            {
                _armColorTween?.Kill();
                _armColorTween = _armRRenderer
                    .DOColor(_armROriginColor, _data?.colorTransitionDuration ?? 0.1f)
                    .SetUpdate(true);
            }
            if (_bodyRenderer != null)
            {
                _bodyColorTween?.Kill();
                _bodyColorTween = _bodyRenderer
                    .DOColor(_data?.colorRecovery ?? Color.red, _data?.colorTransitionDuration ?? 0.1f)
                    .SetUpdate(true);
            }

            // 긴 후딜 — recoveryVulnMultiplier 는 BossWardenAI 가 처리
            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            IsGuarding = false;
            _bodyColorTween?.Kill();
            _armColorTween?.Kill();
            _attackRange?.HideGuardBreakDisc();

            if (_armRTransform != null) _armRTransform.DOLocalMove(_armROriginLocalPos, 0.1f);
            if (_armLTransform != null) _armLTransform.DOLocalMove(_armLOriginLocalPos, 0.1f);

            base.Interrupt();
        }

        // ══════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════

        private Vector2 GetBossWorldPos()
        {
            return GetComponentInParent<Rigidbody2D>()?.position
                   ?? (Vector2)transform.parent.position;
        }
    }
}