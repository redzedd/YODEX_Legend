using System;
using UnityEngine;
using GAS.UI.Inventory;
using Item;

namespace Interaction
{
    /// <summary>
    /// [已棄用] 道具需求傳送門 — 請改用 GenericInteractable + GateInteractionHandler
    /// </summary>
    [Obsolete("請改用 GenericInteractable + GateInteractionHandler")]
    public class ItemPortalGate : InteractableTriggerBase
    {
        [Header("需求")]
        [SerializeField] private ItemData _requiredItem;
        [SerializeField] private int _requiredAmount = 1;

        [Header("傳送門")]
        [SerializeField] private GameObject _portalVisual;
        [SerializeField] private Collider _portalTrigger;

        [Header("音效")]
        [SerializeField] private AudioClip _openSFX;
        [SerializeField] private AudioClip _denySFX;

        private bool _isOpen;
        private AudioSource _audioSource;

        public override int Priority => 1;
        public override string InteractionTypeName => InteractionType.Activate;
        public override string PromptText => "開啟傳送門";
        public override bool CanInteract => !_isOpen;

        protected override void Awake()
        {
            base.Awake();
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
                _audioSource = gameObject.AddComponent<AudioSource>();
            if (_portalVisual != null) _portalVisual.SetActive(false);
            if (_portalTrigger != null) _portalTrigger.enabled = false;
        }

        public override void Interact()
        {
            if (_isOpen) return;
            int count = GetItemCount();
            if (count >= _requiredAmount)
                OpenPortal();
            else
            {
                Debug.Log($"需要 {_requiredItem.itemName} x {_requiredAmount} 才能開啟！");
                if (_denySFX != null && _audioSource != null)
                    _audioSource.PlayOneShot(_denySFX);
            }
        }

        private int GetItemCount()
        {
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
            // InventoryManager.Instance.RemoveItem(_requiredItem, _requiredAmount); // 可選：扣除道具
            if (_portalVisual != null) _portalVisual.SetActive(true);
            if (_portalTrigger != null) _portalTrigger.enabled = true;
            if (_openSFX != null && _audioSource != null)
                _audioSource.PlayOneShot(_openSFX);
            ForceUnregister();
        }
    }
}
