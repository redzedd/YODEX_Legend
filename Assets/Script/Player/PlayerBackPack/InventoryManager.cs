using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance;
    [Header("獲取道具提示 UI Prefab")]
    public GameObject normalItemUIPrefab;
    public GameObject rareItemUIPrefab;
    public GameObject legendItemUIPrefab;

    public Transform newItemCardSpawnPoint;
    public List<InventoryItem> allItems = new List<InventoryItem>(); // 所有已獲得的物品

    private HashSet<ItemData> obtainedItems = new HashSet<ItemData>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    /// <summary>
    /// 增加道具到背包，如果第一次獲得則加入列表
    /// </summary>
    public void AddItem(ItemData data, int amount = 1)
    {
        InventoryItem existingItem = allItems.Find(item => item.itemData == data);
        bool isFirstTimeObtained = !obtainedItems.Contains(data);

        if (existingItem != null)
        {
            existingItem.quantity += amount;
        }
        else
        {
            InventoryItem newItem = new InventoryItem(data, amount);
            allItems.Add(newItem);
        }

        // ➤ 加入到已獲得紀錄
        if (isFirstTimeObtained)
        {
            obtainedItems.Add(data);

            // ➤ 顯示第一次獲得提示 UI
            GameObject prefabToUse = GetUIPrefabByRareLevel(data.rareLevel);

            if (prefabToUse != null && newItemCardSpawnPoint != null)
            {
                GameObject card = Instantiate(prefabToUse, newItemCardSpawnPoint.position, Quaternion.identity, newItemCardSpawnPoint);
                NewItemDisplayUI display = card.GetComponent<NewItemDisplayUI>();
                if (display != null)
                {
                    display.Setup(data);
                }
            }
        }
        Debug.Log($"[AddItem] 是否第一次獲得：{isFirstTimeObtained}, hash: {data.GetHashCode()}");
        Debug.Log($"[AddItem] obtainedItems 裡已有：{string.Join(", ", obtainedItems.Select(i => i.name))}");
        foreach (var item in obtainedItems)
        {
            Debug.Log($"[比較] item: {item.name}, 相同物件: {ReferenceEquals(item, data)}");
        }

        // 更新UI
        FindObjectOfType<InventoryDisplay>()?.UpdateDisplay();
    }

    /// <summary>
    /// 根據分類取得物品清單
    /// </summary>
    public List<InventoryItem> GetItemsByCategory(InventoryDisplay.Category category)
    {
        List<InventoryItem> result = new List<InventoryItem>();

        foreach (InventoryItem item in allItems)
        {
            if (item.itemData.category == category)
                result.Add(item);
        }

        return result;
    }

    public void SortInventory()
    {
        allItems.Sort((a, b) => a.itemData.itemID.CompareTo(b.itemData.itemID));
    }

    public void RemoveItem(ItemData itemData, int amount)
    {
        for (int i = 0; i < allItems.Count; i++)
        {
            if (allItems[i].itemData == itemData) // 確保是正確的物品
            {
                allItems[i].quantity -= amount;
                if (allItems[i].quantity <= 0)
                {
                    allItems.RemoveAt(i);
                }
                break; // 只刪除一個，不影響其他同類物品
            }
        }
    }

    public bool HasItemByName(string itemName)
    {
        foreach (var item in allItems)
        {
            if (item.itemName == itemName)
            {
                return true;
            }
        }
        return false;
    }

    public bool RemoveItemByName(string itemName)
    {
        for (int i = 0; i < allItems.Count; i++)
        {
            if (allItems[i].itemName == itemName)
            {
                allItems[i].quantity--;

                if (allItems[i].quantity <= 0)
                {
                    allItems.RemoveAt(i);
                }

                return true;
            }
        }
        return false;
    }

    public bool HasObtained(ItemData data)
    {
        return obtainedItems.Contains(data);
    }

    private GameObject GetUIPrefabByRareLevel(RareLevel level)
    {
        switch (level)
        {
            case RareLevel.Normal:
                return normalItemUIPrefab;
            case RareLevel.Rare:
                return rareItemUIPrefab;
            case RareLevel.Legend:
                return legendItemUIPrefab;
            default:
                return normalItemUIPrefab;
        }
    }
}

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

