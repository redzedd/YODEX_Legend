using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventoryDisplay : MonoBehaviour
{
    // ===== 基本設定 =====
    public enum Category { Ingredients, Food, Tool, KeyItem }
    private Category currentCategory = Category.Ingredients;

    private const int COLUMNS = 3;
    private const int ROWS = 4;
    private const int PAGE_SIZE = COLUMNS * ROWS;

    // ===== 參考到的 UI 元件 =====
    [Header("Option Menu")]
    public GameObject foodOptionMenu;
    public Button eatButton, cancelButton;

    [Header("Description UI")]
    public GameObject descriptionPanel;
    public Text itemNameText;
    public Text descriptionText;
    public Text effectDescriptionText;
    public Image fullSizeImageDisplay;
    public Button defaultSelectedSlot;
    public Text categoryText;
    public Text pageText;

    [Header("Root UI")]
    public GameObject inventoryPanel;
    
    [System.Serializable]
    public class InventorySlot
    {
        public Button itemButton;
        public Image itemImage;
        public Text itemText;
        public Text quantityText;
    }
    [Header("Slots (3x4)")]
    public InventorySlot[] slots = new InventorySlot[PAGE_SIZE];

    [Header("Default visuals")]
    public Sprite defaultSprite;
    public string defaultText = "？？？";

    // ===== 狀態 =====
    private bool isInventoryOpen = false;
    private bool isFoodMenuOpen = false;
    private bool isConsuming = false;
    private Button lastClickedSlotButton;

    private int currentPage = 0;
    private int totalPages = 1;

    private readonly List<InventoryItem> currentCategoryItems = new();
    private readonly List<InventoryItem?> currentDisplayItems = new();

    private InventoryItem selectedFoodItem;
    private int selectedFoodIndex;

    private readonly Button[] slotButtons = new Button[PAGE_SIZE];

    public static InventoryDisplay Instance { get; private set; }
    public event Action<ItemData> OnItemConsumed;

    // ===== 生命週期 =====
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        // 預設關閉 UIMap；開背包時再開
        PlayerInputHandler.Instance.DisableUIMapInput();

        eatButton.onClick.AddListener(EatSelectedFood);
        cancelButton.onClick.AddListener(CloseFoodOptionMenu);
        OnItemConsumed += ApplyItemEffects;

        RefreshCategoryItems();
        BuildDisplayBuffer();
    }

    private void Update()
    {
        // 翻頁鍵
        if (PlayerInputHandler.Instance.NextPageTriggered) ChangePage(+1);
        else if (PlayerInputHandler.Instance.PrevPageTriggered) ChangePage(-1);

        // 統一開關背包（整合防抖＆封鎖，同時附除錯）
        PlayerInputHandler.InventoryToggleIntent intent;
        if (PlayerInputHandler.Instance.TryToggleInventory(isInventoryOpen, out intent))
        {
            if (intent == PlayerInputHandler.InventoryToggleIntent.Open) OpenInventory();
            if (intent == PlayerInputHandler.InventoryToggleIntent.Close) CloseInventory();
        }

        // 若食物選單開著，處理返回
        if (isFoodMenuOpen)
        {
            if (PlayerInputHandler.Instance.CancelTriggered || PlayerInputHandler.Instance.OpenInventoryTriggered)
                CloseFoodOptionMenu();
            return;
        }

        // 滑鼠懸浮在格子上時停用 UI 導航（避免搖桿/方向鍵搶焦點）
        EventSystem.current.sendNavigationEvents = !IsPointerOverSlot();

        // 排序（依 itemID）
        if (PlayerInputHandler.Instance.UseItemTriggered)
        {
            InventoryManager.Instance.SortInventory();
            UpdateDisplay();
        }
    }

    // ===== 對外刷新 =====
    public void UpdateDisplay()
    {
        RefreshCategoryItems();
        BuildDisplayBuffer();

        totalPages = Mathf.Max(1, Mathf.CeilToInt(currentCategoryItems.Count / (float)PAGE_SIZE));
        if (currentPage >= totalPages) currentPage = 0;

        RenderPage();
        BindSlotButtons();

        categoryText.text = GetCategoryName(currentCategory);
        pageText.text = $"{currentPage + 1}/{Mathf.Max(totalPages, 1)}";

        RefreshCurrentDescription();
    }

    // ===== 資料面 =====
    private void RefreshCategoryItems()
    {
        currentCategoryItems.Clear();
        currentCategoryItems.AddRange(InventoryManager.Instance.GetItemsByCategory(currentCategory));
        totalPages = Mathf.Max(1, Mathf.CeilToInt(currentCategoryItems.Count / (float)PAGE_SIZE));
    }

    private void BuildDisplayBuffer()
    {
        currentDisplayItems.Clear();
        currentDisplayItems.AddRange(currentCategoryItems);

        int padded = PAGE_SIZE * totalPages - currentDisplayItems.Count;
        for (int i = 0; i < padded; i++) currentDisplayItems.Add(null);
    }

    // ===== 呈現面 =====
    private void RenderPage()
    {
        int startIndex = currentPage * PAGE_SIZE;

        for (int i = 0; i < slots.Length; i++)
        {
            int itemIndex = startIndex + i;
            var slot = slots[i];

            //// 回寫 slotIndex & 指向 display（給 InventorySlotUI 用）
            //var slotUI = slot.itemImage.GetComponentInParent<InventorySlotUI>();
            //if (slotUI != null)
            //{
            //    slotUI.slotIndex = i;
            //    slotUI.inventoryDisplay = this;
            //}

            var slotUI = slot.itemButton != null ? slot.itemButton.GetComponent<InventorySlotUI>() : null;
            if (slotUI != null)
            {
                slotUI.slotIndex = i;
                slotUI.inventoryDisplay = this;
            }

            if (itemIndex < currentDisplayItems.Count && currentDisplayItems[itemIndex] != null)
            {
                var item = currentDisplayItems[itemIndex];
                slot.itemImage.sprite = item.icon;
                slot.itemText.text = item.itemName;
                slot.quantityText.text = (item.quantity > 0) ? item.quantity.ToString() : "";
                slot.itemImage.color = (item.quantity > 0) ? Color.white : new Color(1, 1, 1, 0.5f);
            }
            else
            {
                slot.itemImage.sprite = defaultSprite;
                slot.itemText.text = defaultText;
                slot.quantityText.text = "";
                slot.itemImage.color = new Color(1, 1, 1, 0.5f);
            }
        }
    }

    private void BindSlotButtons()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            int indexOnPage = i;

            var slotButton = slots[i].itemButton;   // ★ 直接用 Inspector 指定的 Button
            slotButtons[i] = slotButton;        // 快取，供翻頁後聚焦用

            if (slotButton == null)
            {
                Debug.LogError($"[INV] Slots[{i}] 沒有指定 button，請在 Inspector 設定。");
                continue;
            }

            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(() =>
            {
                lastClickedSlotButton = slotButton;
                ShowItemDescription(indexOnPage);
                OnClickFoodItem(indexOnPage);   // 需要點即開食用選單就保留
            });
        }
    }

    private Button ResolveSlotButtonSafe(int i)
    {
        if (i < 0 || i >= slots.Length) return null;
        var img = slots[i]?.itemImage;
        if (img == null) return null;

        // 1) 父系
        var btn = img.GetComponentInParent<Button>(true);
        if (btn != null) return btn;

        // 2) 自己
        btn = img.GetComponent<Button>();
        if (btn != null) return btn;

        // 3) 子孫（包含未啟用）
        btn = img.GetComponentInChildren<Button>(true);
        return btn;
    }

    // ===== 開/關背包 =====
    private void OpenInventory()
    {
        Debug.Log("[INV] OpenInventory()");
        isInventoryOpen = true;
        inventoryPanel.SetActive(true);
        descriptionPanel.SetActive(false);

        PlayerInputHandler.Instance.DisablePlayerInput();
        PlayerInputHandler.Instance.EnableUIMapInput();
        MouseVisibilityManager.Instance.enableDynamicMouse = true;
        Time.timeScale = 0f;

        currentPage = 0;
        UpdateDisplay();

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(defaultSelectedSlot.gameObject);
    }

    private void CloseInventory()
    {
        Debug.Log("[INV] CloseInventory()");
        isInventoryOpen = false;
        inventoryPanel.SetActive(false);
        descriptionPanel.SetActive(false);

        currentDisplayItems.Clear();
        EventSystem.current.SetSelectedGameObject(null);

        PlayerInputHandler.Instance.EnablePlayerInput();
        PlayerInputHandler.Instance.DisableUIMapInput();
        MouseVisibilityManager.Instance.enableDynamicMouse = false;
        MouseVisibilityManager.Instance.HideCursorImmediate();
        Time.timeScale = 1f;
    }

    // ===== 分頁 / 分類 =====
    private void ChangePage(int direction)
    {
        if (isFoodMenuOpen) return;

        currentPage += direction;

        if (currentPage < 0)
        {
            CycleCategory(-1);
            currentPage = totalPages - 1;
        }
        else if (currentPage >= totalPages)
        {
            CycleCategory(+1);
            currentPage = 0;
        }

        RenderPage();
        BindSlotButtons();
        pageText.text = $"{currentPage + 1}/{Mathf.Max(totalPages, 1)}";
        RefreshCurrentDescription();
        ClearDescription();
    }

    private void CycleCategory(int step)
    {
        int categoryCount = Enum.GetNames(typeof(Category)).Length;
        currentCategory = (Category)(((int)currentCategory + step + categoryCount) % categoryCount);

        RefreshCategoryItems();
        BuildDisplayBuffer();

        categoryText.text = GetCategoryName(currentCategory);
    }

    // ===== 描述面板 =====

    private void SetDescriptionVisible(bool visible)
    {
        if (descriptionPanel != null && descriptionPanel.activeSelf != visible)
            descriptionPanel.SetActive(visible);
    }

    private void ClearDescription()
    {
        // 隱藏並清空文字/圖像
        SetDescriptionVisible(false);
        fullSizeImageDisplay.gameObject.SetActive(false);
        itemNameText.text = "";
        descriptionText.text = "";
        effectDescriptionText.text = "";
    }

    public void ShowItemDescription(int slotIndex)
    {
        int itemIndex = currentPage * PAGE_SIZE + slotIndex;

        if (itemIndex < currentDisplayItems.Count && currentDisplayItems[itemIndex] != null)
        {
            var item = currentDisplayItems[itemIndex];

            SetDescriptionVisible(true); // ★ 有物品時才顯示
            itemNameText.text = item.itemName;
            descriptionText.text = item.itemData.description;
            effectDescriptionText.text = item.itemData.effectDescription;

            if (item.itemData.fullSizeImage != null)
            {
                fullSizeImageDisplay.gameObject.SetActive(true);
                fullSizeImageDisplay.sprite = item.itemData.fullSizeImage;
            }
            else
            {
                fullSizeImageDisplay.gameObject.SetActive(false);
            }
        }
        else
        {
            ClearDescription(); // ★ 沒物品就隱藏
        }
    }

    private void RefreshCurrentDescription()
    {
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected != null)
        {
            var slotUI = selected.GetComponent<InventorySlotUI>();
            if (slotUI != null) { ShowItemDescription(slotUI.slotIndex); return; }
        }
        TryRefreshHoveredSlot();
    }

    private void TryRefreshHoveredSlot()
    {
        var pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };
        var raycastResults = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        foreach (var result in raycastResults)
        {
            var slotUI = result.gameObject.GetComponent<InventorySlotUI>();
            if (slotUI != null)
            {
                ShowItemDescription(slotUI.slotIndex);
                return;
            }
        }

        // 滑鼠不在任何格子上：隱藏描述區
        ClearDescription();
    }

    private bool IsPointerOverSlot()
    {
        var pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        foreach (var r in results)
            if (r.gameObject.GetComponent<InventorySlotUI>() != null) return true;
        return false;
    }

    // ===== 食用 / 套用效果 =====

    public void OnClickFoodItem(int localSlotIndex)
    {
        // 轉換成整體索引（含當前頁）
        int globalIndex = currentPage * PAGE_SIZE + localSlotIndex;

        // 索引範圍 & 物件存在檢查
        if (globalIndex < 0 || globalIndex >= currentDisplayItems.Count) return;
        if (currentDisplayItems[globalIndex] == null) return;

        var clickedItem = currentDisplayItems[globalIndex];
        if (clickedItem.quantity <= 0) return;

        // 記錄選中
        selectedFoodItem = clickedItem;
        selectedFoodIndex = globalIndex;

        // 只有 食材/料理 類別才打開「食用/取消」選單
        if (selectedFoodItem.itemData.category == Category.Ingredients ||
            selectedFoodItem.itemData.category == Category.Food)
        {
            // 打開子選單
            foodOptionMenu.SetActive(true);
            isFoodMenuOpen = true;

            // 決定 Eat 是否可點：
            // 規則：未滿血且有 healAmount，或 有 buffDefinition（有 Buff 就允許吃）
            bool isMaxHealth = PlayerHealth.Instance != null &&
                               PlayerHealth.Instance.currentHealth >= PlayerHealth.Instance.maxHealth;

            bool canConsume =
                (selectedFoodItem.itemData.healAmount > 0 && !isMaxHealth) ||
                (selectedFoodItem.itemData.buffDefinition != null);

            eatButton.interactable = canConsume;
            cancelButton.interactable = true;

            // 預設選取按鈕（可吃則預選 Eat，否則選 Cancel）
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(canConsume ? eatButton.gameObject : cancelButton.gameObject);
        }
    }

    private void EatSelectedFood()
    {
        if (isConsuming) return;
        if (selectedFoodItem == null) return;

        if (selectedFoodItem.itemData.category == Category.Ingredients ||
            selectedFoodItem.itemData.category == Category.Food)
        {
            OnItemConsumed?.Invoke(selectedFoodItem.itemData);

            int globalIndex = selectedFoodIndex;

            InventoryManager.Instance.RemoveItem(selectedFoodItem.itemData, 1);

            if (selectedFoodItem.quantity <= 0)
            {
                if (globalIndex >= 0 && globalIndex < currentDisplayItems.Count)
                    currentDisplayItems[globalIndex] = null;

                selectedFoodItem = null;
                selectedFoodIndex = -1;
                CloseFoodOptionMenu();
            }

            UpdateDisplay();

            if (selectedFoodItem != null)
            {
                bool isMaxHealth = PlayerHealth.Instance.currentHealth >= PlayerHealth.Instance.maxHealth;
                bool canConsume =
                    (selectedFoodItem.itemData.healAmount > 0 && !isMaxHealth) ||
                    (selectedFoodItem.itemData.buffDefinition != null);
                eatButton.interactable = canConsume;

                ShowItemDescription(globalIndex % PAGE_SIZE);
            }
        }
    }

    private void CloseFoodOptionMenu()
    {
        foodOptionMenu.SetActive(false);
        isFoodMenuOpen = false;

        EventSystem.current.SetSelectedGameObject(null);
        if (lastClickedSlotButton != null)
            EventSystem.current.SetSelectedGameObject(lastClickedSlotButton.gameObject);
        else
            EventSystem.current.SetSelectedGameObject(defaultSelectedSlot.gameObject);
    }

    private void ApplyItemEffects(ItemData data)
    {
        if (data.healAmount > 0 && PlayerHealth.Instance != null)
            PlayerHealth.Instance.HealDirect(data.healAmount);

        if (data.buffDefinition != null)
            BuffManager.GetOrCreate().Apply(data.buffDefinition, data.effectTier);
    }

    public void SetLastClickedSlotButton(Button button) => lastClickedSlotButton = button;

    public bool IsInventoryOpen() => isInventoryOpen;

    // ===== ★ 關鍵：在格子上左右移動的「跨分類翻頁」 =====
    public bool OnSlotMove(int slotIndex, MoveDirection dir)
    {
        int col = slotIndex % COLUMNS;   // 0,1,2
        int row = slotIndex / COLUMNS;   // 0~3

        // 往右：在最右欄
        if (dir == MoveDirection.Right && col == COLUMNS - 1)
        {
            if (currentPage < totalPages - 1)
            {
                // 下一頁，同一列最左欄
                currentPage++;
                RedrawAndFocus(row * COLUMNS + 0);
            }
            else
            {
                // 已是最後一頁 → 下一分類第一頁，同一列最左欄
                CycleCategory(+1);
                currentPage = 0;
                RedrawAndFocus(row * COLUMNS + 0);
            }
            return true;
        }

        // 往左：在最左欄
        if (dir == MoveDirection.Left && col == 0)
        {
            if (currentPage > 0)
            {
                // 上一頁，同一列最右欄
                currentPage--;
                RedrawAndFocus(row * COLUMNS + (COLUMNS - 1));
            }
            else
            {
                // 已是第一頁 → 上一分類最後一頁，同一列最右欄
                CycleCategory(-1);
                currentPage = totalPages - 1;
                RedrawAndFocus(row * COLUMNS + (COLUMNS - 1));
            }
            return true;
        }

        return false;
    }

    private void RedrawAndFocus(int localSlotIndex)
    {
        RenderPage();
        BindSlotButtons();
        pageText.text = $"{currentPage + 1}/{Mathf.Max(totalPages, 1)}";

        // 先清空當前選取，下一幀再指定，避免同幀 Move 導航覆蓋
        EventSystem.current.SetSelectedGameObject(null);

        // 邊界保護
        if (localSlotIndex < 0 || localSlotIndex >= slots.Length)
            localSlotIndex = 0;

        StartCoroutine(FocusSlotNextFrame(localSlotIndex));
        ClearDescription();
    }

    private System.Collections.IEnumerator FocusSlotNextFrame(int localSlotIndex)
    {
        // 最多嘗試 3 幀（有些 UI 啟用/布局會延一幀才 ready）
        for (int tries = 0; tries < 3; tries++)
        {
            yield return null;

            Button targetBtn = null;

            // 1) 直接用快取的 Button（若快取為 null，再即時解析一次）
            if (localSlotIndex >= 0 && localSlotIndex < slotButtons.Length)
                targetBtn = slotButtons[localSlotIndex] ?? ResolveSlotButtonSafe(localSlotIndex);

            // 2) targetBtn 仍然不行 → 試著即時解析本頁第 1 格（但不要立刻用快取的 slotButtons[0]，避免硬鎖左上）
            if (targetBtn == null || !targetBtn.gameObject.activeInHierarchy || !targetBtn.interactable)
                targetBtn = ResolveSlotButtonSafe(0);

            // 3) 再不行，用 defaultSelectedSlot（通常也是某個格的 Button）
            if ((targetBtn == null || !targetBtn.gameObject.activeInHierarchy || !targetBtn.interactable) && defaultSelectedSlot != null)
                targetBtn = defaultSelectedSlot;

            // 4) 最後備援：整個背包面板底下找第一個 Selectable
            if (targetBtn == null || !targetBtn.gameObject.activeInHierarchy || !targetBtn.interactable)
            {
                var anySelectable = inventoryPanel != null ? inventoryPanel.GetComponentInChildren<Selectable>(true) : null;
                if (anySelectable != null) targetBtn = anySelectable as Button;
            }

            if (targetBtn != null)
            {
                // 設焦點
                EventSystem.current.SetSelectedGameObject(null);
                EventSystem.current.SetSelectedGameObject(targetBtn.gameObject);

                // 若我們選到的就是某一個格的 Button → 同步描述
                for (int i = 0; i < slotButtons.Length; i++)
                {
                    // 注意：slotButtons[i] 可能還沒被填，保底解析一次比較
                    var btn = slotButtons[i] ?? ResolveSlotButtonSafe(i);
                    if (btn == targetBtn)
                    {
                        // 除錯看我們鎖到哪一格
                        int col = i % COLUMNS;
                        int row = i / COLUMNS;
                        Debug.Log($"[INV] Focus 到本頁槽位 index={i} (row={row}, col={col})");

                        ShowItemDescription(i);
                        yield break;
                    }
                }

                // 不是槽位（可能是預設按鈕）就直接結束
                Debug.Log("[INV] Focus 到非槽位 Button（fallback）");
                yield break;
            }
        }

        // 三幀內沒成功 → 清空描述避免殘影
        fullSizeImageDisplay.gameObject.SetActive(false);
        itemNameText.text = "";
        descriptionText.text = "";
        effectDescriptionText.text = "";
    }

    // ===== 工具 =====
    private string GetCategoryName(Category category)
    {
        switch (category)
        {
            case Category.Ingredients: return "食材";
            case Category.Food: return "料理";
            case Category.Tool: return "工具";
            case Category.KeyItem: return "關鍵物品";
            default: return "未知";
        }
    }
}
