// ============================================================
// BossPattern_RageCharge.cs
// Boss_Warden 3연 돌진 패턴 (2페이즈 전용)
//
// [수정 내용]
//   팔(양팔) + 본체 SpriteRenderer 색상 코드 전부 제거
//   → _armLRenderer / _armRRenderer / _bodyRenderer 색상 제거
//   → _armLColorTween / _armRColorTween / _pulseTween 제거
//   → _armLOriginColor / _armROriginColor / _bodyOriginColor 제거
//   → Warning 붉은 Pulse DOColor 제거
//   → Recovery 양팔 + 본체 색상 복귀 DOColor 제거
//   → Interrupt 양팔 색상 즉시 복귀 제거
//
//   Warning AttackRange 점멸 추가
//   → ShowRageChargeLine() 3개 후 StartRageChargePulse() 호출
//   Active 각 돌진 전 점멸 중단
//   → StopAllPulse() 호출
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>Boss_Warden 3연 돌진 패턴. 2페이즈 전용.</summary>
    public class BossPattern_RageCharge : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private Rigidbody2D _rigid2D;
        [SerializeField] private BossWardenAI _ai;

        [Header("── DataSO ──────────────────────")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 Transform ──────────────────────")]
        /// <summary>왼팔 Transform. 백스윙 + 뻗기 연출. 색상 제어 없음.</summary>
        [Tooltip("왼팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;
        /// <summary>오른팔 Transform. 색상 제어 없음.</summary>
        [Tooltip("오른팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armRTransform;

        [Header("── 레이어 ──────────────────────")]
        [Tooltip("플레이어 히트박스 레이어.")]
        [SerializeField] private LayerMask _playerLayer;
        [Tooltip("벽 레이어.")]
        [SerializeField] private LayerMask _wallLayer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private Transform _bossTransform;
        private Vector3 _armLOriginLocalPos;
        private Vector3 _armROriginLocalPos;

        private static readonly Collider2D[] _hitResults = new Collider2D[4];

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_rigid2D == null) _rigid2D = GetComponentInParent<Rigidbody2D>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            _bossTransform = _rigid2D != null ? _rigid2D.transform : transform.parent;

            if (_armLTransform != null) _armLOriginLocalPos = _armLTransform.localPosition;
            if (_armRTransform != null) _armROriginLocalPos = _armRTransform.localPosition;

            _isPhase2Only = true;
        }

        private void CleanupWarning()
        {
            _attackRange?.StopAllPulse();
            _attackRange?.HideAllRageChargeLines();
        }

        // ══════════════════════════════════════════════════════
        // Warning — 예고선 3개 점멸 + 양팔 백스윙
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            Vector2 bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            Vector2 facingDir = _ai != null ? _ai.FacingDir : Vector2.right;

            // 예고선 3개 순차 표시
            for (int i = 0; i < 3; i++)
            {
                if (_isInterrupted) yield break;

                _attackRange?.ShowRageChargeLine(i, bossPos, facingDir, _data.chargeDistance);
                yield return new WaitForSecondsRealtime(_warningDuration / 4f);
            }

            // 3개 전부 표시 후 점멸 시작
            _attackRange?.StartRageChargePulse();

            // 양팔 백스윙 (위치만)
            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos + new Vector3(-facingDir.x, -facingDir.y, 0f) * _data.chargePullAmount,
                                 _warningDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + new Vector3(-facingDir.x, -facingDir.y, 0f) * _data.chargePullAmount,
                                 _warningDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            yield return StartCoroutine(WaitForPattern(_warningDuration / 4f));
        }

        // ══════════════════════════════════════════════════════
        // Active — 3연 돌진
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_data == null) yield break;

            // 점멸 중단
            CleanupWarning();

            int rageCount = _data.rageChargeCount;

            for (int i = 0; i < rageCount; i++)
            {
                if (_isInterrupted) yield break;

                Vector2 chargeDir = _ai != null ? _ai.FacingDir : Vector2.right;

                // 양팔 앞으로 뻗기 (위치만)
                if (_armLTransform != null)
                    _armLTransform
                        .DOLocalMove(_armLOriginLocalPos + new Vector3(chargeDir.x, chargeDir.y, 0f) * _data.chargeThrustAmount,
                                     0.08f)
                        .SetEase(Ease.OutExpo);
                if (_armRTransform != null)
                    _armRTransform
                        .DOLocalMove(_armROriginLocalPos + new Vector3(chargeDir.x, chargeDir.y, 0f) * _data.chargeThrustAmount,
                                     0.08f)
                        .SetEase(Ease.OutExpo);

                // 돌진
                if (_rigid2D != null)
                    _rigid2D.linearVelocity = chargeDir * _data.chargeSpeed * 1.2f;

                Vector2 startPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
                float elapsed = 0f;
                float maxDuration = _data.chargeDistance / Mathf.Max(_data.chargeSpeed * 1.2f, 0.1f) * 2f;
                bool stoppedByWall = false;

                while (elapsed < maxDuration)
                {
                    if (_isInterrupted) { if (_rigid2D != null) _rigid2D.linearVelocity = Vector2.zero; yield break; }

                    Vector2 currentPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
                    float dist = Vector2.Distance(startPos, currentPos);
                    float speed = _rigid2D != null ? _rigid2D.linearVelocity.magnitude : 0f;

                    if (dist >= _data.chargeDistance) break;

                    if (elapsed > 0.1f && speed < 0.5f)
                    {
                        stoppedByWall = true;
                        if (i == rageCount - 1)
                            yield return new WaitForSecondsRealtime(0.3f);
                        break;
                    }

                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (_rigid2D != null)
                    _rigid2D.linearVelocity = Vector2.zero;

                // 팔 살짝 복귀 (다음 돌진 준비)
                if (i < rageCount - 1)
                {
                    _armLTransform?.DOLocalMove(_armLOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
                    _armRTransform?.DOLocalMove(_armROriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
                    yield return StartCoroutine(WaitForPattern(_data.rageChargeInterval));
                }
            }

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 + 피로감 (색상 없음)
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 양팔 원위치 복귀 (위치만)
            _armLTransform?.DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.3f).SetEase(Ease.OutBack);
            _armRTransform?.DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.3f).SetEase(Ease.OutBack);

            // 피로감 연출
            if (_bossTransform != null)
                _bossTransform.DOShakePosition(0.4f, 0.4f, 12, 90f).SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            CleanupWarning();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 팔 즉시 원위치 (위치만)
            _armLTransform?.DOLocalMove(_armLOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
            _armRTransform?.DOLocalMove(_armROriginLocalPos, 0.1f).SetEase(Ease.OutQuart);

            base.Interrupt();
        }
    }
}