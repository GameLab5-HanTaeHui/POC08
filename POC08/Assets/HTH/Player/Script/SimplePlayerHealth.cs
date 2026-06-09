// ============================================================
// SimplePlayerHealth.cs
// 간이 플레이어 체력 컴포넌트 + Slider UI 연동
//
// [v1.2 변경]
//   - Slider UI 연결 추가.
//   - 선택적으로 Fill Image.fillAmount 직접 제어 가능.
//   - TakeDamage / Heal / ResetHealth / Die 시 UI 자동 갱신.
//
// [v1.1 유지]
//   - TakeDamage(float, Vector2) 오버로드.
//   - 실제 피해가 적용된 경우 HitFeedbackController.PlayPlayerHit(hitPoint) 호출.
//
// [부착 위치]
//   Player 루트 오브젝트 권장.
//
// [namespace] SEAL
// ============================================================

using System;
using UnityEngine;
using UnityEngine.UI;

namespace SEAL
{
    /// <summary>
    /// 테스트용 간이 플레이어 체력 컴포넌트.
    /// DummyEnemy AttackHitBox / HitFeedbackController / 간이 HP UI 연결용.
    /// </summary>
    public class SimplePlayerHealth : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        // Inspector — 체력
        // ══════════════════════════════════════════════════════

        [Header("── 체력 ─────────────────────")]
        [SerializeField, Min(1f)] private float _maxHealth = 100f;
        [SerializeField] private bool _resetHealthOnEnable = true;

        // ══════════════════════════════════════════════════════
        // Inspector — UI
        // ══════════════════════════════════════════════════════

        [Header("── 간이 HP UI ─────────────────────")]

        [Tooltip("HP 표시용 Slider. 미연결이면 UI 갱신을 건너뜀.")]
        [SerializeField] private Slider _healthSlider;

        [Tooltip("true: Slider min/max/value를 0~MaxHealth 기준으로 사용. false: Slider value를 0~1 비율로 사용.")]
        [SerializeField] private bool _sliderUsesRawHealthValue = true;

        [Tooltip("Slider의 Fill Image를 직접 연결한 경우 fillAmount도 같이 갱신. 선택 사항.")]
        [SerializeField] private Image _healthFillImage;

        [Tooltip("Awake/OnValidate 시 Slider min/max를 자동 세팅.")]
        [SerializeField] private bool _autoSetupSliderRange = true;

        // ══════════════════════════════════════════════════════
        // Inspector — 피격
        // ══════════════════════════════════════════════════════

        [Header("── 피격 무적 ─────────────────────")]
        [SerializeField, Min(0f)] private float _invincibleDuration = 0.25f;

        [Header("── 피격 이펙트 ─────────────────────")]
        [Tooltip("피해가 실제 적용되었을 때 HitFeedbackController.PlayPlayerHit() 호출.")]
        [SerializeField] private bool _playHitFeedback = true;

        [Tooltip("hitPoint를 받지 못했을 때 사용할 위치 오프셋.")]
        [SerializeField] private Vector2 _fallbackHitOffset = Vector2.zero;

        [Header("── 디버그 ─────────────────────")]
        [SerializeField] private bool _logHit = true;

        // ══════════════════════════════════════════════════════
        // Runtime
        // ══════════════════════════════════════════════════════

        private float _currentHealth;
        private float _invincibleTimer;
        private bool _isDead;

        public event Action<float, float> OnHealthChanged;
        public event Action<float> OnDamaged;
        public event Action OnDead;
        public event Action OnRevived;

        public float MaxHealth => _maxHealth;
        public float CurrentHealth => _currentHealth;
        public float HealthPercent => _maxHealth <= 0f ? 0f : Mathf.Clamp01(_currentHealth / _maxHealth);
        public bool IsDead => _isDead;
        public bool IsInvincible => _invincibleTimer > 0f;

        // ══════════════════════════════════════════════════════
        // Unity Lifecycle
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            _currentHealth = Mathf.Clamp(_currentHealth <= 0f ? _maxHealth : _currentHealth, 0f, _maxHealth);
            SetupHealthUI();
            RefreshHealthUI();
        }

        private void OnEnable()
        {
            if (_resetHealthOnEnable)
                ResetHealth();
            else
                RefreshHealthUI();
        }

        private void Update()
        {
            if (_invincibleTimer > 0f)
                _invincibleTimer -= Time.deltaTime;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_maxHealth < 1f)
                _maxHealth = 1f;

            if (!Application.isPlaying)
            {
                SetupHealthUI();

                // 에디터에서 미리보기용.
                if (_currentHealth <= 0f)
                    _currentHealth = _maxHealth;

                RefreshHealthUI();
            }
        }
#endif

        // ══════════════════════════════════════════════════════
        // Damage / Heal
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 기존 호환용. Hit 위치를 받지 못한 경우 플레이어 위치에서 이펙트를 재생한다.
        /// </summary>
        public bool TakeDamage(float damage)
        {
            Vector2 fallbackPoint = (Vector2)transform.position + _fallbackHitOffset;
            return TakeDamage(damage, fallbackPoint);
        }

        /// <summary>
        /// 피해 + 피격 위치 기반 이펙트 재생.
        /// </summary>
        public bool TakeDamage(float damage, Vector2 hitWorldPosition)
        {
            if (_isDead) return false;
            if (damage <= 0f) return false;
            if (_invincibleTimer > 0f) return false;

            _currentHealth = Mathf.Max(0f, _currentHealth - damage);
            _invincibleTimer = _invincibleDuration;

            if (_playHitFeedback && HitFeedbackController.Instance != null)
                HitFeedbackController.Instance.PlayPlayerHit(hitWorldPosition);

            RefreshHealthUI();

            OnDamaged?.Invoke(damage);
            OnHealthChanged?.Invoke(_currentHealth, _maxHealth);

            if (_logHit)
                Debug.Log($"[SimplePlayerHealth] 피격: -{damage:F1} | HP {_currentHealth:F1}/{_maxHealth:F1} | Pos:{hitWorldPosition}");

            if (_currentHealth <= 0f)
                Die();

            return true;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f) return;
            if (_isDead) return;

            _currentHealth = Mathf.Min(_maxHealth, _currentHealth + amount);

            RefreshHealthUI();
            OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
        }

        public void ResetHealth()
        {
            _isDead = false;
            _invincibleTimer = 0f;
            _currentHealth = _maxHealth;

            RefreshHealthUI();
            OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
            OnRevived?.Invoke();
        }

        public void SetMaxHealth(float newMaxHealth, bool refill = true)
        {
            _maxHealth = Mathf.Max(1f, newMaxHealth);

            if (refill)
                _currentHealth = _maxHealth;
            else
                _currentHealth = Mathf.Clamp(_currentHealth, 0f, _maxHealth);

            SetupHealthUI();
            RefreshHealthUI();
            OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
        }

        private void Die()
        {
            if (_isDead) return;

            _isDead = true;
            _currentHealth = 0f;

            RefreshHealthUI();
            OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
            OnDead?.Invoke();

            if (_logHit)
                Debug.Log("[SimplePlayerHealth] 사망");
        }

        // ══════════════════════════════════════════════════════
        // UI
        // ══════════════════════════════════════════════════════

        private void SetupHealthUI()
        {
            if (_healthSlider == null)
                return;

            if (!_autoSetupSliderRange)
                return;

            if (_sliderUsesRawHealthValue)
            {
                _healthSlider.minValue = 0f;
                _healthSlider.maxValue = _maxHealth;
            }
            else
            {
                _healthSlider.minValue = 0f;
                _healthSlider.maxValue = 1f;
            }

            _healthSlider.wholeNumbers = false;
            _healthSlider.interactable = false;
        }

        private void RefreshHealthUI()
        {
            float percent = HealthPercent;

            if (_healthSlider != null)
            {
                if (_sliderUsesRawHealthValue)
                {
                    if (_autoSetupSliderRange)
                    {
                        _healthSlider.minValue = 0f;
                        _healthSlider.maxValue = _maxHealth;
                    }

                    _healthSlider.value = _currentHealth;
                }
                else
                {
                    if (_autoSetupSliderRange)
                    {
                        _healthSlider.minValue = 0f;
                        _healthSlider.maxValue = 1f;
                    }

                    _healthSlider.value = percent;
                }
            }

            if (_healthFillImage != null)
                _healthFillImage.fillAmount = percent;
        }
    }
}
