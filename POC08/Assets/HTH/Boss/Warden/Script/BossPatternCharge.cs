// ============================================================
// BossPattern_Charge.cs  v1.0
// Boss_Warden 돌진 패턴
//
// [POC07 참고]
//   TestBossPattern_PunchDown.cs 의 팔 DOLocalMove 구조 참고.
//   탑뷰에서 본체 전체가 돌진하는 방식으로 변환.
//
// [1페이즈 흐름]
//   Warning (0.8초)
//     → 플레이어 방향 계산 후 고정
//     → LineRenderer 돌진 예고선 표시
//     → 본체 주황 Pulse DOColor
//
//   Active
//     → 예고선 제거
//     → Rigidbody2D.linearVelocity = direction × chargeSpeed
//     → 거리 도달 or 벽 충돌 시 종료
//     → OverlapBox 히트박스 체크 (매 FixedUpdate)
//     → 플레이어 피격 시 knockback
//
//   Recovery (0.8초)
//     → linearVelocity = 0
//     → DOShakePosition 충격 연출
//     → 취약 구간 표시 (붉은 페이드)
//     → OnPatternGroggy 발행 (_triggerGroggyOnRecovery = true)
//
// [2페이즈 강화]
//   Active: 속도 16 units/s
//   Recovery: 즉시 Slam 패턴 연계 (단, 연계는 BossWardenAI 가 처리)
//   _isPhase2: Active 종료 직후 Recovery 스킵 → OnPatternEnd 즉시 발행
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 돌진 패턴. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [연결 부위] 오른팔 (RightArm)
    /// [그로기 유발] Recovery 완료 시
    /// [2페이즈] 돌진 속도 증가 + Recovery 스킵
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossPattern_Charge : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]

        /// <summary>
        /// 예고 범위 표시 컴포넌트.
        /// GetComponentInParent 로 자동 탐색.
        /// </summary>
        [Tooltip("BossWardenAttackRange. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        /// <summary>
        /// 보스 Rigidbody2D.
        /// GetComponentInParent 로 자동 탐색.
        /// </summary>
        [Tooltip("Rigidbody2D. 미연결 시 자동 탐색.")]
        [SerializeField] private Rigidbody2D _rigid2D;

        /// <summary>
        /// BossWardenAI 참조.
        /// 플레이어 방향/위치 참조.
        /// </summary>
        [Tooltip("BossWardenAI. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAI _ai;

        [Header("── DataSO ──────────────────────")]

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 히트박스 ──────────────────────")]

        /// <summary>
        /// 플레이어 감지 레이어 마스크.
        /// Player 레이어 선택.
        /// </summary>
        [Tooltip("Player 레이어 마스크.")]
        [SerializeField] private LayerMask _playerLayer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> Warning 시 계산한 고정 돌진 방향. </summary>
        private Vector2 _chargeDirection;

        /// <summary> 돌진 시작 위치 (거리 계산용). </summary>
        private Vector2 _chargeStartPos;

        /// <summary> 현재 2페이즈 여부. </summary>
        private bool _isPhase2;

        /// <summary> 이번 Active 에서 이미 플레이어를 피격했는지. </summary>
        private bool _hasHitPlayer;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null)
                _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_rigid2D == null)
                _rigid2D = GetComponentInParent<Rigidbody2D>();
            if (_ai == null)
                _ai = GetComponentInParent<BossWardenAI>();

            // 1페이즈 기본값: Recovery 완료 시 그로기 유발
            _triggerGroggyOnRecovery = true;
        }

        // ══════════════════════════════════════════════════════
        // BossPatternBase 오버라이드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 2페이즈 활성화 시 속도 증가 + Recovery 스킵 플래그 설정.
        /// </summary>
        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 예고선 + 방향 고정
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null || _ai == null) yield break;

            // 플레이어 방향 계산 후 고정 (Warning 중 변경 없음)
            _chargeDirection = _ai.FacingDir;

            // LineRenderer 돌진 예고선 표시
            _attackRange?.ShowChargeLine(
                transform.position,
                _chargeDirection,
                _data.chargeWarningSize.y);

            // Warning 대기 (중단 체크 포함)
            yield return StartCoroutine(WaitForPattern(_warningDuration));

            // Warning 종료 후 예고선 제거는 Active 진입 시
        }

        // ══════════════════════════════════════════════════════
        // Active — 돌진 + 히트박스
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_data == null || _rigid2D == null) yield break;

            // 예고선 제거
            _attackRange?.HideChargeLine();

            // 돌진 시작
            _chargeStartPos = transform.position;
            _hasHitPlayer = false;

            float speed = _isPhase2 ? _data.phase2ChargeSpeed : _data.chargeSpeed;
            _rigid2D.linearVelocity = _chargeDirection * speed;

            // 거리 / 충돌 체크 루프
            while (true)
            {
                if (_isInterrupted)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    yield break;
                }

                float distanceTraveled = Vector2.Distance(_chargeStartPos, transform.position);

                // 히트박스 체크 (1회만)
                if (!_hasHitPlayer)
                    CheckChargeHit();

                // 돌진 거리 도달
                if (distanceTraveled >= _data.chargeDistance)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// OverlapBox 로 플레이어 피격 체크.
        /// 실제 히트박스 = chargeHitboxSize (chargeWarningSize 보다 작음).
        /// </summary>
        private void CheckChargeHit()
        {
            if (_data == null) return;

            float angle = Mathf.Atan2(_chargeDirection.y, _chargeDirection.x) * Mathf.Rad2Deg;

            // 히트박스 중심 = 현재 위치 + 방향 × (히트박스 높이/2)
            Vector2 boxCenter = (Vector2)transform.position
                + _chargeDirection * (_data.chargeHitboxSize.y * 0.5f);

            Collider2D hit = Physics2D.OverlapBox(
                boxCenter,
                _data.chargeHitboxSize,
                angle,
                _playerLayer);

            if (hit != null)
            {
                _hasHitPlayer = true;
                // 플레이어 피격 처리 (넉백 등은 추후 PlayerHitReceiver 연동)
                Debug.Log("[BossPattern_Charge] 플레이어 피격!");
            }
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 충격 연출 + 그로기 유발
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 2페이즈: Recovery 스킵 (즉시 종료 → Slam 연계는 AI 가 처리)
            if (_isPhase2) yield break;

            // 충격 연출 — DOShakePosition
            transform.DOShakePosition(
                duration: 0.3f,
                strength: 0.3f,
                vibrato: 10,
                randomness: 90f)
                .SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드 — 돌진 강제 정지
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            // 돌진 중 강제 중단 시 속도 즉시 0
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            _attackRange?.HideChargeLine();
            base.Interrupt();
        }
    }
}