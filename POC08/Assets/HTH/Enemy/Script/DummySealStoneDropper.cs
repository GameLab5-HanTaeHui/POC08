// ============================================================
// DummySealStoneDropper.cs
// DummyEnemy 봉인 완료 → 봉인석 변환/드랍 연출
//
// [역할]
//   - DummyEnemy.OnSealed 이벤트를 받아 봉인석 드랍 연출을 실행한다.
//   - 봉인 파티클 재생 + DOTween 축소 연출 후 봉인석 프리팹을 생성한다.
//   - DummyEnemy의 즉시 비활성화와 충돌하지 않도록 별도 Runtime Runner에서 드랍 타이밍을 처리한다.
//
// [namespace] SEAL
// ============================================================

using System.Collections;
using DG.Tweening;
using UnityEngine;

namespace SEAL
{
    [DisallowMultipleComponent]
    public sealed class DummySealStoneDropper : MonoBehaviour
    {
        [Header("── 봉인석 프리팹 ─────────────────────")]
        [Tooltip("봉인 완료 후 생성할 봉인석 프리팹. SealStonePickup 컴포넌트를 붙여두는 것을 권장한다.")]
        [SerializeField] private GameObject _sealStonePrefab;

        [Tooltip("봉인석 생성 위치. 비어 있으면 이 오브젝트 위치를 사용한다.")]
        [SerializeField] private Transform _dropPoint;

        [Tooltip("생성되는 봉인석 재화 수량. SealStonePickup이 있으면 이 값으로 덮어쓴다.")]
        [Min(1)]
        [SerializeField] private int _stoneAmount = 1;

        [Header("── 변환 연출 ─────────────────────")]
        [Tooltip("봉인석으로 바뀌는 데 걸리는 시간.")]
        [Min(0f)]
        [SerializeField] private float _convertDuration = 0.55f;

        [Tooltip("축소 연출 대상. 비어 있으면 SpriteRenderer가 붙은 첫 자식을 자동 탐색한다.")]
        [SerializeField] private Transform _visualRoot;

        [Tooltip("봉인 완료 시 VisualRoot를 점점 작게 만든다.")]
        [SerializeField] private bool _shrinkVisual = true;

        [Tooltip("축소 연출 Ease.")]
        [SerializeField] private Ease _shrinkEase = Ease.InBack;

        [Tooltip("봉인 완료 직후 Collider2D들을 끈다. HurtBox/AttackHitBox가 남는 문제 방지용.")]
        [SerializeField] private bool _disableCollidersOnDropStart = true;

        [Header("── 파티클 ─────────────────────")]
        [Tooltip("봉인 변환 중 재생할 파티클 프리팹. 프리팹을 쓰면 적이 비활성화되어도 파티클이 유지된다.")]
        [SerializeField] private ParticleSystem _convertParticlePrefab;

        [Tooltip("이미 DummyEnemy 자식에 있는 파티클을 직접 재생하고 싶을 때 연결한다.")]
        [SerializeField] private ParticleSystem _localConvertParticle;

        [Tooltip("파티클 자동 제거 시간. 0 이하이면 main.duration + lifetime 기준으로 계산한다.")]
        [SerializeField] private float _particleDestroyDelay = 0f;

        [Header("── 봉인석 등장 연출 ─────────────────────")]
        [Tooltip("생성된 봉인석이 0 스케일에서 원래 크기로 커지는 연출.")]
        [SerializeField] private bool _playStonePopTween = true;

        [Min(0f)]
        [SerializeField] private float _stonePopDuration = 0.18f;

        [SerializeField] private Ease _stonePopEase = Ease.OutBack;

        [Header("── 적 비활성화 ─────────────────────")]
        [Tooltip("연출 후 DummyEnemy GameObject를 비활성화한다. 기존 DummyEnemyDataSO의 DeactivateOnSealed와 중복되지 않게 주의.")]
        [SerializeField] private bool _deactivateEnemyAfterDrop = true;

        [Min(0f)]
        [SerializeField] private float _deactivateDelayAfterStoneSpawn = 0.05f;

        private DummyEnemy _enemy;
        private bool _dropStarted;
        private Vector3 _visualOriginalScale = Vector3.one;

        private void Awake()
        {
            _enemy = GetComponent<DummyEnemy>();

            if (_visualRoot == null)
            {
                SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null)
                    _visualRoot = sr.transform;
            }

            if (_visualRoot != null)
                _visualOriginalScale = _visualRoot.localScale;
        }

        private void OnEnable()
        {
            _dropStarted = false;

            if (_enemy == null)
                _enemy = GetComponent<DummyEnemy>();

            if (_enemy != null)
            {
                _enemy.OnSealed -= HandleEnemySealed;
                _enemy.OnSealed += HandleEnemySealed;
            }
        }

        private void OnDisable()
        {
            if (_enemy != null)
                _enemy.OnSealed -= HandleEnemySealed;
        }

        private void HandleEnemySealed(DummyEnemy enemy)
        {
            if (_dropStarted) return;
            _dropStarted = true;

            BeginDropSequence();
        }

        public void BeginDropSequence()
        {
            Vector3 dropPosition = _dropPoint != null ? _dropPoint.position : transform.position;
            Quaternion dropRotation = _dropPoint != null ? _dropPoint.rotation : transform.rotation;
            GameObject enemyObject = gameObject;

            if (_disableCollidersOnDropStart)
            {
                Collider2D[] cols = GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] == null) continue;
                    cols[i].enabled = false;
                }
            }

            PlayConvertParticle(dropPosition, dropRotation);
            PlayShrinkTween();

            DummySealStoneDropRuntime.Run(SpawnStoneAfterDelay(enemyObject, dropPosition, dropRotation));
        }

        private void PlayConvertParticle(Vector3 position, Quaternion rotation)
        {
            if (_convertParticlePrefab != null)
            {
                ParticleSystem ps = Instantiate(_convertParticlePrefab, position, rotation);
                ps.Play(true);

                float destroyDelay = _particleDestroyDelay;
                if (destroyDelay <= 0f)
                    destroyDelay = ps.main.duration + ps.main.startLifetime.constantMax + 0.25f;

                Destroy(ps.gameObject, destroyDelay);
                return;
            }

            if (_localConvertParticle != null)
            {
                _localConvertParticle.transform.position = position;
                _localConvertParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _localConvertParticle.Play(true);
            }
        }

        private void PlayShrinkTween()
        {
            if (!_shrinkVisual) return;
            if (_visualRoot == null) return;

            _visualRoot.DOKill();
            _visualRoot.DOScale(Vector3.zero, _convertDuration)
                .SetEase(_shrinkEase);
        }

        private IEnumerator SpawnStoneAfterDelay(GameObject enemyObject, Vector3 position, Quaternion rotation)
        {
            if (_convertDuration > 0f)
                yield return new WaitForSeconds(_convertDuration);

            GameObject stone = null;
            if (_sealStonePrefab != null)
            {
                stone = Instantiate(_sealStonePrefab, position, rotation);

                SealStonePickup pickup = stone.GetComponent<SealStonePickup>();
                if (pickup != null)
                    pickup.SetAmount(_stoneAmount);

                if (_playStonePopTween)
                {
                    Vector3 originalScale = stone.transform.localScale;
                    stone.transform.localScale = Vector3.zero;
                    stone.transform.DOScale(originalScale, _stonePopDuration)
                        .SetEase(_stonePopEase);
                }
            }
            else
            {
                Debug.LogWarning($"[DummySealStoneDropper] {name} — SealStonePrefab 미연결.");
            }

            if (_deactivateEnemyAfterDrop && enemyObject != null)
            {
                if (_deactivateDelayAfterStoneSpawn > 0f)
                    yield return new WaitForSeconds(_deactivateDelayAfterStoneSpawn);

                enemyObject.SetActive(false);
            }
        }

        public void ResetVisualScale()
        {
            if (_visualRoot == null) return;
            _visualRoot.DOKill();
            _visualRoot.localScale = _visualOriginalScale;
        }
    }
}
