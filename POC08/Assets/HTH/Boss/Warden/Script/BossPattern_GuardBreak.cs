// ============================================================
// BossPattern_GuardBreak.cs
// Boss_Warden 가드→강타 패턴
//
// [수정 내용]
//   팔(양팔) + 본체 SpriteRenderer 색상 코드 전부 제거
//   Warning AttackRange 점멸 추가
//   실제 SerializeField 필드 사용:
//   _guardForwardAmount, _windupPullAmount,
//   _thrustDistance, _thrustDuration
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    /// <summary>Boss_Warden 가드→강타 패턴.</summary>
    public class BossPattern_GuardBreak : BossPatternBase
    {
        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── 컴포넌트 연결 ──────────────────────")]
        [SerializeField] private BossWardenAttackRange _attackRange;
        [SerializeField] private BossWardenAI _ai;

        [Header("── DataSO ──────────────────────")]
        [SerializeField] private BossWardenDataSO _data;

        [Header("── 팔 Transform ──────────────────────")]
        /// <summary>오른팔 Transform. 색상 제어 없음.</summary>
        [Tooltip("오른팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armRTransform;
        /// <summary>왼팔 Transform. 색상 제어 없음.</summary>
        [Tooltip("왼팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;

        [Header("── 레이어 ──────────────────────")]
        [Tooltip("플레이어 히트박스 레이어.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 ──────────────────────")]

        /// <summary>가드 자세 시 팔이 앞으로 이동하는 거리.</summary>
        [Tooltip("가드 자세 앞으로 나오는 거리. 권장: 0.4")]
        [Min(0f)]
        [SerializeField] private float _guardForwardAmount = 0.4f;

        /// <summary>백스윙 뒤로 당기는 거리.</summary>
        [Tooltip("백스윙 당기는 거리. 권장: 0.5")]
        [Min(0f)]
        [SerializeField] private float _windupPullAmount = 0.5f;

        /// <summary>찌르기 뻗는 거리.</summary>
        [Tooltip("찌르기 뻗는 거리. 권장: 0.8")]
        [Min(0f)]
        [SerializeField] private float _thrustDistance = 0.8f;

        /// <summary>찌르기 이동 시간 (초).</summary>
        [Tooltip("찌르기 이동 시간 (초). 권장: 0.08")]
        [Min(0.03f)]
        [SerializeField] private float _thrustDuration = 0.08f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private Vector3 _armROriginLocalPos;
        private Vector3 _armLOriginLocalPos;
        private bool _isPhase2;
        private Vector2 _facingDir;
        private Vector3 _thrustLocalOff;
        private Rigidbody2D _rigid2D;

        /// <summary>가드 자세 중 여부. BossWardenArmPart 가 정면 봉인도 차단 참조.</summary>
        public bool IsGuarding { get; private set; }

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();
            _rigid2D = GetComponentInParent<Rigidbody2D>();

            if (_armRTransform != null) _armROriginLocalPos = _armRTransform.localPosition;
            if (_armLTransform != null) _armLOriginLocalPos = _armLTransform.localPosition;

            _triggerGroggyOnRecovery = true;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 가드 자세 → 백스윙 → 디스크 점멸
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            float guardDuration = _isPhase2 ? 0.5f : _data.guardBreakGuardDuration;
            float remainingWarning = _warningDuration - guardDuration;

            // ── 전반부: 가드 자세 ──
            IsGuarding = true;
            _facingDir = _ai != null ? _ai.FacingDir : Vector2.right;

            // 양팔 앞으로 가드 자세 (위치만)
            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + new Vector3(_facingDir.x, _facingDir.y, 0f) * _guardForwardAmount,
                                 guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            if (_armLTransform != null)
                _armLTransform
                    .DOLocalMove(_armLOriginLocalPos + new Vector3(_facingDir.x, _facingDir.y, 0f) * (_guardForwardAmount * 0.6f),
                                 guardDuration * 0.4f)
                    .SetEase(Ease.OutBack);

            yield return StartCoroutine(WaitForPattern(guardDuration));
            if (_isInterrupted) yield break;

            IsGuarding = false;

            // ── 후반부: 백스윙 + 디스크 점멸 ──
            Vector3 backDir = new Vector3(-_facingDir.x, -_facingDir.y, 0f);
            _thrustLocalOff = new Vector3(_facingDir.x, _facingDir.y, 0f) * _thrustDistance;

            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + backDir * _windupPullAmount,
                                 remainingWarning * 0.4f)
                    .SetEase(Ease.OutBack);

            // 디스크 표시 + 점멸
            Vector2 bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            Vector2 hitCenter = bossPos + _facingDir * (_data.guardBreakHitboxSize.y * 0.5f);
            float angle = Mathf.Atan2(_facingDir.y, _facingDir.x) * Mathf.Rad2Deg;

            _attackRange?.ShowGuardBreakDisc(hitCenter, _data.guardBreakHitboxSize, angle);
            _attackRange?.StartGuardBreakPulse();

            yield return StartCoroutine(WaitForPattern(remainingWarning));
        }

        // ══════════════════════════════════════════════════════
        // Active — 찌르기 (색상 없음)
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_data == null) yield break;

            // 점멸 중단
            _attackRange?.StopAllPulse();
            _attackRange?.HideGuardBreakDisc();

            // 팔 빠른 찌르기 (위치만)
            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + _thrustLocalOff, _thrustDuration)
                    .SetEase(Ease.OutExpo);

            yield return new WaitForSecondsRealtime(_thrustDuration);
            if (_isInterrupted) yield break;

            // 히트박스 판정
            CheckGuardBreakHit();

            yield return new WaitForSecondsRealtime(0.05f);
            if (_isInterrupted) yield break;

            // 팔 살짝 복귀
            if (_armRTransform != null)
                _armRTransform
                    .DOLocalMove(_armROriginLocalPos + _thrustLocalOff * 0.6f, 0.1f)
                    .SetEase(Ease.OutQuart);
        }

        private void CheckGuardBreakHit()
        {
            if (_data == null) return;

            Vector2 bossPos = _rigid2D != null ? _rigid2D.position : (Vector2)transform.position;
            float angle = Mathf.Atan2(_facingDir.y, _facingDir.x) * Mathf.Rad2Deg;
            Vector2 boxSize = _isPhase2 ? new Vector2(1.2f, 0.8f) : _data.guardBreakHitboxSize;
            Vector2 boxCenter = bossPos + _facingDir * (boxSize.y * 0.5f);

            Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, angle, _playerLayer);
            if (hit != null)
                Debug.Log("[BossPattern_GuardBreak] 찌르기 피격!");
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 복귀 (색상 없음)
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            _armRTransform?.DOLocalMove(_armROriginLocalPos, _recoveryDuration * 0.4f).SetEase(Ease.OutBack);
            _armLTransform?.DOLocalMove(_armLOriginLocalPos, _recoveryDuration * 0.4f).SetEase(Ease.OutBack);

            transform.parent?.DOShakePosition(0.3f, 0.2f, 10, 90f).SetUpdate(true);

            IsGuarding = false;

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            IsGuarding = false;
            _attackRange?.StopAllPulse();
            _attackRange?.HideGuardBreakDisc();

            _armRTransform?.DOLocalMove(_armROriginLocalPos, 0.1f).SetEase(Ease.OutQuart);
            _armLTransform?.DOLocalMove(_armLOriginLocalPos, 0.1f).SetEase(Ease.OutQuart);

            base.Interrupt();
        }
    }
}