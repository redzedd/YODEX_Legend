using UnityEngine;

public class PickUpItem : MonoBehaviour
{
    public ItemData itemData;
    public int quantity = 1;

    public void Pickup(AudioSource sfxSource, AudioClip sfx)
    {
        if (sfxSource != null && sfx != null)
            sfxSource.PlayOneShot(sfx);

        PickupNotificationManager.Instance.ShowNotification(itemData.icon, itemData.itemName, quantity);
        InventoryManager.Instance.AddItem(itemData, quantity);
        Destroy(gameObject);
    }
}