using System;
using Unity.Cinemachine;
using UnityEngine;
using System.Collections;
using GAS.UI.Inventory;
using Item;

namespace Interaction
{
    /// <summary>
    /// [已棄用] 鑰匙傳送門 — 請改用 GenericInteractable + PortalInteractionHandler
    /// </summary>
    [Obsolete("請改用 GenericInteractable + PortalInteractionHandler")]
    public class NAPortal : InteractableTriggerBase
    {
        [Header("鑰匙需求")]
        [SerializeField] private string _requiredKeyItemName = "傳送門魔法石";

        [Header("獎勵")]
        [SerializeField] private ItemData _rewardKeyFragment;

        [Header("解鎖後啟動")]
        [SerializeField] private GameObject _portalAfterUnlockObject;

        [Header("音效")]
        [SerializeField] private AudioClip _interactSFX;
        [SerializeField] private AudioClip _activationPortalSFX;
        [SerializeField] private AudioSource _audioSource;

        [Header("Cinemachine")]
        [SerializeField] private CinemachineCamera _focusCamera;
        [SerializeField] private int _cameraBoostPriority = 20;

        [Header("演出時間")]
        [SerializeField] private float _cameraFocusDuration = 2f;
        [SerializeField] private float _delayBeforeUnlock = 0.5f;
        [SerializeField] private float _delayBeforeDisable = 3f;

        public override int Priority => 3;
        public override string InteractionTypeName => InteractionType.Activate;
        public override string PromptText => "解鎖";
        public override bool CanInteract => HasRequiredKeyItem();

        public override void Interact()
        {
            if (HasRequiredKeyItem())
                StartCoroutine(UnlockPortalSequence());
            else
                Debug.Log($"傳送門尚未解鎖，需要鑰匙：{_requiredKeyItemName}");
        }

        /// <summary>Override: 僅在持有鑰匙時才註冊</summary>
        protected override void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            if (!HasRequiredKeyItem()) return;
            base.OnTriggerEnter(other);
        }

        private bool HasRequiredKeyItem()
        {
            return InventoryManager.Instance != null
                && InventoryManager.Instance.HasItemByName(_requiredKeyItemName);
        }

        private void UnlockPortalLogic()
        {
            ForceUnregister();
            if (_audioSource != null && _activationPortalSFX != null)
                _audioSource.PlayOneShot(_activationPortalSFX);
            InventoryManager.Instance.RemoveItemByName(_requiredKeyItemName);
            if (_rewardKeyFragment != null)
                InventoryManager.Instance.AddItem(_rewardKeyFragment);
            if (_portalAfterUnlockObject != null)
                _portalAfterUnlockObject.SetActive(true);
        }

        private IEnumerator UnlockPortalSequence()
        {
            // 聚焦相機
            if (_focusCamera != null)
            {
                _focusCamera.Priority = _cameraBoostPriority;
                _focusCamera.gameObject.SetActive(true);
            }
            if (_audioSource != null && _interactSFX != null)
                _audioSource.PlayOneShot(_interactSFX);
            // 等待聚焦
            yield return new WaitForSeconds(_cameraFocusDuration);
            UnlockPortalLogic();
            // 等待解鎖延遲
            yield return new WaitForSeconds(_delayBeforeUnlock);
            if (_focusCamera != null)
                _focusCamera.Priority = -1;
            // 等待剩餘時間
            float extraWait = Mathf.Max(0,
                _delayBeforeDisable - _cameraFocusDuration - _delayBeforeUnlock);
            yield return new WaitForSeconds(extraWait);
            // 清理
            if (_focusCamera != null)
                Destroy(_focusCamera.gameObject);
            Destroy(gameObject);
        }
    }
}
