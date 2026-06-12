using System.Collections;
using UnityEngine;
using CameraSystem;
using GAS.UI.Inventory;
using Item;

namespace Interaction
{
    /// <summary>
    /// 鑰匙傳送門處理器 — 持有指定關鍵物品時可解鎖
    /// 解鎖演出：CinematicCameraSequence（含 Cinemachine 聚焦）→ 扣除鑰匙 + 開啟傳送門 + 獎勵碎片
    /// 由 GenericInteractable 委派呼叫
    /// </summary>
    public class PortalInteractionHandler : InteractionHandler
    {
        [Header("鑰匙需求")]
        [Tooltip("需要的鑰匙物品名稱")]
        [SerializeField] private string _requiredKeyItemName = "傳送門魔法石";

        [Header("獎勵")]
        [Tooltip("解鎖後獲得的獎勵物品")]
        [SerializeField] private ItemData _rewardKeyFragment;

        [Header("解鎖後啟動")]
        [Tooltip("解鎖後要啟動的物件（傳送門本體）")]
        [SerializeField] private GameObject _portalAfterUnlockObject;

        [Header("音效")]
        [Tooltip("互動瞬間音效（演出開始前播放）")]
        [SerializeField] private AudioClip _interactSFX;
        [Tooltip("傳送門啟動音效（演出動作時播放）")]
        [SerializeField] private AudioClip _activationPortalSFX;
        [SerializeField] private AudioSource _audioSource;

        [Header("演出序列")]
        [Tooltip("Camera 切換 / 計時等設定")]
        [SerializeField] private CinematicCameraSequence _sequence = new();

        [Header("結束行為")]
        [Tooltip("演出結束後是否銷毀本物件（一次性使用的鑰匙傳送門）")]
        [SerializeField] private bool _destroyOnFinish = true;

        public override bool CanExecute() => HasRequiredKeyItem();

        public override void Execute()
        {
            if (!HasRequiredKeyItem())
            {
                Debug.Log($"傳送門尚未解鎖，需要鑰匙：{_requiredKeyItemName}");
                return;
            }
            if (_audioSource != null && _interactSFX != null)
                _audioSource.PlayOneShot(_interactSFX);
            StartCoroutine(PlayThenMaybeDestroy());
        }

        private void OnDestroy()
        {
            _sequence?.Cleanup();
        }

        private IEnumerator PlayThenMaybeDestroy()
        {
            yield return _sequence.Play(UnlockPortalLogic);
            if (_destroyOnFinish) Destroy(gameObject);
        }

        private bool HasRequiredKeyItem()
        {
            return InventoryManager.Instance != null
                && InventoryManager.Instance.HasItemByName(_requiredKeyItemName);
        }

        // 演出動作回呼：扣鑰匙、給獎勵、啟動傳送門本體、播音效
        private void UnlockPortalLogic()
        {
            if (_audioSource != null && _activationPortalSFX != null)
                _audioSource.PlayOneShot(_activationPortalSFX);
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.RemoveItemByName(_requiredKeyItemName);
                if (_rewardKeyFragment != null)
                    InventoryManager.Instance.AddItem(_rewardKeyFragment);
            }
            if (_portalAfterUnlockObject != null)
                _portalAfterUnlockObject.SetActive(true);
        }
    }
}
