// ============================================================
// BossPattern_RageCharge.cs  v2.0
// Boss_Warden 3연 돌진 패턴 (2페이즈 전용) — 팔 모션 + 안전장치 + 색상 복귀
//
// [v2.0 변경]
//   기존(v1.1) 문제점:
//     🔴 Warning 예고선 위치 = transform.position (Patterns 자식 기준) → 불명확
//        수정: _rigid2D.position 으로 명시
//     🔴 Active 안전장치 없음 (Charge v1.3 과 불일치)
//        수정: 타임아웃 / 속도0 감지 / 거리도달 3종 추가
//     🔴 Recovery 색상 복귀 없음 (Pulse Kill 후 중간 색상 고정)
//        수정: Recovery 진입 시 본체 + 팔 색상 복귀 DOColor
//     🔴 팔 연출 없음 — Warning 백스윙 / Active 뻗기 전혀 없음
//        수정: 양팔 백스윙(뒤로 당기기) Warning + 각 돌진 전 앞으로 뻗기 Active
//     🟡 3번째 돌진 벽 충돌 시 추가 경직 없음 (기획서 명시 항목)
//        수정: 마지막 돌진 속도0 감지 시 WaitForPattern(0.3f) 추가 경직
//
// [v1.1 유지]
//   _rigid2D.position 기반 거리 계산 (transform.position 버그 수정 유지)
//   _isPhase2Only = true (2페이즈 전용)
//   _triggerGroggyOnRecovery = false
//
// [팔 연출 흐름]
//   Warning:  양팔 뒤로 당기기 DOLocalMove(backswingOffset, OutBack)
//             전신 붉은 Pulse DOColor Yoyo
//   Active:   각 돌진 시작 전 양팔 앞으로 뻗기 DOLocalMove(thrustOffset, OutExpo)
//             돌진 종료 후 팔 살짝 복귀 (다음 돌진 준비)
//   Recovery: 양팔 원위치 복귀 DOLocalMove(OutBack)
//             본체 + 팔 색상 복귀 DOColor
//             DOShakePosition 피로감
//
// [안전장치 3종 — Charge v2.0 동일]
//   ① 거리 도달: dist >= chargeDistance → 정상 종료
//   ② 속도0 감지: elapsed > 0.1s && speed < 0.5 → 벽 충돌 판단 → 강제 종료
//      마지막 돌진(i == rageChargeCount-1) 이면 추가 경직 0.3초
//   ③ 타임아웃: elapsed >= maxDuration → 강제 종료
//
// [연결 부위] 없음 (독립 패턴, 2페이즈 전용)
// [그로기 유발] 없음
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 3연 돌진 패턴. 2페이즈 전용. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning : 예고선 3개 순차 + 양팔 백스윙 + 붉은 Pulse
    ///   Active  : 각 돌진 전 양팔 앞으로 뻗기 → linearVelocity 돌진 → 안전장치 3종
    ///   Recovery: 양팔 원위치 복귀 → 색상 복귀 → DOShakePosition
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

        [Tooltip("보스 본체 SpriteRenderer. 색상 연출용.")]
        [SerializeField] private SpriteRenderer _bodyRenderer;

        [Header("── 팔 Transform / Renderer ──────────────────────")]

        /// <summary>
        /// 왼팔 Transform. 백스윙 + 돌진 뻗기 연출.
        /// </summary>
        [Tooltip("왼팔 Transform.")]
        [SerializeField] private Transform _armLTransform;

        /// <summary>
        /// 오른팔 Transform. 백스윙 + 돌진 뻗기 연출.
        /// </summary>
        [Tooltip("오른팔 Transform.")]
        [SerializeField] private Transform _armRTransform;

        [Tooltip("왼팔 SpriteRenderer. 색상 연출용.")]
        [SerializeField] private SpriteRenderer _armLRenderer;

        [Tooltip("오른팔 SpriteRenderer. 색상 연출용.")]
        [SerializeField] private SpriteRenderer _armRRenderer;

        [Header("── 레이어 ──────────────────────")]

        /// <summary>
        /// 플레이어 HurtBox 레이어 마스크.
        /// PlayerAttackHitBox 레이어 선택.
        /// </summary>
        [Tooltip("플레이어 HurtBox 레이어. PlayerAttackHitBox 레이어 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 ──────────────────────")]

        /// <summary>
        /// Warning 백스윙 당기는 거리.
        /// 돌진 반대 방향으로 이 거리만큼 양팔을 당김.
        /// </summary>
        [Tooltip("백스윙 당기는 거리. 권장: 0.4")]
        [Min(0f)]
        [SerializeField] private float _windupPullAmount = 0.4f;

        /// <summary>
        /// 각 돌진 시작 시 팔이 앞으로 뻗는 거리.
        /// </summary>
        [Tooltip("돌진 시 팔 뻗기 거리. 권장: 0.3")]
        [Min(0f)]
        [SerializeField] private float _thrustAmount = 0.3f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 3연 돌진 방향 배열. Warning 시 계산 후 고정. </summary>
        private Vector2[] _chargeDirections = new Vector2[3];

        /// <summary> 왼팔 원래 로컬 위치. </summary>
        private Vector3 _armLOriginLocalPos;

        /// <summary> 오른팔 원래 로컬 위치. </summary>
        private Vector3 _armROriginLocalPos;

        /// <summary> 본체 원래 색상. </summary>
        private Color _bodyOriginColor;

        /// <summary> 왼팔 원래 색상. </summary>
        private Color _armLOriginColor;

        /// <summary> 오른팔 원래 색상. </summary>
        private Color _armROriginColor;

        /// <summary>
        /// Warning 붉은 Pulse Tween 핸들.
        /// CleanupWarning() 에서 Kill.
        /// </summary>
        private Tween _pulseTween;

        private Tweener _armLColorTween;
        private Tweener _armRColorTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_rigid2D == null) _rigid2D = GetComponentInParent<Rigidbody2D>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInParent<SpriteRenderer>();

            if (_armLTransform != null) _armLOriginLocalPos = _armLTransform.localPosition;
            if (_armRTransform != null) _armROriginLocalPos = _armRTransform.localPosition;
            if (_bodyRenderer != null) _bodyOriginColor = _bodyRenderer.color;
            if (_armLRenderer != null) _armLOriginColor = _armLRenderer.color;
            if (_armRRenderer != null) _armROriginColor = _armRRenderer.color;

            // 2페이즈 전용 설정
            _isPhase2Only = true;
            _triggerGroggyOnRecovery = false;
        }

        private void OnDestroy()
        {
            _pulseTween?.Kill();
            _armLColorTween?.Kill();
            _armRColorTween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // Warning — 예고선 3개 순차 + 양팔 백스윙 + 붉은 Pulse
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

            // ③ 전신 붉은 Pulse (분노 연출)
            if (_bodyRenderer != null)
            {
                _pulseTween?.Kill();
                _pulseTween = _bodyRenderer
                    .DOColor(new Color(0.6f, 0f, 0f), _data.ColorData.sealReadyPulseDuration * 0.4f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }

            // ④ 팔 색상 붉은 전환
            if (_armLRenderer != null && _data != null)
            {
                _armLColorTween?.Kill();
                _armLColorTween = _armLRenderer
                    .DOColor(_data.colorWarning, 0.2f)
                    .SetUpdate(true);
            }
            if (_armRRenderer != null && _data != null)
            {
                _armRColorTween?.Kill();
                _armRColorTween = _armRRenderer
                    .DOColor(_data.colorWarning, 0.2f)
                    .SetUpdate(true);
            }

            // ⑤ 예고선 3개 순차 표시
            // ✅ v2.0 수정: transform.position → _rigid2D.position
            // 이유: transform 은 Patterns 자식 기준 → 부모 이동 시 위치 불명확
            float lineLength = _data.chargeDistance * 1.2f;

            // 0.0초 - 첫 번째 예고선
            Vector2 bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            _attackRange?.ShowRageChargeLine(0, bossPos, _chargeDirections[0], lineLength);
            yield return StartCoroutine(WaitForPattern(0.3f));
            if (_isInterrupted) { CleanupWarning(); yield break; }

            // 0.3초 - 두 번째 예고선
            bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            _attackRange?.ShowRageChargeLine(1, bossPos, _chargeDirections[1], lineLength);
            yield return StartCoroutine(WaitForPattern(0.3f));
            if (_isInterrupted) { CleanupWarning(); yield break; }

            // 0.6초 - 세 번째 예고선
            bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            _attackRange?.ShowRageChargeLine(2, bossPos, _chargeDirections[2], lineLength);
            yield return StartCoroutine(WaitForPattern(0.2f));
        }

        /// <summary>
        /// Warning 정리.
        /// Pulse Kill + 예고선 전체 제거.
        /// </summary>
        private void CleanupWarning()
        {
            _pulseTween?.Kill();
            _attackRange?.HideAllRageChargeLines();
        }

        // ══════════════════════════════════════════════════════
        // Active — 3회 순차 돌진 + 안전장치 3종
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted || _data == null || _rigid2D == null) yield break;

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
                float elapsed = 0f;
                float maxDuration = (_data.chargeDistance / Mathf.Max(speed, 0.1f)) * 1.5f;
                bool hitWall = false;

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

                        // ✅ v2.0 추가: 마지막 돌진 벽 충돌 시 추가 경직 (기획서 명시)
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

                // ③ 돌진 후 팔 살짝 복귀 (다음 돌진 준비 또는 Recovery 준비)
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
        // Recovery — 팔 원위치 + 색상 복귀 + 피로감
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // ① velocity 명시 정지 보장
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // ② 양팔 원위치 복귀
            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            // ③ 색상 복귀 — ✅ v2.0 추가 (Pulse Kill 후 중간 색상 고정 버그 수정)
            if (_bodyRenderer != null)
            {
                _pulseTween?.Kill();
                _bodyRenderer
                    .DOColor(_bodyOriginColor, _data?.ColorData.sealTransitionDuration ?? 0.1f)
                    .SetUpdate(true);
            }

            if (_armLRenderer != null)
            {
                _armLColorTween?.Kill();
                _armLColorTween = _armLRenderer
                    .DOColor(_armLOriginColor, _data?.ColorData.sealTransitionDuration ?? 0.1f)
                    .SetUpdate(true);
            }

            if (_armRRenderer != null)
            {
                _armRColorTween?.Kill();
                _armRColorTween = _armRRenderer
                    .DOColor(_armROriginColor, _data?.ColorData.sealTransitionDuration ?? 0.1f)
                    .SetUpdate(true);
            }

            // ④ 피로감 연출
            if (transform.parent != null)
            {
                transform.parent.DOShakePosition(
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
            _armLColorTween?.Kill();
            _armRColorTween?.Kill();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 팔 즉시 원위치
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armLOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armROriginLocalPos, 0.1f).SetEase(Ease.OutQuart);

            // 색상 즉시 복귀
            if (_armLRenderer != null)
                _armLRenderer.DOColor(_armLOriginColor, 0.1f).SetUpdate(true);
            if (_armRRenderer != null)
                _armRRenderer.DOColor(_armROriginColor, 0.1f).SetUpdate(true);

            base.Interrupt();
        }
    }
}