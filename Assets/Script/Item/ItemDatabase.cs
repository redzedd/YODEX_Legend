using System.Collections.Generic;
using UnityEngine;

namespace Item
{
    /// <summary>
    /// 道具資料庫 — 依 itemID 快速查找 ItemData
    /// </summary>
    public class ItemDatabase : MonoBehaviour
    {
        public static ItemDatabase Instance { get; private set; }

        [SerializeField] private List<ItemData> _allItemDataList;

        private Dictionary<int, ItemData> _itemDict = new Dictionary<int, ItemData>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            foreach (var item in _allItemDataList)
            {
                if (item != null)
                    _itemDict[item.itemID] = item;
            }
        }

        public ItemData GetItemByID(int id)
        {
            return _itemDict.TryGetValue(id, out var item) ? item : null;
        }
    }
}
