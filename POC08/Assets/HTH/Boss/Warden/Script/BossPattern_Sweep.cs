// ============================================================
// BossPattern_Sweep.cs
// Boss_Warden 회전 스윕 패턴
//
// [수정 내용]
//   팔(양팔) SpriteRenderer 색상 코드 전부 제거
//   → _armLRenderer / _armRRenderer / _bodyRenderer 색상 제거
//   → _armLColorTween / _armRColorTween / _bodyColorTween 제거
//   → _armLOriginColor / _armROriginColor / _bodyOriginColor 제거
//   → Warning DOColor 본체 Pulse 제거
//   → Recovery 양팔 색상 복귀 제거
//   → Interrupt 양팔 색상 즉시 복귀 제거
//   → ReattachArms() 색상 스냅 제거
//
//   Warning AttackRange 점멸 추가
//   → ShowSweepDisc() 후 StartSweepPulse() 호출
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
    /// <summary>Boss_Warden 회전 스윕 패턴.</summary>
    public class BossPattern_Sweep : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private Rigidbody2D _rigid2D;
        [SerializeField] private BossWardenAI _ai;

        [Header("── DataSO ──────────────────────")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 Transform ──────────────────────")]
        /// <summary>왼팔 Transform. 회전 + 분리 연출. 색상 제어 없음.</summary>
        [Tooltip("왼팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;
        /// <summary>오른팔 Transform. 색상 제어 없음.</summary>
        [Tooltip("오른팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armRTransform;

        [Header("── 레이어 ──────────────────────")]
        [Tooltip("플레이어 히트박스 레이어.")]
        [SerializeField] private LayerMask _playerLayer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private Transform _bossTransform;
        private Vector3 _armLOriginLocalPos;
        private Vector3 _armROriginLocalPos;
        private bool _isPhase2;
        private bool _isArmsDetached;

        private Tween _rotateTween;

        private float _returnDuration;
        private float _sweepVulnDuration;
        private float _sweepFlyDist;

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
        }

        private void ApplyDataValues()
        {
            if (_data == null) return;
            _returnDuration = _data.sweepReturnDuration;
            _sweepVulnDuration = _data.sweepVulnDuration;
            _sweepFlyDist = _isPhase2
                ? _data.sweepFlyDistance * 1.5f
                : _data.sweepFlyDistance;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        private void OnDestroy()
        {
            _rotateTween?.Kill();
            ReattachArms();
        }

        // ══════════════════════════════════════════════════════
        // Warning — 디스크 점멸 + 팔 벌리기
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            ApplyDataValues();

            Vector2 bossPos = GetBossWorldPos();

            // 디스크 표시 + 점멸 시작
            _attackRange?.ShowSweepDisc(bossPos, _data.sweepHitRadius);
            _attackRange?.StartSweepPulse();

            // 팔 벌리기 (위치만)
            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos + new Vector3(-_data.sweepSpreadAmount, 0f, 0f),
                                 _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);

            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + new Vector3(_data.sweepSpreadAmount, 0f, 0f),
                                 _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 회전 + 히트박스 + 팔 분리 + 공략 타임
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_data == null || _bossTransform == null) yield break;

            // 점멸 중단
            _attackRange?.StopAllPulse();
            _attackRange?.HideSweepDisc();

            int rotCount = _isPhase2 ? 2 : 1;

            for (int r = 0; r < rotCount; r++)
            {
                if (_isInterrupted) yield break;

                // 360도 회전
                _rotateTween?.Kill();
                _rotateTween = _bossTransform
                    .DORotate(new Vector3(0f, 0f, 360f), _data.sweepRotateDuration,
                              RotateMode.FastBeyond360)
                    .SetEase(Ease.Linear);

                float elapsed = 0f;
                while (elapsed < _data.sweepRotateDuration)
                {
                    if (_isInterrupted) yield break;

                    // 히트박스 판정
                    Vector2 bossPos = GetBossWorldPos();
                    _attackRange?.UpdateSweepDiscPosition(bossPos);

                    Collider2D hit = Physics2D.OverlapCircle(bossPos, _data.sweepHitRadius, _playerLayer);
                    if (hit != null)
                        Debug.Log("[BossPattern_Sweep] 스윕 피격!");

                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            if (_isInterrupted) yield break;

            // 팔 분리 + 날아가기
            SeparateAndFlyArms();

            yield return new WaitForSecondsRealtime(_sweepVulnDuration);
            if (_isInterrupted) { ReattachArms(); yield break; }

            // 팔 귀환
            Vector3 bossCurrentPos = _bossTransform.position;
            Vector3 returnL = bossCurrentPos + _armLOriginLocalPos;
            Vector3 returnR = bossCurrentPos + _armROriginLocalPos;

            if (_armLTransform != null)
                _armLTransform.DOMove(returnL, _returnDuration).SetEase(Ease.InBack);
            if (_armRTransform != null)
                _armRTransform.DOMove(returnR, _returnDuration).SetEase(Ease.InBack);

            yield return new WaitForSecondsRealtime(_returnDuration);

            ReattachArms();
        }

        private void SeparateAndFlyArms()
        {
            if (_bossTransform == null) return;

            if (_armLTransform != null)
            {
                _armLTransform.SetParent(null, worldPositionStays: true);
                Vector2 flyDirL = ((Vector2)_armLTransform.position
                    - (Vector2)_bossTransform.position).normalized;
                _armLTransform
                    .DOMove((Vector2)_armLTransform.position + flyDirL * _sweepFlyDist,
                            _sweepVulnDuration * 0.5f)
                    .SetEase(Ease.OutExpo);
            }

            if (_armRTransform != null)
            {
                _armRTransform.SetParent(null, worldPositionStays: true);
                Vector2 flyDirR = ((Vector2)_armRTransform.position
                    - (Vector2)_bossTransform.position).normalized;
                _armRTransform
                    .DOMove((Vector2)_armRTransform.position + flyDirR * _sweepFlyDist,
                            _sweepVulnDuration * 0.5f)
                    .SetEase(Ease.OutExpo);
            }

            _isArmsDetached = true;
        }

        // ══════════════════════════════════════════════════════
        // Recovery — Z각도 복구 + 팔 원위치 보정 (색상 없음)
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 보정 (위치만)
            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);
            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            // 본체 Z각도 복구
            if (_bossTransform != null)
                _bossTransform
                    .DORotate(Vector3.zero, _recoveryDuration * 0.5f)
                    .SetEase(Ease.OutCubic);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt
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
        // 양팔 재부착 (색상 없음)
        // ══════════════════════════════════════════════════════

        private void ReattachArms()
        {
            if (!_isArmsDetached) return;
            if (_bossTransform == null) return;

            if (_armLTransform != null)
            {
                _armLTransform.DOKill();
                _armLTransform.SetParent(_bossTransform, worldPositionStays: true);
                _armLTransform.localPosition = _armLOriginLocalPos;

                _armLTransform.GetComponent<BossWardenArmPart>()?.SetSlamVuln(false, 1f);
            }

            if (_armRTransform != null)
            {
                _armRTransform.DOKill();
                _armRTransform.SetParent(_bossTransform, worldPositionStays: true);
                _armRTransform.localPosition = _armROriginLocalPos;

                _armRTransform.GetComponent<BossWardenArmPart>()?.SetSlamVuln(false, 1f);
            }

            _isArmsDetached = false;
            Debug.Log("[BossPattern_Sweep] 양팔 재부착 완료");
        }

        private Vector2 GetBossWorldPos()
        {
            return _rigid2D != null
                ? _rigid2D.position
                : (_bossTransform != null ? (Vector2)_bossTransform.position : Vector2.zero);
        }
    }
}