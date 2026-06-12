namespace Player.Locomotion.States
{
    /// <summary>
    /// 可在武器切換時報告當前動畫 slot 與 NormalizedTime 的狀態介面。
    /// 由 NewGASPlayerController.PrepareForModelSwitch 讀取,將資訊帶到新模型重建後的 context,
    /// 讓對應 State.Enter 跳到新 AnimationSet 同名 slot 的相同時間點接播。
    /// 未實作此介面的狀態(FastRunTurn / FastRunStop / Jump / Dodge / Hit / Knockback)
    /// 切武器時一律退回 Idle 的 fallback 行為。
    /// </summary>
    public interface IResumableLocomotionState
    {
        /// <summary>當前狀態對應的動畫 slot;無法對應時回傳 <see cref="LocomotionAnimSlot.None"/>。</summary>
        LocomotionAnimSlot CurrentSlot { get; }

        /// <summary>當前 AnimancerState 的 NormalizedTime;未播放時回傳 0。</summary>
        float CurrentNormalizedTime { get; }
    }
}
