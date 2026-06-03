// ============================================================
// BossPattern_Slam.cs  v1.0
// Boss_Warden 내려치기 패턴
//
// [흐름]
//   Warning: 플레이어 현재 위치 스냅 → 원형 디스크 표시 + 왼팔 들어올림
//   Active:  왼팔 내려침 (OutBounce) + OverlapCircle 히트
//   Recovery: 왼팔 원위치 복귀 + DOShakeScale
//
// [2페이즈]: 내려치기 2회 연속 (0.5초 간격)
//
// [연결 부위] 왼팔 (LeftArm)
// [그로기 유발] 없음
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 내려치기 패턴. (v1.0)
    /// </summary>
    public class BossPattern_Slam : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]

        [Tooltip("BossWardenAttackRange.")]
        [SerializeField] private BossWardenAttackRange _attackRange;

        [Tooltip("BossWardenAI.")]
        [SerializeField] private BossWardenAI _ai;

        [Tooltip("BossWardenDataSO.")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 왼팔 Transform ──────────────────────")]

        /// <summary>
        /// 왼팔 Transform.
        /// DOLocalMoveY 로 들어올림 / 내려침 연출.
        /// </summary>
        [Tooltip("왼팔 Transform. LeftArm 오브젝트 연결.")]
        [SerializeField] private Transform _armLTransform;

        [Header("── 히트박스 ──────────────────────")]

        [Tooltip("Player 레이어 마스크.")]
        [SerializeField] private LayerMask _playerLayer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 왼팔 원래 로컬 위치 (Awake 에서 캐싱). </summary>
        private Vector3 _armOriginLocalPos;

        /// <summary> Warning 시 스냅한 플레이어 월드 위치 (1번째). </summary>
        private Vector2 _slamTarget0;

        /// <summary> 2페이즈 두 번째 내려치기 위치. </summary>
        private Vector2 _slamTarget1;

        private bool _isPhase2;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            if (_armLTransform != null)
                _armOriginLocalPos = _armLTransform.localPosition;

            _triggerGroggyOnRecovery = false;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            // 플레이어 위치 스냅 (이후 고정)
            _slamTarget0 = _ai != null && _ai.PlayerTransform != null
                ? (Vector2)_ai.PlayerTransform.position
                : (Vector2)transform.position;

            // 2페이즈: 두 번째 위치는 0.5초 후 결정 (여기서는 동일 위치로 초기화)
            _slamTarget1 = _slamTarget0;

            // 원형 디스크 표시
            _attackRange?.ShowSlamDisc(_slamTarget0, _data.slamWarningRadius, 0);

            // 왼팔 들어올림
            if (_armLTransform != null)
            {
                _armLTransform.DOLocalMoveY(
                    _armOriginLocalPos.y + 0.5f,
                    _warningDuration * 0.5f)
                    .SetEase(Ease.OutQuart);
            }

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;

            // 첫 번째 내려치기
            yield return StartCoroutine(ExecuteSlam(_slamTarget0, 0));
            if (_isInterrupted) yield break;

            // 2페이즈: 0.5초 후 두 번째 내려치기
            if (_isPhase2)
            {
                yield return StartCoroutine(WaitForPattern(0.5f));
                if (_isInterrupted) yield break;

                // 두 번째 위치: 현재 플레이어 위치로 갱신
                _slamTarget1 = _ai != null && _ai.PlayerTransform != null
                    ? (Vector2)_ai.PlayerTransform.position
                    : _slamTarget0;

                _attackRange?.ShowSlamDisc(_slamTarget1, _data.slamWarningRadius, 1);

                yield return StartCoroutine(WaitForPattern(0.2f)); // 짧은 예고
                yield return StartCoroutine(ExecuteSlam(_slamTarget1, 1));
            }
        }

        /// <summary>
        /// 단일 내려치기 실행 코루틴.
        /// 팔 내려침 + 디스크 플래시 + OverlapCircle.
        /// </summary>
        private IEnumerator ExecuteSlam(Vector2 targetPos, int discIndex)
        {
            if (_isInterrupted) yield break;

            float slamDuration = 0.2f;

            // 왼팔 빠르게 내려침
            if (_armLTransform != null)
            {
                _armLTransform.DOLocalMoveY(
                    _armOriginLocalPos.y - 0.3f,
                    slamDuration)
                    .SetEase(Ease.OutBounce);
            }

            // 디스크 플래시
            _attackRange?.FlashAndHideSlamDisc(discIndex);

            yield return new WaitForSeconds(slamDuration);

            // OverlapCircle 히트박스
            Collider2D hit = Physics2D.OverlapCircle(
                targetPos,
                _data != null ? _data.slamHitRadius : 2.5f,
                _playerLayer);

            if (hit != null)
                Debug.Log("[BossPattern_Slam] 플레이어 피격!");
        }

        // ══════════════════════════════════════════════════════
        // Recovery
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 왼팔 원위치 복귀
            if (_armLTransform != null)
            {
                _armLTransform.DOLocalMoveY(
                    _armOriginLocalPos.y,
                    _recoveryDuration * 0.5f)
                    .SetEase(Ease.OutQuart);
            }

            // 본체 DOShakeScale
            transform.DOShakeScale(
                duration: 0.3f,
                strength: 0.1f,
                vibrato: 8)
                .SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        public override void Interrupt()
        {
            _attackRange?.HideSlamDisc(0);
            _attackRange?.HideSlamDisc(1);

            // 팔 강제 원위치
            if (_armLTransform != null)
                _armLTransform.DOLocalMoveY(_armOriginLocalPos.y, 0.1f);

            base.Interrupt();
        }
    }
}

// ============================================================
// BossPattern_Sweep.cs  v1.0
// Boss_Warden 회전 스윕 패턴
//
// [흐름]
//   Warning: 반원형 디스크 표시 + 본체 느리게 회전 시작
//   Active:  본체 Z축 360° 회전 (DORotate FastBeyond360)
//            매 프레임 OverlapCircle 히트박스 체크
//   Recovery: 회전 정지
//
// [2페이즈]: 2회전 + 속도 증가
// [연결 부위] 왼팔 (LeftArm)
// [그로기 유발] 없음
// [namespace] SEAL
// ============================================================

namespace SEAL
{
    using System.Collections;
    using UnityEngine;
    using DG.Tweening;

    /// <summary>
    /// Boss_Warden 회전 스윕 패턴. (v1.0)
    /// </summary>
    public class BossPattern_Sweep : BossPatternBase
    {
        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private BossWardenAI _ai;
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 히트박스 ──────────────────────")]
        [SerializeField] private LayerMask _playerLayer;

        private bool _isPhase2;
        private bool _isSweeping;
        private Tween _rotateTween;

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            _triggerGroggyOnRecovery = false;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            float radius = _isPhase2 ? _data.sweepWarningRadius + 0.5f : _data.sweepWarningRadius;
            _attackRange?.ShowSweepDisc(transform.position, radius);

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted || _data == null) yield break;

            _isSweeping = true;

            // 회전 속도 → 지속시간 계산
            float rotateSpeed = _isPhase2 ? _data.phase2SweepRotateSpeed : _data.sweepRotateSpeed;
            int rotations = _isPhase2 ? 2 : 1;
            float totalAngle = 360f * rotations;
            float duration = totalAngle / rotateSpeed;

            // DORotate Z축 회전
            _rotateTween?.Kill();
            _rotateTween = transform
                .DORotate(
                    new Vector3(0f, 0f, transform.eulerAngles.z + totalAngle),
                    duration,
                    RotateMode.FastBeyond360)
                .SetEase(Ease.Linear)
                .SetUpdate(false); // 정상 TimeScale 기반

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (_isInterrupted)
                {
                    _rotateTween?.Kill();
                    _isSweeping = false;
                    yield break;
                }

                // 디스크 위치 업데이트 (보스와 함께)
                _attackRange?.UpdateSweepDiscPosition(transform.position);

                // 매 프레임 OverlapCircle 히트박스
                CheckSweepHit();

                elapsed += Time.deltaTime;
                yield return null;
            }

            _isSweeping = false;
            _attackRange?.HideSweepDisc();
        }

        private void CheckSweepHit()
        {
            if (_data == null) return;

            float radius = _isPhase2
                ? _data.sweepWarningRadius + 0.5f
                : _data.sweepHitRadius;

            Collider2D hit = Physics2D.OverlapCircle(
                transform.position,
                radius,
                _playerLayer);

            if (hit != null)
                Debug.Log("[BossPattern_Sweep] 플레이어 피격!");
        }

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;
            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        public override void Interrupt()
        {
            _rotateTween?.Kill();
            _isSweeping = false;
            _attackRange?.HideSweepDisc();
            base.Interrupt();
        }
    }
}

// ============================================================
// BossPattern_GuardBreak.cs  v1.0
// Boss_Warden 가드 → 강타 패턴
//
// [흐름]
//   Warning 전반부 (guardBreakGuardDuration):
//     가드 자세 유지 — 예고선 없음
//     정면 피격 봉인도 무효 (_isGuarding = true)
//   Warning 후반부:
//     정면 직사각형 예고 디스크 표시
//   Active (빠른 강타):
//     오른팔 DOLocalMove 전방 타격
//     OverlapBox 히트박스
//   Recovery (긴 후딜):
//     취약 구간 — recoveryVulnMultiplier 배율은 AI가 처리
//     OnPatternGroggy 발행
//
// [2페이즈]: 가드 구간 단축 (0.5초) + 히트박스 확장
// [연결 부위] 오른팔 (RightArm)
// [그로기 유발] Recovery 완료 시
// [namespace] SEAL
// ============================================================

namespace SEAL
{
    using System.Collections;
    using UnityEngine;
    using DG.Tweening;

    /// <summary>
    /// Boss_Warden 가드 → 강타 패턴. (v1.0)
    /// </summary>
    public class BossPattern_GuardBreak : BossPatternBase
    {
        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private BossWardenAI _ai;
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 오른팔 Transform ──────────────────────")]

        /// <summary>
        /// 오른팔 Transform.
        /// DOLocalMove 로 전방 강타 연출.
        /// </summary>
        [Tooltip("오른팔 Transform. RightArm 오브젝트 연결.")]
        [SerializeField] private Transform _armRTransform;

        [Header("── 히트박스 ──────────────────────")]
        [SerializeField] private LayerMask _playerLayer;

        // ── 내부 상태 ──
        private Vector3 _armOriginLocalPos;
        private bool _isPhase2;

        /// <summary>
        /// 가드 중 여부.
        /// true 시 이 패턴의 정면 피격은 봉인도 무효.
        /// BossWardenArmPart 에서 별도 체크하지 않고,
        /// 이 플래그를 public 으로 노출하여 외부에서 참조.
        /// </summary>
        public bool IsGuarding { get; private set; }

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            if (_armRTransform != null)
                _armOriginLocalPos = _armRTransform.localPosition;

            _triggerGroggyOnRecovery = true;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            float guardDuration = _isPhase2 ? 0.5f : _data.guardBreakGuardDuration;
            float totalWarning = _warningDuration;

            // 전반부: 가드 자세 (예고 없음)
            IsGuarding = true;
            yield return StartCoroutine(WaitForPattern(guardDuration));
            if (_isInterrupted)
            {
                IsGuarding = false;
                yield break;
            }

            // 후반부: 예고 디스크 표시
            IsGuarding = false;

            Vector2 dir = _ai != null ? _ai.FacingDir : Vector2.right;
            Vector2 warningSize = _isPhase2 ? new Vector2(1.8f, 1.0f) : _data.guardBreakWarningSize;
            _attackRange?.ShowGuardBreakDisc(transform.position, dir, warningSize);

            float remainingWarning = totalWarning - guardDuration;
            yield return StartCoroutine(WaitForPattern(Mathf.Max(0f, remainingWarning)));
        }

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted) yield break;
            if (_data == null) yield break;

            _attackRange?.HideGuardBreakDisc();

            Vector2 dir = _ai != null ? _ai.FacingDir : Vector2.right;

            // 오른팔 빠른 전방 타격
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(
                        _armOriginLocalPos + new Vector3(dir.x * 0.8f, dir.y * 0.8f, 0f),
                        0.1f)
                    .SetEase(Ease.OutExpo);
            }

            yield return new WaitForSeconds(0.1f);

            // OverlapBox 히트박스
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Vector2 boxSize = _isPhase2
                ? new Vector2(1.2f, 0.8f)
                : _data.guardBreakHitboxSize;

            Vector2 boxCenter = (Vector2)transform.position + dir * (boxSize.y * 0.5f);

            Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, angle, _playerLayer);
            if (hit != null)
                Debug.Log("[BossPattern_GuardBreak] 플레이어 피격!");

            // 팔 원위치 복귀
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armOriginLocalPos, 0.15f)
                    .SetEase(Ease.OutQuart);
            }
        }

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 긴 후딜 — recoveryVulnMultiplier 는 BossWardenAI 가 SetArmsRecoveryVuln 으로 처리
            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        public override void Interrupt()
        {
            IsGuarding = false;
            _attackRange?.HideGuardBreakDisc();

            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armOriginLocalPos, 0.1f);

            base.Interrupt();
        }
    }
}

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