using System.Collections.Generic;
using UnityEngine;

public class ItemPickupHandler : MonoBehaviour, IInteractable
{
    public AudioSource audioSource;
    public AudioClip pickUpSFX;
    public GameObject interactPrompt;

    private readonly List<PickUpItem> itemsInRange = new List<PickUpItem>();
    private bool isRegisteredToManager = false;

    public int Priority => 1;

    private void Update()
    {
        interactPrompt.SetActive(itemsInRange.Count > 0);
        bool hasItem = itemsInRange.Count > 0;

        if (hasItem && !isRegisteredToManager)
        {
            InteractionManager.Instance.RegisterInteractable(this);
            isRegisteredToManager = true;
        }
        else if (!hasItem && isRegisteredToManager)
        {
            InteractionManager.Instance.UnregisterInteractable(this);
            isRegisteredToManager = false;
        }
    }

    public void Interact()
    {
        if (itemsInRange.Count == 0) return;

        PickUpItem closest = GetClosestItem();
        if (closest != null)
        {
            closest.Pickup(audioSource, pickUpSFX);
            itemsInRange.Remove(closest);
        }
    }

    public void OnFocus()
    {
        
    }

    public void OnUnfocus()
    {
        interactPrompt.SetActive(false);
    }

    private PickUpItem GetClosestItem()
    {
        float minDistance = float.MaxValue;
        PickUpItem closestItem = null;

        foreach (var item in itemsInRange)
        {
            if (item == null) continue;

            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestItem = item;
            }
        }

        return closestItem;
    }

    private void OnTriggerEnter(Collider other)
    {
        PickUpItem item = other.GetComponent<PickUpItem>();
        if (item != null && !itemsInRange.Contains(item))
        {
            itemsInRange.Add(item);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PickUpItem item = other.GetComponent<PickUpItem>();
        if (item != null && itemsInRange.Contains(item))
        {
            itemsInRange.Remove(item);
        }
    }
}
