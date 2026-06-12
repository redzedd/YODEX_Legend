using Animancer;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 快跑高速迴轉：依 context.TurnDirection 選擇 FastRunTurnLeft / FastRunTurnRight clip。
    /// 由 RootMotion 控制旋轉，期間 scripted rotation 被禁用。
    /// 必須等 NormalizedTime >= 1 動畫完整播完才切出，以確保旋轉量完整應用（若提前淡出會使 deltaRotation 被 crossfade 權重削弱造成校正抽動）。
    /// 完成後若無移動輸入則切到 FastRunStopState 而非直接回 Idle。
    /// </summary>
    public sealed class FastRunTurnState : ILocomotionState, IResumableLocomotionState
    {
        private enum TurnSide { Left, Right }
        private AnimancerState _currentAnimState;
        private TurnSide _turnSide;

        public LocomotionAnimSlot CurrentSlot => _turnSide == TurnSide.Right
            ? LocomotionAnimSlot.FastRunTurnRight
            : LocomotionAnimSlot.FastRunTurnLeft;
        public float CurrentNormalizedTime => _currentAnimState != null ? _currentAnimState.NormalizedTime : 0f;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            context.UseRootMotionRotation = true;
            if (context.IsRefreshingFromModelSwitch && TryResumeFromSlot(context))
            {
                return;
            }
            ClipTransition clip;
            if (context.TurnDirection >= 0)
            {
                _turnSide = TurnSide.Right;
                clip = context.AnimationSet.FastRunTurnRight;
            }
            else
            {
                _turnSide = TurnSide.Left;
                clip = context.AnimationSet.FastRunTurnLeft;
            }
            _currentAnimState = context.AnimatorDriver.Play(clip, context.Config.FastRunTurnFadeDuration);
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = 0f;
            if (_currentAnimState == null || _currentAnimState.NormalizedTime < 1f)
            {
                return;
            }
            if (context.RunButtonHeld && context.HasMoveInput)
            {
                context.StateMachine.ChangeState(context.FastRun);
                return;
            }
            if (context.HasMoveInput)
            {
                if (context.InputMagnitude > context.Config.WalkMagnitudeThreshold)
                {
                    context.StateMachine.ChangeState(context.Run);
                    return;
                }
                context.StateMachine.ChangeState(context.Walk);
                return;
            }
            context.StateMachine.ChangeState(context.FastRunStop);
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            context.UseRootMotionRotation = false;
            _currentAnimState = null;
        }

        private bool TryResumeFromSlot(LocomotionStateContext context)
        {
            switch (context.ResumeSlot)
            {
                case LocomotionAnimSlot.FastRunTurnLeft:
                    _turnSide = TurnSide.Left;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.FastRunTurnLeft, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
                case LocomotionAnimSlot.FastRunTurnRight:
                    _turnSide = TurnSide.Right;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.FastRunTurnRight, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
            }
            return false;
        }
    }
}
