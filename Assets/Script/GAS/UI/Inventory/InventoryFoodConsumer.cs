using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Item;

namespace GAS.UI.Inventory
{
    /// <summary>
    /// 背包食物消耗系統 — 從 InventoryDisplay 提取的食用邏輯
    /// 管理食用選單 UI、HP 回復、Buff 套用
    /// </summary>
    public class InventoryFoodConsumer : MonoBehaviour
    {
        #region Serialized Fields

        [Header("食用選單")]
        [SerializeField] private GameObject _foodOptionMenu;
        [SerializeField] private Button _eatButton;
        [SerializeField] private Button _cancelButton;

        [Header("玩家引用")]
        [Tooltip("玩家 ASC（自動尋找)")]
        [SerializeField] private AbilitySystemComponent _asc;

        #endregion

        #region Private Fields

        private CombatAttributeSet _cachedAttrSet;
        private InventoryItem _selectedFoodItem;
        private int _selectedFoodIndex = -1;
        private bool _isConsuming;
        private bool _isFoodMenuOpen;

        #endregion

        #region 事件

        /// <summary>道具被消耗時觸發</summary>
        public event Action<ItemData> OnItemConsumed;

        /// <summary>食用選單開關狀態變更</summary>
        public event Action<bool> OnFoodMenuToggled;

        #endregion

        #region 屬性

        public bool IsFoodMenuOpen => _isFoodMenuOpen;

        #endregion

        #region 生命週期

        private void Awake()
        {
            // 快取玩家引用
            if (_asc == null)
                _asc = FindFirstObjectByType<AbilitySystemComponent>();
            if (_asc != null)
                _cachedAttrSet = _asc.GetAttributeSet<CombatAttributeSet>();
        }

        private void Start()
        {
            _eatButton.onClick.AddListener(EatSelectedFood);
            _cancelButton.onClick.AddListener(CloseFoodOptionMenu);
        }

        #endregion

        #region 公開方法

        /// <summary>點擊食物格子 — 開啟食用選單</summary>
        public void OnClickFoodItem(InventoryItem item, int globalIndex)
        {
            if (item == null || item.quantity <= 0) return;
            // 只有食材/料理類別才開食用選單
            if (item.itemData.category != InventoryDisplay.Category.Ingredients &&
                item.itemData.category != InventoryDisplay.Category.Food)
                return;
            _selectedFoodItem = item;
            _selectedFoodIndex = globalIndex;
            _foodOptionMenu.SetActive(true);
            _isFoodMenuOpen = true;
            OnFoodMenuToggled?.Invoke(true);
            // 決定 Eat 是否可點
            bool canConsume = CanConsumeItem(item.itemData);
            _eatButton.interactable = canConsume;
            _cancelButton.interactable = true;
            // 預設選取按鈕
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(
                canConsume ? _eatButton.gameObject : _cancelButton.gameObject);
        }

        /// <summary>關閉食用選單</summary>
        public void CloseFoodOptionMenu()
        {
            _foodOptionMenu.SetActive(false);
            _isFoodMenuOpen = false;
            OnFoodMenuToggled?.Invoke(false);
        }

        /// <summary>取得目前選中的食物索引</summary>
        public int GetSelectedFoodIndex() => _selectedFoodIndex;

        /// <summary>取得目前選中的食物</summary>
        public InventoryItem GetSelectedFoodItem() => _selectedFoodItem;

        #endregion

        #region 私有方法

        private void EatSelectedFood()
        {
            if (_isConsuming || _selectedFoodItem == null) return;
            if (_selectedFoodItem.itemData.category != InventoryDisplay.Category.Ingredients &&
                _selectedFoodItem.itemData.category != InventoryDisplay.Category.Food)
                return;
            _isConsuming = true;
            // 套用效果
            ApplyItemEffects(_selectedFoodItem.itemData);
            OnItemConsumed?.Invoke(_selectedFoodItem.itemData);
            // 從背包移除
            InventoryManager.Instance.RemoveItem(_selectedFoodItem.itemData, 1);
            if (_selectedFoodItem.quantity <= 0)
            {
                _selectedFoodItem = null;
                _selectedFoodIndex = -1;
                CloseFoodOptionMenu();
            }
            else
            {
                // 更新食用按鈕狀態
                bool canConsume = CanConsumeItem(_selectedFoodItem.itemData);
                _eatButton.interactable = canConsume;
            }
            _isConsuming = false;
        }

        private void ApplyItemEffects(ItemData data)
        {
            if (_asc == null) return;
            // 確保快取的屬性集合是最新的
            if (_cachedAttrSet == null)
                _cachedAttrSet = _asc.GetAttributeSet<CombatAttributeSet>();
            if (_cachedAttrSet == null) return;
            if (data.healAmount > 0)
                _cachedAttrSet.ApplyHealing(data.healAmount);
            if (data.buffDefinition != null)
                BuffEffectApplicator.GetOrCreate().ApplyBuff(
                    _asc, data.buffDefinition, data.effectTier);
        }

        private bool CanConsumeItem(ItemData data)
        {
            if (_cachedAttrSet == null && _asc != null)
                _cachedAttrSet = _asc.GetAttributeSet<CombatAttributeSet>();
            bool isMaxHealth = false;
            if (_cachedAttrSet != null)
                isMaxHealth = _cachedAttrSet.Health.CurrentValue >= _cachedAttrSet.MaxHealth.CurrentValue;
            return (data.healAmount > 0 && !isMaxHealth) ||
                   (data.buffDefinition != null);
        }

        #endregion
    }
}
