using UnityEngine;
using GAS.UI.Inventory;

namespace Item
{
    /// <summary>
    /// 場景可拾取物件 — 掛在世界中的道具上
    /// 由 ItemPickupHandler 偵測並呼叫 Pickup()
    /// </summary>
    public class PickUpItem : MonoBehaviour
    {
        public ItemData itemData;
        public int quantity = 1;

        public void Pickup(AudioSource sfxSource, AudioClip sfx)
        {
            if (sfxSource != null && sfx != null)
                sfxSource.PlayOneShot(sfx);
            PickupNotificationManager.Instance.ShowNotification(
                itemData.icon, itemData.itemName, quantity);
            InventoryManager.Instance.AddItem(itemData, quantity);
            Destroy(gameObject);
        }
    }
}
