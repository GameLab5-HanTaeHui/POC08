// ============================================================
// BossPatternCharge.cs  v2.4
// Boss_Warden 돌진 패턴
//
// [v2.4 수정 — 히트박스 판정 개선 + 예고선 너비 시각화]
//
//   [변경 1] CheckChargeHit() boxCenter 수정
//     기존: boxCenter = bossPos + dir * (hitboxSize.y * 0.5f)
//           → 보스 앞 절반에만 박스 생성
//           → 보스가 이미 지나친 플레이어 감지 불가
//           → 돌진 시작 지점 플레이어도 놓침
//
//     변경: boxCenter = bossPos (보스 중심 기준)
//           → 보스 몸통 전체가 히트박스로 작동
//           → 돌진 경로 위 플레이어 모두 감지 가능
//           → 탑뷰 2D 돌진 패턴 정합성 확보
//
//   [변경 2] OverlapBox → OverlapBoxAll 전환
//     기존: OverlapBox → 단일 Collider2D 반환 → 1명만 피격
//     변경: OverlapBoxAll + _hitResults 버퍼 → 다중 피격 지원
//           동일 플레이어 중복 피격 방지: _hasHitPlayer 플래그 유지
//
//   [변경 3] ShowChargeLine 너비 파라미터 추가
//     기존: ShowChargeLine(bossPos, dir, chargeWarningSize.y)
//           → 길이만 표시, 너비(폭) 시각화 없음
//           → 플레이어가 실제 피격 폭을 인지 불가
//
//     변경: ShowChargeLine(bossPos, dir, chargeWarningSize.y, chargeWarningSize.x)
//           → BossWardenAttackRange 에서 너비 파라미터 수신
//           → LineRenderer 에서 startWidth/endWidth 에 너비 적용
//           → 실제 히트박스 폭과 예고선 폭이 대응
//
// [v2.3 유지]
//   벽 충돌 ContactFilter2D 정확 감지
//   팔 DOLocalRotate 방향 회전
//   Recovery DOShakePosition
//   Interrupt 오버라이드
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 돌진 패턴. (v2.4)
    ///
    /// ────────────────────────────────────────────────────
    /// [히트박스 구조 — v2.4]
    ///   boxCenter = _rigid2D.position (보스 중심)
    ///   boxSize   = chargeHitboxSize  (width × height)
    ///   angle     = Atan2(dir) * Rad2Deg
    ///   OverlapBoxAll 로 다중 피격 지원
    ///   _hasHitPlayer 로 중복 피격 방지
    ///
    /// [예고선 시각화 — v2.4]
    ///   길이: chargeWarningSize.y
    ///   너비: chargeWarningSize.x → LineRenderer.startWidth/endWidth
    ///   → 실제 히트박스 너비와 예고선 너비가 대응
    /// ────────────────────────────────────────────────────
    /// </summary>
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

        [Tooltip("BossWardenAI. 미연결 시 자동 탐색.")]
        [SerializeField] private BossWardenAI _ai;

        [Header("── DataSO ──────────────────────")]

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 오른팔 Transform / Renderer ──────────────────────")]

        [Tooltip("오른팔 Transform.")]
        [SerializeField] private Transform _armRTransform;

        [Tooltip("오른팔 SpriteRenderer. 색상 연출용.")]
        [SerializeField] private SpriteRenderer _armRRenderer;

        [Header("── 레이어 ──────────────────────")]

        [Tooltip("플레이어 HurtBox 레이어. PlayerAttackHitBox 레이어 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        [Tooltip("Wall 레이어만 선택. 벽 충돌 정확 감지용.")]
        [SerializeField] private LayerMask _wallLayer;

        [Header("── 연출 수치 ──────────────────────")]

        [Tooltip("백스윙 당기는 거리. 권장: 0.5")]
        [Min(0f)]
        [SerializeField] private float _windupPullAmount = 0.5f;

        [Tooltip("팔 앞으로 뻗는 거리. 권장: 0.4")]
        [Min(0f)]
        [SerializeField] private float _thrustAmount = 0.4f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>돌진 방향 (Warning 시 고정).</summary>
        private Vector2 _chargeDirection;

        /// <summary>돌진 시작 위치 (거리 계산 기준).</summary>
        private Vector2 _chargeStartPos;

        /// <summary>플레이어 피격 완료 여부 (중복 피격 방지).</summary>
        private bool _hasHitPlayer;

        /// <summary>2페이즈 활성 여부.</summary>
        private bool _isPhase2;

        /// <summary>팔 원래 로컬 위치.</summary>
        private Vector3 _armOriginLocalPos;

        /// <summary>팔 원래 색상.</summary>
        private Color _armOriginColor;

        /// <summary>보스 본체 Transform.</summary>
        private Transform _bossTransform;

        /// <summary>색상 Tween 핸들.</summary>
        private Tweener _armColorTween;

        /// <summary>
        /// OverlapBoxAll 결과 버퍼.
        /// GC 할당 방지용 미리 할당.
        /// </summary>
        private readonly Collider2D[] _hitResults = new Collider2D[8];

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

            _bossTransform = _rigid2D != null ? _rigid2D.transform : transform.parent;

            if (_armRTransform != null)
                _armOriginLocalPos = _armRTransform.localPosition;
            if (_armRRenderer != null)
                _armOriginColor = _armRRenderer.color;

            // ⚠️ _triggerGroggyOnRecovery 는 Inspector/Prefab 값 사용
            // Awake 에서 강제 설정 금지 (Prefab 직렬화 값 덮어쓰기 버그 방지)
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
        // Warning — 예고선 + 팔 백스윙 + Pulse
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            // ① 돌진 방향 고정
            _chargeDirection = _ai != null ? _ai.FacingDir : Vector2.right;

            // ② 예고선 표시
            // v2.4: chargeWarningSize.x(너비) 도 함께 전달
            Vector2 bossPos = _rigid2D != null
                ? _rigid2D.position
                : (Vector2)transform.position;

            _attackRange?.ShowChargeLine(
                bossPos,
                _chargeDirection,
                _data.chargeWarningSize.y,
                _data.chargeWarningSize.x);

            // ③ 팔 백스윙
            if (_armRTransform != null)
            {
                Vector3 worldBackDir = new Vector3(
                    -_chargeDirection.x, -_chargeDirection.y, 0f);
                Vector3 localBackDir = _bossTransform != null
                    ? _bossTransform.InverseTransformDirection(worldBackDir)
                    : worldBackDir;

                _armRTransform
                    .DOLocalMove(
                        _armOriginLocalPos + localBackDir * _windupPullAmount,
                        _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);

                // 팔이 돌진 방향을 바라보도록 Z 회전
                float lookAngle = Mathf.Atan2(_chargeDirection.y, _chargeDirection.x)
                                  * Mathf.Rad2Deg + 90f;
                _armRTransform
                    .DOLocalRotate(
                        new Vector3(0f, 0f, lookAngle),
                        _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            // ④ 팔 주황 Pulse
            if (_armRRenderer != null && _data != null)
            {
                _armColorTween?.Kill();
                _armColorTween = _armRRenderer
                    .DOColor(_data.colorWarning,
                             _data.ColorData.sealReadyPulseDuration * 0.4f)
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
            if (_data == null || _rigid2D == null) yield break;

            // ① 예고선 제거 + Pulse 정지
            _attackRange?.HideChargeLine();
            _armColorTween?.Kill();

            // ② 팔 앞으로 뻗기
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

            // ③ 팔 순간 흰색
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

            Debug.Log($"[BossPattern_Charge] ▶ 돌진 시작 | 방향:{_chargeDirection} " +
                      $"속도:{speed} 최대거리:{_data.chargeDistance}");

            while (true)
            {
                if (_isInterrupted)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    yield break;
                }

                float distanceTraveled = Vector2.Distance(_chargeStartPos, _rigid2D.position);
                float currentSpeed = _rigid2D.linearVelocity.magnitude;
                elapsed += Time.deltaTime;

                frameLog++;
                if (frameLog % 30 == 0)
                    Debug.Log($"[BossPattern_Charge] 진행 | " +
                              $"거리:{distanceTraveled:F2}/{_data.chargeDistance} " +
                              $"속도:{currentSpeed:F2}");

                // 피격 체크 (중복 피격 방지)
                if (!_hasHitPlayer)
                    CheckChargeHit();

                // 안전장치 ① 거리 도달
                if (distanceTraveled >= _data.chargeDistance)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    Debug.Log($"[BossPattern_Charge] 종료 — 거리 도달 ({distanceTraveled:F2})");
                    break;
                }

                // 안전장치 ② 벽 충돌 감지 (Wall 레이어만)
                if (elapsed > 0.1f && currentSpeed < 0.5f)
                {
                    bool isWallContact = false;
                    if (_rigid2D != null && _wallLayer != 0)
                    {
                        ContactFilter2D filter = new ContactFilter2D();
                        filter.SetLayerMask(_wallLayer);
                        filter.useTriggers = false;
                        var contacts = new Collider2D[4];
                        int count = _rigid2D.Overlap(filter, contacts);
                        isWallContact = count > 0;
                    }

                    if (isWallContact)
                    {
                        _rigid2D.linearVelocity = Vector2.zero;
                        Debug.LogWarning("[BossPattern_Charge] 종료 — Wall 충돌");
                        break;
                    }
                    else
                    {
                        // 벽 아닌 레이어 충돌 → 재가속
                        _rigid2D.linearVelocity = _chargeDirection * speed;
                    }
                }

                // 안전장치 ③ 타임아웃
                if (elapsed >= maxDuration)
                {
                    _rigid2D.linearVelocity = Vector2.zero;
                    Debug.LogWarning("[BossPattern_Charge] 종료 — 타임아웃");
                    break;
                }

                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        // OverlapBox 히트박스 — v2.4 수정
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// OverlapBoxAll 로 플레이어 피격 체크.
        ///
        /// [v2.4 변경]
        ///   boxCenter = _rigid2D.position (보스 중심)
        ///     기존: bossPos + dir * (height * 0.5f) → 앞 절반에만 박스
        ///     변경: 보스 중심 기준 → 돌진 경로 전체 커버
        ///
        ///   OverlapBox → OverlapBoxAll
        ///     기존: 단일 Collider2D 반환 → 1명만 피격
        ///     변경: _hitResults 버퍼 → 다중 피격 지원
        /// </summary>
        private void CheckChargeHit()
        {
            if (_data == null || _rigid2D == null) return;

            float angle = Mathf.Atan2(_chargeDirection.y, _chargeDirection.x) * Mathf.Rad2Deg;

            // v2.4: 보스 중심 기준 (앞쪽 오프셋 제거)
            Vector2 boxCenter = _rigid2D.position;

            int count = Physics2D.OverlapBoxNonAlloc(
                boxCenter,
                _data.chargeHitboxSize,
                angle,
                _hitResults,
                _playerLayer);

            if (count > 0)
            {
                _hasHitPlayer = true;
                for (int i = 0; i < count; i++)
                {
                    if (_hitResults[i] != null)
                        Debug.Log($"[BossPattern_Charge] ✅ 피격: {_hitResults[i].name}");
                }
            }
        }

        // ══════════════════════════════════════════════════════
        // Recovery
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;
            if (_isPhase2) yield break; // 2페이즈: Recovery 스킵

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            // 팔 원위치 복귀
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armOriginLocalPos, _recoveryDuration * 0.35f)
                    .SetEase(Ease.OutBack);
                _armRTransform
                    .DOLocalRotate(Vector3.zero, _recoveryDuration * 0.35f)
                    .SetEase(Ease.OutBack);
            }

            // 충격 흔들림
            _bossTransform?.DOShakePosition(
                duration: 0.3f,
                strength: 0.25f,
                vibrato: 10,
                randomness: 90f)
                .SetUpdate(true);

            // 팔 색상 복귀
            if (_armRRenderer != null)
            {
                _armColorTween?.Kill();
                _armColorTween = _armRRenderer
                    .DOColor(
                        _armOriginColor,
                        _data?.ColorData.sealTransitionDuration ?? 0.1f)
                    .SetUpdate(true);
            }

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            _armColorTween?.Kill();
            _attackRange?.HideChargeLine();

            if (_armRTransform != null)
            {
                _armRTransform.DOLocalMove(_armOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
                _armRTransform.DOLocalRotate(Vector3.zero, 0.1f).SetEase(Ease.OutQuart);
            }

            if (_armRRenderer != null)
                _armRRenderer.DOColor(_armOriginColor, 0.1f).SetUpdate(true);

            base.Interrupt();
        }

        // ══════════════════════════════════════════════════════
        // Gizmos
        // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_data == null || _rigid2D == null) return;

            // 히트박스 시각화 (보스 중심 기준)
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            float angle = Mathf.Atan2(_chargeDirection.y, _chargeDirection.x) * Mathf.Rad2Deg;
            Vector2 center = Application.isPlaying ? _rigid2D.position : (Vector2)transform.position;

            // GizmosMatrix 로 회전 적용
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                new Vector3(center.x, center.y, 0f),
                Quaternion.Euler(0f, 0f, angle),
                Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero,
                new Vector3(_data.chargeHitboxSize.x, _data.chargeHitboxSize.y, 0.1f));
            Gizmos.matrix = oldMatrix;

            // 예고 범위 (너비 포함)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
            Gizmos.matrix = Matrix4x4.TRS(
                new Vector3(center.x, center.y, 0f),
                Quaternion.Euler(0f, 0f, angle),
                Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero,
                new Vector3(_data.chargeWarningSize.x, _data.chargeWarningSize.y, 0.1f));
            Gizmos.matrix = oldMatrix;
        }
#endif
    }
}