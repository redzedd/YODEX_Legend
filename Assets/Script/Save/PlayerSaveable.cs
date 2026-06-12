using UnityEngine;
using GAS;

namespace Save
{
    /// <summary>
    /// 玩家屬性與位置存檔元件 — 掛在玩家 GameObject 上
    /// 透過 AttributeSet 反射動態存取所有 GameplayAttribute
    /// </summary>
    [RequireComponent(typeof(AbilitySystemComponent))]
    public class PlayerSaveable : MonoBehaviour, ISaveable
    {
        private AbilitySystemComponent _asc;

        public string SaveKey => "player";

        private void Awake()
        {
            _asc = GetComponent<AbilitySystemComponent>();
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
            PlayerSaveData data = new PlayerSaveData
            {
                position = new SerializableVector3(transform.position),
                rotation = new SerializableQuaternion(transform.rotation)
            };
            CombatAttributeSet combatSet = _asc.GetAttributeSet<CombatAttributeSet>();
            if (combatSet != null)
            {
                foreach (GameplayAttribute attr in combatSet.GetAllAttributes())
                {
                    data.attributes.Add(new AttributeEntry
                    {
                        name = attr.AttributeName,
                        baseValue = attr.BaseValue
                    });
                }
            }
            return JsonUtility.ToJson(data);
        }

        public void Deserialize(string json)
        {
            PlayerSaveData data = JsonUtility.FromJson<PlayerSaveData>(json);
            if (data == null) return;
            // 恢復位置與旋轉
            transform.position = data.position.ToVector3();
            transform.rotation = data.rotation.ToQuaternion();
            // 恢復屬性
            CombatAttributeSet combatSet = _asc.GetAttributeSet<CombatAttributeSet>();
            if (combatSet == null) return;
            for (int i = 0; i < data.attributes.Count; i++)
            {
                AttributeEntry entry = data.attributes[i];
                combatSet.SetAttributeBaseValue(entry.name, entry.baseValue);
            }
        }
    }
}
