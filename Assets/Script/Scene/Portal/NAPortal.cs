using Unity.Cinemachine;
using UnityEngine;
using System.Collections;

public class NAPortal : MonoBehaviour, IInteractable
{
    public string requiredKeyItemName = "傳送門魔法石";
    public ItemData rewardKeyFragment;
    public GameObject portalAfterUnlockObject;
    public GameObject interactPrompt;
    public AudioClip interactSFX;
    public AudioClip activationPortalSFX;
    public AudioSource audioSource;
    public CinemachineCamera focusCamera;
    public int cameraBoostPriority = 20;
    public float cameraFocusDuration = 2f;   // A秒
    public float delayBeforeUnlock = 0.5f;   // A+0.5秒
    public float delayBeforeDisable = 3f;    // B秒

    private bool isRegistered = false;

    public int Priority => 3;

    public void Interact()
    {
        if (HasRequiredKeyItem())
        {
            StartCoroutine(UnlockPortalWithCameraRoutine());
        }
        else
        {
            Debug.Log($"🔒 傳送門尚未解鎖，需要鑰匙：{requiredKeyItemName}");
        }
    }

    public void OnFocus()
    {
        // ✅ 顯示提示 UI
        interactPrompt.SetActive(true);
    }

    public void OnUnfocus()
    {
        // ✅ 隱藏提示 UI
        interactPrompt.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (HasRequiredKeyItem() && !isRegistered)
        {
            InteractionManager.Instance.RegisterInteractable(this);
            isRegistered = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (isRegistered)
        {
            InteractionManager.Instance.UnregisterInteractable(this);
            isRegistered = false;
        }
    }

    private bool HasRequiredKeyItem()
    {
        return InventoryManager.Instance.HasItemByName(requiredKeyItemName);
    }

    private void UnlockPortalLogic()
    {
        Debug.Log("✅ 傳送門已解鎖！");
        InteractionManager.Instance.UnregisterInteractable(this);
        OnUnfocus();

        audioSource.PlayOneShot(activationPortalSFX);
        InventoryManager.Instance.RemoveItemByName(requiredKeyItemName);

        if (rewardKeyFragment != null)
        {
            InventoryManager.Instance.AddItem(rewardKeyFragment);
            Debug.Log($"🧩 已獲得碎片：{rewardKeyFragment.itemName}");
        }

        if (portalAfterUnlockObject != null)
        {
            portalAfterUnlockObject.SetActive(true);
        }

        // ❌ 不在這裡 Destroy
    }

    private IEnumerator UnlockPortalWithCameraRoutine()
    {
        if (focusCamera != null)
        {
            focusCamera.Priority = cameraBoostPriority;
            focusCamera.gameObject.SetActive(true);
            interactPrompt.SetActive(false);
        }

        // A 秒後觸發解鎖邏輯（不銷毀）
        audioSource.PlayOneShot(interactSFX);
        yield return new WaitForSeconds(cameraFocusDuration);
        UnlockPortalLogic();

        // 再等 0.5 秒
        yield return new WaitForSeconds(delayBeforeUnlock);

        if (focusCamera != null)
        {
            focusCamera.Priority = -1;
        }

        // 再等剩下時間（B - A - 0.5）
        float extraWait = Mathf.Max(0, delayBeforeDisable - cameraFocusDuration - delayBeforeUnlock);
        yield return new WaitForSeconds(extraWait);

        Destroy(focusCamera.gameObject);
        Destroy(gameObject); // ✅ 協程執行完畢後銷毀
    }

}
