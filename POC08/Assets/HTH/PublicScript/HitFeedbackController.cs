// ============================================================
// HitFeedbackController.cs  v2.0
// 피격 파티클 Dynamic Pool 재생 전담 싱글턴
//
// [v2.0 변경 — Dynamic Object Pool 방식으로 전면 재설계]
//
//   [기존 v1.x 문제]
//     단일 인스턴스 방식:
//       씬에 배치된 ParticleSystem 1개를 재사용
//       → 동시에 여러 곳에서 피격 발생 시 1개만 재생
//       → isPlaying 인 인스턴스를 Stop() 후 재사용 → 연출 끊김
//
//   [v2.0 해결 — Dynamic Pool]
//     Pool 에서 isPlaying=false 인 인스턴스를 찾아 반환.
//     Pool 에 사용 가능한 인스턴스가 없으면 새로 Instantiate 후 Pool 추가.
//     isPlaying=true 인 인스턴스는 절대 재사용하지 않음.
//     Destroy 없음 → GC 부담 없음.
//     동시 피격 수만큼 Pool 이 자동으로 늘어남 (최초 1회만 Instantiate).
//
//   [Pool 규칙]
//     Get():
//       List 순회 → isPlaying=false 인 인스턴스 반환
//       없으면 → Instantiate → List 추가 → 반환
//     isPlaying=true 인 인스턴스 Stop()/재사용 절대 금지
//     반환(Return) 개념 없음 — isPlaying=false 가 되면 자동으로 재사용 가능
//
//   [Inspector 연결]
//     _enemyHitVfxPrefab  → EnemyHitParticle.prefab (Project 에셋 직접 연결)
//     _playerHitVfxPrefab → HitParticle.prefab      (Project 에셋 직접 연결)
//     _poolRoot           → Pool 인스턴스 부모 오브젝트 (EffectRoot 하위 권장)
//
//   [씬 배치]
//     EffectRoot
//       └─ HitFeedbackController  [이 컴포넌트]
//            └─ PoolRoot          [Pool 인스턴스 정리용 부모 — 선택]
//
// [누락 연결 항목 — POC08 프로젝트 파일에서 직접 추가 필요]
//   BossPattern_Slam       → HitFeedbackController.Instance.PlayPlayerHit(pos)
//   BossPattern_Sweep      → HitFeedbackController.Instance.PlayPlayerHit(pos)
//   BossPattern_GuardBreak → HitFeedbackController.Instance.PlayPlayerHit(pos)
//   BossPattern_RageCharge → HitFeedbackController.Instance.PlayPlayerHit(pos)
//
// [파티클 Prefab 설정 필수]
//   playOnAwake      = false
//   looping          = false
//   Simulation Space = World
//
// [네임스페이스]
//   namespace : SEAL
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 피격 파티클 Dynamic Pool 재생 싱글턴. (v2.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [Pool 동작 원칙]
    ///   isPlaying=false 인 인스턴스만 재사용.
    ///   isPlaying=true 인 인스턴스는 절대 건드리지 않음.
    ///   Pool 이 부족하면 Instantiate 로 자동 확장.
    ///   Destroy 없음.
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class HitFeedbackController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // 싱글턴
        // ══════════════════════════════════════════════════════

        /// <summary>전역 단일 인스턴스.</summary>
        public static HitFeedbackController Instance { get; private set; }

        // ══════════════════════════════════════════════════════
        // Inspector
        // ══════════════════════════════════════════════════════

        [Header("── Prefab 연결 ──────────────────────")]

        /// <summary>
        /// 적 피격 파티클 Prefab.
        /// EnemyHitParticle.prefab 을 Project 에서 직접 연결.
        /// playOnAwake=false / looping=false / SimulationSpace=World 필수.
        /// </summary>
        [Tooltip("EnemyHitParticle.prefab 연결. Project 에셋 직접 참조.")]
        [SerializeField] private ParticleSystem _enemyHitVfxPrefab;

        /// <summary>
        /// 플레이어 피격 파티클 Prefab.
        /// HitParticle.prefab 을 Project 에서 직접 연결.
        /// playOnAwake=false / looping=false / SimulationSpace=World 필수.
        /// </summary>
        [Tooltip("HitParticle.prefab 연결. Project 에셋 직접 참조.")]
        [SerializeField] private ParticleSystem _playerHitVfxPrefab;

        [Header("── Pool 설정 ──────────────────────")]

        /// <summary>
        /// Pool 인스턴스들의 부모 Transform.
        /// 미연결 시 HitFeedbackController 자신이 부모가 됨.
        /// Hierarchy 정리 목적.
        /// </summary>
        [Tooltip("Pool 인스턴스 부모 오브젝트. 미연결 시 자신이 부모.")]
        [SerializeField] private Transform _poolRoot;

        /// <summary>
        /// 초기 Pool 크기.
        /// 씬 시작 시 각 Prefab 을 이 수만큼 미리 Instantiate.
        /// 부족하면 자동 확장.
        /// </summary>
        [Tooltip("초기 Pool 크기. 부족하면 자동 확장됨.")]
        [SerializeField] private int _initialPoolSize = 3;

        // ══════════════════════════════════════════════════════
        // 내부 Pool
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 적 피격 파티클 Pool.
        /// isPlaying=false 인 인스턴스를 꺼내 쓰고 재생 완료 후 자동 반환 가능 상태.
        /// </summary>
        private List<ParticleSystem> _enemyHitPool;

        /// <summary>
        /// 플레이어 피격 파티클 Pool.
        /// 동일 규칙 적용.
        /// </summary>
        private List<ParticleSystem> _playerHitPool;

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // 싱글턴 설정
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Pool Root 미연결 시 자신으로 설정
            if (_poolRoot == null)
                _poolRoot = transform;

            // 초기 Pool 생성
            _enemyHitPool = new List<ParticleSystem>(_initialPoolSize);
            _playerHitPool = new List<ParticleSystem>(_initialPoolSize);

            PrewarmPool(_enemyHitVfxPrefab, _enemyHitPool, _initialPoolSize);
            PrewarmPool(_playerHitVfxPrefab, _playerHitPool, _initialPoolSize);

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

        /// <summary>
        /// 적 피격 파티클 재생.
        /// 플레이어 공격이 적에 적중했을 때 호출.
        ///
        /// [호출 위치]
        ///   PlayerAttackController.HandleHitboxHit()
        ///   BossWardenArmPart.HandlePlayerHit()
        /// </summary>
        /// <param name="worldPosition">피격 월드 좌표.</param>
        public void PlayEnemyHit(Vector2 worldPosition)
        {
            if (_enemyHitVfxPrefab == null)
            {
                Debug.LogWarning("[HitFeedbackController] _enemyHitVfxPrefab 미연결.");
                return;
            }

            ParticleSystem ps = GetFromPool(_enemyHitPool, _enemyHitVfxPrefab);
            PlayAt(ps, worldPosition);
        }

        /// <summary>
        /// 플레이어 피격 파티클 재생.
        /// 보스 패턴이 플레이어에 적중했을 때 호출.
        ///
        /// [호출 위치]
        ///   BossPattern_Charge.CheckChargeHit()
        ///   BossPattern_Slam / Sweep / GuardBreak / RageCharge
        /// </summary>
        /// <param name="worldPosition">피격 월드 좌표.</param>
        public void PlayPlayerHit(Vector2 worldPosition)
        {
            if (_playerHitVfxPrefab == null)
            {
                Debug.LogWarning("[HitFeedbackController] _playerHitVfxPrefab 미연결.");
                return;
            }

            ParticleSystem ps = GetFromPool(_playerHitPool, _playerHitVfxPrefab);
            PlayAt(ps, worldPosition);
        }

        // ══════════════════════════════════════════════════════
        // Pool 내부 로직
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Pool 에서 사용 가능한 인스턴스를 반환.
        ///
        /// [규칙]
        ///   isPlaying=false 인 인스턴스만 반환.
        ///   isPlaying=true 인 인스턴스는 절대 반환하지 않음.
        ///   Pool 이 모두 isPlaying=true 이면 새로 Instantiate → Pool 추가 → 반환.
        /// </summary>
        private ParticleSystem GetFromPool(List<ParticleSystem> pool, ParticleSystem prefab)
        {
            // Pool 순회 — isPlaying=false 인 것 반환
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && !pool[i].isPlaying)
                    return pool[i];
            }

            // Pool 에 사용 가능한 인스턴스 없음 → 새로 생성 후 Pool 에 추가
            ParticleSystem newPs = CreateInstance(prefab);
            pool.Add(newPs);

            Debug.Log($"[HitFeedbackController] Pool 확장 — {prefab.name} | 현재 Pool 크기: {pool.Count}");

            return newPs;
        }

        /// <summary>
        /// 파티클 인스턴스를 월드 위치에서 재생.
        /// </summary>
        private void PlayAt(ParticleSystem ps, Vector2 worldPosition)
        {
            if (ps == null) return;

            ps.transform.position = worldPosition;
            ps.Play(true);  // withChildren=true — 자식 ParticleSystem 포함 재생
        }

        /// <summary>
        /// Prefab 으로부터 새 인스턴스 생성.
        /// _poolRoot 하위에 배치 후 반환.
        /// </summary>
        private ParticleSystem CreateInstance(ParticleSystem prefab)
        {
            ParticleSystem ps = Instantiate(prefab, _poolRoot);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// <summary>
        /// 초기 Pool 워밍업.
        /// Awake 시 _initialPoolSize 만큼 미리 Instantiate.
        /// </summary>
        private void PrewarmPool(ParticleSystem prefab, List<ParticleSystem> pool, int count)
        {
            if (prefab == null) return;

            for (int i = 0; i < count; i++)
                pool.Add(CreateInstance(prefab));
        }

        // ══════════════════════════════════════════════════════
        // 검증
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Prefab 설정 검증.
        /// playOnAwake / looping / SimulationSpace 확인.
        /// </summary>
        private void ValidatePrefabs()
        {
            ValidateSingle(_enemyHitVfxPrefab, "EnemyHitVfx");
            ValidateSingle(_playerHitVfxPrefab, "PlayerHitVfx");
        }

        private void ValidateSingle(ParticleSystem prefab, string label)
        {
            if (prefab == null)
            {
                Debug.LogWarning($"[HitFeedbackController] {label} Prefab 미연결.");
                return;
            }

            var main = prefab.main;

            if (main.playOnAwake)
                Debug.LogWarning($"[HitFeedbackController] {label}.playOnAwake = true — false 로 변경 필요.");

            if (main.loop)
                Debug.LogWarning($"[HitFeedbackController] {label}.looping = true — false 로 변경 필요.");

            if (main.simulationSpace != ParticleSystemSimulationSpace.World)
                Debug.LogWarning($"[HitFeedbackController] {label}.SimulationSpace ≠ World " +
                                 "— Particle System > Main > Simulation Space 를 World 로 변경 필요.");
        }
    }
}