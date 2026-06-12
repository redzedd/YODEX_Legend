using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using GAS.UI;
using GAS.UI.Inventory;
using Player.Input;

public class CookingInventoryDisplay : MonoBehaviour
{
    public GameObject cookingPanel;
    public Button defaultSelectedSlot;
    public Text pageText;
    public CookingManager cookingManager;
    [SerializeField] private CookingInventoryAnimator _cookingAnimator;

    public InventoryDisplay.InventorySlot[] slots = new InventoryDisplay.InventorySlot[12];
    public Sprite defaultSprite;
    public string defaultText = "？？？";

    private int currentPage = 0;
    private int totalPages = 1;

    private List<InventoryItem> ingredientItems = new();
    private List<InventoryItem> currentDisplayItems = new();
    private bool isOpen = false;
    private bool _wasCardActive;
    public bool IsOpen => isOpen;

    private void Start()
    {
        InitializeSlotButtons();
    }

    private void InitializeSlotButtons()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].itemButton == null) continue;
            int index = i;
            slots[i].itemButton.onClick.RemoveAllListeners();
            slots[i].itemButton.onClick.AddListener(() => OnClickIngredient(index));
        }
    }

    private void Update()
    {
        if (SystemInputReader.Instance == null) return;
        // ⭐ 滑鼠控制切換判定
        EventSystem.current.sendNavigationEvents = !IsPointerOverSlot();
        if (!isOpen) return;
        // 字卡顯示期間暫停烹飪 UI 的輸入處理 —
        // 否則玩家按鍵關閉字卡時, CancelTriggered 同幀也會被這裡讀到造成誤觸發。
        bool cardActive = NewItemDisplayUI.IsAnyCardActive;
        if (cardActive)
        {
            _wasCardActive = true;
            return;
        }
        // 字卡剛關閉那一幀:重抓 inventory + 重新對焦預設按鈕 + 清掉殘留的觸發旗標,
        // 避免「煮完字卡關掉後 cooking UI 看起來開著但點不動」的卡關狀態。
        if (_wasCardActive)
        {
            _wasCardActive = false;
            RebuildFromInventory();
            EventSystem.current.SetSelectedGameObject(null);
            if (defaultSelectedSlot != null)
                EventSystem.current.SetSelectedGameObject(defaultSelectedSlot.gameObject);
            SystemInputReader.Instance.ResetTriggeredFlags();
            // 額外封鎖 Cancel 一小段時間 — 防止用同一個按鍵收掉字卡的那次按壓
            // 同幀又被讀成 Cancel 把整個 cooking UI 也關掉。
            SystemInputReader.Instance.BlockCancelFor(0.2f);
            return;
        }
        if (SystemInputReader.Instance.NextPageTriggered)
            ChangePage(1);
        else if (SystemInputReader.Instance.PrevPageTriggered)
            ChangePage(-1);
        if (SystemInputReader.Instance.CancelTriggered)
        {
            if (cookingManager.HasIngredients())
            {
                cookingManager.ClearIngredients(refund: true);
                Debug.Log("🧹 已清空鍋中材料（第一次取消）");
            }
            else
            {
                CloseCookingUI();
            }
        }
    }

    public void OpenUI()
    {
        isOpen = true;
        // 立即停用玩家輸入，避免開啟後第一幀仍可操控角色
        SystemInputReader.Instance.DisablePlayerInput();
        // 暫時封鎖 Cancel — 若互動鍵與 Cancel 鍵共用同一個物理鍵,
        // 啟用 UIMap 瞬間該鍵的「正在被按住」狀態會立刻 fire Cancel,造成「剛開就關」。
        SystemInputReader.Instance.BlockCancelFor(0.25f);
        SystemInputReader.Instance.EnableUIMapInput();
        MouseVisibilityManager.Instance.enableDynamicMouse = true;
        Time.timeScale = 0f;
        cookingPanel.SetActive(true);
        if (_cookingAnimator != null) _cookingAnimator.PlayOpen();

        if (InventoryManager.Instance == null)
        {
            Debug.LogWarning("InventoryManager 尚未初始化，Cooking UI 暫緩初始化");
            return;
        }

        List<InventoryItem> items = InventoryManager.Instance.GetItemsByCategory(InventoryDisplay.Category.Ingredients);
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

        if (_cookingAnimator != null)
        {
            _cookingAnimator.PlayPageTransition(direction, RefreshDisplay);
        }
        else
        {
            RefreshDisplay();
        }
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

    /// <summary>從 InventoryManager 重新抓取食材並重建顯示資料 —
    /// 字卡關閉後 / 連續料理之間呼叫,避免 currentDisplayItems 殘留 stale 資料。</summary>
    private void RebuildFromInventory()
    {
        if (InventoryManager.Instance == null) return;
        List<InventoryItem> items = InventoryManager.Instance.GetItemsByCategory(InventoryDisplay.Category.Ingredients);
        items.Sort((a, b) => a.itemData.itemID.CompareTo(b.itemData.itemID));
        int newTotalPages = Mathf.Max(1, Mathf.CeilToInt(items.Count / 12f));
        if (currentPage >= newTotalPages) currentPage = 0;
        RebuildDisplayData(items, newTotalPages);
    }

    public void CloseCookingUI(bool refundIngredients = true)
    {
        isOpen = false;
        currentDisplayItems.Clear();
        // 料理成功時材料已用掉,不能退還;玩家手動取消才需退還
        if (refundIngredients)
            cookingManager.ClearIngredients(refund: true);
        Time.timeScale = 1f;
        // 延後啟用 Player ActionMap — 等玩家放開關閉鍵再啟用,
        // 否則同一個物理鍵會同幀觸發 Player.Jump / Player.Interact (跳躍 / 互動)。
        SystemInputReader.Instance.EnablePlayerInputDeferred(0.5f);
        // 清除因 Unity Input System 狀態恢復而殘留的 Triggered 旗標
        SystemInputReader.Instance.ResetTriggeredFlags();
        SystemInputReader.Instance.DisableUIMapInput();
        MouseVisibilityManager.Instance.enableDynamicMouse = false;
        MouseVisibilityManager.Instance.HideCursorImmediate();
        EventSystem.current.SetSelectedGameObject(null);
        if (_cookingAnimator != null)
            _cookingAnimator.PlayClose(() => cookingPanel.SetActive(false));
        else
            cookingPanel.SetActive(false);
    }
}
