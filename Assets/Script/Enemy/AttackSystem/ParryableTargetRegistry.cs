using System;
using System.Collections.Generic;
using UnityEngine;

namespace Enemy.AttackSystem
{
    /// <summary>
    /// 全域可招架目標清單。
    /// 敵人攻擊進入「可招架窗（黃光）」時自動註冊，窗結束、攻擊被取消、敵人銷毀時自動移除。
    /// 玩家側的招架攔截邏輯（換武器時）查詢此清單來決定是否觸發招架支援。
    /// </summary>
    public static class ParryableTargetRegistry
    {
        private static readonly HashSet<EnemyAttackExecutor> _targets = new HashSet<EnemyAttackExecutor>();

        // ────── 對外查詢 ──────
        public static IReadOnlyCollection<EnemyAttackExecutor> Targets => _targets;
        public static int Count => _targets.Count;
        public static bool HasAnyTarget => _targets.Count > 0;

        // ────── 對外事件 ──────
        public static event Action<EnemyAttackExecutor> OnTargetEntered;
        public static event Action<EnemyAttackExecutor> OnTargetExited;

        // ────── 註冊 / 移除 ──────
        public static void Register(EnemyAttackExecutor target)
        {
            if (target == null)
            {
                return;
            }
            if (_targets.Add(target))
            {
                OnTargetEntered?.Invoke(target);
            }
        }

        public static void Unregister(EnemyAttackExecutor target)
        {
            if (target == null)
            {
                return;
            }
            if (_targets.Remove(target))
            {
                OnTargetExited?.Invoke(target);
            }
        }

        // ────── 查詢輔助 ──────
        /// <summary>
        /// 找出距離指定位置最近的可招架目標。沒有目標則回傳 null。
        /// </summary>
        public static EnemyAttackExecutor GetClosest(Vector3 from)
        {
            EnemyAttackExecutor closest = null;
            float closestSqr = float.MaxValue;
            foreach (EnemyAttackExecutor target in _targets)
            {
                if (target == null)
                {
                    continue;
                }
                float sqr = (target.transform.position - from).sqrMagnitude;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    closest = target;
                }
            }
            return closest;
        }

        /// <summary>
        /// 找出距離指定位置最近、且在最大距離內的可招架目標。沒有則回傳 null。
        /// </summary>
        public static EnemyAttackExecutor GetClosestInRange(Vector3 from, float maxDistance)
        {
            EnemyAttackExecutor closest = GetClosest(from);
            if (closest == null)
            {
                return null;
            }
            float maxSqr = maxDistance * maxDistance;
            float sqr = (closest.transform.position - from).sqrMagnitude;
            return sqr <= maxSqr ? closest : null;
        }

        // 進入 Play Mode 時清空靜態狀態，避免 Domain Reload 關閉時殘留上次遊玩的資料
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayMode()
        {
            _targets.Clear();
            OnTargetEntered = null;
            OnTargetExited = null;
        }
    }
}
