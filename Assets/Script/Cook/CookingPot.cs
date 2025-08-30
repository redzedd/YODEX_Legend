using UnityEngine;

public class CookingPot : MonoBehaviour, IInteractable
{
    public GameObject cookingUIPanel; // 🔥 指向整個烹飪 UI 物件（開啟/關閉）
    public GameObject interactPrompt;
    public AudioClip activationPortalSFX;
    public AudioSource audioSource;

    public CookingInventoryDisplay cookingInventoryDisplay;
    public CookingManager cookingManager;

    public int Priority => 2;

    public void Interact()
    {
        Debug.Log("🍳 打開烹飪 UI");
        audioSource.PlayOneShot(activationPortalSFX);

        if (cookingUIPanel != null)
        {
            cookingUIPanel.SetActive(true);
            cookingInventoryDisplay.OpenUI(); // ⭐ 改成這樣主動初始化
        }

        if (cookingManager != null)
        {
            cookingManager.ClearIngredients();
        }
    }

    public void OnFocus()
    {
        interactPrompt.SetActive(true);
    }

    public void OnUnfocus()
    {
        interactPrompt.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        InteractionManager.Instance.RegisterInteractable(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        InteractionManager.Instance.UnregisterInteractable(this);
    }
}
