// ============================================================
// SealStoneWallet.cs
// 플레이어 봉인석 재화 보관용 간이 Wallet
// ============================================================

using System;
using UnityEngine;

namespace SEAL
{
    public sealed class SealStoneWallet : MonoBehaviour
    {
        [SerializeField] private int _currentAmount;

        public int CurrentAmount => _currentAmount;

        public event Action<int> OnAmountChanged;

        public void Add(int amount)
        {
            if (amount <= 0) return;

            _currentAmount += amount;
            OnAmountChanged?.Invoke(_currentAmount);
            Debug.Log($"[SealStoneWallet] 봉인석 +{amount} | 현재:{_currentAmount}");
        }

        public bool TrySpend(int amount)
        {
            if (amount <= 0) return true;
            if (_currentAmount < amount) return false;

            _currentAmount -= amount;
            OnAmountChanged?.Invoke(_currentAmount);
            return true;
        }
    }
}
