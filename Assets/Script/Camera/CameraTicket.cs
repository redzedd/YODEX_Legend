namespace CameraSystem
{
    /// <summary>
    /// 鏡頭請求票 — 由 Director.Request 回傳。
    /// 拿著票時鏡頭啟用，呼叫 Release() 後鏡頭退場。
    /// </summary>
    public class CameraTicket
    {
        public CameraEntry Entry { get; }

        /// <summary>是否仍在 Director 的 active stack 中</summary>
        public bool IsActive { get; internal set; }

        internal int PushOrder;

        private readonly CameraDirector _director;

        internal CameraTicket(CameraDirector director, CameraEntry entry, int pushOrder)
        {
            _director = director;
            Entry = entry;
            PushOrder = pushOrder;
            IsActive = true;
        }

        /// <summary>釋放票 — 鏡頭退場，Director 重新計算誰勝出</summary>
        public void Release()
        {
            if (!IsActive) return;
            if (_director == null) return;
            _director.Release(this);
        }
    }
}
