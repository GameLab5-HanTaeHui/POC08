// ============================================================
// BossPatternCharge.cs
// Boss_Warden 돌진 패턴
//
// [수정 내용]
//   팔(오른팔) SpriteRenderer 색상 코드 전부 제거
//   → _armRRenderer 필드 제거
//   → _armOriginColor 필드 제거
//   → _armColorTween 핸들 제거
//   → OnWarning() DOColor Pulse 제거
//   → OnRecovery() 팔 색상 복귀 DOColor 제거
//   → Interrupt() 팔 색상 즉시 복귀 DOColor 제거
//   → OnDestroy() _armColorTween.Kill() 제거
//
//   Warning AttackRange 점멸 추가
//   → ShowChargeLine() 후 StartChargePulse() 호출
//   Active 진입 시 점멸 중단
//   → StopAllPulse() 호출
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>Boss_Warden 돌진 패턴.</summary>
    public class BossPattern_Charge : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]

        [Tooltip("BossWardenAttackRange. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        [Tooltip("Rigidbody2D. 미연결 시 자동 탐색.")]
        [SerializeField] private Rigidbody2D _rigid2D;

        [Tooltip("BossWardenAI.")]
        [SerializeField] private BossWardenAI _ai;

        [Header("── DataSO ──────────────────────")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 오른팔 Transform ──────────────────────")]
        /// <summary>오른팔 Transform. 백스윙 / 뻗기 / 복귀 연출 주체. 색상 제어 없음.</summary>
        [Tooltip("오른팔 Transform. 백스윙 + 뻗기 연출용. 색상 제어 없음.")]
        [SerializeField] private Transform _armRTransform;

        [Header("── 레이어 ──────────────────────")]
        [Tooltip("플레이어 히트박스 레이어. PlayerAttackHitBox 레이어 선택.")]
        [SerializeField] private LayerMask _playerLayer;
        [Tooltip("벽 레이어. Wall 레이어만 선택.")]
        [SerializeField] private LayerMask _wallLayer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private Transform _bossTransform;
        private Vector3 _armOriginLocalPos;
        private bool _isPhase2;
        private bool _hasHitPlayer;
        private Vector2 _chargeDirection;

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

            if (_armRTransform != null)
                _armOriginLocalPos = _armRTransform.localPosition;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 예고선 점멸 + 팔 백스윙
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            _chargeDirection = _ai != null ? _ai.FacingDir : Vector2.right;
            _hasHitPlayer = false;

            Vector2 bossPos = _rigid2D != null
                ? _rigid2D.position
                : (Vector2)transform.position;

            // 예고선 표시 + 점멸 시작
            _attackRange?.ShowChargeLine(
                bossPos,
                _chargeDirection,
                _data.chargeWarningSize.y,
                _data.chargeWarningSize.x);
            _attackRange?.StartChargePulse();

            // 팔 백스윙 (위치 연출만 — 색상 없음)
            if (_armRTransform != null)
            {
                Vector3 worldBackDir = new Vector3(-_chargeDirection.x, -_chargeDirection.y, 0f);
                Vector3 localBackDir = _bossTransform != null
                    ? _bossTransform.InverseTransformDirection(worldBackDir)
                    : worldBackDir;

                _armRTransform
                    .DOLocalMove(_armOriginLocalPos + localBackDir * _data.chargePullAmount,
                                 _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            // 본체 웅크리기
            if (_bossTransform != null)
                _bossTransform.DOScale(1.1f, _warningDuration * 0.3f).SetEase(Ease.OutQuad);

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 팔 뻗기 + 돌진
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_data == null) yield break;

            // 점멸 중단 (Active 진입 시 고정)
            _attackRange?.StopAllPulse();
            _attackRange?.HideChargeLine();

            // 팔 앞으로 뻗기
            if (_armRTransform != null)
            {
                Vector3 worldDir = new Vector3(_chargeDirection.x, _chargeDirection.y, 0f);
                Vector3 localDir = _bossTransform != null
                    ? _bossTransform.InverseTransformDirection(worldDir)
                    : worldDir;

                _armRTransform
                    .DOLocalMove(_armOriginLocalPos + localDir * _data.chargeThrustAmount,
                                 0.1f)
                    .SetEase(Ease.OutExpo);
            }

            // 돌진
            if (_rigid2D != null)
                _rigid2D.linearVelocity = _chargeDirection * _data.chargeSpeed;

            Vector2 startPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            float elapsed = 0f;
            float maxDuration = _data.chargeDistance / Mathf.Max(_data.chargeSpeed, 0.1f) * 2f;
            int logFrame = 0;
            bool stoppedByWall = false;

            while (elapsed < maxDuration)
            {
                if (_isInterrupted) yield break;

                Vector2 currentPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
                float dist = Vector2.Distance(startPos, currentPos);
                float speed = _rigid2D != null ? _rigid2D.linearVelocity.magnitude : 0f;

                // 30프레임마다 로그
                if (logFrame++ % 30 == 0)
                    Debug.Log($"[BossPattern_Charge] 진행 | 거리:{dist:F2}/{_data.chargeDistance} 속도:{speed:F2}");

                // ① 거리 도달
                if (dist >= _data.chargeDistance) break;

                // ② 속도 0 감지 (벽 충돌)
                if (elapsed > 0.1f && speed < 0.5f)
                {
                    stoppedByWall = true;
                    Debug.LogWarning("[BossPattern_Charge] 종료 — 벽 충돌 감지");
                    break;
                }

                CheckChargeHit();
                elapsed += Time.deltaTime;
                yield return null;
            }

            // 타임아웃 체크
            if (elapsed >= maxDuration)
                Debug.LogWarning("[BossPattern_Charge] 종료 — 타임아웃");

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 복귀 + 크기 복귀 (색상 코드 없음)
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;
            if (_isPhase2) yield break;

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 팔 원위치 복귀 (위치만)
            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armOriginLocalPos, _recoveryDuration * 0.35f)
                    .SetEase(Ease.OutBack);

            // 본체 크기 복귀
            if (_bossTransform != null)
                _bossTransform
                    .DOScale(1.0f, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            // 충격 흔들림
            _bossTransform?.DOShakePosition(0.3f, 0.25f, 10, 90f).SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            _attackRange?.StopAllPulse();
            _attackRange?.HideChargeLine();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 팔 즉시 원위치 (위치만)
            _armRTransform?.DOLocalMove(_armOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);

            // 본체 크기 복귀
            _bossTransform?.DOScale(1.0f, 0.1f).SetEase(Ease.OutQuart);

            base.Interrupt();
        }

        // ══════════════════════════════════════════════════════
        // 히트박스 체크
        // ══════════════════════════════════════════════════════

        private void CheckChargeHit()
        {
            if (_data == null || _rigid2D == null || _hasHitPlayer) return;

            float angle = Mathf.Atan2(_chargeDirection.y, _chargeDirection.x) * Mathf.Rad2Deg;
            Vector2 boxCenter = _rigid2D.position;

            int count = Physics2D.OverlapBoxNonAlloc(
                boxCenter, _data.chargeHitboxSize, angle, _hitResults, _playerLayer);

            if (count > 0)
            {
                _hasHitPlayer = true;
                Debug.Log("[BossPattern_Charge] 플레이어 피격!");
            }
        }
    }
}