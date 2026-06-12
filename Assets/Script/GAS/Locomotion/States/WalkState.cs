using Animancer;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 走路狀態：依子階段播放 WalkStart → WalkLoop → WalkEnd。
    /// 從 Run/FastRun 轉入時跳過 Start 直接進 Loop；輸入消失（經去抖動後）進 End，End 途中若輸入回來可直接復原為 Loop。
    /// 階段推進以 AnimancerState.NormalizedTime 輪詢，不依賴 OnEnd 事件。
    /// </summary>
    public sealed class WalkState : ILocomotionState, IResumableLocomotionState
    {
        private enum Phase { Start, Loop, End }
        private Phase _phase;
        private AnimancerState _currentAnimState;

        public LocomotionAnimSlot CurrentSlot => _phase switch
        {
            Phase.Start => LocomotionAnimSlot.WalkStart,
            Phase.Loop => LocomotionAnimSlot.WalkLoop,
            Phase.End => LocomotionAnimSlot.WalkEnd,
            _ => LocomotionAnimSlot.None,
        };
        public float CurrentNormalizedTime => _currentAnimState != null ? _currentAnimState.NormalizedTime : 0f;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            if (context.IsRefreshingFromModelSwitch)
            {
                if (TryResumeFromSlot(context))
                {
                    return;
                }
                EnterLoopPhase(context, context.Config.StartToLoopFadeDuration);
                return;
            }
            if (previous == context.Jump)
            {
                EnterLoopPhase(context, context.Config.JumpLandingToMoveFadeDuration);
                return;
            }
            if (previous == context.Dodge)
            {
                EnterLoopPhase(context, context.Config.DodgeToMoveFadeDuration);
                return;
            }
            // 前一個狀態剛在 End 收尾(WalkEnd / RunEnd / FastRunEnd)— 重新起步一律走 Start,不跳接 Loop
            if (previous is IResumableLocomotionState prevResumable && prevResumable.CurrentSlot.IsEndSlot())
            {
                EnterStartPhase(context, context.Config.EndAnimFadeDuration);
                return;
            }
            if (previous == context.Run || previous == context.FastRun || previous == context.FastRunTurn || previous == context.FastRunStop)
            {
                EnterLoopPhase(context, context.Config.WalkToRunFadeDuration);
                return;
            }
            EnterStartPhase(context, context.Config.StartAnimFadeDuration);
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = _phase == Phase.Start
                ? context.Config.IdleRotationSpeed
                : context.Config.WalkRotationSpeed;
            switch (_phase)
            {
                case Phase.Start:
                    TickStartPhase(context);
                    break;
                case Phase.Loop:
                    TickLoopPhase(context);
                    break;
                case Phase.End:
                    TickEndPhase(context);
                    break;
            }
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            _currentAnimState = null;
        }

        private void TickStartPhase(LocomotionStateContext context)
        {
            if (context.NoInputTime > context.Config.InputReleaseDebounce)
            {
                // 放開移動鍵 — 即使仍在 Start 階段,也走 WalkEnd 收尾,不要直接跳 Idle
                EnterEndPhase(context);
                return;
            }
            if (context.RunButtonHeld && context.HasMoveInput && context.CanStartSprint)
            {
                context.StateMachine.ChangeState(context.FastRun);
                return;
            }
            if (context.InputMagnitude > context.Config.WalkMagnitudeThreshold)
            {
                context.StateMachine.ChangeState(context.Run);
                return;
            }
            if (LocomotionAnimatorDriver.IsReadyForExitFade(_currentAnimState, context.Config.StartToLoopFadeDuration))
            {
                EnterLoopPhase(context, context.Config.StartToLoopFadeDuration);
            }
        }

        private void TickLoopPhase(LocomotionStateContext context)
        {
            if (context.NoInputTime > context.Config.InputReleaseDebounce)
            {
                EnterEndPhase(context);
                return;
            }
            if (!context.HasMoveInput)
            {
                return;
            }
            if (context.RunButtonHeld && context.CanStartSprint)
            {
                context.StateMachine.ChangeState(context.FastRun);
                return;
            }
            if (context.InputMagnitude > context.Config.WalkMagnitudeThreshold)
            {
                context.StateMachine.ChangeState(context.Run);
            }
        }

        private void TickEndPhase(LocomotionStateContext context)
        {
            if (context.HasMoveInput)
            {
                if (context.RunButtonHeld && context.CanStartSprint)
                {
                    context.StateMachine.ChangeState(context.FastRun);
                    return;
                }
                if (context.InputMagnitude > context.Config.WalkMagnitudeThreshold)
                {
                    context.StateMachine.ChangeState(context.Run);
                    return;
                }
                // End 中途輸入回來 — 重新起步一律走 WalkStart,不跳接 Loop
                EnterStartPhase(context, context.Config.EndAnimFadeDuration);
                return;
            }
            if (_currentAnimState != null && _currentAnimState.NormalizedTime >= 1f)
            {
                context.StateMachine.ChangeState(context.Idle);
            }
        }

        private void EnterStartPhase(LocomotionStateContext context, float fadeDuration)
        {
            _phase = Phase.Start;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.WalkStart, fadeDuration);
        }

        private void EnterLoopPhase(LocomotionStateContext context, float fadeDuration)
        {
            _phase = Phase.Loop;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.WalkLoop, fadeDuration);
        }

        private void EnterEndPhase(LocomotionStateContext context)
        {
            _phase = Phase.End;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.WalkEnd, context.Config.EndAnimFadeDuration);
        }

        private bool TryResumeFromSlot(LocomotionStateContext context)
        {
            switch (context.ResumeSlot)
            {
                case LocomotionAnimSlot.WalkStart:
                    _phase = Phase.Start;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.WalkStart, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
                case LocomotionAnimSlot.WalkLoop:
                    _phase = Phase.Loop;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.WalkLoop, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
                case LocomotionAnimSlot.WalkEnd:
                    _phase = Phase.End;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.WalkEnd, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
            }
            return false;
        }
    }
}
