using UnityEngine;
using GAS.UI.Inventory;
using Item;

namespace Interaction
{
    /// <summary>
    /// 道具需求門處理器 — 持有指定數量道具時可開啟傳送門
    /// 開啟後啟動傳送門視覺 + 碰撞器，並自動從 InteractionManager 取消註冊
    /// 由 GenericInteractable 委派呼叫
    /// </summary>
    public class GateInteractionHandler : InteractionHandler
    {
        [Header("需求")]
        [Tooltip("需要的物品")]
        [SerializeField] private ItemData _requiredItem;
        [Tooltip("需要的數量")]
        [SerializeField] private int _requiredAmount = 1;

        [Header("傳送門")]
        [Tooltip("傳送門視覺物件（開啟後顯示）")]
        [SerializeField] private GameObject _portalVisual;
        [Tooltip("傳送門碰撞觸發器（開啟後啟用）")]
        [SerializeField] private Collider _portalTrigger;

        [Header("失敗提示")]
        [Tooltip("道具不足時的提示文字")]
        [SerializeField] private string _denyMessage = "需要更多道具才能開啟";
        [Tooltip("失敗提示音效（傳給 InteractionHintUI）")]
        [SerializeField] private AudioClip _denySFX;

        [Header("音效")]
        [SerializeField] private AudioClip _openSFX;

        private bool _isOpen;
        private AudioSource _audioSource;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
                _audioSource = gameObject.AddComponent<AudioSource>();
            if (_portalVisual != null) _portalVisual.SetActive(false);
            if (_portalTrigger != null) _portalTrigger.enabled = false;
        }

        public override bool CanExecute() => !_isOpen;

        public override void Execute()
        {
            if (_isOpen) return;
            int count = GetItemCount();
            if (count >= _requiredAmount)
                OpenPortal();
            else
            {
                if (InteractionHintUI.Instance != null)
                    InteractionHintUI.Instance.Show(_denyMessage, _denySFX);
            }
        }

        private int GetItemCount()
        {
            if (InventoryManager.Instance == null || _requiredItem == null) return 0;
            var items = InventoryManager.Instance.GetItemsByCategory(_requiredItem.category);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].itemData == _requiredItem)
                    return items[i].quantity;
            }
            return 0;
        }

        private void OpenPortal()
        {
            _isOpen = true;
            if (_portalVisual != null) _portalVisual.SetActive(true);
            if (_portalTrigger != null) _portalTrigger.enabled = true;
            if (_openSFX != null && _audioSource != null)
                _audioSource.PlayOneShot(_openSFX);
        }
    }
}
