namespace Player.Locomotion
{
    /// <summary>
    /// 所有移動狀態的共同介面。生命週期由 LocomotionStateMachine 驅動。
    /// </summary>
    public interface ILocomotionState
    {
        void Enter(LocomotionStateContext context, ILocomotionState previous);
        void Tick(LocomotionStateContext context, float deltaTime);
        void Exit(LocomotionStateContext context, ILocomotionState next);
    }
}
