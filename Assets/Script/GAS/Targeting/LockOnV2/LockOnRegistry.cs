using System.Collections.Generic;

namespace GAS.Targeting.LockOnV2
{
    /// <summary>
    /// 全域可鎖定目標註冊表
    /// LockOnTarget 在 OnEnable / OnDisable 時自動註冊與移除
    /// 後續搜尋系統 (Step 4) 直接遍歷此表，免於每次 Physics.OverlapSphere
    /// </summary>
    public static class LockOnRegistry
    {
        private static readonly HashSet<LockOnTarget> _targets = new();

        public static IReadOnlyCollection<LockOnTarget> All => _targets;

        public static int Count => _targets.Count;

        public static void Register(LockOnTarget target)
        {
            if (target == null) return;
            _targets.Add(target);
        }

        public static void Unregister(LockOnTarget target)
        {
            if (target == null) return;
            _targets.Remove(target);
        }

        public static bool Contains(LockOnTarget target) => target != null && _targets.Contains(target);
    }
}
