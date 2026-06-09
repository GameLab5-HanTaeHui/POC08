// ============================================================
// DummyEnemy.cs
// 추적 + 플레이어 공격 피격 + 봉인도 누적 + Ready 타임아웃 부활
//
// [역할]
//   - 플레이어를 항상 추적한다.
//   - PlayerAttackHitboxManager.OnHit 을 구독한다.
//   - 자신의 HurtBox Collider 가 맞으면 봉인도를 누적한다.
//   - 봉인도 100% 도달 시 봉인 집행 가능 상태가 된다.
//   - 일정 시간 안에 집행되지 않으면 부활/복귀한다.
//   - 집행 자체는 DummySealExecutionController 가 호출한다.
//
// [namespace] SEAL
// ============================================================

using System;
using System.Collections;
using UnityEngine;

namespace SEAL
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class DummyEnemy : MonoBehaviour
    {
        public enum DummyEnemyState
        {
            Alive,
            ReadyToExecute,
            Sealed,
        }

        [Header("── DataSO ─────────────────────")]
        [SerializeField] private DummyEnemyDataSO _data;

        [Header("── 컴포넌트 연결 ─────────────────────")]
        [Tooltip("플레이어 공격에 맞는 HurtBox Collider2D. Layer는 EnemyAttackHitBox 권장.")]
        [SerializeField] private Collider2D _hurtBox;

        [Tooltip("플레이어에게 피해를 줄 Enemy AttackHitBox. PlayerHit 구현은 추후 추가. isTrigger=true 권장.")]
        [SerializeField] private Collider2D[] _attackHitBoxes;

        [Tooltip("_attackHitBoxes가 비어 있으면 자식 AttackHitBox 오브젝트에서 Collider2D를 자동 수집한다.")]
        [SerializeField] private bool _autoFindAttackHitBoxChild = true;

        [Tooltip("Awake/OnValidate에서 AttackHitBox Collider의 isTrigger를 자동으로 true로 맞춘다.")]
        [SerializeField] private bool _forceAttackHitBoxTrigger = true;

        [Tooltip("일섬/봉인 집행 중 외부 컨트롤러가 공격 판정을 끈 상태인지 디버그 확인용.")]
        [SerializeField, HideInInspector] private bool _attackHitBoxSuppressed;

        [Tooltip("일섬 도착 지점. null이면 자신의 Transform 위치를 사용한다.")]
        [SerializeField] private Transform _executionPoint;

        [Tooltip("미연결 시 PlayerMoveController를 자동 탐색한다.")]
        [SerializeField] private Transform _target;

        [SerializeField] private SpriteRenderer _spriteRenderer;

        private Rigidbody2D _rigid2D;
        private PlayerAttackHitboxManager _playerHitboxManager;
        private Coroutine _readyTimeoutRoutine;
        private Coroutine _hitFlashRoutine;

        private DummyEnemyState _state = DummyEnemyState.Alive;
        private float _currentSealGauge;
        private float _runtimeMoveSpeed;
        private float _runtimeMaxSealGauge;
        private bool _runtimeStatsInitialized;
        private bool[] _attackHitBoxDefaultEnabledStates;

        public event Action<DummyEnemy, float, float> OnSealGaugeChanged;
        public event Action<DummyEnemy> OnReadyToExecute;
        public event Action<DummyEnemy> OnSealed;
        public event Action<DummyEnemy> OnRevived;

        public DummyEnemyDataSO Data => _data;
        public DummyEnemyState State => _state;
        public bool IsAlive => _state == DummyEnemyState.Alive;
        public bool IsReadyToExecute => _state == DummyEnemyState.ReadyToExecute;
        public bool IsSealed => _state == DummyEnemyState.Sealed;
        public float CurrentSealGauge => _currentSealGauge;
        public float MaxSealGauge => _runtimeStatsInitialized ? _runtimeMaxSealGauge : (_data != null ? _data.MaxSealGauge : 100f);
        public float MaxMoveSpeed => _runtimeStatsInitialized ? _runtimeMoveSpeed : (_data != null ? _data.MoveSpeed : 2.8f);
        public float SealPercent => MaxSealGauge <= 0f ? 0f : Mathf.Clamp01(_currentSealGauge / MaxSealGauge);
        public Vector2 ExecutionPosition => _executionPoint != null ? (Vector2)_executionPoint.position : (Vector2)transform.position;

        private void Awake()
        {
            _rigid2D = GetComponent<Rigidbody2D>();

            if (_hurtBox == null)
                _hurtBox = GetComponent<Collider2D>();

            AutoFindAttackHitBoxesIfNeeded();
            ForceAttackHitBoxesTriggerIfNeeded();
            CacheAttackHitBoxDefaultStates();

            if (_spriteRenderer == null)
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            if (_data == null)
                Debug.LogError($"[DummyEnemy] {name} — DummyEnemyDataSO 미연결.");
            else
                RollRuntimeStats();

            if (_hurtBox == null)
                Debug.LogError($"[DummyEnemy] {name} — HurtBox Collider2D 미연결.");
        }

        private void OnEnable()
        {
            if (_data != null && !_runtimeStatsInitialized)
                RollRuntimeStats();

            DummyEnemyRegistry.Register(this);
            ApplyAttackHitBoxState();
        }

        private void Start()
        {
            CacheTarget();
            SubscribePlayerHitbox();
            ApplyStateVisual();
        }

        private void OnDisable()
        {
            DummyEnemyRegistry.Unregister(this);
            UnsubscribePlayerHitbox();
        }

        private void OnDestroy()
        {
            DummyEnemyRegistry.Unregister(this);
            UnsubscribePlayerHitbox();
        }

        private void FixedUpdate()
        {
            if (_data == null) return;
            if (_rigid2D == null) return;

            if (_state != DummyEnemyState.Alive)
            {
                _rigid2D.linearVelocity = Vector2.zero;
                return;
            }

            if (_target == null)
                CacheTarget();

            if (_target == null)
            {
                _rigid2D.linearVelocity = Vector2.zero;
                return;
            }

            Vector2 current = _rigid2D.position;
            Vector2 target = _target.position;
            Vector2 toTarget = target - current;

            if (toTarget.magnitude <= _data.StopDistance)
            {
                _rigid2D.linearVelocity = Vector2.zero;
                return;
            }

            _rigid2D.linearVelocity = toTarget.normalized * MaxMoveSpeed;
        }


        private void RollRuntimeStats()
        {
            if (_data == null)
            {
                _runtimeMoveSpeed = 2.8f;
                _runtimeMaxSealGauge = 100f;
                _runtimeStatsInitialized = true;
                return;
            }

            _runtimeMoveSpeed = _data.GetRuntimeMoveSpeed();
            _runtimeMaxSealGauge = _data.GetRuntimeMaxSealGauge();
            _runtimeStatsInitialized = true;

            Debug.Log($"[DummyEnemy] {name} 런타임 수치 결정 | MoveSpeed:{_runtimeMoveSpeed:F2}, MaxSealGauge:{_runtimeMaxSealGauge:F1}");
        }

        private void CacheTarget()
        {
            if (_target != null) return;

            var players = FindObjectsByType<PlayerMoveController>(FindObjectsSortMode.None);
            if (players.Length > 0)
                _target = players[0].transform;
        }

        private void SubscribePlayerHitbox()
        {
            var managers = FindObjectsByType<PlayerAttackHitboxManager>(FindObjectsSortMode.None);
            if (managers.Length <= 0)
            {
                Debug.LogWarning($"[DummyEnemy] {name} — PlayerAttackHitboxManager 없음.");
                return;
            }

            _playerHitboxManager = managers[0];
            _playerHitboxManager.OnHit -= HandlePlayerHit;
            _playerHitboxManager.OnHit += HandlePlayerHit;
        }

        private void UnsubscribePlayerHitbox()
        {
            if (_playerHitboxManager == null) return;
            _playerHitboxManager.OnHit -= HandlePlayerHit;
            _playerHitboxManager = null;
        }

        private void HandlePlayerHit(Collider2D hitCol, float sealAmount)
        {
            if (_state != DummyEnemyState.Alive) return;
            if (_hurtBox == null) return;
            if (hitCol != _hurtBox) return;

            AddSealGauge(sealAmount);
            PlayHitFlash();
        }

        public void AddSealGauge(float amount)
        {
            if (_data == null) return;
            if (_state != DummyEnemyState.Alive) return;
            if (amount <= 0f) return;

            _currentSealGauge = Mathf.Clamp(_currentSealGauge + amount, 0f, MaxSealGauge);
            OnSealGaugeChanged?.Invoke(this, _currentSealGauge, MaxSealGauge);

            Debug.Log($"[DummyEnemy] {name} 봉인도 {_currentSealGauge:F1}/{MaxSealGauge:F1}");

            if (_currentSealGauge >= MaxSealGauge)
                EnterReadyToExecute();
        }

        private void EnterReadyToExecute()
        {
            if (_state != DummyEnemyState.Alive) return;

            _state = DummyEnemyState.ReadyToExecute;
            _rigid2D.linearVelocity = Vector2.zero;
            ApplyStateVisual();
            ApplyAttackHitBoxState();

            OnReadyToExecute?.Invoke(this);

            if (_readyTimeoutRoutine != null)
                StopCoroutine(_readyTimeoutRoutine);
            _readyTimeoutRoutine = StartCoroutine(ReadyTimeoutRoutine());

            Debug.Log($"[DummyEnemy] {name} 봉인 집행 가능 상태 진입");
        }

        private IEnumerator ReadyTimeoutRoutine()
        {
            float duration = _data != null ? _data.ReadyReviveTime : 4f;
            yield return new WaitForSeconds(duration);

            _readyTimeoutRoutine = null;

            if (_state == DummyEnemyState.ReadyToExecute)
                ReviveFromReady();
        }

        public void ExecuteSeal()
        {
            if (_state != DummyEnemyState.ReadyToExecute) return;

            if (_readyTimeoutRoutine != null)
            {
                StopCoroutine(_readyTimeoutRoutine);
                _readyTimeoutRoutine = null;
            }

            _state = DummyEnemyState.Sealed;
            _currentSealGauge = MaxSealGauge;
            _rigid2D.linearVelocity = Vector2.zero;

            if (_hurtBox != null)
                _hurtBox.enabled = false;

            ApplyStateVisual();
            ApplyAttackHitBoxState();
            OnSealed?.Invoke(this);

            Debug.Log($"[DummyEnemy] {name} 봉인 집행 완료");

            if (_data != null && _data.DeactivateOnSealed)
                StartCoroutine(DeactivateRoutine());
        }

        private IEnumerator DeactivateRoutine()
        {
            float delay = _data != null ? _data.DeactivateDelay : 0f;
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            gameObject.SetActive(false);
        }

        public void ReviveFromReady()
        {
            if (_state == DummyEnemyState.Sealed) return;

            _state = DummyEnemyState.Alive;
            _currentSealGauge = 0f;

            if (_hurtBox != null)
                _hurtBox.enabled = true;

            ApplyStateVisual();
            ApplyAttackHitBoxState();
            OnSealGaugeChanged?.Invoke(this, _currentSealGauge, MaxSealGauge);
            OnRevived?.Invoke(this);

            Debug.Log($"[DummyEnemy] {name} 집행 시간 초과 → 부활");
        }

        public void ForceResetEnemy()
        {
            if (_readyTimeoutRoutine != null)
            {
                StopCoroutine(_readyTimeoutRoutine);
                _readyTimeoutRoutine = null;
            }

            _state = DummyEnemyState.Alive;
            _currentSealGauge = 0f;

            if (_hurtBox != null)
                _hurtBox.enabled = true;

            ApplyAttackHitBoxState();

            if (_rigid2D != null)
                _rigid2D.linearVelocity = Vector2.zero;

            ApplyStateVisual();
            OnSealGaugeChanged?.Invoke(this, _currentSealGauge, MaxSealGauge);
        }


        // ══════════════════════════════════════════════════════
        // Enemy AttackHitBox 제어
        // ══════════════════════════════════════════════════════

        private void AutoFindAttackHitBoxesIfNeeded()
        {
            if (!_autoFindAttackHitBoxChild) return;
            if (_attackHitBoxes != null && _attackHitBoxes.Length > 0) return;

            Transform attackHitBoxRoot = transform.Find("AttackHitBox");
            if (attackHitBoxRoot == null) return;

            _attackHitBoxes = attackHitBoxRoot.GetComponentsInChildren<Collider2D>(true);
        }

        private void ForceAttackHitBoxesTriggerIfNeeded()
        {
            if (!_forceAttackHitBoxTrigger) return;
            if (_attackHitBoxes == null) return;

            for (int i = 0; i < _attackHitBoxes.Length; i++)
            {
                if (_attackHitBoxes[i] == null) continue;
                _attackHitBoxes[i].isTrigger = true;
            }
        }

        private void CacheAttackHitBoxDefaultStates()
        {
            if (_attackHitBoxes == null)
            {
                _attackHitBoxDefaultEnabledStates = Array.Empty<bool>();
                return;
            }

            _attackHitBoxDefaultEnabledStates = new bool[_attackHitBoxes.Length];
            for (int i = 0; i < _attackHitBoxes.Length; i++)
                _attackHitBoxDefaultEnabledStates[i] = _attackHitBoxes[i] != null && _attackHitBoxes[i].enabled;
        }

        /// <summary>
        /// 일섬/봉인 집행 중 적 공격 판정을 임시로 끄기 위한 외부 API.
        /// true  = AttackHitBox 비활성화
        /// false = 현재 상태(Alive/Ready/Sealed)에 맞게 복구
        /// </summary>
        public void SetAttackHitBoxSuppressed(bool suppressed)
        {
            _attackHitBoxSuppressed = suppressed;
            ApplyAttackHitBoxState();
        }

        /// <summary>
        /// 현재 상태와 외부 Suppress 상태에 맞게 AttackHitBox를 켜거나 끈다.
        /// HurtBox와는 별개이며, 플레이어 공격 피격 판정은 HurtBox가 담당한다.
        /// </summary>
        private void ApplyAttackHitBoxState()
        {
            if (_attackHitBoxes == null || _attackHitBoxes.Length <= 0)
                return;

            bool shouldEnableByState = true;

            if (_state == DummyEnemyState.ReadyToExecute && _data != null && _data.DisableAttackHitBoxWhenReady)
                shouldEnableByState = false;

            if (_state == DummyEnemyState.Sealed && (_data == null || _data.DisableAttackHitBoxWhenSealed))
                shouldEnableByState = false;

            if (_attackHitBoxSuppressed)
                shouldEnableByState = false;

            for (int i = 0; i < _attackHitBoxes.Length; i++)
            {
                Collider2D attackHitBox = _attackHitBoxes[i];
                if (attackHitBox == null) continue;

                bool defaultEnabled = true;
                if (_attackHitBoxDefaultEnabledStates != null && i < _attackHitBoxDefaultEnabledStates.Length)
                    defaultEnabled = _attackHitBoxDefaultEnabledStates[i];

                attackHitBox.enabled = defaultEnabled && shouldEnableByState;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            AutoFindAttackHitBoxesIfNeeded();
            ForceAttackHitBoxesTriggerIfNeeded();
        }
#endif

        private void ApplyStateVisual()
        {
            if (_spriteRenderer == null || _data == null) return;

            switch (_state)
            {
                case DummyEnemyState.Alive:
                    _spriteRenderer.color = _data.AliveColor;
                    break;
                case DummyEnemyState.ReadyToExecute:
                    _spriteRenderer.color = _data.ReadyColor;
                    break;
                case DummyEnemyState.Sealed:
                    _spriteRenderer.color = _data.SealedColor;
                    break;
            }
        }

        private void PlayHitFlash()
        {
            if (_spriteRenderer == null || _data == null) return;

            if (_hitFlashRoutine != null)
                StopCoroutine(_hitFlashRoutine);
            _hitFlashRoutine = StartCoroutine(HitFlashRoutine());
        }

        private IEnumerator HitFlashRoutine()
        {
            _spriteRenderer.color = _data.HitFlashColor;
            yield return new WaitForSeconds(0.06f);
            ApplyStateVisual();
            _hitFlashRoutine = null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_data == null) return;

            Gizmos.color = new Color(0.8f, 0.25f, 1f, 0.25f);
            Gizmos.DrawWireSphere(ExecutionPosition, _data.ExecuteRange);

            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.18f);
            Gizmos.DrawWireSphere(ExecutionPosition, _data.ChainRadius);
        }
#endif
    }
}
