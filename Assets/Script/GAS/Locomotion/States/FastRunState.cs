using Animancer;
using UnityEngine;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 快跑狀態：依子階段播放 FastRunStart → FastRunLoop(Lean Mixer) → FastRunEnd。
    /// Loop 時根據輸入與角色朝向夾角計算 Lean 參數；夾角超過閾值則切到 FastRunTurn。
    /// Start / End 階段使用 NormalizedTime 輪詢推進，並支援釋放去抖動與 End 階段中斷復原。
    /// </summary>
    public sealed class FastRunState : ILocomotionState, IResumableLocomotionState
    {
        private enum Phase { Start, Loop, End }
        private Phase _phase;
        private AnimancerState _currentAnimState;
        private LinearMixerState _mixerState;
        private float _turnConditionHoldTime;

        public LocomotionAnimSlot CurrentSlot => _phase switch
        {
            Phase.Start => LocomotionAnimSlot.FastRunStart,
            Phase.Loop => LocomotionAnimSlot.FastRunLoop,
            Phase.End => LocomotionAnimSlot.FastRunEnd,
            _ => LocomotionAnimSlot.None,
        };
        public float CurrentNormalizedTime => _currentAnimState != null ? _currentAnimState.NormalizedTime : 0f;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            context.AnimatorDriver.ResetLeanSmoothing();
            _turnConditionHoldTime = 0f;
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
            if (previous == context.Walk || previous == context.Run || previous == context.FastRunStop)
            {
                EnterLoopPhase(context, context.Config.FastRunFadeDuration);
                return;
            }
            EnterStartPhase(context, context.Config.StartAnimFadeDuration);
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = _phase == Phase.Start
                ? context.Config.IdleRotationSpeed
                : context.Config.FastRunRotationSpeed;
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
            _mixerState = null;
        }

        private void TickStartPhase(LocomotionStateContext context)
        {
            if (context.NoInputTime > context.Config.InputReleaseDebounce || !context.RunButtonHeld)
            {
                // 鬆開衝刺但仍在移動 — 直接降檔到 Run/Walk Loop,避免播 FastRunEnd 再被即時打斷導致播 RunStart 的卡頓
                if (TryDownshiftWhileMoving(context))
                {
                    return;
                }
                // 放開移動鍵 — 走 FastRunEnd 收尾
                EnterEndPhase(context);
                return;
            }
            if (LocomotionAnimatorDriver.IsReadyForExitFade(_currentAnimState, context.Config.StartToLoopFadeDuration))
            {
                EnterLoopPhase(context, context.Config.StartToLoopFadeDuration);
            }
        }

        private void TickLoopPhase(LocomotionStateContext context, float deltaTime)
        {
            if (context.NoInputTime > context.Config.InputReleaseDebounce || !context.RunButtonHeld)
            {
                _turnConditionHoldTime = 0f;
                // 鬆開衝刺但仍在移動 — 直接降檔到 Run/Walk Loop,避免播 FastRunEnd 再被即時打斷導致播 RunStart 的卡頓
                if (TryDownshiftWhileMoving(context))
                {
                    return;
                }
                EnterEndPhase(context);
                return;
            }
            // 耐力扣光 → 直接降檔回 Run(搖桿若放鬆,Run 會再自行降 Walk)。
            // 僅 Loop 階段扣除,Start / End / Turn 不扣,避免起跑/收尾/迴轉中段被打斷。
            if (!context.TryConsumeSprintStamina(deltaTime))
            {
                _turnConditionHoldTime = 0f;
                context.StateMachine.ChangeState(context.Run);
                return;
            }
            if (!context.HasMoveInput)
            {
                _turnConditionHoldTime = 0f;
                return;
            }
            float unsignedAngle = LocomotionRotator.GetUnsignedAngle(context.ActorTransform, context.DesiredWorldDirection);
            bool turnCondition = unsignedAngle > context.Config.FastRunTurnAngleThreshold
                              && context.InputMagnitude >= context.Config.TurnTriggerMinMagnitude;
            if (turnCondition)
            {
                _turnConditionHoldTime += deltaTime;
                if (_turnConditionHoldTime >= context.Config.TurnTriggerHoldTime)
                {
                    float signedYaw = LocomotionRotator.GetSignedYawDelta(context.ActorTransform, context.DesiredWorldDirection);
                    context.TurnDirection = signedYaw >= 0f ? 1 : -1;
                    _turnConditionHoldTime = 0f;
                    context.StateMachine.ChangeState(context.FastRunTurn);
                    return;
                }
            }
            else
            {
                _turnConditionHoldTime = 0f;
            }
            UpdateLeanBlend(context, deltaTime);
        }

        private void TickEndPhase(LocomotionStateContext context)
        {
            if (context.HasMoveInput && context.RunButtonHeld)
            {
                // End 中途衝刺輸入回來 — 重新起步一律走 FastRunStart,不跳接 Loop
                EnterStartPhase(context, context.Config.EndAnimFadeDuration);
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
            if (_currentAnimState != null && _currentAnimState.NormalizedTime >= 1f)
            {
                context.StateMachine.ChangeState(context.Idle);
            }
        }

        private void EnterStartPhase(LocomotionStateContext context, float fadeDuration)
        {
            _phase = Phase.Start;
            _mixerState = null;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.FastRunStart, fadeDuration);
        }

        private void EnterLoopPhase(LocomotionStateContext context, float fadeDuration)
        {
            _phase = Phase.Loop;
            _mixerState = context.AnimatorDriver.PlayMixer(context.AnimationSet.FastRunLoopMixer, fadeDuration);
            _currentAnimState = _mixerState;
            if (_mixerState != null)
            {
                _mixerState.Parameter = 0f;
            }
        }

        private void EnterEndPhase(LocomotionStateContext context)
        {
            _phase = Phase.End;
            _mixerState = null;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.FastRunEnd, context.Config.EndAnimFadeDuration);
        }

        // 鬆開衝刺鍵但仍在移動時,直接切到 Run / Walk Loop,讓動畫保持 Loop→Loop 不經過 FastRunEnd。
        private bool TryDownshiftWhileMoving(LocomotionStateContext context)
        {
            if (context.RunButtonHeld || !context.HasMoveInput)
            {
                return false;
            }
            ILocomotionState next = context.InputMagnitude > context.Config.WalkMagnitudeThreshold
                ? (ILocomotionState)context.Run
                : context.Walk;
            context.StateMachine.ChangeState(next);
            return true;
        }

        private void UpdateLeanBlend(LocomotionStateContext context, float deltaTime)
        {
            if (_mixerState == null)
            {
                return;
            }
            float signedYaw = LocomotionRotator.GetSignedYawDelta(context.ActorTransform, context.DesiredWorldDirection);
            float target = Mathf.Clamp(signedYaw / context.Config.LeanMaxAngle, -1f, 1f);
            context.AnimatorDriver.SmoothMixerParameter(_mixerState, target, context.Config.LeanBlendSmoothTime, deltaTime);
        }

        private bool TryResumeFromSlot(LocomotionStateContext context)
        {
            switch (context.ResumeSlot)
            {
                case LocomotionAnimSlot.FastRunStart:
                    _phase = Phase.Start;
                    _mixerState = null;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.FastRunStart, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
                case LocomotionAnimSlot.FastRunLoop:
                    _phase = Phase.Loop;
                    _mixerState = context.AnimatorDriver.PlayMixer(context.AnimationSet.FastRunLoopMixer, 0f);
                    _currentAnimState = _mixerState;
                    if (_mixerState != null)
                    {
                        _mixerState.NormalizedTime = context.ResumeNormalizedTime;
                        _mixerState.Parameter = 0f;
                    }
                    return true;
                case LocomotionAnimSlot.FastRunEnd:
                    _phase = Phase.End;
                    _mixerState = null;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.FastRunEnd, 0f);
                    if (_currentAnimState != null) _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                    return true;
            }
            return false;
        }
    }
}
