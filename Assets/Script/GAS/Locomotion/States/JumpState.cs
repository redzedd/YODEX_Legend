using Animancer;
using UnityEngine;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 跳躍狀態：依子階段播放 JumpStart → JumpLoop → JumpEnd。
    /// 起跳瞬間寫入 PendingJumpImpulse 與 JumpHorizontalVelocity，由 Controller 在 OnAnimatorMove 消費。
    /// 滯空期間以 AirControlWeight 將水平速度朝玩家輸入方向插值，達成中等空中控制手感。
    /// 落地判定：Phase.Loop 中偵測 IsGrounded 且已過 MinAirborneTimeBeforeLand 進入 Phase.End。
    /// </summary>
    public sealed class JumpState : ILocomotionState, IResumableLocomotionState
    {
        private enum Phase { Start, Loop, End }
        private Phase _phase;
        private AnimancerState _currentAnimState;
        private float _airborneTime;
        private float _initialHorizontalSpeed;
        private float _endPhaseElapsed;

        public LocomotionAnimSlot CurrentSlot => _phase switch
        {
            Phase.Start => LocomotionAnimSlot.JumpStart,
            Phase.Loop => LocomotionAnimSlot.JumpLoop,
            Phase.End => LocomotionAnimSlot.JumpEnd,
            _ => LocomotionAnimSlot.None,
        };
        public float CurrentNormalizedTime => _currentAnimState != null ? _currentAnimState.NormalizedTime : 0f;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            if (context.IsRefreshingFromModelSwitch && TryResumeFromSlot(context))
            {
                return;
            }
            context.UseRootMotionRotation = false;
            // 落地分支:外部狀態(如 GliderState)落地時要求直接播 JumpEnd 收尾,
            // 跳過 JumpStart / JumpLoop,清空空中標旗讓重力於下一幀自然把垂直速度貼地
            if (context.EnterJumpEnd)
            {
                context.EnterJumpEnd = false;
                _phase = Phase.End;
                _endPhaseElapsed = 0f;
                _airborneTime = 0f;
                _initialHorizontalSpeed = 0f;
                context.IsAirborne = false;
                context.JumpHorizontalVelocity = Vector3.zero;
                _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.JumpEnd, context.Config.JumpEndFadeDuration);
                return;
            }
            context.IsAirborne = true;
            Vector3 initialHorizontal = context.LastHorizontalVelocity;
            initialHorizontal.y = 0f;
            _initialHorizontalSpeed = initialHorizontal.magnitude;
            context.JumpHorizontalVelocity = initialHorizontal;
            // 走「非跳躍下落」分支:跳過 JumpStart,直接 Loop;不寫 PendingJumpImpulse,
            // 讓 Controller 既有的負垂直速度繼續累加重力,避免下落時被起跳衝量蓋掉
            if (context.EnterFallLoop)
            {
                context.EnterFallLoop = false;
                _phase = Phase.Loop;
                // 補滿 MinAirborneTimeBeforeLand 累計,讓下一幀 TryHandleLanding 能立刻判定落地
                _airborneTime = context.Config.MinAirborneTimeBeforeLand + 0.01f;
                _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.JumpLoop, context.Config.JumpLoopFadeDuration);
                return;
            }
            _phase = Phase.Start;
            _airborneTime = 0f;
            context.PendingJumpImpulse = context.Config.JumpInitialUpVelocity;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.JumpStart, context.Config.JumpStartFadeDuration);
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = context.Config.JumpRotationSpeed;
            _airborneTime += deltaTime;
            switch (_phase)
            {
                case Phase.Start:
                    TickStartPhase(context);
                    break;
                case Phase.Loop:
                    TickLoopPhase(context, deltaTime);
                    break;
                case Phase.End:
                    TickEndPhase(context, deltaTime);
                    break;
            }
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            context.IsAirborne = false;
            context.PendingJumpImpulse = 0f;
            context.JumpHorizontalVelocity = Vector3.zero;
            _currentAnimState = null;
            _airborneTime = 0f;
            _initialHorizontalSpeed = 0f;
            _endPhaseElapsed = 0f;
        }

        private void TickStartPhase(LocomotionStateContext context)
        {
            UpdateAirHorizontalVelocity(context, Time.deltaTime);
            if (TryHandleLanding(context))
            {
                return;
            }
            if (LocomotionAnimatorDriver.IsReadyForExitFade(_currentAnimState, context.Config.JumpLoopFadeDuration))
            {
                EnterLoopPhase(context);
            }
        }

        private void TickLoopPhase(LocomotionStateContext context, float deltaTime)
        {
            UpdateAirHorizontalVelocity(context, deltaTime);
            TryHandleLanding(context);
        }

        private void TickEndPhase(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = 0f;
            _endPhaseElapsed += deltaTime;
            if (_endPhaseElapsed < context.Config.JumpLandingLockDuration)
            {
                return;
            }
            if (context.HasMoveInput)
            {
                TransitionToGroundState(context);
                return;
            }
            if (_currentAnimState != null && _currentAnimState.NormalizedTime >= 1f)
            {
                TransitionToGroundState(context);
            }
        }

        private void EnterLoopPhase(LocomotionStateContext context)
        {
            _phase = Phase.Loop;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.JumpLoop, context.Config.JumpLoopFadeDuration);
        }

        private void EnterEndPhase(LocomotionStateContext context)
        {
            _phase = Phase.End;
            _endPhaseElapsed = 0f;
            context.IsAirborne = false;
            context.JumpHorizontalVelocity = Vector3.zero;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.JumpEnd, context.Config.JumpEndFadeDuration);
        }

        private bool TryHandleLanding(LocomotionStateContext context)
        {
            if (_airborneTime < context.Config.MinAirborneTimeBeforeLand)
            {
                return false;
            }
            if (!context.IsGrounded)
            {
                return false;
            }
            EnterEndPhase(context);
            return true;
        }

        private void UpdateAirHorizontalVelocity(LocomotionStateContext context, float deltaTime)
        {
            float weight = context.Config.AirControlWeight;
            if (weight <= 0f)
            {
                return;
            }
            Vector3 target;
            if (context.HasMoveInput)
            {
                Vector3 desiredDir = context.DesiredWorldDirection;
                desiredDir.y = 0f;
                if (desiredDir.sqrMagnitude < 0.0001f)
                {
                    return;
                }
                desiredDir.Normalize();
                float desiredMagnitude = Mathf.Max(_initialHorizontalSpeed, context.Config.AirMoveBaseSpeed * context.InputMagnitude);
                target = desiredDir * desiredMagnitude;
            }
            else
            {
                return;
            }
            float responsiveness = context.Config.AirControlResponsiveness * weight;
            float t = 1f - Mathf.Exp(-responsiveness * deltaTime);
            context.JumpHorizontalVelocity = Vector3.Lerp(context.JumpHorizontalVelocity, target, t);
        }

        private void TransitionToGroundState(LocomotionStateContext context)
        {
            if (!context.HasMoveInput)
            {
                context.StateMachine.ChangeState(context.Idle);
                return;
            }
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
        }

        /// <summary>
        /// 武器切換時接回同 phase 的 JumpStart / JumpLoop / JumpEnd。
        /// 刻意不觸發 PendingJumpImpulse(避免二次推升)或覆寫 JumpHorizontalVelocity(由 Controller 的 pending 欄位在 InitializeLocomotion 恢復);
        /// IsAirborne / UseRootMotionRotation 同樣由 pending 恢復。
        /// 內部累計器(_airborneTime / _endPhaseElapsed)無法從 Animancer 推得,用 NormalizedTime × clip length 做近似值避免落地判定或落地鎖不觸發。
        /// </summary>
        private bool TryResumeFromSlot(LocomotionStateContext context)
        {
            switch (context.ResumeSlot)
            {
                case LocomotionAnimSlot.JumpStart:
                    _phase = Phase.Start;
                    _initialHorizontalSpeed = context.JumpHorizontalVelocity.magnitude;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.JumpStart, 0f);
                    if (_currentAnimState != null)
                    {
                        _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                        _airborneTime = _currentAnimState.Length * context.ResumeNormalizedTime;
                    }
                    return true;
                case LocomotionAnimSlot.JumpLoop:
                    _phase = Phase.Loop;
                    _initialHorizontalSpeed = context.JumpHorizontalVelocity.magnitude;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.JumpLoop, 0f);
                    if (_currentAnimState != null)
                    {
                        _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                        // Loop 階段已過 MinAirborneTimeBeforeLand,補滿累計器確保下一幀 TryHandleLanding 能立刻偵測落地
                        _airborneTime = context.Config.MinAirborneTimeBeforeLand + 0.01f;
                    }
                    return true;
                case LocomotionAnimSlot.JumpEnd:
                    _phase = Phase.End;
                    _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.JumpEnd, 0f);
                    if (_currentAnimState != null)
                    {
                        _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                        _endPhaseElapsed = _currentAnimState.Length * context.ResumeNormalizedTime;
                    }
                    return true;
            }
            return false;
        }
    }
}
