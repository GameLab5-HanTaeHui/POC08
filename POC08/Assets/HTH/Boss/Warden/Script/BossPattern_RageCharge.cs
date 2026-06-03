// ============================================================
// BossPattern_RageCharge.cs  v1.0
// Boss_Warden 3연 돌진 패턴 (2페이즈 전용)
//
// [흐름]
//   Warning (0.8초):
//     0.0초: 1번 예고선 표시
//     0.3초: 2번 예고선 표시
//     0.6초: 3번 예고선 표시
//     전신 붉은 Pulse DOColor
//   Active:
//     3회 돌진 순차 실행 (0.2초 간격)
//     각 돌진 방향은 Warning 시 계산한 방향 유지
//     3번째 돌진 종료 후 추가 경직 가능
//   Recovery (1.2초):
//     피로감 DOShakePosition
//     recoveryVulnMultiplier 배율은 AI 가 처리
//
// [연결 부위] 없음 (독립 패턴)
// [그로기 유발] 없음
// [2페이즈 전용] _isPhase2Only = true
// [namespace] SEAL
// ============================================================

namespace SEAL
{
    using System.Collections;
    using UnityEngine;
    using DG.Tweening;

    /// <summary>
    /// Boss_Warden 3연 돌진 패턴. 2페이즈 전용. (v1.0)
    /// </summary>
    public class BossPattern_RageCharge : BossPatternBase
    {
        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private Rigidbody2D _rigid2D;
        [SerializeField] private BossWardenAI _ai;
        [SerializeField] private BossWardenDataSO _data;
        [SerializeField] private SpriteRenderer _bodyRenderer;

        [Header("── 히트박스 ──────────────────────")]
        [SerializeField] private LayerMask _playerLayer;

        // ── 내부 상태 ──
        private Vector2[] _chargeDirections = new Vector2[3];
        private Tween _pulseTween;

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_rigid2D == null) _rigid2D = GetComponentInParent<Rigidbody2D>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInParent<SpriteRenderer>();

            // 2페이즈 전용 설정
            _isPhase2Only = true;
            _triggerGroggyOnRecovery = false;
        }

        protected override IEnumerator OnWarning()
        {
            if (_data == null || _ai == null) yield break;

            // 전신 붉은 Pulse
            if (_bodyRenderer != null)
            {
                _pulseTween?.Kill();
                _pulseTween = _bodyRenderer
                    .DOColor(new Color(0.6f, 0f, 0f), _data.pulsePeriod * 0.4f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }

            // 3방향 순차 예고선
            Vector2 baseDir = _ai.FacingDir;

            // 방향 : 정면 / 약간 왼쪽 / 약간 오른쪽
            _chargeDirections[0] = baseDir;
            _chargeDirections[1] = Quaternion.Euler(0, 0, 20f) * baseDir;
            _chargeDirections[2] = Quaternion.Euler(0, 0, -20f) * baseDir;

            float lineLength = _data.chargeDistance * 1.2f;

            // 0.0초 - 첫 번째 예고선
            _attackRange?.ShowRageChargeLine(0, transform.position, _chargeDirections[0], lineLength);
            yield return StartCoroutine(WaitForPattern(0.3f));
            if (_isInterrupted) { CleanupWarning(); yield break; }

            // 0.3초 - 두 번째 예고선
            _attackRange?.ShowRageChargeLine(1, transform.position, _chargeDirections[1], lineLength);
            yield return StartCoroutine(WaitForPattern(0.3f));
            if (_isInterrupted) { CleanupWarning(); yield break; }

            // 0.6초 - 세 번째 예고선
            _attackRange?.ShowRageChargeLine(2, transform.position, _chargeDirections[2], lineLength);
            yield return StartCoroutine(WaitForPattern(0.2f));
        }

        private void CleanupWarning()
        {
            _pulseTween?.Kill();
            _attackRange?.HideAllRageChargeLines();
        }

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted || _data == null) yield break;

            CleanupWarning();

            float speed = _data.rageChargeSpeed;

            // 3회 순차 돌진
            for (int i = 0; i < _data.rageChargeCount; i++)
            {
                if (_isInterrupted) break;

                Vector2 dir = _chargeDirections[i];
                Vector2 startPos = transform.position;
                bool hasHit = false;

                _rigid2D.linearVelocity = dir * speed;

                while (true)
                {
                    if (_isInterrupted)
                    {
                        _rigid2D.linearVelocity = Vector2.zero;
                        yield break;
                    }

                    float dist = Vector2.Distance(startPos, transform.position);

                    if (!hasHit)
                    {
                        Collider2D hit = Physics2D.OverlapBox(
                            (Vector2)transform.position + dir * (_data.chargeHitboxSize.y * 0.5f),
                            _data.chargeHitboxSize,
                            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg,
                            _playerLayer);

                        if (hit != null)
                        {
                            hasHit = true;
                            Debug.Log($"[BossPattern_RageCharge] {i + 1}번 돌진 피격!");
                        }
                    }

                    if (dist >= _data.chargeDistance)
                    {
                        _rigid2D.linearVelocity = Vector2.zero;
                        break;
                    }

                    yield return null;
                }

                // 돌진 간격 대기
                if (i < _data.rageChargeCount - 1)
                    yield return StartCoroutine(WaitForPattern(_data.rageChargeInterval));
            }

            _rigid2D.linearVelocity = Vector2.zero;
        }

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            transform.DOShakePosition(
                duration: 0.4f,
                strength: 0.4f,
                vibrato: 12,
                randomness: 90f)
                .SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        public override void Interrupt()
        {
            CleanupWarning();
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;
            base.Interrupt();
        }
    }
}