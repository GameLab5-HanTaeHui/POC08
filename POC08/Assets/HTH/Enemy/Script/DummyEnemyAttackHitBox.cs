// ============================================================
// DummyEnemyAttackHitBox.cs
// DummyEnemy의 플레이어 피격용 AttackHitBox
//
// [v1.1 변경]
//   SimplePlayerHealth.TakeDamage(damage, hitPoint) 호출.
//   플레이어 피격 파티클은 SimplePlayerHealth에서 재생한다.
//
// [부착 위치]
//   DummyEnemy/AttackHitBox 자식 오브젝트.
//
// [namespace] SEAL
// ============================================================

using UnityEngine;

namespace SEAL
{
    [RequireComponent(typeof(Collider2D))]
    public class DummyEnemyAttackHitBox : MonoBehaviour
    {
        [Header("── 소유자 ─────────────────────")]
        [Tooltip("미연결 시 부모 DummyEnemy를 자동 탐색한다.")]
        [SerializeField] private DummyEnemy _owner;

        [Header("── 공격 수치 폴백 ─────────────────────")]
        [Tooltip("Owner/DataSO가 없을 때 사용할 임시 피해량.")]
        [SerializeField, Min(0f)] private float _fallbackDamage = 10f;

        [Tooltip("Owner/DataSO가 없을 때 사용할 임시 피격 쿨타임.")]
        [SerializeField, Min(0f)] private float _fallbackHitCooldown = 0.6f;

        [Header("── 대상 필터 ─────────────────────")]
        [Tooltip("비어 있으면 SimplePlayerHealth 컴포넌트만으로 판정한다. 설정 시 해당 레이어만 피격 처리한다.")]
        [SerializeField] private LayerMask _playerLayer;

        [Tooltip("Trigger가 닿아 있는 동안 쿨타임마다 반복 피해를 줄지 여부.")]
        [SerializeField] private bool _damageOnStay = true;

        [Header("── 디버그 ─────────────────────")]
        [SerializeField] private bool _logHit;

        private Collider2D _collider;
        private float _nextHitAllowedTime;

        private void Awake()
        {
            _collider = GetComponent<Collider2D>();
            _collider.isTrigger = true;

            if (_owner == null)
                _owner = GetComponentInParent<DummyEnemy>();
        }

        private void OnEnable()
        {
            _nextHitAllowedTime = 0f;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryHit(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (!_damageOnStay) return;
            TryHit(other);
        }

        private void TryHit(Collider2D other)
        {
            if (other == null) return;
            if (Time.time < _nextHitAllowedTime) return;
            if (!IsLayerAllowed(other.gameObject.layer)) return;

            SimplePlayerHealth health = other.GetComponentInParent<SimplePlayerHealth>();
            if (health == null) return;

            float damage = GetDamage();
            float cooldown = GetHitCooldown();
            Vector2 hitPoint = GetHitPoint(other);

            bool applied = health.TakeDamage(damage, hitPoint);
            if (!applied) return;

            _nextHitAllowedTime = Time.time + cooldown;

            if (_logHit)
                Debug.Log($"[DummyEnemyAttackHitBox] {name} → {health.name} 피격 | Damage:{damage:F1} | Pos:{hitPoint}");
        }

        private Vector2 GetHitPoint(Collider2D playerCollider)
        {
            if (_collider == null)
                return playerCollider.bounds.center;

            Vector2 attackCenter = _collider.bounds.center;
            Vector2 closest = playerCollider.ClosestPoint(attackCenter);

            // ClosestPoint가 내부/동일 위치로 애매하게 반환될 때 폴백.
            if ((closest - attackCenter).sqrMagnitude < 0.0001f)
                closest = playerCollider.bounds.center;

            return closest;
        }

        private bool IsLayerAllowed(int layer)
        {
            if (_playerLayer.value == 0)
                return true;

            return (_playerLayer.value & (1 << layer)) != 0;
        }

        private float GetDamage()
        {
            if (_owner != null && _owner.Data != null)
                return _owner.Data.AttackDamage;

            return _fallbackDamage;
        }

        private float GetHitCooldown()
        {
            if (_owner != null && _owner.Data != null)
                return _owner.Data.AttackHitCooldown;

            return _fallbackHitCooldown;
        }
    }
}
