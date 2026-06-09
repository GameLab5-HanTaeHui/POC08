// ============================================================
// DummyEnemySimpleSpawner.cs
// 간이 DummyEnemy 생성기 — Player 주변 랜덤 소환 / Destroy 방식 / UI Button 연결
//
// [목적]
//   - DummyEnemy Prefab을 Player 위치 기준 주변에 랜덤 소환한다.
//   - Pool을 사용하지 않고 Instantiate / Destroy 방식으로 처리한다.
//   - UI Button.OnClick 에 연결해서 테스트 웨이브를 생성할 수 있다.
//
// [기본 흐름]
//   SpawnWave()
//     → 기존 웨이브 Destroy 선택
//     → Player 주변 원형 범위 안에서 랜덤 위치 계산
//     → DummyEnemy Prefab Instantiate
//     → 생성 목록에 등록
//
// [UI 연결]
//   방법 A: Button.OnClick 에서 DummyEnemySimpleSpawner.OnClickSpawnWave() 직접 연결
//   방법 B: _spawnButton 연결 + _autoBindSpawnButton = true 로 자동 AddListener
//
// [namespace]
//   namespace : SEAL
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SEAL
{
    /// <summary>
    /// DummyEnemy 간이 생성기.
    /// 플레이어 위치 기준 주변에 지정된 수만큼 DummyEnemy를 랜덤 소환한다.
    /// Pool 없이 Instantiate / Destroy 방식 사용.
    /// UI Button.OnClick 연결 지원.
    /// </summary>
    public class DummyEnemySimpleSpawner : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector — 기본 연결
        // ══════════════════════════════════════════════════════

        [Header("── 기본 연결 ─────────────────────")]

        [Tooltip("소환할 DummyEnemy Prefab.")]
        [SerializeField] private GameObject _dummyEnemyPrefab;

        [Tooltip("소환 기준이 되는 Player Transform. 비워두면 Tag가 Player인 오브젝트를 자동 탐색.")]
        [SerializeField] private Transform _player;

        [Tooltip("생성된 Enemy들을 정리해서 담을 부모 Transform. 비워두면 Spawner 자신 하위에 생성.")]
        [SerializeField] private Transform _spawnRoot;

        // ══════════════════════════════════════════════════════
        // Inspector — UI Button 연결
        // ══════════════════════════════════════════════════════

        [Header("── UI Button 연결 ─────────────────────")]

        [Tooltip("소환 버튼. 비워두고 Button.OnClick에서 OnClickSpawnWave()를 직접 연결해도 됨.")]
        [SerializeField] private Button _spawnButton;

        [Tooltip("선택: 현재 생성된 적 제거 버튼. 비워둬도 됨.")]
        [SerializeField] private Button _destroyButton;

        [Tooltip("true면 OnEnable에서 _spawnButton.onClick에 OnClickSpawnWave를 자동 연결.")]
        [SerializeField] private bool _autoBindSpawnButton = true;

        [Tooltip("true면 OnEnable에서 _destroyButton.onClick에 OnClickDestroySpawnedEnemies를 자동 연결.")]
        [SerializeField] private bool _autoBindDestroyButton = true;

        // ══════════════════════════════════════════════════════
        // Inspector — 소환 수치
        // ══════════════════════════════════════════════════════

        [Header("── 소환 수치 ─────────────────────")]

        [Tooltip("한 번에 소환할 DummyEnemy 수.")]
        [Min(1)]
        [SerializeField] private int _spawnCount = 10;

        [Tooltip("플레이어로부터 최소 소환 거리.")]
        [Min(0f)]
        [SerializeField] private float _minSpawnRadius = 4f;

        [Tooltip("플레이어로부터 최대 소환 거리.")]
        [Min(0.1f)]
        [SerializeField] private float _maxSpawnRadius = 9f;

        [Tooltip("소환 위치의 Z값. 2D에서는 보통 0.")]
        [SerializeField] private float _spawnZ = 0f;

        [Tooltip("true면 Player의 Z값을 소환 Z값으로 사용.")]
        [SerializeField] private bool _usePlayerZ = false;

        // ══════════════════════════════════════════════════════
        // Inspector — 시작 / 디버그
        // ══════════════════════════════════════════════════════

        [Header("── 시작 / 디버그 ─────────────────────")]

        [Tooltip("Start 시 자동으로 한 웨이브 소환. UI Button 테스트면 false 권장.")]
        [SerializeField] private bool _spawnOnStart = false;

        [Tooltip("SpawnOnStart 사용 시 시작 지연 시간.")]
        [Min(0f)]
        [SerializeField] private float _spawnStartDelay = 0f;

        [Tooltip("새 웨이브 소환 전 기존에 생성된 DummyEnemy를 Destroy.")]
        [SerializeField] private bool _destroyPreviousWaveBeforeSpawn = true;

        [Tooltip("소환 시 랜덤 Z 회전을 적용할지 여부.")]
        [SerializeField] private bool _randomZRotation = false;

        // ══════════════════════════════════════════════════════
        // Inspector — 충돌 회피 옵션
        // ══════════════════════════════════════════════════════

        [Header("── 충돌 회피 옵션 ─────────────────────")]

        [Tooltip("true면 지정 Layer와 겹치는 위치는 피해서 소환을 시도.")]
        [SerializeField] private bool _avoidBlockedPosition = false;

        [Tooltip("소환 위치 검사 반경.")]
        [Min(0.01f)]
        [SerializeField] private float _positionCheckRadius = 0.35f;

        [Tooltip("소환 위치 검사에 사용할 LayerMask. 벽, 장애물, 적 등을 선택 가능.")]
        [SerializeField] private LayerMask _blockedLayerMask;

        [Tooltip("한 마리당 랜덤 위치를 다시 뽑는 최대 시도 횟수.")]
        [Min(1)]
        [SerializeField] private int _maxTryPerEnemy = 30;

        // ══════════════════════════════════════════════════════
        // 내부 상태
        // ══════════════════════════════════════════════════════

        private readonly List<GameObject> _spawnedEnemies = new List<GameObject>();

        // ══════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_spawnRoot == null)
                _spawnRoot = transform;

            if (_player == null)
                TryFindPlayerByTag();
        }

        private void OnEnable()
        {
            BindButtons();
        }

        private void Start()
        {
            if (!_spawnOnStart)
                return;

            if (_spawnStartDelay <= 0f)
                SpawnWave();
            else
                Invoke(nameof(SpawnWave), _spawnStartDelay);
        }

        private void OnDisable()
        {
            UnbindButtons();
        }

        private void OnValidate()
        {
            if (_maxSpawnRadius < _minSpawnRadius)
                _maxSpawnRadius = _minSpawnRadius + 0.1f;
        }

        // ══════════════════════════════════════════════════════
        // UI Button API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// UI Button.OnClick에 직접 연결할 함수.
        /// </summary>
        public void OnClickSpawnWave()
        {
            SpawnWave();
        }

        /// <summary>
        /// UI Button.OnClick에 직접 연결할 함수.
        /// 현재 Spawner가 생성한 DummyEnemy들을 전부 제거한다.
        /// </summary>
        public void OnClickDestroySpawnedEnemies()
        {
            DestroySpawnedEnemies();
        }

        /// <summary>
        /// UI Button.OnClick에 직접 연결할 함수.
        /// null 참조만 정리한다.
        /// </summary>
        public void OnClickCleanupNullReferences()
        {
            CleanupNullReferences();
        }

        private void BindButtons()
        {
            if (_autoBindSpawnButton && _spawnButton != null)
            {
                _spawnButton.onClick.RemoveListener(OnClickSpawnWave);
                _spawnButton.onClick.AddListener(OnClickSpawnWave);
            }

            if (_autoBindDestroyButton && _destroyButton != null)
            {
                _destroyButton.onClick.RemoveListener(OnClickDestroySpawnedEnemies);
                _destroyButton.onClick.AddListener(OnClickDestroySpawnedEnemies);
            }
        }

        private void UnbindButtons()
        {
            if (_spawnButton != null)
                _spawnButton.onClick.RemoveListener(OnClickSpawnWave);

            if (_destroyButton != null)
                _destroyButton.onClick.RemoveListener(OnClickDestroySpawnedEnemies);
        }

        // ══════════════════════════════════════════════════════
        // 외부 API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// DummyEnemy 웨이브를 생성한다.
        /// Inspector ContextMenu로도 실행 가능.
        /// </summary>
        [ContextMenu("Spawn Wave")]
        public void SpawnWave()
        {
            if (_dummyEnemyPrefab == null)
            {
                Debug.LogError("[DummyEnemySimpleSpawner] DummyEnemy Prefab 미연결.");
                return;
            }

            if (_player == null)
                TryFindPlayerByTag();

            if (_player == null)
            {
                Debug.LogError("[DummyEnemySimpleSpawner] Player Transform 미연결. 직접 연결하거나 Player Tag를 설정하세요.");
                return;
            }

            if (_destroyPreviousWaveBeforeSpawn)
                DestroySpawnedEnemies();
            else
                CleanupNullReferences();

            for (int i = 0; i < _spawnCount; i++)
            {
                Vector3 spawnPos = GetValidSpawnPosition();
                Quaternion rot = _randomZRotation
                    ? Quaternion.Euler(0f, 0f, Random.Range(0f, 360f))
                    : Quaternion.identity;

                GameObject enemy = Instantiate(_dummyEnemyPrefab, spawnPos, rot, _spawnRoot);
                enemy.name = $"{_dummyEnemyPrefab.name}_{i + 1:00}";

                _spawnedEnemies.Add(enemy);
            }
        }

        /// <summary>
        /// 현재 Spawner가 생성한 DummyEnemy들을 모두 Destroy한다.
        /// </summary>
        [ContextMenu("Destroy Spawned Enemies")]
        public void DestroySpawnedEnemies()
        {
            for (int i = _spawnedEnemies.Count - 1; i >= 0; i--)
            {
                GameObject enemy = _spawnedEnemies[i];

                if (enemy != null)
                    Destroy(enemy);
            }

            _spawnedEnemies.Clear();
        }

        /// <summary>
        /// 현재 생성 목록에서 Destroy된 null 참조를 정리한다.
        /// </summary>
        [ContextMenu("Cleanup Null References")]
        public void CleanupNullReferences()
        {
            for (int i = _spawnedEnemies.Count - 1; i >= 0; i--)
            {
                if (_spawnedEnemies[i] == null)
                    _spawnedEnemies.RemoveAt(i);
            }
        }

        // ══════════════════════════════════════════════════════
        // 내부 로직
        // ══════════════════════════════════════════════════════

        private void TryFindPlayerByTag()
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");

            if (playerObj != null)
                _player = playerObj.transform;
        }

        private Vector3 GetValidSpawnPosition()
        {
            Vector3 fallback = GetRandomSpawnPosition();

            if (!_avoidBlockedPosition)
                return fallback;

            for (int tryIndex = 0; tryIndex < _maxTryPerEnemy; tryIndex++)
            {
                Vector3 candidate = GetRandomSpawnPosition();

                Collider2D hit = Physics2D.OverlapCircle(candidate, _positionCheckRadius, _blockedLayerMask);
                if (hit == null)
                    return candidate;
            }

            // 모든 시도가 실패하면 마지막 fallback 위치 사용.
            return fallback;
        }

        private Vector3 GetRandomSpawnPosition()
        {
            Vector2 dir = Random.insideUnitCircle.normalized;

            if (dir.sqrMagnitude < 0.001f)
                dir = Vector2.right;

            float dist = Random.Range(_minSpawnRadius, _maxSpawnRadius);
            Vector2 offset = dir * dist;

            Vector3 basePos = _player.position;
            float z = _usePlayerZ ? basePos.z : _spawnZ;

            return new Vector3(basePos.x + offset.x, basePos.y + offset.y, z);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform center = _player != null ? _player : transform;

            Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
            Gizmos.DrawWireSphere(center.position, _minSpawnRadius);

            Gizmos.color = new Color(1f, 0.6f, 0f, 0.35f);
            Gizmos.DrawWireSphere(center.position, _maxSpawnRadius);
        }
#endif
    }
}
