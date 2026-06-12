using System;
using System.Collections.Generic;
using UnityEngine;

namespace Save
{
    /// <summary>
    /// 存檔主容器 — 包含所有子系統的序列化資料
    /// 使用 keys/values 平行陣列模擬 Dictionary（因為 JsonUtility 不支援 Dictionary）
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();

        public void Set(string key, string json)
        {
            int index = keys.IndexOf(key);
            if (index >= 0)
            {
                values[index] = json;
            }
            else
            {
                keys.Add(key);
                values.Add(json);
            }
        }

        public string Get(string key)
        {
            int index = keys.IndexOf(key);
            return index >= 0 ? values[index] : null;
        }
    }

    /// <summary>
    /// 玩家屬性存檔資料
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        public List<AttributeEntry> attributes = new List<AttributeEntry>();
        public SerializableVector3 position;
        public SerializableQuaternion rotation;
    }

    /// <summary>
    /// 屬性鍵值對
    /// </summary>
    [Serializable]
    public class AttributeEntry
    {
        public string name;
        public float baseValue;
    }

    /// <summary>
    /// 武器存檔資料
    /// </summary>
    [Serializable]
    public class WeaponSaveData
    {
        public int currentWeaponIndex;
    }

    /// <summary>
    /// 背包存檔資料
    /// </summary>
    [Serializable]
    public class InventorySaveData
    {
        public List<ItemEntry> items = new List<ItemEntry>();
        public List<int> obtainedItemIDs = new List<int>();
    }

    /// <summary>
    /// 背包物品項目（僅存 ID 與數量）
    /// </summary>
    [Serializable]
    public class ItemEntry
    {
        public int itemID;
        public int quantity;
    }

    /// <summary>
    /// 可序列化的 Vector3（JsonUtility 無法直接序列化 Unity 的 Vector3）
    /// </summary>
    [Serializable]
    public struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public SerializableVector3(Vector3 v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        public Vector3 ToVector3() => new Vector3(x, y, z);
    }

    /// <summary>
    /// 可序列化的 Quaternion
    /// </summary>
    [Serializable]
    public struct SerializableQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public SerializableQuaternion(Quaternion q)
        {
            x = q.x;
            y = q.y;
            z = q.z;
            w = q.w;
        }

        public Quaternion ToQuaternion() => new Quaternion(x, y, z, w);
    }
}
