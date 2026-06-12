namespace Interaction
{
    /// <summary>
    /// 互動介面 — 所有可互動物件的契約
    /// 提供優先級、類型、提示文字等元資料供 InteractionPromptUI 使用
    /// </summary>
    public interface IInteractable
    {
        /// <summary>互動優先級（數值越低越優先）</summary>
        int Priority { get; }

        /// <summary>互動類型字串（用於 UI 圖示、動畫分類，可在 Inspector 自訂）</summary>
        string InteractionTypeName { get; }

        /// <summary>互動提示文字（例如「拾取」「烹飪」「調查」）</summary>
        string PromptText { get; }

        /// <summary>是否可互動（支援條件性啟用/停用）</summary>
        bool CanInteract { get; }

        /// <summary>執行互動</summary>
        void Interact();

        /// <summary>進入聚焦（InteractionPromptUI 已集中處理提示顯示）</summary>
        void OnFocus();

        /// <summary>離開聚焦</summary>
        void OnUnfocus();
    }
}
