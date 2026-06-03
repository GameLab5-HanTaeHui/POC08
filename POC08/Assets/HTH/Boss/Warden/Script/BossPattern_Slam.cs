// ============================================================
// BossPattern_Slam.cs  v1.0
// Boss_Warden 내려치기 패턴
//
// [흐름]
//   Warning: 플레이어 현재 위치 스냅 → 원형 디스크 표시 + 왼팔 들어올림
//   Active:  왼팔 내려침 (OutBounce) + OverlapCircle 히트
//   Recovery: 왼팔 원위치 복귀 + DOShakeScale
//
// [2페이즈]: 내려치기 2회 연속 (0.5초 간격)
//
// [연결 부위] 왼팔 (LeftArm)
// [그로기 유발] 없음
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 내려치기 패턴. (v1.0)
    /// </summary>
    public class BossPattern_Slam : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]

        [Tooltip("BossWardenAttackRange.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        [Tooltip("BossWardenAI.")]
        [SerializeField] private BossWardenAI _ai;

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 왼팔 Transform ──────────────────────")]

        /// <summary>
        /// 왼팔 Transform.
        /// DOLocalMoveY 로 들어올림 / 내려침 연출.
        /// </summary>
        [Tooltip("왼팔 Transform. LeftArm 오브젝트 연결.")]
        [SerializeField] private Transform _armLTransform;

        [Header("── 히트박스 ──────────────────────")]

        [Tooltip("Player 레이어 마스크.")]
        [SerializeField] private LayerMask _playerLayer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 왼팔 원래 로컬 위치 (Awake 에서 캐싱). </summary>
        private Vector3 _armOriginLocalPos;

        /// <summary> Warning 시 스냅한 플레이어 월드 위치 (1번째). </summary>
        private Vector2 _slamTarget0;

        /// <summary> 2페이즈 두 번째 내려치기 위치. </summary>
        private Vector2 _slamTarget1;

        private bool _isPhase2;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            if (_armLTransform != null)
                _armOriginLocalPos = _armLTransform.localPosition;

            _triggerGroggyOnRecovery = false;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            // 플레이어 위치 스냅 (이후 고정)
            _slamTarget0 = _ai != null && _ai.PlayerTransform != null
                ? (Vector2)_ai.PlayerTransform.position
                : (Vector2)transform.position;

            // 2페이즈: 두 번째 위치는 0.5초 후 결정 (여기서는 동일 위치로 초기화)
            _slamTarget1 = _slamTarget0;

            // 원형 디스크 표시
            _attackRange?.ShowSlamDisc(_slamTarget0, _data.slamWarningRadius, 0);

            // 왼팔 들어올림
            if (_armLTransform != null)
            {
                _armLTransform.DOLocalMoveY(
                    _armOriginLocalPos.y + 0.5f,
                    _warningDuration * 0.5f)
                    .SetEase(Ease.OutQuart);
            }

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;

            // 첫 번째 내려치기
            yield return StartCoroutine(ExecuteSlam(_slamTarget0, 0));
            if (_isInterrupted) yield break;

            // 2페이즈: 0.5초 후 두 번째 내려치기
            if (_isPhase2)
            {
                yield return StartCoroutine(WaitForPattern(0.5f));
                if (_isInterrupted) yield break;

                // 두 번째 위치: 현재 플레이어 위치로 갱신
                _slamTarget1 = _ai != null && _ai.PlayerTransform != null
                    ? (Vector2)_ai.PlayerTransform.position
                    : _slamTarget0;

                _attackRange?.ShowSlamDisc(_slamTarget1, _data.slamWarningRadius, 1);

                yield return StartCoroutine(WaitForPattern(0.2f)); // 짧은 예고
                yield return StartCoroutine(ExecuteSlam(_slamTarget1, 1));
            }
        }

        /// <summary>
        /// 단일 내려치기 실행 코루틴.
        /// 팔 내려침 + 디스크 플래시 + OverlapCircle.
        /// </summary>
        private IEnumerator ExecuteSlam(Vector2 targetPos, int discIndex)
        {
            if (_isInterrupted) yield break;

            float slamDuration = 0.2f;

            // 왼팔 빠르게 내려침
            if (_armLTransform != null)
            {
                _armLTransform.DOLocalMoveY(
                    _armOriginLocalPos.y - 0.3f,
                    slamDuration)
                    .SetEase(Ease.OutBounce);
            }

            // 디스크 플래시
            _attackRange?.FlashAndHideSlamDisc(discIndex);

            yield return new WaitForSeconds(slamDuration);

            // OverlapCircle 히트박스
            Collider2D hit = Physics2D.OverlapCircle(
                targetPos,
                _data != null ? _data.slamHitRadius : 2.5f,
                _playerLayer);

            if (hit != null)
                Debug.Log("[BossPattern_Slam] 플레이어 피격!");
        }

        // ══════════════════════════════════════════════════════
        // Recovery
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 왼팔 원위치 복귀
            if (_armLTransform != null)
            {
                _armLTransform.DOLocalMoveY(
                    _armOriginLocalPos.y,
                    _recoveryDuration * 0.5f)
                    .SetEase(Ease.OutQuart);
            }

            // 본체 DOShakeScale
            transform.DOShakeScale(
                duration: 0.3f,
                strength: 0.1f,
                vibrato: 8)
                .SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        public override void Interrupt()
        {
            _attackRange?.HideSlamDisc(0);
            _attackRange?.HideSlamDisc(1);

            // 팔 강제 원위치
            if (_armLTransform != null)
                _armLTransform.DOLocalMoveY(_armOriginLocalPos.y, 0.1f);

            base.Interrupt();
        }
    }
}
