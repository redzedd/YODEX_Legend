using System;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 遊戲屬性 - 代表一個可修改的數值屬性 (如生命值、攻擊力等)
    /// </summary>
    [Serializable]
    public class GameplayAttribute
    {
        [SerializeField]
        private string _attributeName;

        [SerializeField]
        private float _baseValue;

        [SerializeField]
        private float _currentValue;

        // 用於計算的臨時值
        private float _additiveModifier;
        private float _multiplicativeModifier;
        private float _overrideValue;
        private bool _hasOverride;

        /// <summary>
        /// 屬性名稱
        /// </summary>
        public string AttributeName => _attributeName;

        /// <summary>
        /// 基礎值 (不受 Modifier 影響的原始值)
        /// </summary>
        public float BaseValue
        {
            get => _baseValue;
            set
            {
                float oldValue = _baseValue;
                _baseValue = value;
                RecalculateCurrentValue();
                OnBaseValueChanged?.Invoke(this, oldValue, _baseValue);
            }
        }

        /// <summary>
        /// 當前值 (包含所有 Modifier 計算後的最終值)
        /// </summary>
        public float CurrentValue
        {
            get => _currentValue;
            private set
            {
                if (Math.Abs(_currentValue - value) > float.Epsilon)
                {
                    float oldValue = _currentValue;
                    _currentValue = value;
                    OnCurrentValueChanged?.Invoke(this, oldValue, _currentValue);
                }
            }
        }

        /// <summary>
        /// 當基礎值變化時觸發
        /// </summary>
        public event Action<GameplayAttribute, float, float> OnBaseValueChanged;

        /// <summary>
        /// 當當前值變化時觸發
        /// </summary>
        public event Action<GameplayAttribute, float, float> OnCurrentValueChanged;

        public GameplayAttribute()
        {
            _attributeName = "Unnamed";
            _baseValue = 0f;
            _currentValue = 0f;
            ResetModifiers();
        }

        public GameplayAttribute(string name, float baseValue = 0f)
        {
            _attributeName = name;
            _baseValue = baseValue;
            _currentValue = baseValue;
            ResetModifiers();
        }

        /// <summary>
        /// 重置所有修改器
        /// </summary>
        public void ResetModifiers()
        {
            _additiveModifier = 0f;
            _multiplicativeModifier = 1f;
            _hasOverride = false;
            _overrideValue = 0f;
            RecalculateCurrentValue();
        }

        /// <summary>
        /// 添加加法修改器
        /// </summary>
        public void AddAdditiveModifier(float value)
        {
            _additiveModifier += value;
            RecalculateCurrentValue();
        }

        /// <summary>
        /// 移除加法修改器
        /// </summary>
        public void RemoveAdditiveModifier(float value)
        {
            _additiveModifier -= value;
            RecalculateCurrentValue();
        }

        /// <summary>
        /// 添加乘法修改器 (例如: 1.5 = +50%)
        /// </summary>
        public void AddMultiplicativeModifier(float value)
        {
            _multiplicativeModifier *= value;
            RecalculateCurrentValue();
        }

        /// <summary>
        /// 移除乘法修改器
        /// </summary>
        public void RemoveMultiplicativeModifier(float value)
        {
            if (Math.Abs(value) > float.Epsilon)
            {
                _multiplicativeModifier /= value;
            }
            RecalculateCurrentValue();
        }

        /// <summary>
        /// 設置覆蓋值 (完全替換計算結果)
        /// </summary>
        public void SetOverride(float value)
        {
            _hasOverride = true;
            _overrideValue = value;
            RecalculateCurrentValue();
        }

        /// <summary>
        /// 清除覆蓋值
        /// </summary>
        public void ClearOverride()
        {
            _hasOverride = false;
            RecalculateCurrentValue();
        }

        /// <summary>
        /// 重新計算當前值
        /// 計算公式: (BaseValue + AdditiveModifier) * MultiplicativeModifier
        /// </summary>
        private void RecalculateCurrentValue()
        {
            if (_hasOverride)
            {
                CurrentValue = _overrideValue;
            }
            else
            {
                CurrentValue = (_baseValue + _additiveModifier) * _multiplicativeModifier;
            }
        }

        /// <summary>
        /// 獲取當前的加法修改器總和
        /// </summary>
        public float GetAdditiveModifier() => _additiveModifier;

        /// <summary>
        /// 獲取當前的乘法修改器
        /// </summary>
        public float GetMultiplicativeModifier() => _multiplicativeModifier;

        /// <summary>
        /// 初始化屬性 (設置基礎值並重置修改器)
        /// </summary>
        public void Initialize(float baseValue)
        {
            _baseValue = baseValue;
            ResetModifiers();
        }

        public override string ToString()
        {
            return $"{_attributeName}: {_currentValue:F2} (Base: {_baseValue:F2})";
        }
    }

    /// <summary>
    /// 屬性修改器操作類型
    /// </summary>
    public enum ModifierOperationType
    {
        /// <summary>
        /// 加法: FinalValue = BaseValue + Modifier
        /// </summary>
        Additive,

        /// <summary>
        /// 乘法: FinalValue = BaseValue * Modifier
        /// </summary>
        Multiplicative,

        /// <summary>
        /// 覆蓋: FinalValue = Modifier (忽略其他計算)
        /// </summary>
        Override
    }

    /// <summary>
    /// 屬性修改器的計算通道
    /// 決定修改器在計算順序中的位置
    /// </summary>
    public enum ModifierChannel
    {
        /// <summary>
        /// 基礎通道 (最先計算)
        /// </summary>
        Base = 0,

        /// <summary>
        /// 裝備通道
        /// </summary>
        Equipment = 1,

        /// <summary>
        /// Buff 通道
        /// </summary>
        Buff = 2,

        /// <summary>
        /// 臨時通道 (最後計算)
        /// </summary>
        Temporary = 3
    }
}
