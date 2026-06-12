namespace Player.Locomotion
{
    /// <summary>
    /// 移動動畫 slot 識別 — 用於武器切換時從新 AnimationSet 的同名 slot 同步 NormalizedTime 接回,
    /// 讓切換前後的動畫進度連續(例:WalkStart 50% → 新武器 WalkStart 50%)。
    /// 涵蓋所有 Locomotion 可見的動作:Idle / Walk / Run / FastRun(含 Turn/Stop)/ Jump / Dodge(8 方向)。
    /// Hit / Knockback 不列入 — 受擊狀態切武器由 Q3 守門於 WeaponManager.CanSwitch 阻擋。
    /// </summary>
    public enum LocomotionAnimSlot
    {
        None,
        Idle,
        WalkStart,
        WalkLoop,
        WalkEnd,
        RunStart,
        RunLoop,
        RunEnd,
        FastRunStart,
        FastRunLoop,
        FastRunEnd,
        FastRunTurnLeft,
        FastRunTurnRight,
        FastRunStop,
        JumpStart,
        JumpLoop,
        JumpEnd,
        Glider,
        DodgeForward,
        DodgeForwardRight,
        DodgeRight,
        DodgeBackRight,
        DodgeBack,
        DodgeBackLeft,
        DodgeLeft,
        DodgeForwardLeft,
    }

    /// <summary>
    /// LocomotionAnimSlot 輔助擴充。
    /// </summary>
    public static class LocomotionAnimSlotExtensions
    {
        /// <summary>
        /// 是否為「減速收尾」slot。End 狀態播到一半若輸入回來,應該走對應的 Start 重新起步,
        /// 而不是直接跳 Loop,讓動畫節奏保持「Start → Loop → End → Start(再起步)」的完整循環。
        /// </summary>
        public static bool IsEndSlot(this LocomotionAnimSlot slot)
        {
            return slot == LocomotionAnimSlot.WalkEnd
                || slot == LocomotionAnimSlot.RunEnd
                || slot == LocomotionAnimSlot.FastRunEnd;
        }
    }
}
