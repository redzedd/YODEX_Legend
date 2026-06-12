namespace Player.Locomotion.States
{
    /// <summary>
    /// 死亡狀態(Locomotion 內建)— TopState 切換到 Dead 時由 Controller 主動 ForceChangeState 進入。
    /// 單向狀態:Enter 播動畫後不自動轉出,需等外部(復活 API / 重生流程)再 ForceChangeState 離開。
    /// 每幀強制 CurrentRotationSpeed = 0,避免 Controller.ApplyScriptedRotation 在守衛生效前的極短窗口殘留旋轉。
    /// </summary>
    public sealed class DeathState : ILocomotionState
    {
        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            context.UseRootMotionRotation = false;
            context.CurrentRotationSpeed = 0f;
            PlayerDeathData data = context.DeathData;
            if (data == null || data.DeathClip == null)
            {
                return;
            }
            context.AnimatorDriver.Play(data.DeathClip, data.DeathEnterFadeDuration);
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = 0f;
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
        }
    }
}
