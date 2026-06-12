namespace Player.Locomotion
{
    /// <summary>
    /// 受擊方向(4 向)。由 Controller 以「攻擊者 → 受擊者」向量 Dot 角色 forward / right 計算得出。
    /// Phase 1 僅使用 4 向,未來升級 8 向時改用 SignedAngle 切 45° bucket 即可。
    /// </summary>
    public enum HitDirection
    {
        Front,
        Back,
        Left,
        Right,
    }
}
