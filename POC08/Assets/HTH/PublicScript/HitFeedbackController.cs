// ============================================================
// HitFeedbackController.cs  v1.1
// 피격 파티클 재생 전담 싱글턴 컴포넌트
//
// [v1.1 변경 — 시뮬레이션 공간 검증 추가]
//   ValidateVfx() 에 SimulationSpace.World 체크 추가.
//
//   [문제]
//     파티클 SimulationSpace = Local 이면
//     transform.position 으로 월드 위치 이동 시
//     파티클이 부모(HitFeedbackController) 기준 로컬로 해석되어
//     재생 중 파티클이 부모와 함께 이동하거나 위치가 틀어짐.
//
//   [해결]
//     파티클 Main.SimulationSpace = World 로 설정 필수.
//     ValidateVfx() 에서 경고 로그로 안내.
//
// [누락 연결 항목 — POC08 프로젝트 파일에서 직접 추가 필요]
//   BossPattern_Slam.ExecuteThrow()     → PlayPlayerHit(hit.bounds.center)
//   BossPattern_Sweep.CheckSweepHit()   → PlayPlayerHit(hit.bounds.center)
//   BossPattern_GuardBreak.CheckHit()   → PlayPlayerHit(hit.bounds.center)
//   BossPattern_RageCharge.OnActive()   → PlayPlayerHit(hit.bounds.center)
//
// [씬 배치]
//   EffectRoot 하위 HitFeedbackController 오브젝트.
//   Inspector 에서 _playerHitVfx, _enemyHitVfx 연결.
//   파티클 Main.SimulationSpace = World 필수.
// ============================================================

using UnityEngine;

namespace SEAL
{
    /// <summary>
    /// 피격 파티클 재생 전담 싱글턴. (v1.0)
    ///
    /// ────────────────────────────────────────────────────
    /// [Hierarchy 배치]
    ///   EffectRoot
    ///     └─ HitFeedbackController  [이 컴포넌트]
    ///          [PlayerHitEffect]    ← HitParticle.prefab 인스턴스
    ///          [EnemyHitEffect]     ← EnemyHitParticle.prefab 인스턴스
    ///
    /// [연결 필드]
    ///   _playerHitVfx : PlayerHitEffect 의 ParticleSystem
    ///   _enemyHitVfx  : EnemyHitEffect  의 ParticleSystem
    /// ────────────────────────────────────────────────────
    /// </summary>
    public class HitFeedbackController : MonoBehaviour
    {
        // ──────────────────────────────────────────
        // 싱글턴
        // ──────────────────────────────────────────

        /// <summary>
        /// 전역 단일 인스턴스.
        /// 씬 전환 시 파괴되면 null 초기화.
        /// </summary>
        public static HitFeedbackController Instance { get; private set; }

        // ──────────────────────────────────────────
        // Inspector — 파티클 연결
        // ──────────────────────────────────────────

        [Header("── 파티클 연결 ──────────────────────")]

        /// <summary>
        /// 플레이어 피격 파티클.
        /// HitParticle.prefab 인스턴스의 ParticleSystem 연결.
        /// playOnAwake = false 필수.
        /// </summary>
        [Tooltip("플레이어 피격 파티클. HitParticle.prefab 인스턴스 연결.")]
        [SerializeField] private ParticleSystem _playerHitVfx;

        /// <summary>
        /// 적 피격 파티클 (봉인 부위 피격).
        /// EnemyHitParticle.prefab 인스턴스의 ParticleSystem 연결.
        /// playOnAwake = false 필수.
        /// </summary>
        [Tooltip("적 피격 파티클. EnemyHitParticle.prefab 인스턴스 연결.")]
        [SerializeField] private ParticleSystem _enemyHitVfx;

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

            ValidateVfx();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ══════════════════════════════════════════════════════
        // 외부 API — 각 피격 처리 컴포넌트에서 호출
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 플레이어 피격 파티클 재생.
        /// 보스 패턴이 플레이어를 감지했을 때 호출.
        ///
        /// [호출 위치 예시]
        ///   BossPattern_Charge.CheckChargeHit() 내부
        ///   BossPattern_Slam.ExecuteSlam() 내부
        ///   추후 PlayerHealth.TakeDamage() 에서 통합 호출 예정.
        /// </summary>
        /// <param name="worldPosition">파티클 재생 위치 (월드 좌표).</param>
        public void PlayPlayerHit(Vector2 worldPosition)
        {
            if (_playerHitVfx == null)
            {
                Debug.LogWarning("[HitFeedbackController] _playerHitVfx 미연결.");
                return;
            }

            _playerHitVfx.transform.position = worldPosition;

            // 이미 재생 중이면 중단 후 재시작 (빠른 연속 피격 대응)
            if (_playerHitVfx.isPlaying)
                _playerHitVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _playerHitVfx.Play();
        }

        /// <summary>
        /// 적 피격 파티클 재생 (봉인 부위 적중).
        /// 플레이어 공격이 보스 팔/코어에 적중했을 때 호출.
        ///
        /// [호출 위치 예시]
        ///   PlayerAttackController.HandleHitboxHit(col, sealAmount)
        ///   BossWardenArmPart.HandlePlayerHit(col, sealAmount)
        /// </summary>
        /// <param name="worldPosition">파티클 재생 위치 (월드 좌표).</param>
        public void PlayEnemyHit(Vector2 worldPosition)
        {
            if (_enemyHitVfx == null)
            {
                Debug.LogWarning("[HitFeedbackController] _enemyHitVfx 미연결.");
                return;
            }

            _enemyHitVfx.transform.position = worldPosition;

            if (_enemyHitVfx.isPlaying)
                _enemyHitVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _enemyHitVfx.Play();
        }

        // ══════════════════════════════════════════════════════
        // 내부 유틸리티
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Inspector 연결 누락 경고.
        /// Awake 에서 1회 호출.
        /// </summary>
        private void ValidateVfx()
        {
            if (_playerHitVfx == null)
                Debug.LogWarning("[HitFeedbackController] _playerHitVfx 미연결 — Inspector 에서 HitParticle.prefab 인스턴스 연결 필요.");
            if (_enemyHitVfx == null)
                Debug.LogWarning("[HitFeedbackController] _enemyHitVfx 미연결 — Inspector 에서 EnemyHitParticle.prefab 인스턴스 연결 필요.");

            // playOnAwake 검증
            if (_playerHitVfx != null && _playerHitVfx.main.playOnAwake)
                Debug.LogWarning("[HitFeedbackController] _playerHitVfx.playOnAwake = true — false 로 변경 필요.");
            if (_enemyHitVfx != null && _enemyHitVfx.main.playOnAwake)
                Debug.LogWarning("[HitFeedbackController] _enemyHitVfx.playOnAwake = true — false 로 변경 필요.");

            // [v1.1] SimulationSpace 검증
            // Local 이면 transform.position 으로 위치 이동 시 파티클이 부모 기준으로
            // 해석되어 재생 중 위치가 틀어지거나 부모와 함께 이동하는 문제 발생.
            // 반드시 World 로 설정해야 월드 좌표 기반 재생이 정상 동작.
            if (_playerHitVfx != null &&
                _playerHitVfx.main.simulationSpace != ParticleSystemSimulationSpace.World)
                Debug.LogWarning("[HitFeedbackController] _playerHitVfx.SimulationSpace ≠ World " +
                                 "— Particle System > Main > Simulation Space 를 World 로 변경 필요.");

            if (_enemyHitVfx != null &&
                _enemyHitVfx.main.simulationSpace != ParticleSystemSimulationSpace.World)
                Debug.LogWarning("[HitFeedbackController] _enemyHitVfx.SimulationSpace ≠ World " +
                                 "— Particle System > Main > Simulation Space 를 World 로 변경 필요.");
        }
    }
}