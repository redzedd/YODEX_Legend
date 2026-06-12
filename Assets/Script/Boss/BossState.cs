namespace Boss
{
    /// <summary>
    /// Boss 狀態基類 (純 C#)。
    /// 每個具體狀態 (如 DragonSleepState) 繼承並覆寫需要的生命週期 method。
    /// </summary>
    public abstract class BossState
    {
        /// <summary>進入此狀態時呼叫一次 — 通常用來播動畫、停止移動、設定面向等</summary>
        public virtual void OnEnter() { }

        /// <summary>每幀呼叫 (由 BossStateMachine.Tick 驅動) — 通常用來偵測切換條件、持續更新行為</summary>
        public virtual void OnUpdate() { }

        /// <summary>離開此狀態時呼叫一次 — 通常用來清理 (停止移動、取消轉身等)</summary>
        public virtual void OnExit() { }
    }
}
