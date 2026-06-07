// ============================================================
// BossPattern_Slam.cs
// Boss_Warden 내려찍기 패턴
//
// [수정 내용]
//   팔(왼팔) SpriteRenderer 색상 코드 전부 제거
//   → _armLRenderer / _armColorTween / _armOriginColor 제거
//   → Warning DOColor Pulse 제거
//   → 공략 타임 DOColor Yoyo 제거
//   → Recovery 팔 색상 복귀 제거
//   → Interrupt 팔 색상 복귀 제거
//
//   Warning AttackRange 점멸 추가
//   → ShowSlamDisc() 후 StartSlamPulse() 호출
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
    /// <summary>Boss_Warden 내려찍기 패턴.</summary>
    public class BossPattern_Slam : BossPatternBase
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

        [Header("── 왼팔 Transform ──────────────────────")]
        /// <summary>왼팔 Transform. 분리/귀환 연출 주체. 색상 제어 없음.</summary>
        [Tooltip("왼팔 Transform. 분리/귀환 연출용. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;

        [Header("── 레이어 ──────────────────────")]
        [Tooltip("플레이어 히트박스 레이어.")]
        [SerializeField] private LayerMask _playerLayer;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private Transform _bossTransform;
        private Vector3 _armOriginLocalPos;
        private bool _isPhase2;
        private bool _isArmDetached;

        private float _slamMoveDuration;
        private float _returnDuration;
        private float _slamVulnDuration;
        private float _slamVulnMultiplier;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_attackRange == null) _attackRange = GetComponentInParent<BossWardenAttackRange>();
            if (_rigid2D == null) _rigid2D = GetComponentInParent<Rigidbody2D>();
            if (_ai == null) _ai = GetComponentInParent<BossWardenAI>();

            _bossTransform = _rigid2D != null ? _rigid2D.transform : transform.parent;

            if (_armLTransform != null)
                _armOriginLocalPos = _armLTransform.localPosition;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;

            _slamMoveDuration = _data != null ? _data.slamMoveDuration * 0.8f : 0.4f;
            _returnDuration = _data != null ? _data.slamReturnDuration * 0.8f : 0.5f;
            _slamVulnDuration = _data != null ? _data.slamVulnDuration * 0.6f : 0.6f;
            _slamVulnMultiplier = _data != null ? _data.slamVulnMultiplier * 1.5f : 3f;
        }

        private void ApplyDataValues()
        {
            if (_data == null) return;
            _slamMoveDuration = _data.slamMoveDuration;
            _returnDuration = _data.slamReturnDuration;
            _slamVulnDuration = _data.slamVulnDuration;
            _slamVulnMultiplier = _data.slamVulnMultiplier;
        }

        // ══════════════════════════════════════════════════════
        // Warning — 디스크 점멸 + 팔 백스윙
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            ApplyDataValues();

            Vector2 playerPos = _ai?.PlayerTransform != null
                ? (Vector2)_ai.PlayerTransform.position
                : Vector2.zero;

            int discIndex = 0;

            // 디스크 표시 + 점멸 시작
            _attackRange?.ShowSlamDisc(playerPos, _data.slamHitRadius, discIndex);
            _attackRange?.StartSlamPulse(discIndex);

            // 팔 백스윙 (위치 연출만 — 색상 없음)
            if (_armLTransform != null)
            {
                Vector3 backOffset = new Vector3(0f, _data.slamPullAmount, 0f);
                _armLTransform
                    .DOLocalMove(_armOriginLocalPos + backOffset, _warningDuration * 0.4f)
                    .SetEase(Ease.OutBack);
            }

            yield return StartCoroutine(WaitForPattern(_warningDuration));
        }

        // ══════════════════════════════════════════════════════
        // Active — 팔 분리 + 내려찍기 + 공략 타임 + 귀환
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnActive()
        {
            if (_data == null || _armLTransform == null) yield break;

            // 점멸 중단
            _attackRange?.StopAllPulse();

            yield return StartCoroutine(ExecuteThrow(0));

            if (_isPhase2 && !_isInterrupted)
            {
                yield return new WaitForSecondsRealtime(0.2f);
                if (!_isInterrupted)
                    yield return StartCoroutine(ExecuteThrow(1));
            }
        }

        private IEnumerator ExecuteThrow(int discIndex)
        {
            if (_armLTransform == null || _bossTransform == null) yield break;

            Vector2 targetWorldPos = _ai?.PlayerTransform != null
                ? (Vector2)_ai.PlayerTransform.position
                : (Vector2)_bossTransform.position;

            // 디스크 위치 갱신
            if (discIndex == 1)
            {
                _attackRange?.ShowSlamDisc(targetWorldPos, _data.slamHitRadius, 1);
                _attackRange?.StartSlamPulse(1);
                yield return new WaitForSecondsRealtime(0.15f);
                _attackRange?.StopAllPulse();
            }

            // 팔 분리
            _armLTransform.SetParent(null, worldPositionStays: true);
            _isArmDetached = true;

            // 팔 날아가기
            Vector2 flyDir = (targetWorldPos - (Vector2)_armLTransform.position).normalized;
            float targetAngle = Mathf.Atan2(flyDir.y, flyDir.x) * Mathf.Rad2Deg + 90f;

            _armLTransform
                .DOMove(new Vector3(targetWorldPos.x, targetWorldPos.y, _armLTransform.position.z),
                        _slamMoveDuration)
                .SetEase(Ease.OutExpo);

            _armLTransform
                .DORotate(new Vector3(0f, 0f, targetAngle), _slamMoveDuration * 0.5f)
                .SetEase(Ease.OutQuart);

            yield return new WaitForSecondsRealtime(_slamMoveDuration);
            if (_isInterrupted) { ReattachArm(); yield break; }

            // 히트박스 + 디스크 플래시
            _attackRange?.FlashAndHideSlamDisc(discIndex);

            Collider2D hit = Physics2D.OverlapCircle(targetWorldPos, _data.slamHitRadius, _playerLayer);
            if (hit != null)
                Debug.Log($"[BossPattern_Slam] 내려치기 피격! | 목표:{targetWorldPos}");

            // 공략 타임 — 팔 진동 (색상 없음)
            float vulnDuration = _isPhase2 ? _slamVulnDuration * 0.6f : _slamVulnDuration;

            _armLTransform
                .DOPunchPosition(new Vector3(0.05f, 0.05f, 0f), vulnDuration, 20, 0.5f)
                .SetUpdate(true);

            var armPart = _armLTransform.GetComponent<BossWardenArmPart>();
            armPart?.SetSlamVuln(true, _slamVulnMultiplier);

            yield return new WaitForSecondsRealtime(vulnDuration);

            armPart?.SetSlamVuln(false, 1f);
            if (_isInterrupted) { ReattachArm(); yield break; }

            // 팔 귀환
            Vector3 bossCurrentPos = _bossTransform != null
                ? _bossTransform.position : Vector3.zero;
            Vector3 returnWorldPos = bossCurrentPos + _armOriginLocalPos;

            _armLTransform.DOMove(returnWorldPos, _returnDuration).SetEase(Ease.InBack);
            _armLTransform.DORotate(Vector3.zero, _returnDuration * 0.7f).SetEase(Ease.OutQuart);

            yield return new WaitForSecondsRealtime(_returnDuration);

            ReattachArm();
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 보정 (색상 없음)
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            // 팔 원위치 보정 (위치만)
            if (_armLTransform != null && !_isArmDetached)
                _armLTransform
                    .DOLocalMove(_armOriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);

            // 충격 흔들림
            transform.DOShakePosition(0.3f, 0.2f, 10, 90f).SetUpdate(true);

            yield return StartCoroutine(WaitForPattern(_recoveryDuration));
        }

        // ══════════════════════════════════════════════════════
        // Interrupt
        // ══════════════════════════════════════════════════════

        public override void Interrupt()
        {
            _attackRange?.StopAllPulse();
            _attackRange?.HideSlamDisc(0);
            _attackRange?.HideSlamDisc(1);

            ReattachArm();

            base.Interrupt();
        }

        // ══════════════════════════════════════════════════════
        // 팔 재부착
        // ══════════════════════════════════════════════════════

        private void ReattachArm()
        {
            if (!_isArmDetached || _armLTransform == null || _bossTransform == null) return;

            _armLTransform.DOKill();
            _armLTransform.SetParent(_bossTransform, worldPositionStays: true);
            _armLTransform.localPosition = _armOriginLocalPos;
            _armLTransform.localRotation = Quaternion.identity;

            var armPart = _armLTransform.GetComponent<BossWardenArmPart>();
            armPart?.SetSlamVuln(false, 1f);

            _isArmDetached = false;
            Debug.Log("[BossPattern_Slam] 팔 재부착 완료");
        }

        private void OnDestroy()
        {
            ReattachArm();
        }
    }
}