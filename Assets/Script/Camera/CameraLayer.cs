namespace CameraSystem
{
    /// <summary>
    /// 鏡頭優先層 — Director 依層級決定誰壓誰。
    /// 高層級永遠壓過低層級（例如 Cinematic 永遠壓住 ThirdPerson）。
    /// 同層級內依 Push 時間決定，後 push 的勝出。
    /// </summary>
    public enum CameraLayer
    {
        /// <summary>常駐底層 — 主視角第三人稱</summary>
        Background = 0,

        /// <summary>瞄準（肩射）— 壓過 LockOn 取得清晰準心方向與自由轉向</summary>
        Aim = 1,

        /// <summary>鎖定 — 被任何更高層覆蓋時 LockOnBridge 會自動解除鎖定（連 LockOnAnchor 副作用一起清）</summary>
        LockOn = 2,

        /// <summary>動作特寫 — 格擋等戰鬥演出</summary>
        Action = 3,

        /// <summary>劇情演出 — 互動演出（永遠壓一切）</summary>
        Cinematic = 4,
    }
}
