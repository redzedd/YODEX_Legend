namespace GAS
{
    /// <summary>
    /// 效果持續時間策略
    /// </summary>
    public enum DurationPolicy
    {
        /// <summary>
        /// 即時效果 - 立即應用並結束 (如傷害、治療)
        /// </summary>
        Instant,

        /// <summary>
        /// 持續效果 - 持續一段時間後自動結束 (如 Buff/Debuff)
        /// </summary>
        Duration,

        /// <summary>
        /// 無限效果 - 除非手動移除，否則永久存在 (如被動效果)
        /// </summary>
        Infinite
    }

    /// <summary>
    /// 效果堆疊策略
    /// </summary>
    public enum StackingPolicy
    {
        /// <summary>
        /// 不堆疊 - 新效果替換舊效果
        /// </summary>
        None,

        /// <summary>
        /// 堆疊層數 - 增加效果層數，強化效果
        /// </summary>
        StackCount,

        /// <summary>
        /// 刷新持續時間 - 重置持續時間
        /// </summary>
        RefreshDuration,

        /// <summary>
        /// 堆疊並刷新 - 同時增加層數和刷新時間
        /// </summary>
        StackAndRefresh
    }

    /// <summary>
    /// 效果週期執行策略
    /// </summary>
    public enum PeriodicPolicy
    {
        /// <summary>
        /// 無週期 - 只在開始/結束時執行
        /// </summary>
        None,

        /// <summary>
        /// 週期執行 - 每隔一段時間執行一次
        /// </summary>
        ExecuteOnInterval,

        /// <summary>
        /// 啟動時執行 - 效果開始時立即執行一次
        /// </summary>
        ExecuteOnStart,

        /// <summary>
        /// 啟動時執行並週期執行
        /// </summary>
        ExecuteOnStartAndInterval
    }
}
