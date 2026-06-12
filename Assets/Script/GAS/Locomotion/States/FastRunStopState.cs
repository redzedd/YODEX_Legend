using Animancer;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 快跑停止過渡狀態：在 FastRunTurn 完成後玩家無輸入時播放 FastRunStop 作為過渡，而非直接跳到 Idle。
    /// 播放期間若輸入回來可無縫切回 Walk/Run/FastRun。
    /// </summary>
    public sealed class FastRunStopState : ILocomotionState, IResumableLocomotionState
    {
        private AnimancerState _currentAnimState;

        public LocomotionAnimSlot CurrentSlot => LocomotionAnimSlot.FastRunStop;
        public float CurrentNormalizedTime => _currentAnimState != null ? _currentAnimState.NormalizedTime : 0f;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            if (context.IsRefreshingFromModelSwitch && TryResumeFromSlot(context))
            {
                return;
            }
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.FastRunStop, context.Config.EndAnimFadeDuration);
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = context.Config.WalkRotationSpeed;
            if (context.HasMoveInput)
            {
                if (context.RunButtonHeld)
                {
                    context.StateMachine.ChangeState(context.FastRun);
                    return;
                }
                if (context.InputMagnitude > context.Config.WalkMagnitudeThreshold)
                {
                    context.StateMachine.ChangeState(context.Run);
                    return;
                }
                context.StateMachine.ChangeState(context.Walk);
                return;
            }
            if (_currentAnimState != null && _currentAnimState.NormalizedTime >= 1f)
            {
                context.StateMachine.ChangeState(context.Idle);
            }
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            _currentAnimState = null;
        }

        private bool TryResumeFromSlot(LocomotionStateContext context)
        {
            if (context.ResumeSlot == LocomotionAnimSlot.FastRunStop)
            {
                _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.FastRunStop, 0f);
                if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                return true;
            }
            return false;
        }
    }
}
