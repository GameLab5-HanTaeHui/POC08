// ============================================================
// SealExecutionEffect.cs v2.0
// 부위 로컬 봉인 집행 연출 컴포넌트
//
// [v2.0]
//   봉인 집행 진행도 연출 제거.
//   현재 봉인 집행은 입력 1회 또는 일섬 도착 후 즉시 ExecuteSeal() 된다.
//   이 컴포넌트는 집행 시작/완료/강제해제의 짧은 로컬 연출만 담당한다.
// ============================================================

using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace SEAL
{
    [RequireComponent(typeof(SealableComponent))]
    public class SealExecutionEffect : MonoBehaviour
    {
        [Header("── Scale 연출 수치 ──────────────────────")]
        [Tooltip("집행 완료 Scale Punch 강도. 권장: 0.3.")]
        [Range(0f, 1f)]
        [SerializeField] private float _completePunchStrength = 0.3f;

        [Tooltip("강제 해제 Scale Shake 강도. 권장: 0.15.")]
        [Range(0f, 1f)]
        [SerializeField] private float _forceReleaseShakeStrength = 0.15f;

        [Tooltip("집행 시작 순간의 짧은 예비 Scale 배율. 권장: 1.04.")]
        [Range(1f, 1.3f)]
        [SerializeField] private float _executionStartScale = 1.04f;

        [Tooltip("집행 시작 예비 연출 시간. 권장: 0.08.")]
        [Min(0f)]
        [SerializeField] private float _executionStartDuration = 0.08f;

        private SealableComponent _sealable;
        private BossDataSO _bossData;
        private Transform _visualTransform;
        private Tweener _scaleTween;
        private Coroutine _effectCoroutine;

        private void Awake()
        {
            _sealable = GetComponent<SealableComponent>();
            if (_sealable == null)
            {
                Debug.LogError($"[SealExecutionEffect] {gameObject.name} — SealableComponent 미부착.");
                enabled = false;
                return;
            }

            _visualTransform = transform;
        }

        private void Start()
        {
            _sealable.OnSealCompleted -= HandleSealCompleted;
            _sealable.OnSealCompleted += HandleSealCompleted;
            _sealable.OnForceReleased -= HandleForceReleased;
            _sealable.OnForceReleased += HandleForceReleased;
        }

        private void OnDestroy()
        {
            _scaleTween?.Kill();

            if (_effectCoroutine != null)
                StopCoroutine(_effectCoroutine);

            if (_sealable != null)
            {
                _sealable.OnSealCompleted -= HandleSealCompleted;
                _sealable.OnForceReleased -= HandleForceReleased;
            }
        }

        /// <summary>
        /// 집행 시작 연출.
        /// BossSealManager가 대상 도착 직후 ExecuteSeal() 전에 호출한다.
        /// </summary>
        public void OnExecutionStart()
        {
            StopRunningEffect();

            _scaleTween = _visualTransform
                .DOScale(Vector3.one * _executionStartScale, _executionStartDuration)
                .SetEase(Ease.OutQuart)
                .SetUpdate(true);
        }

        /// <summary>
        /// 집행 완료 연출.
        /// </summary>
        public void OnExecutionComplete()
        {
            StopRunningEffect();
            _effectCoroutine = StartCoroutine(CompleteEffectRoutine());
        }

        /// <summary>
        /// 집행 중단 연출.
        /// 현재 구조에서는 입력 유지 취소가 없으므로 비상 정리용으로만 사용한다.
        /// </summary>
        public void OnExecutionCancel()
        {
            StopRunningEffect();
            _effectCoroutine = StartCoroutine(CancelEffectRoutine());
        }

        private void HandleSealCompleted()
        {
            if (_effectCoroutine != null) return;
            _effectCoroutine = StartCoroutine(CompleteEffectRoutine());
        }

        private void HandleForceReleased()
        {
            StopRunningEffect();
            _effectCoroutine = StartCoroutine(ForceReleaseEffectRoutine());
        }

        private IEnumerator CompleteEffectRoutine()
        {
            bool punchDone = false;
            _visualTransform
                .DOPunchScale(
                    punch: Vector3.one * _completePunchStrength,
                    duration: 0.35f,
                    vibrato: 5,
                    elasticity: 0.5f)
                .SetEase(Ease.OutQuart)
                .SetUpdate(true)
                .OnComplete(() => punchDone = true);

            yield return new WaitUntil(() => punchDone);

            _visualTransform.localScale = Vector3.one;
            _effectCoroutine = null;

            Debug.Log($"[SealExecutionEffect] ✅ {gameObject.name} 집행 완료 연출 종료");
        }

        private IEnumerator CancelEffectRoutine()
        {
            bool scaleDone = false;
            _visualTransform
                .DOScale(Vector3.one, 0.15f)
                .SetEase(Ease.OutQuart)
                .SetUpdate(true)
                .OnComplete(() => scaleDone = true);

            yield return new WaitUntil(() => scaleDone);

            _effectCoroutine = null;
            Debug.Log($"[SealExecutionEffect] ■ {gameObject.name} 집행 중단 연출 종료");
        }

        private IEnumerator ForceReleaseEffectRoutine()
        {
            bool shakeDone = false;
            _visualTransform
                .DOShakeScale(
                    duration: 0.4f,
                    strength: Vector3.one * _forceReleaseShakeStrength,
                    vibrato: 8,
                    randomness: 30f)
                .SetUpdate(true)
                .OnComplete(() => shakeDone = true);

            yield return new WaitUntil(() => shakeDone);

            _visualTransform.localScale = Vector3.one;
            _effectCoroutine = null;

            Debug.Log($"[SealExecutionEffect] ■ {gameObject.name} 강제 해제 연출 종료");
        }

        private void StopRunningEffect()
        {
            _scaleTween?.Kill();

            if (_effectCoroutine != null)
            {
                StopCoroutine(_effectCoroutine);
                _effectCoroutine = null;
            }
        }

        public void Initialize(BossDataSO data)
        {
            _bossData = data;
        }

        public void SetVisualTransform(Transform visual)
        {
            if (visual != null)
                _visualTransform = visual;
        }
    }
}
