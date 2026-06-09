// ============================================================
// SealStonePickup.cs
// 봉인석 흡수형 재화 픽업
//
// [역할]
//   - 플레이어가 일정 거리 안에 들어오면 빨려 들어간다.
//   - CollectRange 안에 들어오면 SealStoneWallet에 재화 추가.
//   - Wallet이 없어도 static event로 외부 재화 시스템과 연결 가능.
// ============================================================

using System;
using DG.Tweening;
using UnityEngine;

namespace SEAL
{
    public sealed class SealStonePickup : MonoBehaviour
    {
        [Header("── 재화 ─────────────────────")]
        [Min(1)]
        [SerializeField] private int _amount = 1;

        [Header("── 흡수 ─────────────────────")]
        [Min(0f)]
        [SerializeField] private float _attractDelay = 0.15f;

        [Min(0.1f)]
        [SerializeField] private float _attractRange = 3.5f;

        [Min(0.01f)]
        [SerializeField] private float _collectRange = 0.25f;

        [Min(0.1f)]
        [SerializeField] private float _startMoveSpeed = 3.0f;

        [Min(0.1f)]
        [SerializeField] private float _maxMoveSpeed = 16.0f;

        [Min(0f)]
        [SerializeField] private float _acceleration = 28.0f;

        [Tooltip("미연결 시 Player 태그 오브젝트를 자동 탐색한다.")]
        [SerializeField] private Transform _target;

        [SerializeField] private string _playerTag = "Player";

        [Header("── 수집 연출 ─────────────────────")]
        [SerializeField] private ParticleSystem _collectParticlePrefab;
        [SerializeField] private bool _scaleOutOnCollect = true;
        [Min(0f)]
        [SerializeField] private float _scaleOutDuration = 0.08f;

        public static event Action<int, Vector3> OnAnySealStoneCollected;

        private float _spawnTime;
        private float _currentSpeed;
        private bool _collected;

        public int Amount => _amount;

        private void OnEnable()
        {
            _spawnTime = Time.time;
            _currentSpeed = _startMoveSpeed;
            _collected = false;
        }

        private void Update()
        {
            if (_collected) return;
            if (Time.time < _spawnTime + _attractDelay) return;

            if (_target == null)
                FindTarget();

            if (_target == null) return;

            Vector3 current = transform.position;
            Vector3 targetPos = _target.position;
            float distance = Vector2.Distance(current, targetPos);

            if (distance > _attractRange)
                return;

            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _maxMoveSpeed, _acceleration * Time.deltaTime);
            transform.position = Vector3.MoveTowards(current, targetPos, _currentSpeed * Time.deltaTime);

            if (distance <= _collectRange)
                Collect(_target.gameObject);
        }

        private void FindTarget()
        {
            GameObject player = GameObject.FindGameObjectWithTag(_playerTag);
            if (player != null)
                _target = player.transform;
        }

        private void Collect(GameObject playerObject)
        {
            if (_collected) return;
            _collected = true;

            if (playerObject != null)
            {
                SealStoneWallet wallet = playerObject.GetComponentInParent<SealStoneWallet>();
                if (wallet != null)
                    wallet.Add(_amount);
            }

            OnAnySealStoneCollected?.Invoke(_amount, transform.position);

            if (_collectParticlePrefab != null)
            {
                ParticleSystem ps = Instantiate(_collectParticlePrefab, transform.position, Quaternion.identity);
                ps.Play(true);
                Destroy(ps.gameObject, ps.main.duration + ps.main.startLifetime.constantMax + 0.25f);
            }

            if (_scaleOutOnCollect && _scaleOutDuration > 0f)
            {
                transform.DOKill();
                transform.DOScale(Vector3.zero, _scaleOutDuration)
                    .OnComplete(() => Destroy(gameObject));
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void SetAmount(int amount)
        {
            _amount = Mathf.Max(1, amount);
        }

        public void SetTarget(Transform target)
        {
            _target = target;
        }
    }
}
