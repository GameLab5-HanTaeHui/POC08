// ============================================================
// BossPattern_Sweep.cs  v3.2
// Boss_Warden 회전 스윕 패턴 — 원심력 팔 날리기 + 수거
//
// [v3.2 수정]
//   🔴 팔 벌리기 방향 오프셋 InverseTransformDirection 적용
//       기존: 월드 방향 벡터를 로컬 오프셋에 그대로 적용
//             → flipX 상태에서 방향 반전 오류
//       수정: _bossTransform.InverseTransformDirection → 로컬 방향 정확 변환
//
//   🔴 팔 벌릴 때 방향 회전 추가 (Warning)
//       Vector.Down 이 팔 벌린 방향을 향함 → + 90f 오프셋
//       Slam / Charge / GuardBreak 와 동일 원칙 적용
//
//   🔴 팔 날아갈 때 회전 오프셋 수정 (Active)
//       기존: Atan2 - 90f → Vector.Up 기준 (잘못된 방향)
//       수정: Atan2 + 90f → Vector.Down 기준 (손바닥 아래 이미지)
//   변경:
//     Warning:  FacingDir 기준 좌우 수직(perpendicular) 방향으로 양팔 벌리기
//               예고 디스크 보스 중심
//               본체 + 팔 주황 Pulse
//     Active-회전: Boss_Warden 본체 Transform DORotate (캐싱된 _bossTransform)
//                  팔이 자식이므로 함께 회전 → 스윕 연출 자연스러움
//                  매 프레임 OverlapCircle 피격 판정
//     Active-날리기: 회전 완료 시 팔의 현재 월드 방향 계산
//                    양팔 SetParent(null) 분리
//                    원심력 방향으로 DOMove (날아감)
//                    공략 타임: 두 팔 모두 공격 가능
//                    귀환: DOMove 보스 위치로 복귀 → SetParent 재부착
//     Recovery: 본체 Z각도 복구 + 색상 복귀
//
// [2페이즈]
//   2회전 + 더 멀리 날아감 (flyDist × 1.5)
//
// [원심력 방향 계산]
//   회전 완료 시점 팔의 현재 월드 방향:
//   bossForward = bossTransform.up (또는 right, Hierarchy 구성에 따라)
//   perpL = (-bossForward.y,  bossForward.x) ← 왼팔 원심력 방향
//   perpR = ( bossForward.y, -bossForward.x) ← 오른팔 원심력 방향
//
// [레이어]
//   _playerLayer = PlayerAttackHitBox 레이어
//   팔 분리 중에도 팔의 EnemyAttackHitBox Collider 살아있음 → 봉인도 정상 누적
//
// [연결 부위] 왼팔 (LeftArm)
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 회전 스윕 패턴 — 원심력 팔 날리기. (v3.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning : 양팔 수직 방향 벌리기 → 예고 디스크 → 주황 Pulse
    ///   Active  : 본체 360° 회전 → 팔 함께 스윕 → 회전 완료 후 원심력으로 분리
    ///             공략 타임 → 귀환 재부착
    ///   Recovery: Z각도 복구 → 팔 원위치 → 색상 복귀
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class BossPattern_Sweep : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private BossWardenAI _ai;
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 Transform / Renderer ──────────────────────")]

        [Tooltip("왼팔 Transform.")]
        [SerializeField] private Transform _armLTransform;

        [Tooltip("오른팔 Transform.")]
        [SerializeField] private Transform _armRTransform;

        [Tooltip("왼팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armLRenderer;

        [Tooltip("오른팔 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _armRRenderer;

        [Tooltip("보스 본체 SpriteRenderer.")]
        [SerializeField] private SpriteRenderer _bodyRenderer;

        [Header("── 레이어 ──────────────────────")]

        /// <summary>
        /// 플레이어 HurtBox 레이어 마스크.
        /// PlayerAttackHitBox 레이어 선택.
        /// </summary>
        [Tooltip("플레이어 HurtBox 레이어. PlayerAttackHitBox 레이어 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 ──────────────────────")]

        /// <summary>
        /// Warning 시 팔이 좌우로 벌어지는 거리 (로컬 오프셋).
        /// </summary>
        [Tooltip("팔 벌리기 거리. 권장: 0.6")]
        [Min(0f)]
        [SerializeField] private float _armSpreadAmount = 0.6f;

        /// <summary>
        /// 회전 완료 후 팔이 날아가는 거리.
        /// 2페이즈에서는 × 1.5 적용.
        /// </summary>
        [Tooltip("원심력 날아가는 거리. 권장: 1.5")]
        [Min(0.5f)]
        [SerializeField] private float _flyDistance = 1.5f;

        /// <summary>
        /// 팔이 날아가는 데 걸리는 시간 (초).
        /// </summary>
        [Tooltip("팔 날아가는 시간 (초). 권장: 0.2")]
        [Min(0.05f)]
        [SerializeField] private float _flyDuration = 0.2f;

        /// <summary>
        /// 팔이 날아간 후 공략 타임 지속 시간 (초).
        /// </summary>
        [Tooltip("팔 공략 타임 지속 시간 (초). 권장: 1.5")]
        [Min(0.5f)]
        [SerializeField] private float _flyVulnDuration = 1.5f;

        /// <summary>
        /// 팔 귀환 시간 (초).
        /// </summary>
        [Tooltip("팔 귀환 시간 (초). 권장: 0.3")]
        [Min(0.05f)]
        [SerializeField] private float _returnDuration = 0.3f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary> 왼팔 원래 로컬 위치. </summary>
        private Vector3 _armLOriginLocalPos;

        /// <summary> 오른팔 원래 로컬 위치. </summary>
        private Vector3 _armROriginLocalPos;

        /// <summary> 왼팔 원래 색상. </summary>
        private Color _armLOriginColor;

        /// <summary> 오른팔 원래 색상. </summary>
        private Color _armROriginColor;

        /// <summary>
        /// 보스 본체 Transform. 회전 대상 + 팔 재부착 대상.
        /// Awake 에서 캐싱 (GetComponentInParent 매 프레임 호출 방지).
        /// </summary>
        private Transform _bossTransform;

        /// <summary>
        /// 보스 Rigidbody2D. 월드 위치 참조.
        /// </summary>
        private Rigidbody2D _rigid2D;

        /// <summary> 2페이즈 여부. </summary>
        /// <summary> 왼팔 원래 로컬 스케일. SetParent 분리/재부착 시 복구용. </summary>
        private Vector3 _armLOriginLocalScale;

        /// <summary> 오른팔 원래 로컬 스케일. SetParent 분리/재부착 시 복구용. </summary>
        private Vector3 _armROriginLocalScale;

        private bool _isPhase2;

        /// <summary>
        /// 팔 분리 상태 추적.
        /// true = 양팔 분리 중 / false = 보스에 부착 중.
        /// </summary>
        private bool _isArmsDetached;

        private Tweener _rotateTween;
        private Tweener _bodyColorTween;
        private Tweener _armLColorTween;
        private Tweener _armRColorTween;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInParent<SpriteRenderer>();

            // Boss_Warden 본체 참조 캐싱 (매 프레임 GetComponentInParent 방지)
            _rigid2D = GetComponentInParent<Rigidbody2D>();
            _bossTransform = _rigid2D != null ? _rigid2D.transform : transform.parent;

            if (_armLTransform != null)
            {
                _armLOriginLocalPos = _armLTransform.localPosition;
                _armLOriginLocalScale = _armLTransform.localScale; // ✅ Scale 캐싱
            }
            if (_armRTransform != null)
            {
                _armROriginLocalPos = _armRTransform.localPosition;
                _armROriginLocalScale = _armRTransform.localScale; // ✅ Scale 캐싱
            }
            if (_armLRenderer != null) _armLOriginColor = _armLRenderer.color;
            if (_armRRenderer != null) _armROriginColor = _armRRenderer.color;

            _triggerGroggyOnRecovery = false;
        }

        private void OnDestroy()
        {
            _rotateTween?.Kill();
            _bodyColorTween?.Kill();
            _armLColorTween?.Kill();
            _armRColorTween?.Kill();

            ReattachArms();
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — FacingDir 기준 양팔 수직 벌리기
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            float radius = _isPhase2 ? _data.sweepHitRadius + 0.5f : _data.sweepHitRadius;
            Vector2 bossPos = GetBossWorldPos();

            // ① 예고 디스크 표시
            _attackRange?.ShowSweepDisc(bossPos, radius);

            // ② FacingDir 기준 좌우 수직 방향 계산
            //    perpL = 왼쪽 수직 / perpR = 오른쪽 수직
            Vector2 forward = _ai != null ? _ai.FacingDir : Vector2.right;
            Vector2 perpL = new Vector2(-forward.y, forward.x);
            Vector2 perpR = new Vector2(forward.y, -forward.x);

            // ✅ v3.2 수정: InverseTransformDirection 으로 월드 방향 → 로컬 변환
            // 기존: 월드 방향 벡터를 로컬 오프셋에 그대로 적용
            //       → flipX 상태에서 방향 반전 오류
            // 수정: _bossTransform.InverseTransformDirection 으로 정확한 로컬 방향 변환
            Vector3 localPerpL = _bossTransform != null
                ? _bossTransform.InverseTransformDirection(new Vector3(perpL.x, perpL.y, 0f))
                : new Vector3(perpL.x, perpL.y, 0f);
            Vector3 localPerpR = _bossTransform != null
                ? _bossTransform.InverseTransformDirection(new Vector3(perpR.x, perpR.y, 0f))
                : new Vector3(perpR.x, perpR.y, 0f);

            // 팔 벌리기: 로컬 수직 방향으로 이동
            if (_armLTransform != null)
            {
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos + localPerpL * _armSpreadAmount,
                                 _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);

                // ✅ v3.2 추가: Vector.Down 이 팔 벌린 방향을 향함 + 90f
                float armLAngleW = Mathf.Atan2(perpL.y, perpL.x) * Mathf.Rad2Deg + 90f;
                _armLTransform
                    .DOLocalRotate(new Vector3(0f, 0f, armLAngleW), _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);
            }
            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + localPerpR * _armSpreadAmount,
                                 _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);

                // ✅ v3.2 추가: Vector.Down 이 팔 벌린 방향을 향함 + 90f
                float armRAngleW = Mathf.Atan2(perpR.y, perpR.x) * Mathf.Rad2Deg + 90f;
                _armRTransform
                    .DOLocalRotate(new Vector3(0f, 0f, armRAngleW), _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);
            }

            // ③ 본체 + 팔 주황 Pulse
            if (_bodyRenderer != null)
            {
                _bodyColorTween?.Kill();
                _bodyColorTween = _bodyRenderer
                    .DOColor(_data.colorWarning, _data.ColorData.sealReadyPulseDuration * 0.4f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
            if (_armLRenderer != null)
            {
                _armLColorTween?.Kill();
                _armLColorTween = _armLRenderer
                    .DOColor(_data.colorWarning, _data.ColorData.sealReadyPulseDuration * 0.4f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
            if (_armRRenderer != null)
            {
                _armRColorTween?.Kill();
                _armRColorTween = _armRRenderer
                    .DOColor(_data.colorWarning, _data.ColorData.sealReadyPulseDuration * 0.4f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 회전 → 원심력 날리기 → 공략 타임 → 귀환
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted || _data == null) yield break;

            // ① Pulse 정지 + 흰색 순간 (공격 시작)
            _bodyColorTween?.Kill();
            _armLColorTween?.Kill();
            _armRColorTween?.Kill();

            if (_bodyRenderer != null) _bodyRenderer.color = _data.colorActive;
            if (_armLRenderer != null) _armLRenderer.color = Color.white;
            if (_armRRenderer != null) _armRRenderer.color = Color.white;

            // ──────────────────────────────────────────
            // ② Boss_Warden 본체 회전
            //    _bossTransform (캐싱) 으로 DORotate → 팔이 자식이므로 함께 회전
            // ──────────────────────────────────────────
            float rotateSpeed = _isPhase2 ? _data.phase2SweepRotateSpeed : _data.sweepRotateSpeed;
            int rotations = _isPhase2 ? 2 : 1;
            float totalAngle = 360f * rotations;
            float rotateDuration = totalAngle / rotateSpeed;

            _rotateTween?.Kill();
            if (_bossTransform != null)
            {
                _rotateTween = _bossTransform
                    .DORotate(
                        new Vector3(0f, 0f, totalAngle),
                        rotateDuration,
                        RotateMode.FastBeyond360)
                    .SetRelative(true)
                    .SetEase(Ease.Linear)
                    .SetUpdate(false);
            }

            // ③ 회전 중 매 프레임 히트박스 + 디스크 위치 갱신
            float elapsed = 0f;
            float hitRadius = _isPhase2 ? _data.sweepHitRadius + 0.5f : _data.sweepHitRadius;

            while (elapsed < rotateDuration)
            {
                if (_isInterrupted)
                {
                    _rotateTween?.Kill();
                    yield break;
                }

                Vector2 bossPos = GetBossWorldPos();
                _attackRange?.UpdateSweepDiscPosition(bossPos);

                Collider2D hit = Physics2D.OverlapCircle(bossPos, hitRadius, _playerLayer);
                if (hit != null)
                    Debug.Log("[BossPattern_Sweep] 스윕 피격!");

                elapsed += Time.deltaTime;
                yield return null;
            }

            _attackRange?.HideSweepDisc();

            if (_isInterrupted) yield break;

            // ──────────────────────────────────────────
            // ④ 회전 완료 — 팔의 현재 월드 방향 계산 (원심력 방향)
            // ──────────────────────────────────────────
            //    회전 완료 시점 bossTransform.up 이 보스의 현재 "위" 방향
            //    왼팔: bossTransform.up 기준 왼쪽 수직
            //    오른팔: bossTransform.up 기준 오른쪽 수직

            Vector2 bossUp = _bossTransform != null
                ? (Vector2)_bossTransform.up
                : Vector2.up;

            // 원심력 방향 = 팔이 벌려진 방향 (bossUp 기준 수직)
            Vector2 flyDirL = new Vector2(-bossUp.y, bossUp.x); // 왼팔 날아가는 방향
            Vector2 flyDirR = new Vector2(bossUp.y, -bossUp.x); // 오른팔 날아가는 방향

            float actualFlyDist = _isPhase2 ? _flyDistance * 1.5f : _flyDistance;

            // ──────────────────────────────────────────
            // ⑤ 양팔 분리 + 원심력 날리기
            // ──────────────────────────────────────────
            Vector3 armLCurrentWorldPos = _armLTransform != null
                ? _armLTransform.position : Vector3.zero;
            Vector3 armRCurrentWorldPos = _armRTransform != null
                ? _armRTransform.position : Vector3.zero;

            if (_armLTransform != null)
            {
                _armLTransform.SetParent(null, worldPositionStays: true);
            }
            if (_armRTransform != null)
            {
                _armRTransform.SetParent(null, worldPositionStays: true);
            }
            _isArmsDetached = true;

            // 날아가는 목표 위치
            Vector3 armLFlyTarget = armLCurrentWorldPos + new Vector3(flyDirL.x, flyDirL.y, 0f) * actualFlyDist;
            Vector3 armRFlyTarget = armRCurrentWorldPos + new Vector3(flyDirR.x, flyDirR.y, 0f) * actualFlyDist;

            // ✅ v3.2 수정: + 90f 오프셋 (Vector.Down 이 날아가는 방향)
            // Slam / Charge 와 동일 원칙 — 손바닥 이미지가 아래를 향하는 구조
            float armLAngle = Mathf.Atan2(flyDirL.y, flyDirL.x) * Mathf.Rad2Deg + 90f;
            float armRAngle = Mathf.Atan2(flyDirR.y, flyDirR.x) * Mathf.Rad2Deg + 90f;

            if (_armLTransform != null)
            {
                _armLTransform.DOMove(armLFlyTarget, _flyDuration).SetEase(Ease.OutCubic);
                _armLTransform.DORotate(new Vector3(0f, 0f, armLAngle), _flyDuration * 0.5f)
                    .SetEase(Ease.OutQuart);
            }
            if (_armRTransform != null)
            {
                _armRTransform.DOMove(armRFlyTarget, _flyDuration).SetEase(Ease.OutCubic);
                _armRTransform.DORotate(new Vector3(0f, 0f, armRAngle), _flyDuration * 0.5f)
                    .SetEase(Ease.OutQuart);
            }

            yield return new WaitForSecondsRealtime(_flyDuration);
            if (_isInterrupted) { ReattachArms(); yield break; }

            // ──────────────────────────────────────────
            // ⑥ 공략 타임 — 양팔 모두 공격 가능
            // ──────────────────────────────────────────
            if (_armLRenderer != null && _data != null)
            {
                _armLColorTween?.Kill();
                _armLColorTween = _armLRenderer
                    .DOColor(_data.ColorData.colorFull, _data.ColorData.sealReadyPulseDuration * 0.3f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
            if (_armRRenderer != null && _data != null)
            {
                _armRColorTween?.Kill();
                _armRColorTween = _armRRenderer
                    .DOColor(_data.ColorData.colorFull, _data.ColorData.sealReadyPulseDuration * 0.3f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }

            // 날아간 팔 진동 연출
            _armLTransform?.DOPunchPosition(
                new Vector3(0.05f, 0.05f, 0f), _flyVulnDuration, 15, 0.5f)
                .SetUpdate(true);
            _armRTransform?.DOPunchPosition(
                new Vector3(0.05f, 0.05f, 0f), _flyVulnDuration, 15, 0.5f)
                .SetUpdate(true);

            // BossWardenArmPart 공략 배율 활성
            var armLPart = _armLTransform?.GetComponent<BossWardenArmPart>();
            var armRPart = _armRTransform?.GetComponent<BossWardenArmPart>();
            armLPart?.SetSlamVuln(true, 1.5f);
            armRPart?.SetSlamVuln(true, 1.5f);

            yield return new WaitForSecondsRealtime(_flyVulnDuration);

            armLPart?.SetSlamVuln(false, 1f);
            armRPart?.SetSlamVuln(false, 1f);
            _armLColorTween?.Kill();
            _armRColorTween?.Kill();

            if (_isInterrupted) { ReattachArms(); yield break; }

            // ──────────────────────────────────────────
            // ⑦ 양팔 귀환
            // ──────────────────────────────────────────
            Vector3 bossCurrentPos = _bossTransform != null ? _bossTransform.position : Vector3.zero;

            Vector3 returnL = bossCurrentPos + _armLOriginLocalPos;
            Vector3 returnR = bossCurrentPos + _armROriginLocalPos;

            if (_armLTransform != null)
                _armLTransform.DOMove(returnL, _returnDuration).SetEase(Ease.InBack);
            if (_armRTransform != null)
                _armRTransform.DOMove(returnR, _returnDuration).SetEase(Ease.InBack);

            yield return new WaitForSecondsRealtime(_returnDuration);

            ReattachArms();
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 본체 Z각도 복구 + 색상 복귀
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 보정 (재부착 후 미세 오차)
            if (_armLTransform != null)
                _armLTransform.DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.3f).SetEase(Ease.OutBack);
            if (_armRTransform != null)
                _armRTransform.DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.3f).SetEase(Ease.OutBack);

            // 본체 Z각도 복구 — 플레이어 방향으로 정렬
            // ✅ v3.1 수정: Z각도를 0으로 단순 초기화
            // 기존: FacingDir 기반 targetAngle 계산 → 좌표계 불일치로 오류 가능
            // 수정: Vector3.zero 로 회전 초기화 (가장 안전한 방식)
            //       보스가 플레이어를 향한 방향은 BossWardenAI 가 이후 정상 갱신함
            if (_bossTransform != null)
            {
                _bossTransform
                    .DORotate(Vector3.zero, _recoveryDuration * 0.5f)
                    .SetEase(Ease.OutCubic);
            }

            // 색상 복귀
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

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            _rotateTween?.Kill();
            _bodyColorTween?.Kill();
            _armLColorTween?.Kill();
            _armRColorTween?.Kill();

            _attackRange?.HideSweepDisc();

            ReattachArms();

            base.Interrupt();
        }

        // ══════════════════════════════════════════════════════
        // 양팔 재부착 공용 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 분리된 양팔을 보스에게 즉시 재부착한다.
        ///
        /// [주의]
        ///   _isArmsDetached = false 이면 이미 재부착 완료 → 스킵.
        ///   Interrupt / OnDestroy / 귀환 완료 후 호출.
        /// </summary>
        private void ReattachArms()
        {
            if (!_isArmsDetached) return;
            if (_bossTransform == null) return;

            if (_armLTransform != null)
            {
                _armLTransform.DOKill();
                _armLTransform.SetParent(_bossTransform, worldPositionStays: true);
                _armLTransform.localPosition = _armLOriginLocalPos;
                _armLTransform.localRotation = Quaternion.identity;
                _armLTransform.localScale = _armLOriginLocalScale; // ✅ Scale 복구
                if (_armLRenderer != null) _armLRenderer.color = _armLOriginColor;

                var armLPart = _armLTransform.GetComponent<BossWardenArmPart>();
                armLPart?.SetSlamVuln(false, 1f);
            }

            if (_armRTransform != null)
            {
                _armRTransform.DOKill();
                _armRTransform.SetParent(_bossTransform, worldPositionStays: true);
                _armRTransform.localPosition = _armROriginLocalPos;
                _armRTransform.localRotation = Quaternion.identity;
                _armRTransform.localScale = _armROriginLocalScale; // ✅ Scale 복구
                if (_armRRenderer != null) _armRRenderer.color = _armROriginColor;

                var armRPart = _armRTransform.GetComponent<BossWardenArmPart>();
                armRPart?.SetSlamVuln(false, 1f);
            }

            _isArmsDetached = false;
            Debug.Log("[BossPattern_Sweep] 양팔 재부착 완료");
        }

        // ══════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 보스 월드 위치 반환. 캐싱된 _rigid2D 사용 (매 프레임 GetComponent 방지).
        /// </summary>
        private Vector2 GetBossWorldPos()
        {
            return _rigid2D != null
                ? _rigid2D.position
                : (_bossTransform != null ? (Vector2)_bossTransform.position : Vector2.zero);
        }
    }
}