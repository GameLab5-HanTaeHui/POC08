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