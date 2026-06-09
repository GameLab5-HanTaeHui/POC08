// ============================================================
// DummySealStoneDropRuntime.cs
// DummyEnemy가 비활성화되어도 봉인석 생성 코루틴이 유지되도록 하는 Runtime Runner
// ============================================================

using System.Collections;
using UnityEngine;

namespace SEAL
{
    public sealed class DummySealStoneDropRuntime : MonoBehaviour
    {
        private static DummySealStoneDropRuntime _instance;

        public static void Run(IEnumerator routine)
        {
            if (routine == null) return;
            EnsureInstance().StartCoroutine(routine);
        }

        private static DummySealStoneDropRuntime EnsureInstance()
        {
            if (_instance != null) return _instance;

            GameObject go = new GameObject("DummySealStoneDropRuntime");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DummySealStoneDropRuntime>();
            return _instance;
        }
    }
}
