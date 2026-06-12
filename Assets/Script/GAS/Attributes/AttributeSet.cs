using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 屬性集基類 - 管理一組相關的 GameplayAttribute
    /// 繼承此類以創建特定的屬性集 (如戰鬥屬性、角色屬性等)
    /// </summary>
    [Serializable]
    public abstract class AttributeSet
    {
        // 快取所有屬性的字典
        private Dictionary<string, GameplayAttribute> _attributeMap;
        private bool _isInitialized;

        /// <summary>
        /// 擁有此屬性集的 AbilitySystemComponent
        /// </summary>
        public AbilitySystemComponent OwningASC { get; private set; }

        /// <summary>
        /// 當任何屬性值變化時觸發
        /// 參數: (屬性名稱, 舊值, 新值)
        /// </summary>
        public event Action<string, float, float> OnAnyAttributeChanged;

        /// <summary>
        /// 初始化屬性集
        /// </summary>
        public virtual void Initialize(AbilitySystemComponent owner)
        {
            OwningASC = owner;
            
            if (_isInitialized) return;
            
            _attributeMap = new Dictionary<string, GameplayAttribute>();
            
            // 使用反射找出所有 GameplayAttribute 欄位
            var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(GameplayAttribute))
                {
                    var attr = field.GetValue(this) as GameplayAttribute;
                    if (attr == null)
                    {
                        // 如果欄位為 null，創建新的屬性
                        attr = new GameplayAttribute(field.Name);
                        field.SetValue(this, attr);
                    }
                    
                    _attributeMap[attr.AttributeName] = attr;
                    
                    // 訂閱屬性變化事件
                    attr.OnCurrentValueChanged += HandleAttributeChanged;
                }
            }
            
            _isInitialized = true;
            
            // 調用子類的初始化
            OnInitialize();
        }

        /// <summary>
        /// 子類可覆寫以進行額外初始化
        /// </summary>
        protected virtual void OnInitialize() { }

        /// <summary>
        /// 根據名稱獲取屬性
        /// </summary>
        public GameplayAttribute GetAttribute(string attributeName)
        {
            if (_attributeMap != null && _attributeMap.TryGetValue(attributeName, out var attr))
            {
                return attr;
            }
            return null;
        }

        /// <summary>
        /// 獲取屬性的當前值
        /// </summary>
        public float GetAttributeValue(string attributeName)
        {
            var attr = GetAttribute(attributeName);
            return attr?.CurrentValue ?? 0f;
        }

        /// <summary>
        /// 設置屬性的基礎值
        /// </summary>
        public void SetAttributeBaseValue(string attributeName, float value)
        {
            var attr = GetAttribute(attributeName);
            if (attr != null)
            {
                attr.BaseValue = value;
            }
        }

        /// <summary>
        /// 獲取所有屬性
        /// </summary>
        public IEnumerable<GameplayAttribute> GetAllAttributes()
        {
            if (_attributeMap == null) yield break;
            
            foreach (var attr in _attributeMap.Values)
            {
                yield return attr;
            }
        }

        /// <summary>
        /// 檢查是否包含指定屬性
        /// </summary>
        public bool HasAttribute(string attributeName)
        {
            return _attributeMap != null && _attributeMap.ContainsKey(attributeName);
        }

        /// <summary>
        /// 處理屬性變化
        /// </summary>
        private void HandleAttributeChanged(GameplayAttribute attr, float oldValue, float newValue)
        {
            // 調用子類的處理
            OnAttributeChanged(attr, oldValue, newValue);
            
            // 觸發事件
            OnAnyAttributeChanged?.Invoke(attr.AttributeName, oldValue, newValue);
        }

        /// <summary>
        /// 子類可覆寫以處理屬性變化
        /// </summary>
        protected virtual void OnAttributeChanged(GameplayAttribute attr, float oldValue, float newValue) { }

        /// <summary>
        /// 在 GameplayEffect 執行前調用
        /// 子類可覆寫以實現預處理邏輯 (如傷害計算前的防禦減免)
        /// </summary>
        public virtual void PreAttributeChange(GameplayAttribute attr, ref float newValue) { }

        /// <summary>
        /// 在 GameplayEffect 執行後調用
        /// 子類可覆寫以實現後處理邏輯 (如生命值歸零時觸發死亡)
        /// </summary>
        public virtual void PostGameplayEffectExecute(GameplayEffectSpec spec) { }

        /// <summary>
        /// 重置所有屬性到基礎值
        /// </summary>
        public void ResetAllAttributes()
        {
            if (_attributeMap == null) return;
            
            foreach (var attr in _attributeMap.Values)
            {
                attr.ResetModifiers();
            }
        }

        /// <summary>
        /// 清理資源
        /// </summary>
        public virtual void Cleanup()
        {
            if (_attributeMap != null)
            {
                foreach (var attr in _attributeMap.Values)
                {
                    attr.OnCurrentValueChanged -= HandleAttributeChanged;
                }
            }
        }
    }
}
