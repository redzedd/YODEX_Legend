using Animancer;
using UnityEngine;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 滑翔翼狀態 — 玩家在空中再次按下跳躍鍵時觸發。
    /// 動畫:全身單一 Glider 動畫,進入時淡入、退出時隨下一個動畫淡出。
    /// 特效:Enter 時對 context.GliderVFX 呼叫 Play(),Exit 時呼叫 Pause(),適用持續射出的循環特效。
    /// 物理:垂直速度由 Controller 在 OnRootMotionUpdate 中 clamp 至 ≥ -GliderDescentSpeed;
    ///       水平速度寫入 JumpHorizontalVelocity,以攝影機相對輸入 × GliderHorizontalSpeed 指數平滑;
    ///       不使用 Root Motion 位移。
    /// 退出:落地 → 走 JumpState.EnterJumpEnd 分支播 JumpEnd 收尾;空中再按跳躍 → 切回 JumpLoop 自由落體。
    /// </summary>
    public sealed class GliderState : ILocomotionState
    {
        private AnimancerState _animState;
        private float _airborneTime;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            context.IsAirborne = true;
            context.IsGliding = true;
            context.UseRootMotionRotation = false;
            context.PendingJumpImpulse = 0f;
            // 進入瞬間繼承當前水平速度,避免空中被重置為 0 造成飄停
            Vector3 carried = context.LastHorizontalVelocity;
            carried.y = 0f;
            // 進入即把速度限制在滑翔翼水平最大值內,避免從衝刺跳延續過大水平速度
            float maxSpeed = context.Config.GliderHorizontalSpeed;
            if (carried.sqrMagnitude > maxSpeed * maxSpeed)
            {
                carried = carried.normalized * maxSpeed;
            }
            context.JumpHorizontalVelocity = carried;
            _airborneTime = 0f;
            if (context.AnimationSet.Glider != null)
            {
                _animState = context.AnimatorDriver.Play(context.AnimationSet.Glider, context.Config.GliderEnterFadeDuration);
            }
            // 身上特效:先 Clear 殘餘粒子,再 Play 從第 0 幀重新撥放,確保每次展開都是乾淨起點
            if (context.GliderVFX != null)
            {
                context.GliderVFX.Clear(true);
                context.GliderVFX.Play(true);
            }
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = context.Config.GliderRotationSpeed;
            _airborneTime += deltaTime;
            UpdateHorizontalVelocity(context, deltaTime);
            // 耐力檢查 — 滑翔每秒扣耐力,扣到 0 時自動收起滑翔翼,切回自由落體
            if (!context.TryConsumeGliderStamina(deltaTime))
            {
                context.EnterFallLoop = true;
                context.StateMachine.ChangeState(context.Jump);
                return;
            }
            // 落地 — 走 JumpState 的 EnterJumpEnd 分支播 JumpEnd 收尾,
            // 由 JumpState.Phase.End 邏輯接管後續轉場(LandingLock + Idle/Walk/Run/FastRun)
            if (context.IsGrounded && _airborneTime > 0.05f)
            {
                context.EnterJumpEnd = true;
                context.StateMachine.ChangeState(context.Jump);
            }
        }

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            context.IsGliding = false;
            context.IsAirborne = false;
            context.JumpHorizontalVelocity = Vector3.zero;
            // 停止「新粒子」射出,但保留已射出的粒子讓它們依 Lifetime 自然消散,
            // 避免落地瞬間粒子全部突兀消失;下次 Enter 仍會 Clear+Play 從頭重撥
            if (context.GliderVFX != null)
            {
                context.GliderVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            _animState = null;
            _airborneTime = 0f;
        }

        private void UpdateHorizontalVelocity(LocomotionStateContext context, float deltaTime)
        {
            Vector3 target;
            if (context.HasMoveInput)
            {
                Vector3 desiredDir = context.DesiredWorldDirection;
                desiredDir.y = 0f;
                if (desiredDir.sqrMagnitude < 0.0001f)
                {
                    target = Vector3.zero;
                }
                else
                {
                    desiredDir.Normalize();
                    target = desiredDir * (context.Config.GliderHorizontalSpeed * context.InputMagnitude);
                }
            }
            else
            {
                target = Vector3.zero;
            }
            float responsiveness = context.Config.GliderHorizontalResponsiveness;
            float t = 1f - Mathf.Exp(-responsiveness * deltaTime);
            context.JumpHorizontalVelocity = Vector3.Lerp(context.JumpHorizontalVelocity, target, t);
        }

    }
}
