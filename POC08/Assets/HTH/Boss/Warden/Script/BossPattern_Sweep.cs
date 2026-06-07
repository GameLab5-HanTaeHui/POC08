// ============================================================
// BossPattern_Sweep.cs
// Boss_Warden 회전 스윕 패턴
//
// [수정 내용]
//   팔(양팔) + 본체 SpriteRenderer 색상 코드 전부 제거
//   Warning AttackRange 점멸 추가
//   실제 SerializeField 필드 사용:
//   _armSpreadAmount, _flyDistance, _flyDuration,
//   _flyVulnDuration, _returnDuration
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
        /// <summary>왼팔 Transform. 색상 제어 없음.</summary>
        [Tooltip("왼팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;
        /// <summary>오른팔 Transform. 색상 제어 없음.</summary>
        [Tooltip("오른팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armRTransform;

        [Header("── 레이어 ──────────────────────")]
        [Tooltip("플레이어 히트박스 레이어.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 ──────────────────────")]

        /// <summary>Warning 시 팔이 좌우로 벌어지는 거리.</summary>
        [Tooltip("팔 벌리기 거리. 권장: 0.6")]
        [Min(0f)]
        [SerializeField] private float _armSpreadAmount = 0.6f;

        /// <summary>회전 완료 후 팔이 날아가는 거리. 2페이즈에서는 × 1.5.</summary>
        [Tooltip("원심력 날아가는 거리. 권장: 1.5")]
        [Min(0.5f)]
        [SerializeField] private float _flyDistance = 1.5f;

        /// <summary>팔이 날아가는 데 걸리는 시간 (초).</summary>
        [Tooltip("팔 날아가는 시간 (초). 권장: 0.2")]
        [Min(0.05f)]
        [SerializeField] private float _flyDuration = 0.2f;

        /// <summary>팔이 날아간 후 공략 타임 지속 시간 (초).</summary>
        [Tooltip("팔 공략 타임 지속 시간 (초). 권장: 1.5")]
        [Min(0.5f)]
        [SerializeField] private float _flyVulnDuration = 1.5f;

        /// <summary>팔 귀환 시간 (초).</summary>
        [Tooltip("팔 귀환 시간 (초). 권장: 0.3")]
        [Min(0.05f)]
        [SerializeField] private float _returnDuration = 0.3f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private Vector3 _armLOriginLocalPos;
        private Vector3 _armROriginLocalPos;
        private Transform _bossTransform;
        private bool _isPhase2;
        private bool _isArmsDetached;
        private Tween _rotateTween;

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

            Vector2 bossPos = GetBossWorldPos();

            // 디스크 표시 + 점멸 시작
            _attackRange?.ShowSweepDisc(bossPos, _data.sweepHitRadius);
            _attackRange?.StartSweepPulse();

            // 팔 벌리기 (위치만 — 색상 없음)
            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos + new Vector3(-_armSpreadAmount, 0f, 0f),
                                 _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);

            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + new Vector3(_armSpreadAmount, 0f, 0f),
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

            float rotateSpeed = _isPhase2 ? _data.phase2SweepRotateSpeed : _data.sweepRotateSpeed;
            float rotateDuration = 360f / Mathf.Max(rotateSpeed, 1f);
            int rotCount = _isPhase2 ? 2 : 1;

            for (int r = 0; r < rotCount; r++)
            {
                if (_isInterrupted) yield break;

                _rotateTween?.Kill();
                _rotateTween = _bossTransform
                    .DORotate(new Vector3(0f, 0f, 360f), rotateDuration, RotateMode.FastBeyond360)
                    .SetEase(Ease.Linear);

                float elapsed = 0f;
                while (elapsed < rotateDuration)
                {
                    if (_isInterrupted) yield break;

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

            yield return new WaitForSecondsRealtime(_flyVulnDuration);
            if (_isInterrupted) { ReattachArms(); yield break; }

            // 팔 귀환
            Vector3 bossCurrentPos = _bossTransform.position;
            if (_armLTransform != null)
                _armLTransform.DOMove(bossCurrentPos + _armLOriginLocalPos, _returnDuration).SetEase(Ease.InBack);
            if (_armRTransform != null)
                _armRTransform.DOMove(bossCurrentPos + _armROriginLocalPos, _returnDuration).SetEase(Ease.InBack);

            yield return new WaitForSecondsRealtime(_returnDuration);

            ReattachArms();
        }

        private void SeparateAndFlyArms()
        {
            if (_bossTransform == null) return;

            float dist = _isPhase2 ? _flyDistance * 1.5f : _flyDistance;

            if (_armLTransform != null)
            {
                _armLTransform.SetParent(null, worldPositionStays: true);
                Vector2 flyDirL = ((Vector2)_armLTransform.position - (Vector2)_bossTransform.position).normalized;
                _armLTransform.DOMove((Vector2)_armLTransform.position + flyDirL * dist, _flyDuration).SetEase(Ease.OutExpo);
            }

            if (_armRTransform != null)
            {
                _armRTransform.SetParent(null, worldPositionStays: true);
                Vector2 flyDirR = ((Vector2)_armRTransform.position - (Vector2)_bossTransform.position).normalized;
                _armRTransform.DOMove((Vector2)_armRTransform.position + flyDirR * dist, _flyDuration).SetEase(Ease.OutExpo);
            }

            _isArmsDetached = true;
        }

        // ══════════════════════════════════════════════════════
        // Recovery — Z각도 복구 + 팔 보정 (색상 없음)
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            _armLTransform?.DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.3f).SetEase(Ease.OutBack);
            _armRTransform?.DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.3f).SetEase(Ease.OutBack);
            _bossTransform?.DORotate(Vector3.zero, _recoveryDuration * 0.5f).SetEase(Ease.OutCubic);

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