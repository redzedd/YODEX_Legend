namespace Boss
{
    /// <summary>
    /// Boss 狀態機 (純 C#)。
    /// 維護當前狀態,提供 ChangeState 切換。Owner 每幀呼叫一次 Tick()。
    /// </summary>
    public class BossStateMachine
    {
        private BossState _current;

        /// <summary>當前狀態 (狀態類別內可用此檢查 FSM 處於哪個狀態)</summary>
        public BossState Current => _current;

        /// <summary>切換到新狀態 — 觸發舊狀態 OnExit → 新狀態 OnEnter。傳入相同狀態無作用</summary>
        public void ChangeState(BossState newState)
        {
            if (_current == newState) return;
            _current?.OnExit();
            _current = newState;
            _current?.OnEnter();
        }

        /// <summary>由 Owner 的 Update 呼叫,驅動當前狀態的 OnUpdate</summary>
        public void Tick()
        {
            _current?.OnUpdate();
        }
    }
}
