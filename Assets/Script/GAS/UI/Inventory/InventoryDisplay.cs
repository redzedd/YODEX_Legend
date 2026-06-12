using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Player.Input;
namespace GAS.UI.Inventory
{
    /// <summary>
    /// 背包 UI 顯示器 — 管理面板開關、分頁、分類切換、格子渲染、描述面板
    /// 食用邏輯委派給 InventoryFoodConsumer，動畫委派給 InventoryAnimator
    /// </summary>
    public class InventoryDisplay : MonoBehaviour
    {
        #region 常數與列舉

        public enum Category { Ingredients, Food, Tool, KeyItem }

        private const int COLUMNS = 3;
        private const int ROWS = 4;
        private const int PAGE_SIZE = COLUMNS * ROWS;

        #endregion

        #region Serialized Fields

        [Header("動畫 & 食用系統")]
        [SerializeField] private InventoryAnimator _animator;
        [SerializeField] private InventoryFoodConsumer _foodConsumer;

        [Header("描述面板 UI")]
        [SerializeField] private GameObject _descriptionPanel;
        [SerializeField] private Text _itemNameText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Text _effectDescriptionText;
        [SerializeField] private Image _fullSizeImageDisplay;
        [SerializeField] private Button _defaultSelectedSlot;
        [SerializeField] private Text _categoryText;
        [SerializeField] private Text _pageText;

        [Header("根面板")]
        [SerializeField] private GameObject _inventoryPanel;

        [Serializable]
        public class InventorySlot
        {
            public Button itemButton;
            public Image itemImage;
            public Text itemText;
            public Text quantityText;
        }

        [Header("格子 (3x4)")]
        [SerializeField] private InventorySlot[] _slots = new InventorySlot[PAGE_SIZE];

        [Header("預設外觀")]
        [SerializeField] private Sprite _defaultSprite;
        [SerializeField] private string _defaultText = "？？？";

        #endregion

        #region Private Fields

        private Category _currentCategory = Category.Ingredients;
        private bool _isInventoryOpen;
        private int _currentPage;
        private int _totalPages = 1;
        private Button _lastClickedSlotButton;
        private readonly List<InventoryItem> _currentCategoryItems = new();
        private readonly List<InventoryItem> _currentDisplayItems = new();
        private readonly Button[] _slotButtons = new Button[PAGE_SIZE];

        #endregion

        #region 屬性

        public static InventoryDisplay Instance { get; private set; }

        /// <summary>供 InventorySlotUI.OnSlotMove 存取格子陣列</summary>
        public InventorySlot[] Slots => _slots;

        #endregion

        #region 生命週期

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            SystemInputReader.Instance.DisableUIMapInput();
            // 訂閱背包資料變更
            if (InventoryManager.Instance != null)
                InventoryManager.Instance.OnInventoryChanged += UpdateDisplay;
            // 訂閱食用選單狀態
            if (_foodConsumer != null)
                _foodConsumer.OnFoodMenuToggled += OnFoodMenuToggled;
            RefreshCategoryItems();
            BuildDisplayBuffer();
        }

        private void OnDestroy()
        {
            if (InventoryManager.Instance != null)
                InventoryManager.Instance.OnInventoryChanged -= UpdateDisplay;
            if (_foodConsumer != null)
                _foodConsumer.OnFoodMenuToggled -= OnFoodMenuToggled;
        }

        private void Update()
        {
            if (SystemInputReader.Instance == null) return;
            // 食用選單開著時優先攔截輸入，避免取消鍵直接關閉整個背包
            if (_foodConsumer != null && _foodConsumer.IsFoodMenuOpen)
            {
                if (SystemInputReader.Instance.CancelTriggered || SystemInputReader.Instance.OpenInventoryTriggered)
                    _foodConsumer.CloseFoodOptionMenu();
                return;
            }
            if (SystemInputReader.Instance.NextPageTriggered) ChangePage(+1);
            else if (SystemInputReader.Instance.PrevPageTriggered) ChangePage(-1);
            // 開關背包
            // 背包未開啟時，若玩家輸入被其他 UI 佔用（如烹飪面板），不允許開啟
            if (!_isInventoryOpen && !SystemInputReader.Instance.IsPlayerInputEnabled) return;
            SystemInputReader.InventoryToggleIntent intent;
            if (SystemInputReader.Instance.TryToggleInventory(_isInventoryOpen, out intent))
            {
                if (intent == SystemInputReader.InventoryToggleIntent.Open) OpenInventory();
                if (intent == SystemInputReader.InventoryToggleIntent.Close) CloseInventory();
            }
            // 滑鼠懸浮在格子上時停用 UI 導航
            EventSystem.current.sendNavigationEvents = !IsPointerOverSlot();
            // 排序
            if (SystemInputReader.Instance.UseItemTriggered)
            {
                InventoryManager.Instance.SortInventory();
            }
        }

        #endregion

        #region 公開方法

        /// <summary>刷新顯示（供事件回呼或外部強制呼叫）</summary>
        public void UpdateDisplay()
        {
            RefreshCategoryItems();
            BuildDisplayBuffer();
            _totalPages = Mathf.Max(1, Mathf.CeilToInt(_currentCategoryItems.Count / (float)PAGE_SIZE));
            if (_currentPage >= _totalPages) _currentPage = 0;
            RenderPage();
            BindSlotButtons();
            _categoryText.text = GetCategoryName(_currentCategory);
            _pageText.text = $"{_currentPage + 1}/{Mathf.Max(_totalPages, 1)}";
            // 食用選單開啟中時焦點在選單按鈕而非格子，跳過刷新避免誤清說明欄
            // 選單關閉後由 RestoreFocusAfterFoodMenu 負責刷新說明欄
            if (_foodConsumer == null || !_foodConsumer.IsFoodMenuOpen)
                RefreshCurrentDescription();
        }

        /// <summary>顯示指定格子的物品描述</summary>
        public void ShowItemDescription(int slotIndex)
        {
            int itemIndex = _currentPage * PAGE_SIZE + slotIndex;
            if (itemIndex < _currentDisplayItems.Count && _currentDisplayItems[itemIndex] != null)
            {
                InventoryItem item = _currentDisplayItems[itemIndex];
                SetDescriptionVisible(true);
                _itemNameText.text = item.itemName;
                _descriptionText.text = item.itemData.description;
                _effectDescriptionText.text = item.itemData.effectDescription;
                if (item.itemData.fullSizeImage != null)
                {
                    _fullSizeImageDisplay.gameObject.SetActive(true);
                    _fullSizeImageDisplay.sprite = item.itemData.fullSizeImage;
                }
                else
                {
                    _fullSizeImageDisplay.gameObject.SetActive(false);
                }
            }
            else
            {
                ClearDescription();
            }
        }

        /// <summary>記錄最後點擊的格子按鈕（供焦點恢復）</summary>
        public void SetLastClickedSlotButton(Button button) => _lastClickedSlotButton = button;

        /// <summary>背包是否已開啟</summary>
        public bool IsInventoryOpen() => _isInventoryOpen;

        /// <summary>跨分類翻頁（由 InventorySlotUI.OnMove 呼叫）</summary>
        public bool OnSlotMove(int slotIndex, MoveDirection dir)
        {
            int col = slotIndex % COLUMNS;
            int row = slotIndex / COLUMNS;
            if (dir == MoveDirection.Right && col == COLUMNS - 1)
            {
                if (_currentPage < _totalPages - 1)
                {
                    _currentPage++;
                    RedrawAndFocus(row * COLUMNS, +1);
                }
                else
                {
                    CycleCategory(+1);
                    _currentPage = 0;
                    RedrawAndFocus(row * COLUMNS, +1);
                }
                return true;
            }
            if (dir == MoveDirection.Left && col == 0)
            {
                if (_currentPage > 0)
                {
                    _currentPage--;
                    RedrawAndFocus(row * COLUMNS + (COLUMNS - 1), -1);
                }
                else
                {
                    CycleCategory(-1);
                    _currentPage = _totalPages - 1;
                    RedrawAndFocus(row * COLUMNS + (COLUMNS - 1), -1);
                }
                return true;
            }
            return false;
        }

        /// <summary>取得指定全域索引的物品（供 FoodConsumer 使用）</summary>
        public InventoryItem GetItemAtGlobalIndex(int globalIndex)
        {
            if (globalIndex < 0 || globalIndex >= _currentDisplayItems.Count) return null;
            return _currentDisplayItems[globalIndex];
        }

        /// <summary>恢復焦點到上次點擊的格子或預設格子，並刷新說明欄</summary>
        public void RestoreFocusAfterFoodMenu()
        {
            EventSystem.current.SetSelectedGameObject(null);
            Button targetBtn = _lastClickedSlotButton != null ? _lastClickedSlotButton : _defaultSelectedSlot;
            if (targetBtn == null) return;
            EventSystem.current.SetSelectedGameObject(targetBtn.gameObject);
            // 找到對應格子索引並刷新說明欄
            for (int i = 0; i < _slotButtons.Length; i++)
            {
                if (_slotButtons[i] == targetBtn)
                {
                    ShowItemDescription(i);
                    return;
                }
            }
        }

        #endregion

        #region 開/關背包

        private void OpenInventory()
        {
            _isInventoryOpen = true;
            _inventoryPanel.SetActive(true);
            _descriptionPanel.SetActive(false);
            SystemInputReader.Instance.DisablePlayerInput();
            SystemInputReader.Instance.EnableUIMapInput();
            MouseVisibilityManager.Instance.enableDynamicMouse = true;
            Time.timeScale = 0f;
            _currentPage = 0;
            UpdateDisplay();
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(_defaultSelectedSlot.gameObject);
            // 播放開啟動畫
            if (_animator != null)
                _animator.PlayOpen();
        }

        private void CloseInventory()
        {
            _isInventoryOpen = false;
            _descriptionPanel.SetActive(false);
            _currentDisplayItems.Clear();
            EventSystem.current.SetSelectedGameObject(null);
            SystemInputReader.Instance.EnablePlayerInput();
            // 清除因 Unity Input System 狀態恢復而殘留的 Triggered 旗標
            SystemInputReader.Instance.ResetTriggeredFlags();
            SystemInputReader.Instance.DisableUIMapInput();
            MouseVisibilityManager.Instance.enableDynamicMouse = false;
            MouseVisibilityManager.Instance.HideCursorImmediate();
            Time.timeScale = 1f;
            // 播放關閉動畫 → 動畫結束後關面板
            if (_animator != null)
                _animator.PlayClose(() => _inventoryPanel.SetActive(false));
            else
                _inventoryPanel.SetActive(false);
        }

        #endregion

        #region 資料面

        private void RefreshCategoryItems()
        {
            _currentCategoryItems.Clear();
            _currentCategoryItems.AddRange(InventoryManager.Instance.GetItemsByCategory(_currentCategory));
            _totalPages = Mathf.Max(1, Mathf.CeilToInt(_currentCategoryItems.Count / (float)PAGE_SIZE));
        }

        private void BuildDisplayBuffer()
        {
            _currentDisplayItems.Clear();
            _currentDisplayItems.AddRange(_currentCategoryItems);
            int padded = PAGE_SIZE * _totalPages - _currentDisplayItems.Count;
            for (int i = 0; i < padded; i++) _currentDisplayItems.Add(null);
        }

        #endregion

        #region 呈現面

        private void RenderPage()
        {
            int startIndex = _currentPage * PAGE_SIZE;
            for (int i = 0; i < _slots.Length; i++)
            {
                int itemIndex = startIndex + i;
                InventorySlot slot = _slots[i];
                // 回寫 slotIndex 給 InventorySlotUI
                InventorySlotUI slotUI = slot.itemButton != null
                    ? slot.itemButton.GetComponent<InventorySlotUI>()
                    : null;
                if (slotUI != null)
                {
                    slotUI.slotIndex = i;
                    slotUI.inventoryDisplay = this;
                }
                if (itemIndex < _currentDisplayItems.Count && _currentDisplayItems[itemIndex] != null)
                {
                    InventoryItem item = _currentDisplayItems[itemIndex];
                    slot.itemImage.sprite = item.icon;
                    slot.itemText.text = item.itemName;
                    slot.quantityText.text = (item.quantity > 0) ? item.quantity.ToString() : "";
                    slot.itemImage.color = (item.quantity > 0) ? Color.white : new Color(1, 1, 1, 0.5f);
                }
                else
                {
                    slot.itemImage.sprite = _defaultSprite;
                    slot.itemText.text = _defaultText;
                    slot.quantityText.text = "";
                    slot.itemImage.color = new Color(1, 1, 1, 0.5f);
                }
            }
        }

        private void BindSlotButtons()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                int indexOnPage = i;
                Button slotButton = _slots[i].itemButton;
                _slotButtons[i] = slotButton;
                if (slotButton == null) continue;
                slotButton.onClick.RemoveAllListeners();
                slotButton.onClick.AddListener(() =>
                {
                    _lastClickedSlotButton = slotButton;
                    ShowItemDescription(indexOnPage);
                    // 委派食用邏輯給 FoodConsumer
                    if (_foodConsumer != null)
                    {
                        int globalIndex = _currentPage * PAGE_SIZE + indexOnPage;
                        InventoryItem item = GetItemAtGlobalIndex(globalIndex);
                        if (item != null)
                            _foodConsumer.OnClickFoodItem(item, globalIndex);
                    }
                });
            }
        }

        private Button ResolveSlotButtonSafe(int i)
        {
            if (i < 0 || i >= _slots.Length) return null;
            Image img = _slots[i]?.itemImage;
            if (img == null) return null;
            Button btn = img.GetComponentInParent<Button>(true);
            if (btn != null) return btn;
            btn = img.GetComponent<Button>();
            if (btn != null) return btn;
            btn = img.GetComponentInChildren<Button>(true);
            return btn;
        }

        #endregion

        #region 分頁 / 分類

        private void ChangePage(int direction)
        {
            if (_foodConsumer != null && _foodConsumer.IsFoodMenuOpen) return;
            _currentPage += direction;
            if (_currentPage < 0)
            {
                CycleCategory(-1);
                _currentPage = _totalPages - 1;
            }
            else if (_currentPage >= _totalPages)
            {
                CycleCategory(+1);
                _currentPage = 0;
            }
            if (_animator != null)
            {
                _animator.PlayPageTransition(direction, () =>
                {
                    RenderPage();
                    BindSlotButtons();
                    _pageText.text = $"{_currentPage + 1}/{Mathf.Max(_totalPages, 1)}";
                    ClearDescription();
                }, RefreshCurrentDescription);
            }
            else
            {
                RenderPage();
                BindSlotButtons();
                _pageText.text = $"{_currentPage + 1}/{Mathf.Max(_totalPages, 1)}";
                RefreshCurrentDescription();
                ClearDescription();
            }
        }

        private void CycleCategory(int step)
        {
            int categoryCount = Enum.GetNames(typeof(Category)).Length;
            _currentCategory = (Category)(((int)_currentCategory + step + categoryCount) % categoryCount);
            RefreshCategoryItems();
            BuildDisplayBuffer();
            _categoryText.text = GetCategoryName(_currentCategory);
        }

        #endregion

        #region 描述面板

        private void SetDescriptionVisible(bool visible)
        {
            if (_descriptionPanel != null && _descriptionPanel.activeSelf != visible)
                _descriptionPanel.SetActive(visible);
        }

        private void ClearDescription()
        {
            SetDescriptionVisible(false);
            _fullSizeImageDisplay.gameObject.SetActive(false);
            _itemNameText.text = "";
            _descriptionText.text = "";
            _effectDescriptionText.text = "";
        }

        private void RefreshCurrentDescription()
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null)
            {
                InventorySlotUI slotUI = selected.GetComponent<InventorySlotUI>();
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
            for (int i = 0; i < raycastResults.Count; i++)
            {
                InventorySlotUI slotUI = raycastResults[i].gameObject.GetComponent<InventorySlotUI>();
                if (slotUI != null)
                {
                    ShowItemDescription(slotUI.slotIndex);
                    return;
                }
            }
            ClearDescription();
        }

        private bool IsPointerOverSlot()
        {
            var pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].gameObject.GetComponent<InventorySlotUI>() != null) return true;
            }
            return false;
        }

        #endregion

        #region 跨分類翻頁輔助

        private void RedrawAndFocus(int localSlotIndex, int direction)
        {
            if (localSlotIndex < 0 || localSlotIndex >= _slots.Length)
                localSlotIndex = 0;
            if (_animator != null)
            {
                _animator.PlayPageTransition(direction, () =>
                {
                    RenderPage();
                    BindSlotButtons();
                    _pageText.text = $"{_currentPage + 1}/{Mathf.Max(_totalPages, 1)}";
                    ClearDescription();
                }, () =>
                {
                    EventSystem.current.SetSelectedGameObject(null);
                    StartCoroutine(FocusSlotNextFrame(localSlotIndex));
                });
            }
            else
            {
                RenderPage();
                BindSlotButtons();
                _pageText.text = $"{_currentPage + 1}/{Mathf.Max(_totalPages, 1)}";
                EventSystem.current.SetSelectedGameObject(null);
                StartCoroutine(FocusSlotNextFrame(localSlotIndex));
                ClearDescription();
            }
        }

        private IEnumerator FocusSlotNextFrame(int localSlotIndex)
        {
            for (int tries = 0; tries < 3; tries++)
            {
                yield return null;
                Button targetBtn = null;
                if (localSlotIndex >= 0 && localSlotIndex < _slotButtons.Length)
                    targetBtn = _slotButtons[localSlotIndex] != null
                        ? _slotButtons[localSlotIndex] : ResolveSlotButtonSafe(localSlotIndex);
                if (targetBtn == null || !targetBtn.gameObject.activeInHierarchy || !targetBtn.interactable)
                    targetBtn = ResolveSlotButtonSafe(0);
                if ((targetBtn == null || !targetBtn.gameObject.activeInHierarchy || !targetBtn.interactable) && _defaultSelectedSlot != null)
                    targetBtn = _defaultSelectedSlot;
                if (targetBtn == null || !targetBtn.gameObject.activeInHierarchy || !targetBtn.interactable)
                {
                    Selectable anySelectable = _inventoryPanel != null
                        ? _inventoryPanel.GetComponentInChildren<Selectable>(true) : null;
                    if (anySelectable != null) targetBtn = anySelectable as Button;
                }
                if (targetBtn != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                    EventSystem.current.SetSelectedGameObject(targetBtn.gameObject);
                    for (int i = 0; i < _slotButtons.Length; i++)
                    {
                        Button btn = _slotButtons[i] != null
                            ? _slotButtons[i] : ResolveSlotButtonSafe(i);
                        if (btn == targetBtn)
                        {
                            ShowItemDescription(i);
                            yield break;
                        }
                    }
                    yield break;
                }
            }
            _fullSizeImageDisplay.gameObject.SetActive(false);
            _itemNameText.text = "";
            _descriptionText.text = "";
            _effectDescriptionText.text = "";
        }

        #endregion

        #region 事件回呼

        private void OnFoodMenuToggled(bool isOpen)
        {
            // 食用選單關閉時恢復焦點
            if (!isOpen)
                RestoreFocusAfterFoodMenu();
        }

        #endregion

        #region 工具

        private string GetCategoryName(Category category)
        {
            return category switch
            {
                Category.Ingredients => "食材",
                Category.Food => "料理",
                Category.Tool => "工具",
                Category.KeyItem => "關鍵物品",
                _ => "未知",
            };
        }

        #endregion
    }
}
