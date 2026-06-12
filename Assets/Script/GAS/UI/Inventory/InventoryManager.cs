using System;
using System.Collections.Generic;
using UnityEngine;
using Item;
using Save;

namespace GAS.UI.Inventory
{
    /// <summary>
    /// 背包管理器 — 物品資料的核心存儲
    /// 提供新增、移除、查詢，並透過事件通知 UI 更新
    /// </summary>
    public class InventoryManager : MonoBehaviour, ISaveable
    {
        public static InventoryManager Instance { get; private set; }

        /// <summary>
        /// 清除靜態殘留 — 防止 Enter Play Mode Settings 關閉 Domain Reload 時
        /// Instance 殘留上次 Play 的已銷毀參照，導致新實例誤判為重複而自我銷毀
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Instance = null;
        }

        [Header("獲取道具提示 UI Prefab")]
        [SerializeField] private GameObject _normalItemUIPrefab;
        [SerializeField] private GameObject _rareItemUIPrefab;
        [SerializeField] private GameObject _legendItemUIPrefab;
        [SerializeField] private Transform _newItemCardSpawnPoint;

        [Header("背包資料")]
        [SerializeField] private List<InventoryItem> _allItems = new List<InventoryItem>();

        private readonly HashSet<ItemData> _obtainedItems = new HashSet<ItemData>();

        /// <summary>背包內容變更事件 — InventoryDisplay 訂閱此事件</summary>
        public event Action OnInventoryChanged;

        /// <summary>所有已獲得的物品（唯讀存取）</summary>
        public List<InventoryItem> AllItems => _allItems;

        public string SaveKey => "inventory";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    $"[InventoryManager] 偵測到重複實例，銷毀 '{gameObject.name}'。" +
                    $"原始實例：'{(Instance != null ? Instance.gameObject.name : "null")}'");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.Register(this);
            }
        }

        private void OnDisable()
        {
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.Unregister(this);
            }
        }

        /// <summary>增加道具到背包（第一次獲得時自動顯示字卡）</summary>
        public void AddItem(ItemData data, int amount = 1)
        {
            InventoryItem existingItem = _allItems.Find(item => item.itemData == data);
            bool isFirstTimeObtained = !_obtainedItems.Contains(data);
            if (existingItem != null)
            {
                existingItem.quantity += amount;
            }
            else
            {
                _allItems.Add(new InventoryItem(data, amount));
            }
            // 第一次獲得：顯示提示 UI
            if (isFirstTimeObtained)
            {
                _obtainedItems.Add(data);
                SpawnNewItemCard(data);
            }
            OnInventoryChanged?.Invoke();
        }

        /// <summary>靜默增加道具到背包（不顯示字卡，供寶箱等需自行控制字卡時機的系統使用）</summary>
        public void AddItemSilently(ItemData data, int amount = 1)
        {
            InventoryItem existingItem = _allItems.Find(item => item.itemData == data);
            if (existingItem != null)
            {
                existingItem.quantity += amount;
            }
            else
            {
                _allItems.Add(new InventoryItem(data, amount));
            }
            if (!_obtainedItems.Contains(data))
                _obtainedItems.Add(data);
            OnInventoryChanged?.Invoke();
        }

        /// <summary>根據分類取得物品清單</summary>
        public List<InventoryItem> GetItemsByCategory(InventoryDisplay.Category category)
        {
            List<InventoryItem> result = new List<InventoryItem>();
            for (int i = 0; i < _allItems.Count; i++)
            {
                if (_allItems[i].itemData.category == category)
                    result.Add(_allItems[i]);
            }
            return result;
        }

        /// <summary>依 itemID 排序背包</summary>
        public void SortInventory()
        {
            _allItems.Sort((a, b) => a.itemData.itemID.CompareTo(b.itemData.itemID));
            OnInventoryChanged?.Invoke();
        }

        /// <summary>移除指定數量的道具</summary>
        public void RemoveItem(ItemData itemData, int amount)
        {
            for (int i = 0; i < _allItems.Count; i++)
            {
                if (_allItems[i].itemData != itemData) continue;
                _allItems[i].quantity -= amount;
                if (_allItems[i].quantity <= 0)
                    _allItems.RemoveAt(i);
                break;
            }
            OnInventoryChanged?.Invoke();
        }

        /// <summary>依名稱檢查是否擁有物品</summary>
        public bool HasItemByName(string itemName)
        {
            for (int i = 0; i < _allItems.Count; i++)
            {
                if (_allItems[i].itemName == itemName)
                    return true;
            }
            return false;
        }

        /// <summary>依名稱移除一個物品</summary>
        public bool RemoveItemByName(string itemName)
        {
            for (int i = 0; i < _allItems.Count; i++)
            {
                if (_allItems[i].itemName != itemName) continue;
                _allItems[i].quantity--;
                if (_allItems[i].quantity <= 0)
                    _allItems.RemoveAt(i);
                OnInventoryChanged?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>是否曾經獲得過此物品</summary>
        public bool HasObtained(ItemData data)
        {
            return _obtainedItems.Contains(data);
        }

        public string Serialize()
        {
            InventorySaveData data = new InventorySaveData();
            for (int i = 0; i < _allItems.Count; i++)
            {
                data.items.Add(new ItemEntry
                {
                    itemID = _allItems[i].itemData.itemID,
                    quantity = _allItems[i].quantity
                });
            }
            foreach (ItemData obtained in _obtainedItems)
            {
                data.obtainedItemIDs.Add(obtained.itemID);
            }
            return JsonUtility.ToJson(data);
        }

        public void Deserialize(string json)
        {
            InventorySaveData data = JsonUtility.FromJson<InventorySaveData>(json);
            if (data == null) return;
            ItemDatabase db = ItemDatabase.Instance;
            if (db == null)
            {
                Debug.LogError("[InventoryManager] ItemDatabase 尚未初始化，無法讀檔");
                return;
            }
            _allItems.Clear();
            _obtainedItems.Clear();
            for (int i = 0; i < data.items.Count; i++)
            {
                ItemData itemData = db.GetItemByID(data.items[i].itemID);
                if (itemData != null)
                {
                    _allItems.Add(new InventoryItem(itemData, data.items[i].quantity));
                }
            }
            for (int i = 0; i < data.obtainedItemIDs.Count; i++)
            {
                ItemData itemData = db.GetItemByID(data.obtainedItemIDs[i]);
                if (itemData != null)
                {
                    _obtainedItems.Add(itemData);
                }
            }
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// 生成新道具字卡 — 回傳字卡 GameObject 供外部等待銷毀
        /// 字卡會自行處理時間凍結、玩家輸入等待與銷毀
        /// </summary>
        public GameObject SpawnNewItemCard(ItemData data)
        {
            GameObject prefab = GetUIPrefabByRareLevel(data.rareLevel);
            if (prefab == null || _newItemCardSpawnPoint == null) return null;
            GameObject card = Instantiate(prefab,
                _newItemCardSpawnPoint.position, Quaternion.identity, _newItemCardSpawnPoint);
            GAS.UI.NewItemDisplayUI display = card.GetComponent<GAS.UI.NewItemDisplayUI>();
            if (display != null)
                display.Setup(data);
            return card;
        }

        private GameObject GetUIPrefabByRareLevel(RareLevel level)
        {
            return level switch
            {
                RareLevel.Rare => _rareItemUIPrefab,
                RareLevel.Legend => _legendItemUIPrefab,
                _ => _normalItemUIPrefab,
            };
        }
    }

    /// <summary>背包物品項目 — 包含 ItemData 參考與數量</summary>
    [System.Serializable]
    public class InventoryItem
    {
        public ItemData itemData;
        public int quantity;

        public InventoryItem(ItemData data, int qty)
        {
            itemData = data;
            quantity = qty;
        }

        public string itemName => itemData.itemName;
        public Sprite icon => itemData.icon;
    }
}
