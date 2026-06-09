// ============================================================
// DummyEnemyRegistry.cs
// 씬에 존재하는 DummyEnemy 목록 관리
//
// [역할]
//   DummySealExecutionController 가 봉인 가능 적을 찾을 수 있도록
//   DummyEnemy 목록을 정적 Registry 로 관리한다.
//
// [namespace] SEAL
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace SEAL
{
    public static class DummyEnemyRegistry
    {
        private static readonly HashSet<DummyEnemy> Enemies = new HashSet<DummyEnemy>();

        public static void Register(DummyEnemy enemy)
        {
            if (enemy != null)
                Enemies.Add(enemy);
        }

        public static void Unregister(DummyEnemy enemy)
        {
            if (enemy != null)
                Enemies.Remove(enemy);
        }


        /// <summary>현재 등록된 DummyEnemy 스냅샷. 일섬 중 AttackHitBox 일괄 제어 등에 사용.</summary>
        public static List<DummyEnemy> GetAllEnemiesSnapshot()
        {
            return new List<DummyEnemy>(Enemies);
        }

        public static DummyEnemy FindNearestReady(Vector2 origin, float range, HashSet<DummyEnemy> ignore = null)
        {
            DummyEnemy nearest = null;
            float bestSqr = range * range;

            foreach (var enemy in Enemies)
            {
                if (enemy == null) continue;
                if (!enemy.IsReadyToExecute) continue;
                if (ignore != null && ignore.Contains(enemy)) continue;

                float sqr = (enemy.ExecutionPosition - origin).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    nearest = enemy;
                }
            }

            return nearest;
        }

        public static DummyEnemy FindNearestReadyInOwnExecuteRange(
            Vector2 origin,
            float fallbackExecuteRange,
            HashSet<DummyEnemy> ignore = null)
        {
            DummyEnemy nearest = null;
            float bestSqr = float.PositiveInfinity;

            foreach (var enemy in Enemies)
            {
                if (enemy == null) continue;
                if (!enemy.IsReadyToExecute) continue;
                if (ignore != null && ignore.Contains(enemy)) continue;

                float range = enemy.Data != null ? enemy.Data.ExecuteRange : fallbackExecuteRange;
                float sqr = (enemy.ExecutionPosition - origin).sqrMagnitude;

                if (sqr > range * range) continue;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = enemy;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 현재 Ready 상태인 적들로 자동 일섬 연결 고리 미리보기를 만든다.
        /// 중간에 Ready 상태가 풀린 적은 자연스럽게 제외된다.
        /// </summary>
        public static List<DummyEnemy> BuildReadyChainPreview(
            DummyEnemy start,
            float chainRadius,
            int maxCount,
            HashSet<DummyEnemy> ignore = null)
        {
            List<DummyEnemy> result = new List<DummyEnemy>();
            if (start == null || !start.IsReadyToExecute)
                return result;

            HashSet<DummyEnemy> visited = ignore != null
                ? new HashSet<DummyEnemy>(ignore)
                : new HashSet<DummyEnemy>();

            DummyEnemy current = start;
            Vector2 origin = current.ExecutionPosition;

            while (current != null)
            {
                if (!current.IsReadyToExecute)
                    break;

                result.Add(current);
                visited.Add(current);

                if (maxCount > 0 && result.Count >= maxCount)
                    break;

                origin = current.ExecutionPosition;
                current = FindNearestReady(origin, chainRadius, visited);
            }

            return result;
        }
    }
}
