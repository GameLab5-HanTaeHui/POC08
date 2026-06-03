// ============================================================
// BossPattern_Slam.cs  v2.0
// Boss_Warden 내려치기 패턴 — 전면 재작성
//
// [v2.0 디테일링 — 원하는 흐름 구현]
//   기존: 수직 움직임만, 디스크 위치 이상함
//   변경:
//     Warning: 플레이어 위치 스냅 → 디스크 정확히 플레이어 위치에 고정
//              왼팔이 플레이어 방향으로 향하도록 DOMove (팔이 플레이어를 "조준")
//              팔 뒤로 젖히는 백스윙 모션 (준비 동작)
//     Active:  팔이 플레이어 위치(월드)로 빠르게 뻗음 → 히트박스 활성
//              팔이 목표 위치까지 실제로 이동하는 "내려치기" 연출
//     Recovery: 팔 원위치 복귀 + DOShakePosition
//
// [레이어]
//   _playerLayer = EnemyAttackHitBox 레이어 (플레이어 HurtBox 레이어)
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
    /// Boss_Warden 내려치기 패턴. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning:
    ///     ① 플레이어 현재 위치 스냅 (이후 고정)
    ///     ② 예고 디스크를 플레이어 위치에 정확히 배치
    ///     ③ 왼팔 → 플레이어 방향으로 로컬 위치 조준 이동 (백스윙)
    ///     ④ DOColor 주황 Pulse (긴장감)
    ///
    ///   Active:
    ///     ① 팔을 목표 월드 위치까지 DOMove (내려치기 모션)
    ///     ② 팔이 목표 도달 시점에 OverlapCircle 히트박스 활성
    ///     ③ 디스크 플래시 후 제거
    ///
    ///   Recovery:
    ///     ① 팔 원위치 복귀 DOLocalMove
    ///     ② DOShakePosition 충격 연출
    ///     ③ DOColor 원래 색상 복귀
    /// ────────────────────────────────────────────────────
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

        [Header("── 왼팔 Transform / Renderer ──────────────────────")]

        /// <summary>
        /// 왼팔 Transform.
        /// Warning 시 플레이어 방향 조준 이동.
        /// Active 시 목표 위치까지 내려치기.
        /// </summary>
        [Tooltip("왼팔 Transform. LeftArm 오브젝트 연결.")]
        [SerializeField] private Transform _armLTransform;

        /// <summary>
        /// 왼팔 SpriteRenderer.
        /// Warning 중 DOColor 주황 Pulse 연출.
        /// </summary>
        [Tooltip("왼팔 SpriteRenderer. 색상 연출용.")]
        [SerializeField] private SpriteRenderer _armLRenderer;

        [Header("── 레이어 ──────────────────────")]

        /// <summary>
        /// 플레이어 HurtBox 레이어 마스크.
        /// EnemyAttackHitBox 레이어 선택.
        ///
        /// [레이어 6분리 구조]
        ///   EnemyAttack    : 보스 공격 발생원 (이 패턴 자체)
        ///   EnemyAttackHitBox : 플레이어가 맞는 HurtBox 레이어 ← 이 값
        /// </summary>
        [Tooltip("플레이어 HurtBox 레이어. EnemyAttackHitBox 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 (Inspector 조정) ──────────────────────")]

        /// <summary>
        /// 팔이 플레이어 방향으로 뻗는 거리 (로컬 기준 오프셋 배율).
        /// 값이 클수록 팔이 더 멀리 뻗음.
        /// </summary>
        [Tooltip("백스윙 당기는 거리 배율. 권장: 0.3")]
        [Min(0f)]
        [SerializeField] private float _windupPullAmount = 0.3f;

        /// <summary>
        /// 팔이 목표 위치까지 이동하는 시간 (초).
        /// 짧을수록 빠른 내려치기 느낌.
        /// </summary>
        [Tooltip("내려치기 이동 시간 (초). 권장: 0.15")]
        [Min(0.05f)]
        [SerializeField] private float _slamMoveDuration = 0.15f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 왼팔 원래 로컬 위치 (Awake 에서 캐싱). </summary>
        private Vector3 _armOriginLocalPos;

        /// <summary> 왼팔 원래 색상. </summary>
        private Color _armOriginColor;

        /// <summary>
        /// Warning 시 스냅한 플레이어 월드 위치.
        /// Active 에서 팔이 이 위치로 이동하여 내려침.
        /// </summary>
        private Vector2 _slamTarget0;
        private Vector2 _slamTarget1;

        private bool _isPhase2;
        private Tweener _armColorTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            if (_armLTransform != null)
                _armOriginLocalPos = _armLTransform.localPosition;

            if (_armLRenderer != null)
                _armOriginColor = _armLRenderer.color;

            _triggerGroggyOnRecovery = false;
        }

        private void OnDestroy()
        {
            _armColorTween?.Kill();
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 플레이어 조준 + 백스윙 + 예고 디스크
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            // ① 플레이어 현재 위치 스냅 (경고 시작 시 고정)
            _slamTarget0 = GetPlayerPosition();
            _slamTarget1 = _slamTarget0;

            // ② 디스크를 플레이어 위치에 정확히 배치
            _attackRange?.ShowSlamDisc(_slamTarget0, _data.slamWarningRadius, 0);

            // ③ 보스→플레이어 방향 계산
            Vector2 toPlayer = (_slamTarget0 - (Vector2)_armLTransform.position).normalized;

            // ④ 백스윙: 팔을 플레이어 반대 방향으로 당김 (준비 동작)
            //    로컬 좌표 기준: 플레이어 방향의 반대로 _windupPullAmount 만큼 이동
            Vector3 windupLocalOffset = new Vector3(-toPlayer.x, -toPlayer.y, 0f) * _windupPullAmount;
            if (_armLTransform != null)
            {
                _armLTransform
                    .DOLocalMove(_armOriginLocalPos + windupLocalOffset, _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            // ⑤ 팔 색상 주황 Pulse (긴장감)
            if (_armLRenderer != null && _data != null)
            {
                _armColorTween?.Kill();
                _armColorTween = _armLRenderer
                    .DOColor(_data.colorWarning, _data.pulsePeriod * 0.5f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 팔이 실제로 목표 위치까지 뻗어서 내려침
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;

            // 색상 Pulse 정지
            _armColorTween?.Kill();

            // 첫 번째 내려치기
            yield return StartCoroutine(ExecuteSlam(_slamTarget0, 0));
            if (_isInterrupted) yield break;

            // 2페이즈: 0.5초 후 두 번째 내려치기
            if (_isPhase2)
            {
                yield return StartCoroutine(WaitForPattern(0.4f));
                if (_isInterrupted) yield break;

                // 두 번째 위치: 현재 플레이어 위치 갱신
                _slamTarget1 = GetPlayerPosition();
                _attackRange?.ShowSlamDisc(_slamTarget1, _data.slamWarningRadius, 1);

                yield return StartCoroutine(WaitForPattern(0.15f)); // 짧은 예고
                yield return StartCoroutine(ExecuteSlam(_slamTarget1, 1));
            }
        }

        /// <summary>
        /// 단일 내려치기 실행.
        ///
        /// [연출 순서]
        ///   ① 팔을 목표 월드 위치로 DOMove (OutExpo — 빠른 내려치기)
        ///   ② 팔이 목표에 도달하는 시점에 OverlapCircle 히트박스 활성
        ///   ③ 디스크 플래시 후 제거
        /// </summary>
        private IEnumerator ExecuteSlam(Vector2 targetWorldPos, int discIndex)
        {
            if (_isInterrupted || _armLTransform == null) yield break;

            // 팔을 목표 월드 위치로 DOMove
            // Move 가 아닌 실제 월드 좌표 이동
            _armLTransform
                .DOMove(new Vector3(targetWorldPos.x, targetWorldPos.y, _armLTransform.position.z),
                        _slamMoveDuration)
                .SetEase(Ease.OutExpo);

            // 팔이 목표에 도달할 때까지 대기
            yield return new WaitForSeconds(_slamMoveDuration);

            if (_isInterrupted) yield break;

            // 디스크 플래시
            _attackRange?.FlashAndHideSlamDisc(discIndex);

            // OverlapCircle 히트박스 — EnemyAttackHitBox 레이어 감지
            if (_data != null)
            {
                Collider2D hit = Physics2D.OverlapCircle(
                    targetWorldPos,
                    _data.slamHitRadius,
                    _playerLayer);

                if (hit != null)
                    Debug.Log($"[BossPattern_Slam] 내려치기 피격! | 목표:{targetWorldPos}");
            }

            // 팔 살짝 바운스 (OutBounce — 타격감)
            if (_armLTransform != null)
            {
                _armLTransform
                    .DOLocalMove(_armOriginLocalPos + Vector3.down * 0.1f, 0.08f)
                    .SetEase(Ease.OutBounce);
            }
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 복귀 + 충격 연출
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 복귀
            if (_armLTransform != null)
            {
                _armLTransform
                    .DOLocalMove(_armOriginLocalPos, _recoveryDuration * 0.5f)
                    .SetEase(Ease.OutBack);
            }

            // 팔 색상 원래 색상으로 복귀
            if (_armLRenderer != null && _data != null)
            {
                _armColorTween?.Kill();
                _armColorTween = _armLRenderer
                    .DOColor(_armOriginColor, _data.colorTransitionDuration)
                    .SetUpdate(true);
            }

            // 본체 DOShakePosition (충격 연출)
            transform.DOShakePosition(
                duration: 0.3f,
                strength: 0.25f,
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
            _armColorTween?.Kill();
            _attackRange?.HideSlamDisc(0);
            _attackRange?.HideSlamDisc(1);

            // 팔 즉시 원위치
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);

            // 팔 색상 복귀
            if (_armLRenderer != null)
                _armLRenderer.DOColor(_armOriginColor, 0.1f).SetUpdate(true);

            base.Interrupt();
        }

        // ══════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════

        private Vector2 GetPlayerPosition()
        {
            return (_ai != null && _ai.PlayerTransform != null)
                ? (Vector2)_ai.PlayerTransform.position
                : (Vector2)transform.position;
        }
    }
}