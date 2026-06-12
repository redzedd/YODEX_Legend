using Animancer;
using UnityEngine;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 受擊硬直狀態(Locomotion 內建)— TopState 切換到 HitStun 時由 Controller 主動觸發。
    /// 讀取 context.PendingHitOutcome 決定要播哪個方向的受擊動畫、硬直多久。
    /// 時序:
    ///   1. Enter 依 Outcome.Clip 播動畫、記錄 StunDuration。
    ///   2. Tick 每幀累積 _elapsed,到期前 CurrentRotationSpeed = 0 鎖旋轉。
    ///   3. 到期後 TransitionToGroundState 依當前輸入回 Idle / Walk / Run / FastRun。
    ///   4. Controller 會在 Update 末端偵測 state != Hit 時把 TopState 切回 Locomotion 並移除 HitStunned Tag。
    /// Phase 1 限制:
    ///   - 不可被 Dodge 取消(Dodge 輸入在 HitStun TopState 不會執行,且 CanDodge 也要求 TopState=Locomotion)。
    ///   - 硬直結束以計時器為準,動畫若較長會被 fade 覆蓋;反之動畫較短會 hold last frame 直到計時器到。
    /// </summary>
    public sealed class HitState : ILocomotionState
    {
        private AnimancerState _currentAnimState;
        private float _elapsed;
        private float _stunDuration;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            _elapsed = 0f;
            context.UseRootMotionRotation = false;
            HitOutcome outcome = context.PendingHitOutcome;
            _stunDuration = Mathf.Max(0f, outcome.StunDuration);
            if (outcome.Clip != null)
            {
                _currentAnimState = context.AnimatorDriver.Play(outcome.Clip, outcome.EnterFadeDuration);
            }
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = 0f;
            _elapsed += deltaTime;
            if (_elapsed < _stunDuration)
            {
                return;
            }
            TransitionToGroundState(context);
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            _currentAnimState = null;
            _elapsed = 0f;
            _stunDuration = 0f;
        }

        /// <summary>
        /// 硬直結束後一律回到 Idle — 讓玩家「穩住」再由 Idle 自然依輸入轉 Walk / Run / FastRun,
        /// 避免受擊結束瞬間從 Stagger 直接爆切到高速移動狀態造成視覺突兀。
        /// 套用 IdleEnterFadeOverride 使用 StunExitFadeDuration 做淡入。
        /// </summary>
        private void TransitionToGroundState(LocomotionStateContext context)
        {
            context.IdleEnterFadeOverride = context.HitReactionData != null
                ? context.HitReactionData.StunExitFadeDuration
                : context.Config.EndAnimFadeDuration;
            context.StateMachine.ChangeState(context.Idle);
        }
    }
}
