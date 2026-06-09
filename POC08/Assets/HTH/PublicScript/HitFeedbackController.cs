// ============================================================
// HitFeedbackController.cs  v2.1
// 피격 파티클 Queue 기반 Object Pool 재생 전담 싱글턴
//
// [v2.1 변경 — 진짜 Pool 방식]
//   기존 v2.0:
//     List 순회 + ParticleSystem.isPlaying=false 인스턴스 재사용
//     → 자식 ParticleSystem / 잔여 파티클 / loop 설정에 취약
//     → 재생 완료 시점과 반환 시점이 명확하지 않음
//
//   변경 v2.1:
//     Queue<ParticleSystem> availablePool 사용
//     Play 시 Queue에서 대여
//     재생 완료 후 ReturnRoutine에서 명시적으로 Pool 반환
//     반환 시 StopEmittingAndClear + SetActive(false)
//     재생 전 StopEmittingAndClear + 위치 이동 + SetActive(true) + Play(true)
//
// [주의]
//   Prefab 및 자식 ParticleSystem의 looping은 false 권장.
//   Simulation Space는 World 권장.
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 피격 파티클 Queue 기반 Object Pool 재생 싱글턴. (v2.1)
    /// </summary>
    public class HitFeedbackController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // 싱글턴
        // ══════════════════════════════════════════════════════

        public static HitFeedbackController Instance { get; private set; }

        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── Prefab 연결 ──────────────────────")]

        [Tooltip("EnemyHitParticle.prefab 연결. Project 에셋 직접 참조.")]
        [SerializeField] private ParticleSystem _enemyHitVfxPrefab;

        [Tooltip("HitParticle.prefab 연결. Project 에셋 직접 참조.")]
        [SerializeField] private ParticleSystem _playerHitVfxPrefab;

        [Header("── Pool 설정 ──────────────────────")]

        [Tooltip("Pool 인스턴스 부모 오브젝트. 미연결 시 자신이 부모.")]
        [SerializeField] private Transform _poolRoot;

        [Tooltip("초기 Pool 크기. 부족하면 자동 확장됨.")]
        [SerializeField, Min(0)] private int _initialPoolSize = 6;

        [Tooltip("Pool 자동 확장 허용 여부.")]
        [SerializeField] private bool _allowExpand = true;

        [Tooltip("Pool 자동 확장 시 최대 개수. 0 이하면 제한 없음.")]
        [SerializeField, Min(0)] private int _maxPoolSize = 0;

        [Tooltip("재생 완료 감지 후 반환 전 추가 대기 시간.")]
        [SerializeField, Min(0f)] private float _returnExtraDelay = 0.05f;

        [Tooltip("looping 등으로 IsAlive가 계속 true일 때 강제 반환하기 위한 최대 대기 시간. 0 이하면 파티클 길이 기반 자동 계산만 사용.")]
        [SerializeField, Min(0f)] private float _forcedReturnTime = 0f;

        [Header("── 디버그 ──────────────────────")]
        [SerializeField] private bool _logPoolExpand = false;
        [SerializeField] private bool _logPoolWarning = true;

        // ══════════════════════════════════════════════════════
        // 내부 Pool
        // ══════════════════════════════════════════════════════

        private ParticlePool _enemyHitPool;
        private ParticlePool _playerHitPool;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (_poolRoot == null)
                _poolRoot = transform;

            _enemyHitPool = new ParticlePool(
                owner: this,
                prefab: _enemyHitVfxPrefab,
                root: _poolRoot,
                initialSize: _initialPoolSize,
                allowExpand: _allowExpand,
                maxPoolSize: _maxPoolSize,
                returnExtraDelay: _returnExtraDelay,
                forcedReturnTime: _forcedReturnTime,
                label: "EnemyHitVfx",
                logExpand: _logPoolExpand,
                logWarning: _logPoolWarning);

            _playerHitPool = new ParticlePool(
                owner: this,
                prefab: _playerHitVfxPrefab,
                root: _poolRoot,
                initialSize: _initialPoolSize,
                allowExpand: _allowExpand,
                maxPoolSize: _maxPoolSize,
                returnExtraDelay: _returnExtraDelay,
                forcedReturnTime: _forcedReturnTime,
                label: "PlayerHitVfx",
                logExpand: _logPoolExpand,
                logWarning: _logPoolWarning);

            ValidatePrefabs();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>적 피격 파티클 재생.</summary>
        public void PlayEnemyHit(Vector2 worldPosition)
        {
            _enemyHitPool?.Play(worldPosition);
        }

        /// <summary>플레이어 피격 파티클 재생.</summary>
        public void PlayPlayerHit(Vector2 worldPosition)
        {
            _playerHitPool?.Play(worldPosition);
        }

        // ══════════════════════════════════════════════════════
        // 검증
        // ══════════════════════════════════════════════════════

        private void ValidatePrefabs()
        {
            ValidateSingle(_enemyHitVfxPrefab, "EnemyHitVfx");
            ValidateSingle(_playerHitVfxPrefab, "PlayerHitVfx");
        }

        private void ValidateSingle(ParticleSystem prefab, string label)
        {
            if (prefab == null)
            {
                if (_logPoolWarning)
                    Debug.LogWarning($"[HitFeedbackController] {label} Prefab 미연결.");
                return;
            }

            ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                var main = ps.main;

                if (main.playOnAwake && _logPoolWarning)
                    Debug.LogWarning($"[HitFeedbackController] {label}/{ps.name}.playOnAwake = true — false 권장.");

                if (main.loop && _logPoolWarning)
                    Debug.LogWarning($"[HitFeedbackController] {label}/{ps.name}.looping = true — Pool 반환 지연 원인. false 권장.");

                if (main.simulationSpace != ParticleSystemSimulationSpace.World && _logPoolWarning)
                    Debug.LogWarning($"[HitFeedbackController] {label}/{ps.name}.SimulationSpace ≠ World — World 권장.");
            }
        }

        // ══════════════════════════════════════════════════════
        // 내부 Pool 클래스
        // ══════════════════════════════════════════════════════

        private sealed class ParticlePool
        {
            private readonly MonoBehaviour _owner;
            private readonly ParticleSystem _prefab;
            private readonly Transform _root;
            private readonly bool _allowExpand;
            private readonly int _maxPoolSize;
            private readonly float _returnExtraDelay;
            private readonly float _forcedReturnTime;
            private readonly string _label;
            private readonly bool _logExpand;
            private readonly bool _logWarning;

            private readonly Queue<ParticleSystem> _available = new Queue<ParticleSystem>();
            private readonly HashSet<ParticleSystem> _active = new HashSet<ParticleSystem>();
            private readonly List<ParticleSystem> _all = new List<ParticleSystem>();

            public ParticlePool(
                MonoBehaviour owner,
                ParticleSystem prefab,
                Transform root,
                int initialSize,
                bool allowExpand,
                int maxPoolSize,
                float returnExtraDelay,
                float forcedReturnTime,
                string label,
                bool logExpand,
                bool logWarning)
            {
                _owner = owner;
                _prefab = prefab;
                _root = root;
                _allowExpand = allowExpand;
                _maxPoolSize = maxPoolSize;
                _returnExtraDelay = returnExtraDelay;
                _forcedReturnTime = forcedReturnTime;
                _label = label;
                _logExpand = logExpand;
                _logWarning = logWarning;

                Prewarm(Mathf.Max(0, initialSize));
            }

            public void Play(Vector2 worldPosition)
            {
                if (_prefab == null)
                {
                    if (_logWarning)
                        Debug.LogWarning($"[HitFeedbackController] {_label} Prefab 미연결.");
                    return;
                }

                ParticleSystem ps = Rent();
                if (ps == null)
                    return;

                PrepareAndPlay(ps, worldPosition);
                _owner.StartCoroutine(ReturnWhenFinished(ps));
            }

            private void Prewarm(int count)
            {
                if (_prefab == null)
                    return;

                for (int i = 0; i < count; i++)
                {
                    ParticleSystem ps = CreateInstance();
                    ReturnImmediately(ps);
                }
            }

            private ParticleSystem Rent()
            {
                while (_available.Count > 0)
                {
                    ParticleSystem ps = _available.Dequeue();
                    if (ps == null)
                        continue;

                    _active.Add(ps);
                    return ps;
                }

                if (!_allowExpand)
                {
                    if (_logWarning)
                        Debug.LogWarning($"[HitFeedbackController] {_label} Pool 부족 — 확장 비활성화 상태.");
                    return null;
                }

                if (_maxPoolSize > 0 && _all.Count >= _maxPoolSize)
                {
                    if (_logWarning)
                        Debug.LogWarning($"[HitFeedbackController] {_label} Pool 최대치 도달 — 재생 생략. Max:{_maxPoolSize}");
                    return null;
                }

                ParticleSystem created = CreateInstance();
                _active.Add(created);

                if (_logExpand)
                    Debug.Log($"[HitFeedbackController] Pool 확장 — {_label} | 현재 Pool 크기: {_all.Count}");

                return created;
            }

            private ParticleSystem CreateInstance()
            {
                ParticleSystem ps = Object.Instantiate(_prefab, _root);
                ps.name = $"{_prefab.name}_Pooled_{_all.Count:00}";
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
                _all.Add(ps);
                return ps;
            }

            private void PrepareAndPlay(ParticleSystem ps, Vector2 worldPosition)
            {
                ps.gameObject.SetActive(true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.transform.position = worldPosition;
                ps.Play(true);
            }

            private IEnumerator ReturnWhenFinished(ParticleSystem ps)
            {
                if (ps == null)
                    yield break;

                float maxWait = GetReturnWaitTime(ps);
                float elapsed = 0f;

                // IsAlive(true) = 자식 파티클까지 포함해서 살아있는지 확인.
                while (ps != null && ps.IsAlive(true) && elapsed < maxWait)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (_returnExtraDelay > 0f)
                    yield return new WaitForSecondsRealtime(_returnExtraDelay);

                ReturnImmediately(ps);
            }

            private void ReturnImmediately(ParticleSystem ps)
            {
                if (ps == null)
                    return;

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);

                _active.Remove(ps);

                if (!_available.Contains(ps))
                    _available.Enqueue(ps);
            }

            private float GetReturnWaitTime(ParticleSystem ps)
            {
                if (_forcedReturnTime > 0f)
                    return _forcedReturnTime;

                float max = 0.1f;
                ParticleSystem[] systems = ps.GetComponentsInChildren<ParticleSystem>(true);

                for (int i = 0; i < systems.Length; i++)
                {
                    var main = systems[i].main;
                    float duration = main.duration;
                    float lifetime = 0f;

                    switch (main.startLifetime.mode)
                    {
                        case ParticleSystemCurveMode.Constant:
                            lifetime = main.startLifetime.constant;
                            break;
                        case ParticleSystemCurveMode.TwoConstants:
                            lifetime = main.startLifetime.constantMax;
                            break;
                        default:
                            // Curve 계열은 정확한 최대값 계산이 복잡하므로 넉넉한 폴백.
                            lifetime = main.duration;
                            break;
                    }

                    max = Mathf.Max(max, duration + lifetime);
                }

                return max + 0.25f;
            }
        }
    }
}
