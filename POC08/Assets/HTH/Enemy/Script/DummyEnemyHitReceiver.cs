// DummyEnemyHitReceiver.cs
// 허수아비 피격 → SealableComponent.AddGauge() 연결

using UnityEngine;
namespace SEAL
{
    public class DummyEnemyHitReceiver : MonoBehaviour
    {
        [SerializeField] private Collider2D _ownCollider;
        [SerializeField] private SealableComponent _sealable;

        private PlayerAttackHitboxManager _hitboxManager;

        private void Start()
        {
            var managers = FindObjectsByType<PlayerAttackHitboxManager>(FindObjectsSortMode.None);
            if (managers.Length > 0)
            {
                _hitboxManager = managers[0];
                _hitboxManager.OnHit += HandleHit;
            }
        }

        private void OnDestroy()
        {
            if (_hitboxManager != null)
                _hitboxManager.OnHit -= HandleHit;
        }

        private void HandleHit(Collider2D hitCol, float sealAmount)
        {
            if (hitCol != _ownCollider) return;
            _sealable?.AddGauge(sealAmount);
            Debug.Log($"[DummyEnemy] 피격 | sealAmount:{sealAmount} | gauge:{_sealable?.UIPercent:F1}%");
        }
    }
}