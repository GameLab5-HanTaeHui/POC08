// ============================================================
// BossPattern_Charge.cs  v2.0
// Boss_Warden 돌진 패턴 — 팔 모션 + 연출 추가
//
// [v2.0 변경]
//   기존(v1.3): 팔 연출 없음. 예고선만 있고 본체가 그냥 돌진.
//   추가:
//     Warning:  오른팔 백스윙 DOLocalMove(반대방향 × pullAmount, OutBack)
//               본체 DOScale(1.1f) 웅크리기 (힘 모으기)
//               본체 + 오른팔 주황 Pulse DOColor
//
//     Active:   예고선 제거
//               오른팔 앞으로 뻗기 DOLocalMove(방향 × thrustAmount, OutExpo)
//               linearVelocity 돌진 (기존 로직 유지)
//               안전장치 3종 유지 (타임아웃 / 속도감지 / 거리도달)
//
//     Recovery: linearVelocity = 0 명시
//               오른팔 원위치 복귀 DOLocalMove(OutBack)
//               본체 DOScale(1.0f) 크기 복귀
//               DOShakePosition 충격 연출
//               오른팔 색상 복귀
//               [2페이즈] Recovery 스킵
//
// [v1.3 유지]
//   _rigid2D.position 기반 거리 계산 (transform.position 버그 수정 완료 상태 유지)
//   안전장치 3종: 거리도달 / 속도0 감지(벽충돌) / 타임아웃
//   상세 디버그 로그 (Active 진행 30프레임마다 + 종료 원인)
//
// [레이어]
//   _playerLayer = PlayerAttackHitBox 레이어
//
// [연결 부위] 오른팔 (RightArm)
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 돌진 패턴. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning : 예고선 + 오른팔 백스윙 + 본체 웅크리기 + 주황 Pulse
    ///   Active  : 팔 앞으로 뻗기 → linearVelocity 돌진 → 안전장치 3종
    ///   Recovery: velocity 0 → 팔 복귀 → 크기 복귀 → DOShakePosition
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossPattern_Charge : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]

        /// <summary>예고 범위 표시. 미연결 시 GetComponentInParent 자동 탐색.</summary>
        [Tooltip("BossWardenAttackRange. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        /// <summary>보스 Rigidbody2D. 미연결 시 GetComponentInParent 자동 탐색.</summary>
        [Tooltip("Rigidbody2D. 미연결 시 자동 탐색.")]
        [SerializeField] private Rigidbody2D _rigid2D;

        /// <summary>BossWardenAI. 플레이어 방향 참조.</summary>
        [Tooltip("BossWardenAI. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAI _ai;

        [Header("── DataSO ──────────────────────")]

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 오른팔 Transform / Renderer ──────────────────────")]

        /// <summary>
        /// 오른팔 Transform.
        /// Warning 백스윙 + Active 뻗기 + Recovery 복귀 연출 주체.
        /// </summary>
        [Tooltip("오른팔 Transform. RightArm 오브젝트 연결.")]
        [SerializeField] private Transform _armRTransform;

        /// <summary>오른팔 SpriteRenderer. 색상 연출용.</summary>
        [Tooltip("오른팔 SpriteRenderer.")]
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
        /// 돌진 반대 방향으로 이 거리만큼 팔을 당김.
        /// </summary>
        [Tooltip("백스윙 당기는 거리. 권장: 0.5")]
        [Min(0f)]
        [SerializeField] private float _windupPullAmount = 0.5f;

        /// <summary>
        /// Active 시작 시 팔이 앞으로 뻗는 거리.
        /// 돌진 방향으로 이 거리만큼 팔을 뻗음.
        /// </summary>
        [Tooltip("돌진 시 팔 뻗기 거리. 권장: 0.4")]
        [Min(0f)]
        [SerializeField] private float _thrustAmount = 0.4f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> Warning 시 고정된 돌진 방향. </summary>
        private Vector2 _chargeDirection;

        /// <summary> 돌진 시작 위치 (거리 계산용). </summary>
        private Vector2 _chargeStartPos;

        /// <summary> 2페이즈 여부. </summary>
        private bool _isPhase2;

        /// <summary> 이번 Active 에서 이미 플레이어를 피격했는지. </summary>
        private bool _hasHitPlayer;

        /// <summary> 오른팔 원래 로컬 위치 (Awake 에서 캐싱). </summary>
        private Vector3 _armOriginLocalPos;

        /// <summary> 오른팔 원래 색상. </summary>
        private Color _armOriginColor;

        /// <summary> 색상 Tween 핸들. </summary>
        private Tweener _armColorTween;

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

            if (_armRTransform != null)
                _armOriginLocalPos = _armRTransform.localPosition;
            if (_armRRenderer != null)
                _armOriginColor = _armRRenderer.color;

            // ✅ v1.1 에서 수정: Awake 강제설정 제거 — Inspector 직렬화값 사용
        }

        private void OnDestroy()
        {
            _armColorTween?.Kill();
        }

        // ══════════════════════════════════════════════════════
        // BossPatternBase 오버라이드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 2페이즈 활성화.
        /// 돌진 속도 증가 + Recovery 스킵 적용.
        /// </summary>
        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 예고선 + 오른팔 백스윙 + 웅크리기
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            // ① 돌진 방향 고정
            _chargeDirection = _ai != null ? _ai.FacingDir : Vector2.right;

            // ② 예고선 표시
            Vector2 bossPos = _rigid2D != null
                ? _rigid2D.position
                : (Vector2)transform.position;

            _attackRange?.ShowChargeLine(bossPos, _chargeDirection, _data.chargeWarningSize.y);

            // ③ 오른팔 백스윙: 돌진 반대 방향으로 당기기
            if (_armRTransform != null)
            {
                Vector3 backswingOffset = new Vector3(
                    -_chargeDirection.x * _windupPullAmount,
                    -_chargeDirection.y * _windupPullAmount,
                    0f);

                _armRTransform
                    .DOLocalMove(_armOriginLocalPos + backswingOffset,
                                 _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            // ④ 본체 웅크리기 (힘 모으기 느낌)
            transform.parent?.DOScale(1.1f, _warningDuration * 0.4f)
                .SetEase(Ease.OutBack);

            // ⑤ 오른팔 + 본체 주황 Pulse
            if (_armRRenderer != null && _data != null)
            {
                _armColorTween?.Kill();
                _armColorTween = _armRRenderer
                    .DOColor(_data.colorWarning, _data.pulsePeriod * 0.4f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 팔 뻗기 + 돌진 + 안전장치 3종
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_data == null || _rigid2D == null)
            {
                Debug.LogError($"[BossPattern_Charge] OnActive: _data={_data}, _rigid2D={_rigid2D} — null 스킵");
                yield break;
            }

            // ① 예고선 제거 + Pulse 정지
            _attackRange?.HideChargeLine();
            _armColorTween?.Kill();

            // ② 오른팔 앞으로 뻗기 (돌진 방향)
            if (_armRTransform != null)
            {
                Vector3 thrustOffset = new Vector3(
                    _chargeDirection.x * _thrustAmount,
                    _chargeDirection.y * _thrustAmount,
                    0f);

                _armRTransform
                    .DOLocalMove(_armOriginLocalPos + thrustOffset, 0.1f)
                    .SetEase(Ease.OutExpo);
            }

            // ③ 오른팔 순간 흰색 (공격 시작 신호)
            if (_armRRenderer != null)
                _armRRenderer.color = Color.white;

            // ④ 돌진 시작
            _chargeStartPos = _rigid2D.position;
            _hasHitPlayer = false;

            float speed = _isPhase2 ? _data.phase2ChargeSpeed : _data.chargeSpeed;
            float maxDuration = (_data.chargeDistance / Mathf.Max(speed, 0.1f)) * 1.5f;
            float elapsed = 0f;
            int frameLog = 0;

            _rigid2D.linearVelocity = _chargeDirection * speed;

            Debug.Log($"[BossPattern_Charge] 돌진 시작 | 방향:{_chargeDirection} 속도:{speed} 최대거리:{_data.chargeDistance} 타임아웃:{maxDuration:F2}s");

            while (true)
            {
                if (_isInterrupted)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    Debug.Log("[BossPattern_Charge] isInterrupted → 돌진 중단");
                    yield break;
                }

                float distanceTraveled = Vector2.Distance(_chargeStartPos, _rigid2D.position);
                float currentSpeed = _rigid2D.linearVelocity.magnitude;
                elapsed += Time.deltaTime;

                frameLog++;
                if (frameLog % 30 == 0)
                    Debug.Log($"[BossPattern_Charge] 진행 | 거리:{distanceTraveled:F2}/{_data.chargeDistance} 속도:{currentSpeed:F2} 경과:{elapsed:F2}s");

                if (!_hasHitPlayer)
                    CheckChargeHit();

                // 안전장치 ① 거리 도달
                if (distanceTraveled >= _data.chargeDistance)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    Debug.Log($"[BossPattern_Charge] 종료 — 거리 도달 ({distanceTraveled:F2})");
                    break;
                }

                // 안전장치 ② 속도 0 감지 (벽 충돌)
                if (elapsed > 0.1f && currentSpeed < 0.5f)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    Debug.LogWarning($"[BossPattern_Charge] 종료 — 벽 충돌 추정 ({distanceTraveled:F2})");
                    break;
                }

                // 안전장치 ③ 타임아웃
                if (elapsed >= maxDuration)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    Debug.LogWarning($"[BossPattern_Charge] 종료 — 타임아웃 ({elapsed:F2}s)");
                    break;
                }

                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        // OverlapBox 히트박스
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// OverlapBox 로 플레이어 피격 체크.
        /// _rigid2D.position 기준 (Patterns 자식 오브젝트 위치 오류 방지).
        /// </summary>
        private void CheckChargeHit()
        {
            if (_data == null || _rigid2D == null) return;

            float angle = Mathf.Atan2(_chargeDirection.y, _chargeDirection.x) * Mathf.Rad2Deg;
            Vector2 boxCenter = _rigid2D.position
                + _chargeDirection * (_data.chargeHitboxSize.y * 0.5f);

            Collider2D hit = Physics2D.OverlapBox(
                boxCenter,
                _data.chargeHitboxSize,
                angle,
                _playerLayer);

            if (hit != null)
            {
                _hasHitPlayer = true;
                Debug.Log("[BossPattern_Charge] 플레이어 피격!");
            }
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 복귀 + 크기 복귀 + 충격 연출
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 2페이즈: Recovery 스킵
            if (_isPhase2) yield break;

            // ① velocity 명시 정지 (Active 종료 후 잔여 속도 보장)
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // ② 오른팔 원위치 복귀
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armOriginLocalPos, _recoveryDuration * 0.35f)
                    .SetEase(Ease.OutBack);
            }

            // ③ 본체 크기 복귀
            if (transform.parent != null)
            {
                transform.parent
                    .DOScale(1.0f, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);
            }

            // ④ 충격 흔들림
            transform.parent?.DOShakePosition(
                duration: 0.3f,
                strength: 0.25f,
                vibrato: 10,
                randomness: 90f)
                .SetUpdate(true);

            // ⑤ 오른팔 색상 복귀
            if (_armRRenderer != null)
            {
                _armColorTween?.Kill();
                _armColorTween = _armRRenderer
                    .DOColor(_armOriginColor, _data?.colorTransitionDuration ?? 0.1f)
                    .SetUpdate(true);
            }

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 강제 중단.
        /// 돌진 중 velocity 즉시 0 + 예고선 제거 + 팔 복귀.
        /// </summary>
        public override void Interrupt()
        {
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            _armColorTween?.Kill();
            _attackRange?.HideChargeLine();

            // 팔 즉시 원위치
            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);

            // 팔 색상 복귀
            if (_armRRenderer != null)
                _armRRenderer.DOColor(_armOriginColor, 0.1f).SetUpdate(true);

            // 본체 크기 복귀
            transform.parent?.DOScale(1.0f, 0.1f).SetEase(Ease.OutQuart);

            base.Interrupt();
        }
    }
}