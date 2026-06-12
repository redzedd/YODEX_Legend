using Animancer;
using UnityEngine;

namespace Player.Locomotion.States
{
    /// <summary>
    /// 閃避狀態（Locomotion 內建）— 純 Root Motion 驅動,不走 GAS Ability 路徑。
    /// 分類（8 方向 + 無輸入）:
    ///   無移動輸入 → Backstep,角色朝向不變,由 clip RM 向後推。
    ///   有移動輸入 → 依角色當前面向（藍線）與搖桿輸入方向（紅線）的 Signed Angle
    ///   選擇 8 方向 RM clip，**角色面向不變**,由 clip 的 RM 把角色推向輸入方向,
    ///   產生「前衝 / 側閃 / 後跳」等動作觀感。
    /// 時序:
    ///   1. DodgeLockDuration 期間鎖定操作,不接受輸入中斷。
    ///   2. 鎖定期結束後偵測輸入 → 依當前輸入轉 Idle / Walk / Run / FastRun。
    ///   3. 輸入皆無且動畫 NormalizedTime >= 1f → 回 Idle。
    /// 旋轉由 CurrentRotationSpeed = 0 關閉,避免腳本旋轉與 RM 位移方向脫鉤。
    /// </summary>
    public sealed class DodgeState : ILocomotionState, IResumableLocomotionState
    {
        private AnimancerState _currentAnimState;
        private float _elapsed;
        private float _lastExitTime = -1f;
        private LocomotionAnimSlot _currentSlot;

        public LocomotionAnimSlot CurrentSlot => _currentSlot;
        public float CurrentNormalizedTime => _currentAnimState != null ? _currentAnimState.NormalizedTime : 0f;

        public void Enter(LocomotionStateContext context, ILocomotionState previous)
        {
            if (context.IsRefreshingFromModelSwitch && TryResumeFromSlot(context))
            {
                return;
            }
            _elapsed = 0f;
            context.IsDodgeLocked = true;
            context.UseRootMotionRotation = false;
            // 8 方向 + Backstep — 不再 snap 朝向,角色面向保持不變,由 RM clip 決定位移方向
            ClipTransition clip = SelectDirectionalClip(context, out _currentSlot);
            if (clip != null)
            {
                // 連擊判定使用「上次離開 Dodge 至今的時間」而非 previous state,原因:
                //   Backstep 連擊 previous == Dodge(一直停留在 Dodge 等動畫播完),
                //   但 Forward 連擊 previous == Run/Walk/FastRun(鎖定期結束即因 HasMoveInput 轉場出去),
                //   用時間窗判定可同時涵蓋兩種情況。
                bool isReentry = _lastExitTime > 0f
                              && Time.time - _lastExitTime <= context.Config.DodgeBufferTime;
                float fadeDuration = isReentry
                    ? context.Config.DodgeReentryFadeDuration
                    : context.Config.DodgeEnterFadeDuration;
                // 連擊時使用 PlayFromStart(FadeMode.FromStart),強制重新淡入；
                // 否則 Animancer 對相同 ClipTransition 會重用既有 state,DodgeReentryFadeDuration 無作用。
                _currentAnimState = isReentry
                    ? context.AnimatorDriver.PlayFromStart(clip, fadeDuration)
                    : context.AnimatorDriver.Play(clip, fadeDuration);
                // 手動設 Time:
                //   - 首次 Dodge:從 0 播起,走完 [Idle > Dodge > Idle] 完整曲線
                //   - 連擊:從 DodgeReentryStartTime 播起,跳過前段 Idle 預備,避免抽動
                // 也修正 Animancer.Play 對相同 clip 不會自動歸零、導致連擊卡在上次位置的問題。
                if (_currentAnimState != null)
                {
                    _currentAnimState.Time = isReentry ? context.Config.DodgeReentryStartTime : 0f;
                }
            }
        }

        public void Tick(LocomotionStateContext context, float deltaTime)
        {
            context.CurrentRotationSpeed = 0f;
            _elapsed += deltaTime;
            if (_elapsed < context.Config.DodgeLockDuration)
            {
                return;
            }
            // 鎖定期結束 — 解鎖閘門,允許再次觸發閃避（Controller 的 TryTriggerDodge 會用 ForceChangeState 重入）
            context.IsDodgeLocked = false;
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

        public void Exit(LocomotionStateContext context, ILocomotionState next)
        {
            _currentAnimState = null;
            _elapsed = 0f;
            context.IsDodgeLocked = false;
            _lastExitTime = Time.time;
        }

        /// <summary>
        /// 依搖桿輸入方向（紅線）與角色當前面向（藍線）的 Signed Angle 挑選對應 8 方向 RM clip。
        /// 無輸入或輸入過小 → Backstep。
        /// 任何方向 clip 未指派 → 回退到 DodgeForward,保證不會因為 Inspector 漏填造成原地不動。
        /// out slot 供 CurrentSlot getter 使用,讓武器切換能知道當時播的是哪個方向。
        /// </summary>
        private static ClipTransition SelectDirectionalClip(LocomotionStateContext context, out LocomotionAnimSlot slot)
        {
            LocomotionAnimationSet set = context.AnimationSet;
            Vector3 desired = context.DesiredWorldDirection;
            desired.y = 0f;
            if (!context.HasMoveInput || desired.sqrMagnitude < 0.0001f)
            {
                slot = LocomotionAnimSlot.DodgeBack;
                return set.Backstep != null ? set.Backstep : set.DodgeForward;
            }
            Vector3 forward = context.ActorTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                slot = LocomotionAnimSlot.DodgeForward;
                return set.DodgeForward;
            }
            forward.Normalize();
            desired.Normalize();
            float angle = Vector3.SignedAngle(forward, desired, Vector3.up);
            float abs = Mathf.Abs(angle);
            ClipTransition clip;
            if (abs <= 22.5f)
            {
                slot = LocomotionAnimSlot.DodgeForward;
                clip = set.DodgeForward;
            }
            else if (abs >= 157.5f)
            {
                // 有輸入但方向指向正後方 — 與無輸入 Backstep 共用 clip
                slot = LocomotionAnimSlot.DodgeBack;
                clip = set.Backstep;
            }
            else if (abs <= 67.5f)
            {
                slot = angle > 0f ? LocomotionAnimSlot.DodgeForwardRight : LocomotionAnimSlot.DodgeForwardLeft;
                clip = angle > 0f ? set.DodgeForwardRight : set.DodgeForwardLeft;
            }
            else if (abs <= 112.5f)
            {
                slot = angle > 0f ? LocomotionAnimSlot.DodgeRight : LocomotionAnimSlot.DodgeLeft;
                clip = angle > 0f ? set.DodgeRight : set.DodgeLeft;
            }
            else
            {
                slot = angle > 0f ? LocomotionAnimSlot.DodgeBackRight : LocomotionAnimSlot.DodgeBackLeft;
                clip = angle > 0f ? set.DodgeBackRight : set.DodgeBackLeft;
            }
            if (clip == null)
            {
                slot = LocomotionAnimSlot.DodgeForward;
                return set.DodgeForward;
            }
            return clip;
        }

        private void TransitionToGroundState(LocomotionStateContext context)
        {
            if (!context.HasMoveInput)
            {
                context.IdleEnterFadeOverride = context.Config.DodgeToMoveFadeDuration;
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
        /// 武器切換時接回同方向 Dodge clip。
        /// IsDodgeLocked 已由 Controller 的 pending 欄位在 InitializeLocomotion 恢復,不在此重置。
        /// _elapsed 無法從 Animancer 推得,用 NormalizedTime × clip length 做近似值,
        /// 避免玩家切完武器後 DodgeLock 計時從 0 重新開始造成閃避僵直延長。
        /// </summary>
        private bool TryResumeFromSlot(LocomotionStateContext context)
        {
            ClipTransition clip = GetClipForSlot(context.AnimationSet, context.ResumeSlot);
            if (clip == null)
            {
                return false;
            }
            _currentSlot = context.ResumeSlot;
            _currentAnimState = context.AnimatorDriver.Play(clip, 0f);
            if (_currentAnimState != null)
            {
                _currentAnimState.NormalizedTime = context.ResumeNormalizedTime;
                _elapsed = _currentAnimState.Length * context.ResumeNormalizedTime;
            }
            else
            {
                _elapsed = 0f;
            }
            return true;
        }

        private static ClipTransition GetClipForSlot(LocomotionAnimationSet set, LocomotionAnimSlot slot)
        {
            switch (slot)
            {
                case LocomotionAnimSlot.DodgeForward: return set.DodgeForward;
                case LocomotionAnimSlot.DodgeForwardRight: return set.DodgeForwardRight;
                case LocomotionAnimSlot.DodgeRight: return set.DodgeRight;
                case LocomotionAnimSlot.DodgeBackRight: return set.DodgeBackRight;
                case LocomotionAnimSlot.DodgeBack: return set.Backstep;
                case LocomotionAnimSlot.DodgeBackLeft: return set.DodgeBackLeft;
                case LocomotionAnimSlot.DodgeLeft: return set.DodgeLeft;
                case LocomotionAnimSlot.DodgeForwardLeft: return set.DodgeForwardLeft;
            }
            return null;
        }
    }
}
