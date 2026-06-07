// ============================================================
// BossPattern_Slam.cs
// Boss_Warden 내려찍기 패턴
//
// [수정 내용]
//   팔(왼팔) SpriteRenderer 색상 코드 전부 제거
//   → _armLRenderer / _armColorTween / _armOriginColor 제거
//   Warning AttackRange 점멸 추가
//   _data.slamMoveDuration 등 없는 필드 → 패턴 자체 SerializeField 사용
//   (_windupPullAmount, _windupLiftAmount, _slamMoveDuration,
//    _slamVulnDuration, _slamVulnMultiplier, _returnDuration)
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
        /// <summary>왼팔 Transform. 분리/귀환 연출. 색상 제어 없음.</summary>
        [Tooltip("왼팔 Transform. 색상 제어 없음.")]
        [SerializeField] private Transform _armLTransform;

        [Header("── 레이어 ──────────────────────")]
        [Tooltip("플레이어 히트박스 레이어.")]
        [SerializeField] private LayerMask _playerLayer;

        [Header("── 연출 수치 ──────────────────────")]

        /// <summary>Warning 백스윙 당기는 거리.</summary>
        [Tooltip("백스윙 당기는 거리. 권장: 0.5")]
        [Min(0f)]
        [SerializeField] private float _windupPullAmount = 0.5f;

        /// <summary>백스윙 들어올리는 높이.</summary>
        [Tooltip("백스윙 들어올리는 높이. 권장: 0.3")]
        [Min(0f)]
        [SerializeField] private float _windupLiftAmount = 0.3f;

        /// <summary>팔이 목표 위치까지 이동하는 시간 (초).</summary>
        [Tooltip("내려치기 이동 시간 (초). 권장: 0.15")]
        [Min(0.05f)]
        [SerializeField] private float _slamMoveDuration = 0.15f;

        /// <summary>팔이 꽂혀있는 공략 타임 지속 시간 (초).</summary>
        [Tooltip("팔 공략 타임 지속 시간 (초). 권장: 2.0")]
        [Min(0.5f)]
        [SerializeField] private float _slamVulnDuration = 2.0f;

        /// <summary>공략 타임 중 봉인도 누적 배율.</summary>
        [Tooltip("공략 타임 봉인도 배율. 권장: 2.0")]
        [Min(1f)]
        [SerializeField] private float _slamVulnMultiplier = 2.0f;

        /// <summary>팔 귀환 이동 시간 (초).</summary>
        [Tooltip("팔 귀환 이동 시간 (초). 권장: 0.25")]
        [Min(0.05f)]
        [SerializeField] private float _returnDuration = 0.25f;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private Vector3 _armOriginLocalPos;
        private Transform _bossTransform;
        private bool _isPhase2;
        private bool _isArmDetached;

        // 2페이즈 수치 (UnlockPhase2 에서 갱신)
        private float _currentSlamMoveDuration;
        private float _currentReturnDuration;
        private float _currentVulnDuration;
        private float _currentVulnMultiplier;

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

            ApplyDataValues();
        }

        private void ApplyDataValues()
        {
            _currentSlamMoveDuration = _slamMoveDuration;
            _currentReturnDuration = _returnDuration;
            _currentVulnDuration = _slamVulnDuration;
            _currentVulnMultiplier = _slamVulnMultiplier;
        }

        public new void UnlockPhase2()
        {
            base.UnlockPhase2();
            _isPhase2 = true;

            // 2페이즈: 더 빠르고 위협적
            _currentSlamMoveDuration = _slamMoveDuration * 0.8f;
            _currentReturnDuration = _returnDuration * 0.8f;
            _currentVulnDuration = _slamVulnDuration * 0.6f;
            _currentVulnMultiplier = _slamVulnMultiplier * 1.5f;
        }

        private void OnDestroy()
        {
            ReattachArm();
        }

        // ══════════════════════════════════════════════════════
        // Warning — 디스크 점멸 + 팔 백스윙
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnWarning()
        {
            if (_data == null) yield break;

            Vector2 playerPos = _ai?.PlayerTransform != null
                ? (Vector2)_ai.PlayerTransform.position
                : Vector2.zero;

            // 디스크 표시 + 점멸 시작
            _attackRange?.ShowSlamDisc(playerPos, _data.slamHitRadius, 0);
            _attackRange?.StartSlamPulse(0);

            // 팔 백스윙 (위치만 — 색상 없음)
            if (_armLTransform != null)
            {
                Vector3 backOffset = new Vector3(0f, _windupLiftAmount, 0f)
                                   + new Vector3(-_windupPullAmount, 0f, 0f);
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

            // 2페이즈 2번째 타격: 새 위치 디스크 표시
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
                        _currentSlamMoveDuration)
                .SetEase(Ease.OutExpo);

            _armLTransform
                .DORotate(new Vector3(0f, 0f, targetAngle), _currentSlamMoveDuration * 0.5f)
                .SetEase(Ease.OutQuart);

            yield return new WaitForSecondsRealtime(_currentSlamMoveDuration);
            if (_isInterrupted) { ReattachArm(); yield break; }

            // 히트박스 + 디스크 플래시
            _attackRange?.FlashAndHideSlamDisc(discIndex);

            Collider2D hit = Physics2D.OverlapCircle(targetWorldPos, _data.slamHitRadius, _playerLayer);
            if (hit != null)
                Debug.Log($"[BossPattern_Slam] 내려치기 피격! | 목표:{targetWorldPos}");

            // 공략 타임 — 팔 진동 (색상 없음)
            _armLTransform
                .DOPunchPosition(new Vector3(0.05f, 0.05f, 0f), _currentVulnDuration, 20, 0.5f)
                .SetUpdate(true);

            var armPart = _armLTransform.GetComponent<BossWardenArmPart>();
            armPart?.SetSlamVuln(true, _currentVulnMultiplier);

            yield return new WaitForSecondsRealtime(_currentVulnDuration);

            armPart?.SetSlamVuln(false, 1f);
            if (_isInterrupted) { ReattachArm(); yield break; }

            // 팔 귀환
            Vector3 bossCurrentPos = _bossTransform.position;
            Vector3 returnWorldPos = bossCurrentPos + _armOriginLocalPos;

            _armLTransform.DOMove(returnWorldPos, _currentReturnDuration).SetEase(Ease.InBack);
            _armLTransform.DORotate(Vector3.zero, _currentReturnDuration * 0.7f).SetEase(Ease.OutQuart);

            yield return new WaitForSecondsRealtime(_currentReturnDuration);

            ReattachArm();
        }

        // ══════════════════════════════════════════════════════
        // Recovery — 팔 원위치 보정 (색상 없음)
        // ══════════════════════════════════════════════════════

        protected override IEnumerator OnRecovery()
        {
            if (_isInterrupted) yield break;

            if (_armLTransform != null && !_isArmDetached)
                _armLTransform
                    .DOLocalMove(_armOriginLocalPos, _recoveryDuration * 0.3f)
                    .SetEase(Ease.OutBack);

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

            _armLTransform.GetComponent<BossWardenArmPart>()?.SetSlamVuln(false, 1f);

            _isArmDetached = false;
            Debug.Log("[BossPattern_Slam] 팔 재부착 완료");
        }
    }
}