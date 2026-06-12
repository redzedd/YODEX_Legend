namespace Save
{
    /// <summary>
    /// 存檔介面 — 實作此介面的組件可被 SaveManager 自動序列化/反序列化
    /// </summary>
    public interface ISaveable
    {
        /// <summary>存檔用的唯一鍵值</summary>
        string SaveKey { get; }

        /// <summary>將當前狀態序列化為 JSON 字串</summary>
        string Serialize();

        /// <summary>從 JSON 字串反序列化恢復狀態</summary>
        void Deserialize(string json);
    }
}
