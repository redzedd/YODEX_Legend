namespace CameraSystem
{
    /// <summary>
    /// 鏡頭身分證 — 每個 CinemachineCamera 在 CameraEntry 元件上選擇自己的 ID。
    /// 同 ID 可有多個 Entry（例如多個 Cinematic 場景演出鏡頭），由 push 順序決定誰勝出。
    /// 新增類型時直接擴充這個 enum 即可。
    /// </summary>
    public enum CameraId
    {
        None = 0,

        // ── 主視角 ──
        ThirdPerson = 100,

        // ── 戰鬥 ──
        Aim = 200,
        LockOn = 300,
        Parry = 400,

        // ── 互動演出 ──
        // 同 ID 可有多個 Entry，每個演出物件自帶一台鏡頭
        // 程式碼端用 Director.Request(CameraEntry) 指定要哪一台
        Cinematic = 1000,
    }
}
