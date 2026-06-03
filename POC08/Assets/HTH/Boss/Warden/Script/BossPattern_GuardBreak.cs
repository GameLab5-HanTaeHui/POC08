// ============================================================
// BossPattern_GuardBreak.cs  v1.0
// Boss_Warden 가드 → 강타 패턴
//
// [흐름]
//   Warning 전반부 (guardBreakGuardDuration):
//     가드 자세 유지 — 예고선 없음
//     정면 피격 봉인도 무효 (_isGuarding = true)
//   Warning 후반부:
//     정면 직사각형 예고 디스크 표시
//   Active (빠른 강타):
//     오른팔 DOLocalMove 전방 타격
//     OverlapBox 히트박스
//   Recovery (긴 후딜):
//     취약 구간 — recoveryVulnMultiplier 배율은 AI가 처리
//     OnPatternGroggy 발행
//
// [2페이즈]: 가드 구간 단축 (0.5초) + 히트박스 확장
// [연결 부위] 오른팔 (RightArm)
// [그로기 유발] Recovery 완료 시
// [namespace] SEAL
// ============================================================

namespace SEAL
{
    using System.Collections;
    using UnityEngine;
    using DG.Tweening;

    /// <summary>
    /// Boss_Warden 가드 → 강타 패턴. (v1.0)
    /// </summary>
    public class BossPattern_GuardBreak : BossPatternBase
    {
        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private BossWardenAI _ai;
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 오른팔 Transform ──────────────────────")]

        /// <summary>
        /// 오른팔 Transform.
        /// DOLocalMove 로 전방 강타 연출.
        /// </summary>
        [Tooltip("오른팔 Transform. RightArm 오브젝트 연결.")]
        [SerializeField] private Transform _armRTransform;

        [Header("── 히트박스 ──────────────────────")]
        [SerializeField] private LayerMask _playerLayer;

        // ── 내부 상태 ──
        private Vector3 _armOriginLocalPos;
        private bool _isPhase2;

        /// <summary>
        /// 가드 중 여부.
        /// true 시 이 패턴의 정면 피격은 봉인도 무효.
        /// BossWardenArmPart 에서 별도 체크하지 않고,
        /// 이 플래그를 public 으로 노출하여 외부에서 참조.
        /// </summary>
        public bool IsGuarding { get; private set; }

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            if (_armRTransform != null)
                _armOriginLocalPos = _armRTransform.localPosition;

            _triggerGroggyOnRecovery = true;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            float guardDuration = _isPhase2 ? 0.5f : _data.guardBreakGuardDuration;
            float totalWarning = _warningDuration;

            // 전반부: 가드 자세 (예고 없음)
            IsGuarding = true;
            yield return StartCoroutine(WaitForPattern(guardDuration));
            if (_isInterrupted)
            {
                IsGuarding = false;
                yield break;
            }

            // 후반부: 예고 디스크 표시
            IsGuarding = false;

            Vector2 dir = _ai != null ? _ai.FacingDir : Vector2.right;
            Vector2 warningSize = _isPhase2 ? new Vector2(1.8f, 1.0f) : _data.guardBreakWarningSize;
            _attackRange?.ShowGuardBreakDisc(transform.position, dir, warningSize);

            float remainingWarning = totalWarning - guardDuration;
            yield return StartCoroutine(WaitForPattern(Mathf.Max(0f, remainingWarning)));
        }

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_data == null) yield break;

            _attackRange?.HideGuardBreakDisc();

            Vector2 dir = _ai != null ? _ai.FacingDir : Vector2.right;

            // 오른팔 빠른 전방 타격
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(
                        _armOriginLocalPos + new Vector3(dir.x * 0.8f, dir.y * 0.8f, 0f),
                        0.1f)
                    .SetEase(Ease.OutExpo);
            }

            yield return new WaitForSeconds(0.1f);

            // OverlapBox 히트박스
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Vector2 boxSize = _isPhase2
                ? new Vector2(1.2f, 0.8f)
                : _data.guardBreakHitboxSize;

            Vector2 boxCenter = (Vector2)transform.position + dir * (boxSize.y * 0.5f);

            Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, angle, _playerLayer);
            if (hit != null)
                Debug.Log("[BossPattern_GuardBreak] 플레이어 피격!");

            // 팔 원위치 복귀
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armOriginLocalPos, 0.15f)
                    .SetEase(Ease.OutQuart);
            }
        }

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 긴 후딜 — recoveryVulnMultiplier 는 BossWardenAI 가 SetArmsRecoveryVuln 으로 처리
            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        public override void Interrupt()
        {
            IsGuarding = false;
            _attackRange?.HideGuardBreakDisc();

            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armOriginLocalPos, 0.1f);

            base.Interrupt();
        }
    }
}