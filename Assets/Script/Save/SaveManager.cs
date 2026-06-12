using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Save
{
    /// <summary>
    /// 存檔管理器 — 集中管理所有 ISaveable 的存檔與讀檔
    /// 單一存檔槽位，儲存至 Application.persistentDataPath/save.json
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        private readonly List<ISaveable> _saveables = new List<ISaveable>();
        private string SaveFilePath => Path.Combine(Application.persistentDataPath, "save.json");

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// 註冊 ISaveable（各元件在 OnEnable 呼叫）
        /// </summary>
        public void Register(ISaveable saveable)
        {
            if (!_saveables.Contains(saveable))
            {
                _saveables.Add(saveable);
            }
        }

        /// <summary>
        /// 取消註冊 ISaveable（各元件在 OnDisable 呼叫）
        /// </summary>
        public void Unregister(ISaveable saveable)
        {
            _saveables.Remove(saveable);
        }

        /// <summary>
        /// 儲存所有已註冊的 ISaveable 資料到檔案
        /// </summary>
        public void Save()
        {
            SaveData data = new SaveData();
            for (int i = 0; i < _saveables.Count; i++)
            {
                ISaveable saveable = _saveables[i];
                string json = saveable.Serialize();
                data.Set(saveable.SaveKey, json);
            }
            string fileJson = JsonUtility.ToJson(data, true);
            File.WriteAllText(SaveFilePath, fileJson);
            Debug.Log($"[SaveManager] 存檔完成：{SaveFilePath}");
        }

        /// <summary>
        /// 從檔案讀取並還原所有已註冊的 ISaveable 資料
        /// </summary>
        public void Load()
        {
            if (!File.Exists(SaveFilePath))
            {
                Debug.LogWarning("[SaveManager] 找不到存檔檔案");
                return;
            }
            string fileJson = File.ReadAllText(SaveFilePath);
            SaveData data = JsonUtility.FromJson<SaveData>(fileJson);
            if (data == null)
            {
                Debug.LogError("[SaveManager] 存檔資料解析失敗");
                return;
            }
            for (int i = 0; i < _saveables.Count; i++)
            {
                ISaveable saveable = _saveables[i];
                string json = data.Get(saveable.SaveKey);
                if (json != null)
                {
                    saveable.Deserialize(json);
                }
            }
            Debug.Log("[SaveManager] 讀檔完成");
        }

        /// <summary>
        /// 刪除存檔
        /// </summary>
        public void DeleteSave()
        {
            if (File.Exists(SaveFilePath))
            {
                File.Delete(SaveFilePath);
                Debug.Log("[SaveManager] 存檔已刪除");
            }
        }

        /// <summary>
        /// 檢查是否有存檔
        /// </summary>
        public bool HasSave()
        {
            return File.Exists(SaveFilePath);
        }
    }
}
