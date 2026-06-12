namespace Player.Locomotion
{
    /// <summary>
    /// 移動狀態機。負責狀態切換與每幀 Tick，狀態本身內部決定是否呼叫 ChangeState。
    /// </summary>
    public sealed class LocomotionStateMachine
    {
        private readonly LocomotionStateContext _context;
        private ILocomotionState _current;

        public ILocomotionState Current => _current;

        public LocomotionStateMachine(LocomotionStateContext context)
        {
            _context = context;
            _context.StateMachine = this;
        }

        public void Start(ILocomotionState initial)
        {
            _current = initial;
            _current?.Enter(_context, null);
        }

        public void ChangeState(ILocomotionState next)
        {
            if (next == null || next == _current)
            {
                return;
            }
            ILocomotionState previous = _current;
            previous?.Exit(_context, next);
            _current = next;
            _current.Enter(_context, previous);
        }

        /// <summary>
        /// 強制切換狀態 — 即使目標狀態與當前相同,仍會執行 Exit + Enter。
        /// 用於離開 Ability / HitStun 等情境:Locomotion 狀態機本身沒變動,
        /// 但動畫已被外部覆蓋,需要重新觸發 Enter 以重播 Locomotion 動畫。
        /// </summary>
        public void ForceChangeState(ILocomotionState next)
        {
            if (next == null)
            {
                return;
            }
            ILocomotionState previous = _current;
            previous?.Exit(_context, next);
            _current = next;
            _current.Enter(_context, previous);
        }

        public void Tick(float deltaTime)
        {
            _current?.Tick(_context, deltaTime);
        }
    }
}
