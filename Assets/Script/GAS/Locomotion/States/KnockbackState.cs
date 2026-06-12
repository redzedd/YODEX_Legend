using Animancer;
using UnityEngine;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 重擊/擊倒狀態(Locomotion 內建)— 3 階段動畫流程:
    ///   Phase 1 StaggerIntro:播 Stagger 四方向 clip 前段,顯示受擊方向與衝擊反應。
    ///   Phase 2 Main       :播單支 Front-view Knockback clip,同時在 KnockbackEnterFadeDuration 期間
    ///                        平滑旋轉角色朝向,讓單一 clip 產生 4 方向的飛出效果(角色 forward 會旋轉至
    ///                        與 clip 向後倒相反的世界方向)。
    ///   Phase 3 StandUp    :播起身 clip,播完自動回 Idle。
    /// 整個過程角色無法操作,TopState 維持 HitStun 直到回 Idle 時由 Controller.SyncHitStunTopState 同步回 Locomotion。
    /// </summary>
    public sealed class KnockbackState : ILocomotionState
    {
        private enum Phase { StaggerIntro, Main, StandUp }
        private Phase _phase;
        private AnimancerState _currentAnimState;
        private float _staggerIntroElapsed;
        private HitDirection _direction;
        private Quaternion _mainRotateStart;
        private Quaternion _mainRotateTarget;
        private float _mainRotateElapsed;
        private float _mainRotateDuration;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            context.UseRootMotionRotation = false;
            _staggerIntroElapsed = 0f;
            HitOutcome outcome = context.PendingHitOutcome;
            _direction = outcome.Direction;
            _phase = Phase.StaggerIntro;
            if (outcome.Clip != null)
            {
                _currentAnimState = context.AnimatorDriver.Play(outcome.Clip, outcome.EnterFadeDuration);
            }
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = 0f;
            switch (_phase)
            {
                case Phase.StaggerIntro:
                    TickStaggerIntro(context, deltaTime);
                    break;
                case Phase.Main:
                    TickMain(context, deltaTime);
                    break;
                case Phase.StandUp:
                    TickStandUp(context);
                    break;
            }
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            _currentAnimState = null;
            _staggerIntroElapsed = 0f;
            _phase = Phase.StaggerIntro;
            _mainRotateElapsed = 0f;
            _mainRotateDuration = 0f;
        }

        private void TickStaggerIntro(LocomotionStateContext context, float deltaTime)
        {
            _staggerIntroElapsed += deltaTime;
            HitReactionData data = context.HitReactionData;
            float introDuration = data != null ? data.StaggerIntroDuration : 0.2f;
            if (_staggerIntroElapsed >= introDuration)
            {
                EnterMainPhase(context);
            }
        }

        private void EnterMainPhase(LocomotionStateContext context)
        {
            _phase = Phase.Main;
            HitReactionData data = context.HitReactionData;
            if (data == null || data.Knockback == null)
            {
                EnterStandUpPhase(context);
                return;
            }
            _currentAnimState = context.AnimatorDriver.Play(data.Knockback, data.KnockbackEnterFadeDuration);
            // 手動設 Time — 跳過 Knockback clip 前段可能與 StaggerIntro 重複的「站立反應」,直接進入主要飛出段
            if (_currentAnimState != null && data.KnockbackStartTime > 0f)
            {
                _currentAnimState.Time = data.KnockbackStartTime;
            }
            // 初始化角色旋轉 — 在 KnockbackRotateDuration 期間平滑轉向目標方向
            _mainRotateStart = context.ActorTransform.rotation;
            _mainRotateTarget = ComputeTargetRotation(context.ActorTransform);
            _mainRotateElapsed = 0f;
            _mainRotateDuration = Mathf.Max(0f, data.KnockbackRotateDuration);
        }

        private void TickMain(LocomotionStateContext context, float deltaTime)
        {
            // 平滑旋轉 — 在 KnockbackEnterFadeDuration 期間從 _mainRotateStart 轉至 _mainRotateTarget
            if (_mainRotateElapsed < _mainRotateDuration)
            {
                _mainRotateElapsed += deltaTime;
                float t = _mainRotateDuration > 0f
                    ? Mathf.Clamp01(_mainRotateElapsed / _mainRotateDuration)
                    : 1f;
                context.ActorTransform.rotation = Quaternion.Slerp(_mainRotateStart, _mainRotateTarget, t);
            }
            else if (_mainRotateDuration > 0f)
            {
                // 第一幀 clamp 完成後鎖定最終角度,避免 clip 本身的 RM Rotation 慢慢漂移
                context.ActorTransform.rotation = _mainRotateTarget;
            }
            if (_currentAnimState != null && _currentAnimState.NormalizedTime >= 1f)
            {
                EnterStandUpPhase(context);
            }
        }

        /// <summary>
        /// 計算 Main 階段的目標旋轉。Knockback clip 是 Front-view(角色面向前、向後倒),
        /// 目標朝向 = 使 clip 的「向後倒」方向對齊世界的「飛出方向」:
        ///   Front 受擊 → 向後飛 → 角色保持原朝向(clip 向後倒即世界向後)
        ///   Back 受擊  → 向前飛 → 角色旋轉 180°(clip 向後倒即世界向前)
        ///   Left 受擊  → 向右飛 → 角色左轉 90°(clip 向後倒即世界向右)
        ///   Right 受擊 → 向左飛 → 角色右轉 90°(clip 向後倒即世界向左)
        /// </summary>
        private Quaternion ComputeTargetRotation(Transform actor)
        {
            float yawDelta = _direction switch
            {
                HitDirection.Front => 0f,
                HitDirection.Back => 180f,
                HitDirection.Left => -90f,
                HitDirection.Right => 90f,
                _ => 0f,
            };
            return actor.rotation * Quaternion.Euler(0f, yawDelta, 0f);
        }

        private void EnterStandUpPhase(LocomotionStateContext context)
        {
            _phase = Phase.StandUp;
            HitReactionData data = context.HitReactionData;
            if (data == null || data.StandUp == null)
            {
                TransitionToIdle(context);
                return;
            }
            _currentAnimState = context.AnimatorDriver.Play(data.StandUp, data.StandUpEnterFadeDuration);
        }

        private void TickStandUp(LocomotionStateContext context)
        {
            if (_currentAnimState != null && _currentAnimState.NormalizedTime >= 1f)
            {
                TransitionToIdle(context);
            }
        }

        private void TransitionToIdle(LocomotionStateContext context)
        {
            HitReactionData data = context.HitReactionData;
            context.IdleEnterFadeOverride = data != null
                ? data.StunExitFadeDuration
                : context.Config.EndAnimFadeDuration;
            context.StateMachine.ChangeState(context.Idle);
        }

    }
}
