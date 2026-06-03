// ============================================================
// BossPattern_RageCharge.cs  v1.1
// Boss_Warden 3연 돌진 패턴 (2페이즈 전용)
//
// [v1.1 변경 — transform.position → _rigid2D.position 수정]
//   🔴 버그 수정: OnActive() 의 startPos 및 dist 계산 오류
//
//   [원인]
//     BossPattern_Charge 와 동일한 구조적 문제.
//     Patterns 오브젝트는 Boss_Warden 의 자식.
//     transform 은 Patterns 오브젝트를 가리킴.
//     Boss_Warden(부모)이 이동하면 Patterns(자식)도 함께 이동.
//     → startPos = transform.position 과 루프 내 transform.position 이 항상 동일
//     → dist = 0 → chargeDistance 도달 불가 → while(true) 무한루프 → AI 정지
//
//   [수정]
//     startPos = transform.position → startPos = _rigid2D.position
//     dist = Vector2.Distance(startPos, transform.position)
//       → dist = Vector2.Distance(startPos, _rigid2D.position)
//     OverlapBox boxCenter : (Vector2)transform.position → _rigid2D.position
//
// [흐름]
//   Warning (0.8초):
//     0.0초: 1번 예고선 표시
//     0.3초: 2번 예고선 표시
//     0.6초: 3번 예고선 표시
//     전신 붉은 Pulse DOColor
//   Active:
//     3회 돌진 순차 실행 (rageChargeInterval 간격)
//     각 돌진 방향은 Warning 시 계산한 방향 유지
//   Recovery (1.2초):
//     피로감 DOShakePosition
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
    /// Boss_Warden 3연 돌진 패턴. 2페이즈 전용. (v1.1)
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

        /// <summary>
        /// 플레이어 피격 감지 레이어 마스크.
        /// PlayerAttackHitBox 레이어 선택.
        /// </summary>
        [Tooltip("플레이어 피격 감지 레이어. PlayerAttackHitBox 레이어 선택.")]
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

            // 3방향 순차 예고선 (정면 / 약간 왼쪽 / 약간 오른쪽)
            Vector2 baseDir = _ai.FacingDir;
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

                // ✅ v1.1 수정: transform.position → _rigid2D.position
                //   이유: Patterns(자식)은 Boss_Warden(부모)과 함께 이동하므로
                //         transform.position 기준 거리는 항상 0 → 무한루프
                Vector2 startPos = _rigid2D.position;
                bool hasHit = false;

                _rigid2D.linearVelocity = dir * speed;

                while (true)
                {
                    if (_isInterrupted)
                    {
                        _rigid2D.linearVelocity = Vector2.zero;
                        yield break;
                    }

                    // ✅ v1.1 수정: transform.position → _rigid2D.position
                    float dist = Vector2.Distance(startPos, _rigid2D.position);

                    if (!hasHit)
                    {
                        // ✅ v1.1 수정: (Vector2)transform.position → _rigid2D.position
                        Collider2D hit = Physics2D.OverlapBox(
                            _rigid2D.position + dir * (_data.chargeHitboxSize.y * 0.5f),
                            _data.chargeHitboxSize,
                            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg,
                            _playerLayer);

                        if (hit != null)
                        {
                            hasHit = true;
                            Debug.Log($"[BossPattern_RageCharge] {i + 1}번 돌진 — 플레이어 피격!");
                        }
                    }

                    if (dist >= _data.chargeDistance)
                    {
                        _rigid2D.linearVelocity = Vector2.zero;
                        break;
                    }

                    yield return null;
                }

                // 돌진 간격 대기 (마지막 돌진 이후는 생략)
                if (i < _data.rageChargeCount - 1)
                    yield return StartCoroutine(WaitForPattern(_data.rageChargeInterval));
            }

            _rigid2D.linearVelocity = Vector2.zero;
        }

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 피로감 연출
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