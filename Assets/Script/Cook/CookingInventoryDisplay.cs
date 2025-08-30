using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class CookingInventoryDisplay : MonoBehaviour
{
    public GameObject cookingPanel;
    public Button defaultSelectedSlot;
    public Text pageText;
    public CookingManager cookingManager;

    public InventoryDisplay.InventorySlot[] slots = new InventoryDisplay.InventorySlot[12];
    public Sprite defaultSprite;
    public string defaultText = "？？？";

    private int currentPage = 0;
    private int totalPages = 1;

    private List<InventoryItem> ingredientItems = new();
    private List<InventoryItem?> currentDisplayItems = new();
    private bool isOpen = false;
    public bool IsOpen => isOpen;

    private void Update()
    {
        if (IsOpen)
        {
            PlayerInputHandler.Instance.DisablePlayerInput();
            PlayerInputHandler.Instance.EnableUIMapInput();
            MouseVisibilityManager.Instance.enableDynamicMouse = true;
        }

        // ⭐ 加入滑鼠控制切換判定
        if (IsPointerOverSlot())
        {
            EventSystem.current.sendNavigationEvents = false;
        }
        else
        {
            EventSystem.current.sendNavigationEvents = true;
        }

        if (PlayerInputHandler.Instance.NextPageTriggered)
        {
            ChangePage(1);
        }
        else if (PlayerInputHandler.Instance.PrevPageTriggered)
        {
            ChangePage(-1);
        }

        if (PlayerInputHandler.Instance.CancelTriggered && isOpen)
        {
            if (cookingManager.HasIngredients())
            {
                cookingManager.ClearIngredients(refund: true);
                Debug.Log("🧹 已清空鍋中材料（第一次取消）");
            }
            else
            {
                CloseCookingUI(); // 沒有材料才關閉
            }
        }
    }

    public void OpenUI()
    {
        isOpen = true;

        if (InventoryManager.Instance == null)
        {
            Debug.LogWarning("InventoryManager 尚未初始化，Cooking UI 暫緩初始化");
            return;
        }

        var items = InventoryManager.Instance.GetItemsByCategory(InventoryDisplay.Category.Ingredients);
        InventoryManager.Instance.SortInventory();

        totalPages = Mathf.CeilToInt(items.Count / 12f);
        currentDisplayItems.Clear();
        currentDisplayItems.AddRange(items);

        int remainder = 12 * totalPages - currentDisplayItems.Count;
        for (int i = 0; i < remainder; i++) currentDisplayItems.Add(null);

        RefreshDisplay();

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(defaultSelectedSlot.gameObject);
    }

    public void RefreshDisplay()
    {
        totalPages = Mathf.CeilToInt(currentDisplayItems.Count / 12f);
        if (currentPage >= totalPages) currentPage = 0;

        int startIndex = currentPage * 12;

        for (int i = 0; i < slots.Length; i++)
        {
            int itemIndex = startIndex + i;

            if (itemIndex < currentDisplayItems.Count && currentDisplayItems[itemIndex] != null)
            {
                InventoryItem item = currentDisplayItems[itemIndex];
                slots[i].itemImage.sprite = item.icon;
                slots[i].itemText.text = item.itemName;
                slots[i].quantityText.text = item.quantity.ToString();
                slots[i].itemImage.color = Color.white;
            }
            else
            {
                slots[i].itemImage.sprite = defaultSprite;
                slots[i].itemText.text = defaultText;
                slots[i].quantityText.text = "";
                slots[i].itemImage.color = new Color(1, 1, 1, 0.5f);
            }

            Button slotButton = slots[i].itemImage.GetComponentInParent<Button>();
            if (slotButton != null)
            {
                int index = i;
                slotButton.onClick.RemoveAllListeners();
                slotButton.onClick.AddListener(() => OnClickIngredient(index));
            }
        }

        pageText.text = $"{currentPage + 1}/{Mathf.Max(totalPages, 1)}";
    }

    public void OnClickIngredient(int slotIndex)
    {
        int itemIndex = currentPage * 12 + slotIndex;
        if (itemIndex >= currentDisplayItems.Count || currentDisplayItems[itemIndex] == null) return;

        InventoryItem selectedItem = currentDisplayItems[itemIndex];
        if (selectedItem.quantity <= 0) return;

        cookingManager.TryAddIngredient(selectedItem);

        // 數量扣完就從顯示中移除
        if (selectedItem.quantity <= 0)
        {
            currentDisplayItems[itemIndex] = null;
        }

        RefreshDisplay();
    }

    private void ChangePage(int direction)
    {
        currentPage += direction;
        totalPages = Mathf.CeilToInt(currentDisplayItems.Count / 12f);

        if (currentPage < 0)
            currentPage = totalPages > 0 ? totalPages - 1 : 0;
        else if (currentPage >= totalPages)
            currentPage = 0;

        RefreshDisplay();
    }

    private bool IsPointerOverSlot()
    {
        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            if (result.gameObject.GetComponent<InventorySlotUI>() != null)
            {
                return true; // 有指到格子
            }
        }
        return false;
    }

    public void OnPointerHover(int slotIndex)
    {
        // ❗這裡不需顯示詳細說明，但若你日後想顯示效果圖或簡略說明可擴充
        // 目前可以用來做高亮選取、特效等
    }

    public void RebuildDisplayData(List<InventoryItem> items, int totalPages)
    {
        this.totalPages = totalPages;
        currentDisplayItems.Clear();
        currentDisplayItems.AddRange(items);

        int remainder = 12 * totalPages - currentDisplayItems.Count;
        for (int i = 0; i < remainder; i++) currentDisplayItems.Add(null);

        RefreshDisplay();
    }

    public void CloseCookingUI()
    {
        isOpen = false;
        currentDisplayItems.Clear();
        cookingManager.ClearIngredients(refund: true);
        PlayerInputHandler.Instance.EnablePlayerInput();
        PlayerInputHandler.Instance.DisableUIMapInput();
        MouseVisibilityManager.Instance.enableDynamicMouse = false;
        MouseVisibilityManager.Instance.HideCursorImmediate();
        EventSystem.current.SetSelectedGameObject(null);
        cookingPanel.SetActive(false);
    }
}
