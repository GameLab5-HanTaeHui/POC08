// ============================================================
// BossPattern_Sweep.cs  v3.3
// Boss_Warden 회전 스윕 패턴 — 원심력 팔 날리기 + 수거
//
// [v3.3 — 색상 코드 제거 (BossWardenFeedback 위임)]
//   제거: _armLRenderer / _armRRenderer / _bodyRenderer 색상
//   제거: _armLColorTween / _armRColorTween / _bodyColorTween
//   제거: _armLOriginColor / _armROriginColor / _bodyOriginColor
//   제거: Warning DOColor 본체 Pulse
//   제거: Recovery 양팔 색상 복귀
//   제거: Interrupt 양팔 색상 즉시 복귀
//   제거: ReattachArms() 색상 스냅
//   → 팔/본체 색상은 SealableComponent + BossWardenFeedback 이 전담
//
//   Warning AttackRange 점멸 추가 (v1.2 AttackRange 연동)
//   → ShowSweepDisc() 후 StartSweepPulse() 호출
//   Active 진입 시 점멸 중단
//   → StopAllPulse() 호출
//
// [v3.2 유지 — 원복]
//   팔 벌리기 InverseTransformDirection 로컬 좌표 변환
//   팔 Z축 회전 DOLocalRotate (+90f 오프셋)
//   원심력 날리기: bossTransform.up 기준 perpendicular 방향 계산
//   팔 날아갈 때 Atan2 + 90f 회전 오프셋
//   공략 타임 SetSlamVuln 배율 (양팔)
//   DOPunchPosition 진동 연출 (양팔)
//   Scale 캐싱/복구 (_armLOriginLocalScale / _armROriginLocalScale)
//   ReattachArms(): localRotation / localScale 복구
//   2페이즈: 2회전 + flyDistance × 1.5
//
// [연결 부위] 왼팔 + 오른팔
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>
    /// Boss_Warden 회전 스윕 패턴 — 원심력 팔 날리기. (v3.3)
    ///
    /// ────────────────────────────────────────────────────
    /// [연출 흐름]
    ///   Warning : 양팔 수직 방향 벌리기 → 예고 디스크 점멸
    ///   Active  : StopAllPulse → 본체 360° 회전 → 팔 함께 스윕 → 원심력 날리기
    ///             공략 타임 → 귀환 재부착
    ///   Recovery: Z각도 복구 → 팔 원위치
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

        [Header("── 팔 Transform ──────────────────────")]

        /// <summary>왼팔 Transform. 색상 제어 없음 (SealableComponent 전담).</summary>
        [Tooltip("왼팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;

        /// <summary>오른팔 Transform. 색상 제어 없음.</summary>
        [Tooltip("오른팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armRTransform;

        [Header("── 레이어 ──────────────────────")]

        [Tooltip("플레이어 HurtBox 레이어. PlayerAttackHitBox 레이어 선택.")]
        [SerializeField] private LayerMask _playerLayer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        /// <summary>왼팔 원래 로컬 위치.</summary>
        private Vector3 _armLOriginLocalPos;

        /// <summary>오른팔 원래 로컬 위치.</summary>
        private Vector3 _armROriginLocalPos;

        /// <summary>왼팔 원래 로컬 스케일. SetParent 재부착 시 복구용.</summary>
        private Vector3 _armLOriginLocalScale;

        /// <summary>오른팔 원래 로컬 스케일.</summary>
        private Vector3 _armROriginLocalScale;

        /// <summary>
        /// 보스 본체 Transform. 회전 대상 + 팔 재부착 대상.
        /// Awake 에서 캐싱.
        /// </summary>
        private Transform _bossTransform;

        /// <summary>보스 Rigidbody2D. 월드 위치 참조.</summary>
        private Rigidbody2D _rigid2D;

        /// <summary>2페이즈 여부.</summary>
        private bool _isPhase2;

        /// <summary>팔 분리 상태 추적.</summary>
        private bool _isArmsDetached;

        private Tweener _rotateTween;

        private BossWardenPatternDataSO.SweepSettings SweepData
            => _data != null && _data.PatternData != null
                ? _data.PatternData.Sweep
                : BossWardenPatternDataSO.DefaultSweep;

        private BossWardenPatternDataSO.CommonSettings CommonData
            => _data != null && _data.PatternData != null
                ? _data.PatternData.Common
                : BossWardenPatternDataSO.DefaultCommon;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            _rigid2D = GetComponentInParent<Rigidbody2D>();
            _bossTransform = _rigid2D != null ? _rigid2D.transform : transform.parent;

            if (_armLTransform != null)
            {
                _armLOriginLocalPos = _armLTransform.localPosition;
                _armLOriginLocalScale = _armLTransform.localScale;
            }
            if (_armRTransform != null)
            {
                _armROriginLocalPos = _armRTransform.localPosition;
                _armROriginLocalScale = _armRTransform.localScale;
            }

            _triggerGroggyOnRecovery = false;
            ApplyPatternData();
        }

        public override void Initialize(BossWardenDataSO data)
        {
            _data = data;
            ApplyPatternData();
        }

        private void ApplyPatternData()
        {
            ConfigureLifecycle(SweepData.lifecycle, CommonData);
            _triggerGroggyOnRecovery = false;
        }

        private void OnDestroy()
        {
            _rotateTween?.Kill();
            ReattachArms();
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — FacingDir 기준 양팔 수직 벌리기 + 디스크 점멸
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            var pattern = SweepData;
            float radius = pattern.GetHitRadius(_isPhase2);
            Vector2 bossPos = GetBossWorldPos();

            // ① 예고 디스크 표시 + 점멸 시작 (v3.3)
            _attackRange?.ShowSweepDisc(bossPos, radius);
            _attackRange?.StartSweepPulse();

            // ② FacingDir 기준 좌우 수직 방향 계산
            Vector2 forward = _ai != null ? _ai.FacingDir : Vector2.right;
            Vector2 perpL = new Vector2(-forward.y, forward.x);
            Vector2 perpR = new Vector2(forward.y, -forward.x);

            // v3.2: InverseTransformDirection — 월드 방향 → 로컬 변환 (flipX 무관)
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
                    .DOLocalMove(_armLOriginLocalPos + localPerpL * pattern.armSpreadAmount,
                                 _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);

                // v3.2: Vector.Down 이 팔 벌린 방향을 향함 + 90f
                float armLAngle = Mathf.Atan2(perpL.y, perpL.x) * Mathf.Rad2Deg + 90f;
                _armLTransform
                    .DOLocalRotate(new Vector3(0f, 0f, armLAngle), _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);
            }

            if (_armRTransform != null)
            {
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + localPerpR * pattern.armSpreadAmount,
                                 _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);

                float armRAngle = Mathf.Atan2(perpR.y, perpR.x) * Mathf.Rad2Deg + 90f;
                _armRTransform
                    .DOLocalRotate(new Vector3(0f, 0f, armRAngle), _warningDuration * 0.5f)
                    .SetEase(Ease.OutBack);
            }

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 회전 → 원심력 날리기 → 공략 타임 → 귀환
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_isInterrupted || _data == null) yield break;

            var pattern = SweepData;

            // 점멸 중단 (v3.3)
            _attackRange?.StopAllPulse();

            // ── 본체 360° 회전 (팔이 자식이므로 함께 회전) ──
            float rotateSpeed = pattern.GetRotateSpeed(_isPhase2);
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

            // 회전 중 매 프레임 히트박스 + 디스크 위치 갱신
            float elapsed = 0f;
            float hitRadius = pattern.GetHitRadius(_isPhase2);

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

            // ── 원심력 방향 계산 (회전 완료 시 bossTransform.up 기준) ──
            Vector2 bossUp = _bossTransform != null ? (Vector2)_bossTransform.up : Vector2.up;
            Vector2 flyDirL = new Vector2(-bossUp.y, bossUp.x);   // 왼팔
            Vector2 flyDirR = new Vector2(bossUp.y, -bossUp.x);   // 오른팔

            float actualFlyDist = pattern.GetFlyDistance(_isPhase2);

            // ── 양팔 분리 + 원심력 날리기 ──
            Vector3 armLCurrentWorldPos = _armLTransform != null
                ? _armLTransform.position : Vector3.zero;
            Vector3 armRCurrentWorldPos = _armRTransform != null
                ? _armRTransform.position : Vector3.zero;

            if (_armLTransform != null)
                _armLTransform.SetParent(null, worldPositionStays: true);
            if (_armRTransform != null)
                _armRTransform.SetParent(null, worldPositionStays: true);
            _isArmsDetached = true;

            Vector3 armLFlyTarget = armLCurrentWorldPos + new Vector3(flyDirL.x, flyDirL.y, 0f) * actualFlyDist;
            Vector3 armRFlyTarget = armRCurrentWorldPos + new Vector3(flyDirR.x, flyDirR.y, 0f) * actualFlyDist;

            // v3.2: + 90f 오프셋 (Vector.Down 이 날아가는 방향)
            float armLAngle = Mathf.Atan2(flyDirL.y, flyDirL.x) * Mathf.Rad2Deg + 90f;
            float armRAngle = Mathf.Atan2(flyDirR.y, flyDirR.x) * Mathf.Rad2Deg + 90f;

            if (_armLTransform != null)
            {
                _armLTransform.DOMove(armLFlyTarget, pattern.flyDuration).SetEase(Ease.OutCubic);
                _armLTransform
                    .DORotate(new Vector3(0f, 0f, armLAngle), pattern.flyDuration * 0.5f)
                    .SetEase(Ease.OutQuart);
            }
            if (_armRTransform != null)
            {
                _armRTransform.DOMove(armRFlyTarget, pattern.flyDuration).SetEase(Ease.OutCubic);
                _armRTransform
                    .DORotate(new Vector3(0f, 0f, armRAngle), pattern.flyDuration * 0.5f)
                    .SetEase(Ease.OutQuart);
            }

            yield return new WaitForSecondsRealtime(pattern.flyDuration);
            if (_isInterrupted) { ReattachArms(); yield break; }

            // ── 공략 타임 — 양팔 모두 공격 가능 ──
            _armLTransform?.DOPunchPosition(
                new Vector3(0.05f, 0.05f, 0f), pattern.vulnerableDuration, 15, 0.5f)
                .SetUpdate(true);
            _armRTransform?.DOPunchPosition(
                new Vector3(0.05f, 0.05f, 0f), pattern.vulnerableDuration, 15, 0.5f)
                .SetUpdate(true);

            var armLPart = _armLTransform?.GetComponent<BossWardenPart>();
            var armRPart = _armRTransform?.GetComponent<BossWardenPart>();
            armLPart?.SetSlamVuln(true, 1.5f);
            armRPart?.SetSlamVuln(true, 1.5f);

            yield return new WaitForSecondsRealtime(pattern.vulnerableDuration);
            if (_isInterrupted) yield break;

            armLPart?.SetSlamVuln(false, 1f);
            armRPart?.SetSlamVuln(false, 1f);

            // ── 양팔 귀환 ──
            Vector3 bossCurrentPos = _bossTransform != null
                ? _bossTransform.position : Vector3.zero;

            Vector3 returnL = bossCurrentPos + _armLOriginLocalPos;
            Vector3 returnR = bossCurrentPos + _armROriginLocalPos;

            _armLTransform?.DOMove(returnL, pattern.returnDuration).SetEase(Ease.InBack);
            _armRTransform?.DOMove(returnR, pattern.returnDuration).SetEase(Ease.InBack);

            yield return new WaitForSecondsRealtime(pattern.returnDuration);

            ReattachArms();
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 본체 Z각도 복구 + 팔 원위치 보정
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 보정 (재부착 후 미세 오차)
            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);
            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            // 본체 Z각도 복구 — Vector3.zero 초기화 (가장 안전한 방식)
            if (_bossTransform != null)
                _bossTransform
                    .DORotate(Vector3.zero, _recoveryDuration * 0.5f)
                    .SetEase(Ease.OutCubic);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt 오버라이드
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            _rotateTween?.Kill();
            _attackRange?.StopAllPulse();
            _attackRange?.HideSweepDisc();
            ReattachArms();
            base.Interrupt();
        }

        // ══════════════════════════════════════════════════════
        // 양팔 재부착 공용 처리
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 분리된 양팔을 보스에게 즉시 재부착한다.
        /// localPosition / localRotation / localScale 전부 복구.
        /// </summary>
        private void ReattachArms()
        {
            if (!_isArmsDetached) return;
            if (_bossTransform == null) { _isArmsDetached = false; return; }

            if (_armLTransform != null)
            {
                _armLTransform.DOKill();
                _armLTransform.SetParent(_bossTransform, worldPositionStays: true);
                _armLTransform.localPosition = _armLOriginLocalPos;
                _armLTransform.localRotation = Quaternion.identity;
                _armLTransform.localScale = _armLOriginLocalScale;

                _armLTransform.GetComponent<BossWardenPart>()?.SetSlamVuln(false, 1f);
            }

            if (_armRTransform != null)
            {
                _armRTransform.DOKill();
                _armRTransform.SetParent(_bossTransform, worldPositionStays: true);
                _armRTransform.localPosition = _armROriginLocalPos;
                _armRTransform.localRotation = Quaternion.identity;
                _armRTransform.localScale = _armROriginLocalScale;

                _armRTransform.GetComponent<BossWardenPart>()?.SetSlamVuln(false, 1f);
            }

            _isArmsDetached = false;
            Debug.Log("[BossPattern_Sweep] 양팔 재부착 완료");
        }

        // ══════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════

        private Vector2 GetBossWorldPos()
        {
            return _rigid2D != null
                ? _rigid2D.position
                : (Vector2)(_bossTransform != null ? _bossTransform.position : transform.position);
        }
    }
}