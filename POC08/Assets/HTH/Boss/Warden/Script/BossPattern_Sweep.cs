// ============================================================
// BossPattern_Sweep.cs  v2.0
// Boss_Warden 회전 스윕 패턴 — 전면 재작성
//
// [v2.0 디테일링]
//   기존: 그냥 본체 회전 + 원형 디스크. 전혀 직관적이지 않음.
//   변경:
//     Warning:
//       ① 양팔이 좌우로 벌어지는 준비 모션 (팔이 넓게 펼침)
//       ② 원형 예고 디스크 표시 (보스 중심)
//       ③ 본체 DOColor 붉은 Pulse
//
//     Active:
//       ① 본체 + 양팔이 함께 회전 (DORotate FastBeyond360)
//       ② 팔 회전 시 팔도 실제로 회전 (부모 본체 회전 따라감)
//       ③ 매 프레임 OverlapCircle 피격 판정
//       ④ 디스크 보스와 함께 이동 (중심 유지)
//
//     Recovery:
//       ① 양팔 원위치 복귀 DOLocalMove
//       ② 본체 DOColor 원래 색상
//
// [레이어]
//   _playerLayer = EnemyAttackHitBox 레이어
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
    /// Boss_Warden 회전 스윕 패턴. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning: 양팔 벌리기 준비 → 붉은 Pulse → 예고 디스크
    ///   Active:  본체 360° 회전 → 팔이 함께 휩쓸기 → 히트박스 판정
    ///   Recovery: 양팔 원위치 복귀 → 색상 복귀
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossPattern_Sweep : BossPatternBase
    {
        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private BossWardenAI _ai;
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 Transform / Renderer ──────────────────────")]

        /// <summary>왼팔 Transform. 스윕 회전 시 팔도 함께 이동.</summary>
        [Tooltip("왼팔 Transform.")]
        [SerializeField] private Transform _armLTransform;

        /// <summary>오른팔 Transform.</summary>
        [Tooltip("오른팔 Transform.")]
        [SerializeField] private Transform _armRTransform;

        [SerializeField] private SpriteRenderer _armLRenderer;
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

        /// <summary>Warning 중 팔이 좌우로 벌어지는 거리 (로컬 X 오프셋).</summary>
        [Tooltip("Warning 팔 벌리기 거리. 권장: 0.5")]
        [Min(0f)]
        [SerializeField] private float _armSpreadAmount = 0.5f;

        // ── 내부 상태 ──
        private Vector3 _armLOriginLocalPos;
        private Vector3 _armROriginLocalPos;
        private Color _armLOriginColor;
        private Color _armROriginColor;
        private bool _isPhase2;
        private bool _isSweeping;
        private Tweener _rotateTween;
        private Tweener _bodyColorTween;

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInParent<SpriteRenderer>();

            if (_armLTransform != null) _armLOriginLocalPos = _armLTransform.localPosition;
            if (_armRTransform != null) _armROriginLocalPos = _armRTransform.localPosition;
            if (_armLRenderer != null) _armLOriginColor = _armLRenderer.color;
            if (_armRRenderer != null) _armROriginColor = _armRRenderer.color;

            _triggerGroggyOnRecovery = false;
        }

        private void OnDestroy()
        {
            _rotateTween?.Kill();
            _bodyColorTween?.Kill();
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 양팔 벌리기 + 예고 디스크 + 붉은 Pulse
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            float radius = _isPhase2 ? _data.sweepWarningRadius + 0.5f : _data.sweepWarningRadius;

            // ① 예고 디스크 표시 (보스 중심)
            _attackRange?.ShowSweepDisc(GetBossWorldPos(), radius);

            // ② 양팔 좌우로 벌리기 (준비 동작)
            //    왼팔: X 방향 왼쪽으로 / 오른팔: X 방향 오른쪽으로
            if (_armLTransform != null)
            {
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos + new Vector3(-_armSpreadAmount, 0.2f, 0f),
                                 _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);
            }
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + new Vector3(_armSpreadAmount, 0.2f, 0f),
                                 _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);
            }

            // ③ 본체 붉은 Pulse (경고)
            if (_bodyRenderer != null && _data != null)
            {
                _bodyColorTween?.Kill();
                _bodyColorTween = _bodyRenderer
                    .DOColor(_data.colorWarning, _data.pulsePeriod * 0.4f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }

            // ④ 팔 색상도 주황으로
            if (_armLRenderer != null && _data != null)
                _armLRenderer.DOColor(_data.colorWarning, _warningDuration * 0.3f).SetUpdate(true);
            if (_armRRenderer != null && _data != null)
                _armRRenderer.DOColor(_data.colorWarning, _warningDuration * 0.3f).SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 본체 + 팔 360° 회전 + 히트박스
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted || _data == null) yield break;

            _bodyColorTween?.Kill();

            // 팔이 벌려진 상태에서 즉시 흰색 (공격 임박 신호)
            if (_bodyRenderer != null)
                _bodyRenderer.DOColor(_data.colorActive, 0.05f).SetUpdate(true);
            if (_armLRenderer != null)
                _armLRenderer.DOColor(Color.white, 0.05f).SetUpdate(true);
            if (_armRRenderer != null)
                _armRRenderer.DOColor(Color.white, 0.05f).SetUpdate(true);

            _isSweeping = true;

            float rotateSpeed = _isPhase2 ? _data.phase2SweepRotateSpeed : _data.sweepRotateSpeed;
            int rotations = _isPhase2 ? 2 : 1;
            float totalAngle = 360f * rotations;
            float duration = totalAngle / rotateSpeed;

            // 본체 회전 (팔이 자식이므로 함께 회전됨)
            _rotateTween?.Kill();
            _rotateTween = GetComponentInParent<Transform>()
                .DORotate(
                    new Vector3(0f, 0f, totalAngle),
                    duration,
                    RotateMode.FastBeyond360)
                .SetEase(Ease.Linear)
                .SetRelative(true)
                .SetUpdate(false);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (_isInterrupted)
                {
                    _rotateTween?.Kill();
                    _isSweeping = false;
                    yield break;
                }

                // 디스크 중심 보스와 함께
                _attackRange?.UpdateSweepDiscPosition(GetBossWorldPos());

                // 매 프레임 히트박스 판정
                float hitRadius = _isPhase2
                    ? _data.sweepHitRadius + 0.5f
                    : _data.sweepHitRadius;

                Collider2D hit = Physics2D.OverlapCircle(
                    GetBossWorldPos(),
                    hitRadius,
                    _playerLayer);

                if (hit != null)
                    Debug.Log("[BossPattern_Sweep] 스윕 피격!");

                elapsed += Time.deltaTime;
                yield return null;
            }

            _isSweeping = false;
            _attackRange?.HideSweepDisc();
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔/색상 복귀
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 복귀
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.5f).SetEase(Ease.OutBack);
            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.5f).SetEase(Ease.OutBack);

            // 색상 복귀
            if (_armLRenderer != null)
                _armLRenderer.DOColor(_armLOriginColor, _data?.colorTransitionDuration ?? 0.1f).SetUpdate(true);
            if (_armRRenderer != null)
                _armRRenderer.DOColor(_armROriginColor, _data?.colorTransitionDuration ?? 0.1f).SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            _rotateTween?.Kill();
            _bodyColorTween?.Kill();
            _isSweeping = false;
            _attackRange?.HideSweepDisc();

            // 팔 즉시 원위치
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armLOriginLocalPos, 0.1f);
            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armROriginLocalPos, 0.1f);

            // 색상 즉시 복귀
            if (_armLRenderer != null) _armLRenderer.DOColor(_armLOriginColor, 0.1f).SetUpdate(true);
            if (_armRRenderer != null) _armRRenderer.DOColor(_armROriginColor, 0.1f).SetUpdate(true);

            base.Interrupt();
        }

        // ══════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════

        private Vector2 GetBossWorldPos()
        {
            // Patterns 자식 오브젝트이므로 부모 Boss_Warden 위치 참조
            return GetComponentInParent<Rigidbody2D>()?.position
                   ?? (Vector2)transform.parent.position;
        }
    }
}