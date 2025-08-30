using System.Collections.Generic;
using UnityEngine;

public class ItemDatabase : MonoBehaviour
{
    public static ItemDatabase Instance;
    public List<ItemData> allItemDataList;

    private Dictionary<int, ItemData> itemDict = new Dictionary<int, ItemData>();

    void Awake()
    {
        Instance = this;
        foreach (var item in allItemDataList)
        {
            itemDict[item.itemID] = item;
        }
    }

    public ItemData GetItemByID(int id)
    {
        return itemDict.TryGetValue(id, out var item) ? item : null;
    }
}
