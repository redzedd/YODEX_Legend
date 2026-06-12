using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 通用互動觸發器 — 在 Inspector 設定互動類型、提示文字、優先級
    /// 實際互動邏輯委派給 InteractionHandler 組件
    /// 場景中的可互動物件只需掛此腳本 + 對應的 Handler 即可
    /// </summary>
    public class GenericInteractable : InteractableTriggerBase
    {
        [Header("互動設定")]
        [Tooltip("互動類型名稱（決定提示圖示，可自訂）")]
        [SerializeField] private string _interactionTypeName = InteractionType.Activate;
        [Tooltip("提示文字（顯示於互動提示 UI）")]
        [SerializeField] private string _promptText = "互動";
        [Tooltip("優先級（數值越低越優先）")]
        [SerializeField] private int _priority = 1;

        [Header("互動處理器")]
        [Tooltip("拖放指定互動處理器（掛在同物件或子物件上）")]
        [SerializeField] private InteractionHandler _handler;

        public override int Priority => _priority;
        public override string InteractionTypeName => _interactionTypeName;
        public override string PromptText => _promptText;
        public override bool CanInteract => _handler != null && _handler.CanExecute();

        public override void Interact()
        {
            if (_handler == null) return;
            _handler.Execute();
        }

        protected override void OnTriggerEnter(Collider other)
        {
            base.OnTriggerEnter(other);
            if (IsRegistered && _handler != null)
                _handler.OnPlayerEnterRange();
        }

        protected override void OnTriggerExit(Collider other)
        {
            bool wasRegistered = IsRegistered;
            base.OnTriggerExit(other);
            if (wasRegistered && !IsRegistered && _handler != null)
                _handler.OnPlayerExitRange();
        }
    }
}
