// ============================================================
// BossPattern_RageCharge.cs  v2.1
// Boss_Warden 3연 돌진 패턴 (2페이즈 전용)
//
// [v2.1 — 색상 코드 제거 (BossWardenFeedback 위임)]
//   제거: _armLRenderer / _armRRenderer / _bodyRenderer 색상
//   제거: _armLColorTween / _armRColorTween / _pulseTween
//   제거: _armLOriginColor / _armROriginColor / _bodyOriginColor
//   제거: Warning 붉은 Pulse DOColor Yoyo
//   제거: Recovery 양팔 + 본체 색상 복귀
//   제거: Interrupt 양팔 색상 즉시 복귀
//   → 색상은 SealableComponent + BossWardenFeedback 이 전담
//
//   Warning AttackRange 점멸 추가 (v1.2 AttackRange 연동)
//   → ShowRageChargeLine() 3개 후 StartRageChargePulse() 호출
//   Active 각 돌진 전 점멸 중단
//   → CleanupWarning() 에서 StopAllPulse() 호출
//
// [v2.0 유지 — 원복]
//   3방향 계산: 정면 / 좌 20° / 우 20° (_chargeDirections 배열)
//   양팔 백스윙 DOLocalMove (경직 방향 반대)
//   각 돌진 전 양팔 뻗기 DOLocalMove
//   안전장치 3종: 거리도달 / 속도0 감지(벽충돌) / 타임아웃
//   마지막 돌진 벽 충돌 시 추가 경직 0.3초 + DOShakePosition
//   돌진 간격 대기 (rageChargeInterval)
//   Recovery DOShakePosition 피로감 연출
//   Interrupt: linearVelocity 즉시 0 + 양팔 원위치
//
// [2페이즈 전용]
//   _isPhase2Only = true
//   _triggerGroggyOnRecovery = false
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 3연 돌진 패턴. 2페이즈 전용. (v2.1)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning : 예고선 3개 순차 점멸 + 양팔 백스윙
    ///   Active  : CleanupWarning → 각 돌진 전 양팔 뻗기 → linearVelocity 돌진 → 안전장치 3종
    ///   Recovery: 양팔 원위치 복귀 → DOShakePosition
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossPattern_RageCharge : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]

        [Tooltip("BossWardenAttackRange. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        [Tooltip("Rigidbody2D. 미연결 시 자동 탐색.")]
        [SerializeField] private Rigidbody2D _rigid2D;

        [Tooltip("BossWardenAI. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAI _ai;

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 Transform ──────────────────────")]

        /// <summary>왼팔 Transform. 백스윙 + 돌진 뻗기 연출. 색상 제어 없음.</summary>
        [Tooltip("왼팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;

        /// <summary>오른팔 Transform. 색상 제어 없음.</summary>
        [Tooltip("오른팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armRTransform;

        [Header("── 레이어 ──────────────────────")]

        [Tooltip("플레이어 HurtBox 레이어. PlayerAttackHitBox 레이어 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 ──────────────────────")]

        /// <summary>Warning 백스윙 당기는 거리. 돌진 반대 방향으로 양팔 당김.</summary>
        [Tooltip("백스윙 당기는 거리. 권장: 0.4")]
        [Min(0f)]
        [SerializeField] private float _windupPullAmount = 0.4f;

        /// <summary>각 돌진 시작 시 팔이 앞으로 뻗는 거리.</summary>
        [Tooltip("돌진 시 팔 뻗기 거리. 권장: 0.3")]
        [Min(0f)]
        [SerializeField] private float _thrustAmount = 0.3f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>3연 돌진 방향 배열. Warning 시 계산 후 고정.</summary>
        private Vector2[] _chargeDirections = new Vector2[3];

        /// <summary>왼팔 원래 로컬 위치.</summary>
        private Vector3 _armLOriginLocalPos;

        /// <summary>오른팔 원래 로컬 위치.</summary>
        private Vector3 _armROriginLocalPos;

        /// <summary>보스 본체 Transform. Recovery DOShakePosition 대상.</summary>
        private Transform _bossTransform;

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
            _triggerGroggyOnRecovery = false;
        }

        /// <summary>
        /// Warning 정리.
        /// AttackRange 점멸 중단 + 예고선 전체 제거.
        /// </summary>
        private void CleanupWarning()
        {
            _attackRange?.StopAllPulse();
            _attackRange?.HideAllRageChargeLines();
        }

        // ══════════════════════════════════════════════════════
        // Warning — 예고선 3개 순차 점멸 + 양팔 백스윙
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null || _ai == null) yield break;

            // ① 3방향 계산: 정면 / 좌 20° / 우 20°
            Vector2 baseDir = _ai.FacingDir;
            _chargeDirections[0] = baseDir;
            _chargeDirections[1] = Quaternion.Euler(0f, 0f, 20f) * baseDir;
            _chargeDirections[2] = Quaternion.Euler(0f, 0f, -20f) * baseDir;

            // ② 양팔 백스윙: 기본 방향(baseDir) 반대로 당기기
            Vector3 backswingOffset = new Vector3(
                -baseDir.x * _windupPullAmount,
                -baseDir.y * _windupPullAmount,
                0f);

            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos + backswingOffset, 0.2f)
                    .SetEase(Ease.OutBack);

            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + backswingOffset, 0.2f)
                    .SetEase(Ease.OutBack);

            // ③ 예고선 3개 순차 표시 (0.3초 간격)
            float lineLength = _data.chargeDistance * 1.2f;

            // 0.0초 — 첫 번째 예고선
            Vector2 bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            _attackRange?.ShowRageChargeLine(0, bossPos, _chargeDirections[0], lineLength);
            yield return StartCoroutine(WaitForPattern(0.3f));
            if (_isInterrupted) { CleanupWarning(); yield break; }

            // 0.3초 — 두 번째 예고선
            bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            _attackRange?.ShowRageChargeLine(1, bossPos, _chargeDirections[1], lineLength);
            yield return StartCoroutine(WaitForPattern(0.3f));
            if (_isInterrupted) { CleanupWarning(); yield break; }

            // 0.6초 — 세 번째 예고선
            bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            _attackRange?.ShowRageChargeLine(2, bossPos, _chargeDirections[2], lineLength);

            // 모든 예고선 표시 후 점멸 시작 (v2.1)
            _attackRange?.StartRageChargePulse();

            yield return StartCoroutine(WaitForPattern(0.2f));
        }

        // ══════════════════════════════════════════════════════
        // Active — 3회 순차 돌진 + 안전장치 3종
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted || _data == null || _rigid2D == null) yield break;

            // 점멸 중단 + 예고선 제거 (v2.1)
            CleanupWarning();

            float speed = _data.rageChargeSpeed;
            bool lastChargeHitWall = false;

            for (int i = 0; i < _data.rageChargeCount; i++)
            {
                if (_isInterrupted) break;

                Vector2 dir = _chargeDirections[i];

                // ① 각 돌진 전 양팔 앞으로 뻗기 (겨누기 연출)
                Vector3 thrustOffset = new Vector3(
                    dir.x * _thrustAmount,
                    dir.y * _thrustAmount,
                    0f);

                if (_armLTransform != null)
                    _armLTransform
                        .DOLocalMove(_armLOriginLocalPos + thrustOffset, 0.05f)
                        .SetEase(Ease.OutExpo);

                if (_armRTransform != null)
                    _armRTransform
                        .DOLocalMove(_armROriginLocalPos + thrustOffset, 0.05f)
                        .SetEase(Ease.OutExpo);

                // ② 돌진 시작
                Vector2 startPos = _rigid2D.position;
                bool hasHit = false;
                bool hitWall = false;
                float elapsed = 0f;
                float maxDuration = (_data.chargeDistance / Mathf.Max(speed, 0.1f)) * 1.5f;

                _rigid2D.linearVelocity = dir * speed;

                Debug.Log($"[BossPattern_RageCharge] {i + 1}번 돌진 시작 | 방향:{dir} 속도:{speed}");

                while (true)
                {
                    if (_isInterrupted)
                    {
                        _rigid2D.linearVelocity = Vector2.zero;
                        yield break;
                    }

                    float dist = Vector2.Distance(startPos, _rigid2D.position);
                    float currentSpeed = _rigid2D.linearVelocity.magnitude;
                    elapsed += Time.deltaTime;

                    // 히트박스 체크 (1회만)
                    if (!hasHit)
                    {
                        Collider2D hit = Physics2D.OverlapBox(
                            _rigid2D.position + dir * (_data.chargeHitboxSize.y * 0.5f),
                            _data.chargeHitboxSize,
                            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg,
                            _playerLayer);

                        if (hit != null)
                        {
                            hasHit = true;
                            Debug.Log($"[BossPattern_RageCharge] {i + 1}번 돌진 피격!");
                        }
                    }

                    // 안전장치 ① 거리 도달
                    if (dist >= _data.chargeDistance)
                    {
                        _rigid2D.linearVelocity = Vector2.zero;
                        Debug.Log($"[BossPattern_RageCharge] {i + 1}번 돌진 종료 — 거리 도달");
                        break;
                    }

                    // 안전장치 ② 속도 0 감지 (벽 충돌)
                    if (elapsed > 0.1f && currentSpeed < 0.5f)
                    {
                        _rigid2D.linearVelocity = Vector2.zero;
                        hitWall = true;
                        Debug.LogWarning($"[BossPattern_RageCharge] {i + 1}번 돌진 — 벽 충돌 감지");

                        // 마지막 돌진 벽 충돌 시 추가 경직 (기획서 명시)
                        if (i == _data.rageChargeCount - 1)
                        {
                            lastChargeHitWall = true;
                            Debug.Log("[BossPattern_RageCharge] 마지막 돌진 벽 충돌 → 추가 경직 0.3초");

                            // 충격 흔들림 연출
                            if (transform.parent != null)
                            {
                                transform.parent.DOShakePosition(
                                    duration: 0.3f,
                                    strength: 0.35f,
                                    vibrato: 12,
                                    randomness: 90f)
                                    .SetUpdate(true);
                            }

                            yield return StartCoroutine(WaitForPattern(0.3f));
                        }
                        break;
                    }

                    // 안전장치 ③ 타임아웃
                    if (elapsed >= maxDuration)
                    {
                        _rigid2D.linearVelocity = Vector2.zero;
                        Debug.LogWarning($"[BossPattern_RageCharge] {i + 1}번 돌진 — 타임아웃");
                        break;
                    }

                    yield return null;
                }

                // ③ 돌진 후 팔 살짝 복귀 (다음 돌진 준비)
                if (_armLTransform != null)
                    _armLTransform
                        .DOLocalMove(_armLOriginLocalPos, 0.08f)
                        .SetEase(Ease.OutQuart);

                if (_armRTransform != null)
                    _armRTransform
                        .DOLocalMove(_armROriginLocalPos, 0.08f)
                        .SetEase(Ease.OutQuart);

                // 돌진 간격 대기 (마지막 돌진 이후 생략)
                if (i < _data.rageChargeCount - 1)
                    yield return StartCoroutine(WaitForPattern(_data.rageChargeInterval));
            }

            _rigid2D.linearVelocity = Vector2.zero;
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 + 피로감 연출
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // velocity 명시 정지 보장
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 양팔 원위치 복귀
            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            // 피로감 연출 (마지막 돌진 경직과 별개로 항상 실행)
            if (_bossTransform != null)
            {
                _bossTransform.DOShakePosition(
                    duration: 0.4f,
                    strength: 0.4f,
                    vibrato: 12,
                    randomness: 90f)
                    .SetUpdate(true);
            }

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            CleanupWarning();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 팔 즉시 원위치
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armLOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armROriginLocalPos, 0.1f).SetEase(Ease.OutQuart);

            base.Interrupt();
        }
    }
}