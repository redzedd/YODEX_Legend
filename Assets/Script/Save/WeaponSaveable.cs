using UnityEngine;
using GAS;

namespace Save
{
    /// <summary>
    /// 武器狀態存檔元件 — 掛在玩家 GameObject 上
    /// 儲存當前武器索引，讀檔時切換回該武器
    /// </summary>
    [RequireComponent(typeof(WeaponManager))]
    public class WeaponSaveable : MonoBehaviour, ISaveable
    {
        private WeaponManager _weaponManager;

        public string SaveKey => "weapon";

        private void Awake()
        {
            _weaponManager = GetComponent<WeaponManager>();
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

        public string Serialize()
        {
            WeaponSaveData data = new WeaponSaveData
            {
                currentWeaponIndex = _weaponManager.CurrentIndex
            };
            return JsonUtility.ToJson(data);
        }

        public void Deserialize(string json)
        {
            WeaponSaveData data = JsonUtility.FromJson<WeaponSaveData>(json);
            if (data == null) return;
            // 只有索引不同時才切換
            if (_weaponManager.CurrentIndex != data.currentWeaponIndex)
            {
                _weaponManager.SwitchToIndex(data.currentWeaponIndex);
            }
        }
    }
}
