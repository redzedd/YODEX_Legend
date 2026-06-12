using Animancer;
using UnityEngine;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 待機狀態：播放 Idle，等待任何移動輸入後判斷進入 Walk / Run / FastRun。
    /// 偵測到輸入後不立刻決定層級，而是在一個 settle window 內追蹤 peak magnitude：
    /// 若期間 peak 已超過升檔閾值，立刻切 Run（幾乎無延遲，確保急推搖桿能觸發 RunStart）；
    /// 若 settle 時間內 peak 仍弱則切 Walk。此機制避免搖桿推到滿的過渡幾幀被當成 Walk 輸入、造成 Walk→Run 跳過 RunStart。
    /// </summary>
    public sealed class IdleState : ILocomotionState, IResumableLocomotionState
    {
        private float _settleTimer;
        private float _peakMagnitude;
        private AnimancerState _currentAnimState;

        public LocomotionAnimSlot CurrentSlot => LocomotionAnimSlot.Idle;
        public float CurrentNormalizedTime => _currentAnimState != null ? _currentAnimState.NormalizedTime : 0f;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            _settleTimer = 0f;
            _peakMagnitude = 0f;
            if (context.IsRefreshingFromModelSwitch && context.ResumeSlot == LocomotionAnimSlot.Idle)
            {
                _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.Idle, 0f);
                if (_currentAnimState != null)
                {
                    _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                }
                return;
            }
            float fadeDuration = context.IdleEnterFadeOverride ?? context.Config.EndAnimFadeDuration;
            context.IdleEnterFadeOverride = null;
            _currentAnimState = context.AnimatorDriver.Play(context.AnimationSet.Idle, fadeDuration);
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = context.Config.IdleRotationSpeed;
            if (!context.HasMoveInput)
            {
                _settleTimer = 0f;
                _peakMagnitude = 0f;
                return;
            }
            _peakMagnitude = Mathf.Max(_peakMagnitude, context.InputMagnitude);
            if (context.RunButtonHeld && context.CanStartSprint)
            {
                context.StateMachine.ChangeState(context.FastRun);
                return;
            }
            if (_peakMagnitude > context.Config.WalkMagnitudeThreshold)
            {
                context.StateMachine.ChangeState(context.Run);
                return;
            }
            _settleTimer += deltaTime;
            if (_settleTimer >= context.Config.IdleInputSettleTime)
            {
                context.StateMachine.ChangeState(context.Walk);
            }
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            _settleTimer = 0f;
            _peakMagnitude = 0f;
            _currentAnimState = null;
        }
    }
}
