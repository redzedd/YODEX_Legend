using Animancer;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 跑步狀態：Idle 直接進入時播 RunStart；從 Walk/FastRun/FastRunTurn 轉入時跳過 Start。
    /// 降檔為走路採遲滯判定（_walkInputHoldTime 必須累積超過 RunDownshiftHoldTime），避免快速釋放搖桿時經過走路區間而觸發 WalkEnd。
    /// 階段推進以 AnimancerState.NormalizedTime 輪詢。
    /// </summary>
    public sealed class RunState : ILocomotionState, IResumableLocomotionState
    {
        private enum Phase { Start, Loop, End }
        private Phase _phase;
        private AnimancerState _currentAnimState;
        private float _walkInputHoldTime;

        public LocomotionAnimSlot CurrentSlot => _phase switch
        {
            Phase.Start => LocomotionAnimSlot.RunStart,
            Phase.Loop => LocomotionAnimSlot.RunLoop,
            Phase.End => LocomotionAnimSlot.RunEnd,
            _ => LocomotionAnimSlot.None,
        };
        public float CurrentNormalizedTime => _currentAnimState != null ? _currentAnimState.NormalizedTime : 0f;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            _walkInputHoldTime = 0f;
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
            if (previous == context.FastRunTurn)
            {
                EnterLoopPhase(context, context.Config.PostTurnFadeDuration);
                return;
            }
            // 前一個狀態剛在 End 收尾(WalkEnd / RunEnd / FastRunEnd)— 重新起步一律走 Start,不跳接 Loop
            if (previous is IResumableLocomotionState prevResumable && prevResumable.CurrentSlot.IsEndSlot())
            {
                EnterStartPhase(context, context.Config.EndAnimFadeDuration);
                return;
            }
            if (previous == context.Walk || previous == context.FastRun || previous == context.FastRunStop)
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
                : context.Config.RunRotationSpeed;
            switch (_phase)
            {
                case Phase.Start:
                    TickStartPhase(context);
                    break;
                case Phase.Loop:
                    TickLoopPhase(context, deltaTime);
                    break;
                case Phase.End:
                    TickEndPhase(context);
                    break;
            }
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            _currentAnimState = null;
            _walkInputHoldTime = 0f;
        }

        private void TickStartPhase(LocomotionStateContext context)
        {
            if (context.NoInputTime > context.Config.InputReleaseDebounce)
            {
                // 放開移動鍵 — 即使仍在 Start 階段,也走 RunEnd 收尾,不要直接跳 Idle
                EnterEndPhase(context);
                return;
            }
            if (context.RunButtonHeld && context.HasMoveInput && context.CanStartSprint)
            {
                context.StateMachine.ChangeState(context.FastRun);
                return;
            }
            if (LocomotionAnimatorDriver.IsReadyForExitFade(_currentAnimState, context.Config.StartToLoopFadeDuration))
            {
                EnterLoopPhase(context, context.Config.StartToLoopFadeDuration);
            }
        }

        private void TickLoopPhase(LocomotionStateContext context, float deltaTime)
        {
            if (context.NoInputTime > context.Config.InputReleaseDebounce)
            {
                _walkInputHoldTime = 0f;
                EnterEndPhase(context);
                return;
            }
            if (!context.HasMoveInput)
            {
                _walkInputHoldTime = 0f;
                return;
            }
            if (context.RunButtonHeld && context.CanStartSprint)
            {
                _walkInputHoldTime = 0f;
                context.StateMachine.ChangeState(context.FastRun);
                return;
            }
            if (context.InputMagnitude < context.Config.RunToWalkMagnitudeThreshold)
            {
                _walkInputHoldTime += deltaTime;
                if (_walkInputHoldTime >= context.Config.RunDownshiftHoldTime)
                {
                    _walkInputHoldTime = 0f;
                    context.StateMachine.ChangeState(context.Walk);
                }
                return;
            }
            _walkInputHoldTime = 0f;
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
                    // End 中途輸入回來 — 重新起步一律走 RunStart,不跳接 Loop
                    EnterStartPhase(context, context.Config.EndAnimFadeDuration);
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

        private void EnterStartPhase(LocomotionStateContext context, float fadeDuration)
        {
            _phase = Phase.Start;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.RunStart, fadeDuration);
        }

        private void EnterLoopPhase(LocomotionStateContext context, float fadeDuration)
        {
            _phase = Phase.Loop;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.RunLoop, fadeDuration);
        }

        private void EnterEndPhase(LocomotionStateContext context)
        {
            _phase = Phase.End;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.RunEnd, context.Config.EndAnimFadeDuration);
        }

        private bool TryResumeFromSlot(LocomotionStateContext context)
        {
            switch (context.ResumeSlot)
            {
                case LocomotionAnimSlot.RunStart:
                    _phase = Phase.Start;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.RunStart, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
                case LocomotionAnimSlot.RunLoop:
                    _phase = Phase.Loop;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.RunLoop, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
                case LocomotionAnimSlot.RunEnd:
                    _phase = Phase.End;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.RunEnd, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
            }
            return false;
        }
    }
}
