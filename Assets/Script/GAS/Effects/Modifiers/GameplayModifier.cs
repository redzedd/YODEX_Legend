using System;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 遊戲效果修改器 - 定義如何修改一個屬性
    /// </summary>
    [Serializable]
    public class GameplayModifier
    {
        [Header("Target")]
        [Tooltip("要修改的屬性名稱")]
        public string AttributeName;

        [Header("Operation")]
        [Tooltip("修改器操作類型")]
        public ModifierOperationType OperationType = ModifierOperationType.Additive;

        [Header("Magnitude")]
        [Tooltip("修改器數值")]
        public float Magnitude = 0f;

        [Tooltip("數值計算方式")]
        public ModifierMagnitudeType MagnitudeType = ModifierMagnitudeType.ScalableFloat;

        [Header("Scalable Float Settings")]
        [Tooltip("係數曲線 (用於根據等級等因素縮放數值)")]
        public AnimationCurve ScalingCurve = AnimationCurve.Linear(0, 1, 1, 1);

        [Header("SetByCaller Settings")]
        [Tooltip("SetByCaller 的資料標籤（例如 Data.Damage）")]
        public string SetByCallerDataTag;

        [Header("Attribute Based Settings")]
        [Tooltip("基於來源或目標的屬性")]
        public ModifierAttributeSource AttributeSource = ModifierAttributeSource.Source;

        [Tooltip("參考的屬性名稱")]
        public string SourceAttributeName;

        [Tooltip("屬性值的係數")]
        public float AttributeCoefficient = 1f;

        /// <summary>
        /// 計算最終的修改器數值
        /// </summary>
        public float CalculateMagnitude(GameplayEffectSpec spec)
        {
            switch (MagnitudeType)
            {
                case ModifierMagnitudeType.ScalableFloat:
                    // 使用曲線縮放基礎數值；曲線為空時視為 1（不縮放）
                    float scaleFactor = (spec != null && ScalingCurve != null && ScalingCurve.length > 0)
                        ? ScalingCurve.Evaluate(spec.Level)
                        : 1f;
                    return Magnitude * scaleFactor;

                case ModifierMagnitudeType.AttributeBased:
                    return CalculateAttributeBasedMagnitude(spec);

                case ModifierMagnitudeType.CustomCalculation:
                    // 子類可覆寫實現自定義計算
                    return CustomCalculateMagnitude(spec);

                case ModifierMagnitudeType.SetByCaller:
                    // 從 Spec 取得由呼叫方設定的動態數值，取不到時使用 Magnitude 作為 fallback
                    if (spec != null && !string.IsNullOrEmpty(SetByCallerDataTag))
                    {
                        return spec.GetSetByCallerMagnitude(SetByCallerDataTag, Magnitude);
                    }
                    return Magnitude;

                default:
                    return Magnitude;
            }
        }

        /// <summary>
        /// 基於屬性計算數值
        /// </summary>
        private float CalculateAttributeBasedMagnitude(GameplayEffectSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(SourceAttributeName))
                return Magnitude;

            float attributeValue = 0f;

            AttributeSet targetSet = AttributeSource == ModifierAttributeSource.Source
                ? spec.Instigator?.GetAttributeSet()
                : spec.Target?.GetAttributeSet();

            if (targetSet != null)
            {
                attributeValue = targetSet.GetAttributeValue(SourceAttributeName);
            }

            return (Magnitude + attributeValue * AttributeCoefficient);
        }

        /// <summary>
        /// 自定義計算 (子類可覆寫)
        /// </summary>
        protected virtual float CustomCalculateMagnitude(GameplayEffectSpec spec)
        {
            return Magnitude;
        }

        /// <summary>
        /// 應用修改器到屬性
        /// </summary>
        public void ApplyToAttribute(GameplayAttribute attribute, float calculatedMagnitude)
        {
            if (attribute == null) return;

            switch (OperationType)
            {
                case ModifierOperationType.Additive:
                    attribute.AddAdditiveModifier(calculatedMagnitude);
                    break;

                case ModifierOperationType.Multiplicative:
                    attribute.AddMultiplicativeModifier(calculatedMagnitude);
                    break;

                case ModifierOperationType.Override:
                    attribute.SetOverride(calculatedMagnitude);
                    break;
            }
        }

        /// <summary>
        /// 從屬性移除修改器
        /// </summary>
        public void RemoveFromAttribute(GameplayAttribute attribute, float calculatedMagnitude)
        {
            if (attribute == null) return;

            switch (OperationType)
            {
                case ModifierOperationType.Additive:
                    attribute.RemoveAdditiveModifier(calculatedMagnitude);
                    break;

                case ModifierOperationType.Multiplicative:
                    attribute.RemoveMultiplicativeModifier(calculatedMagnitude);
                    break;

                case ModifierOperationType.Override:
                    attribute.ClearOverride();
                    break;
            }
        }

        public override string ToString()
        {
            string op = OperationType switch
            {
                ModifierOperationType.Additive => "+",
                ModifierOperationType.Multiplicative => "*",
                ModifierOperationType.Override => "=",
                _ => "?"
            };
            return $"{AttributeName} {op} {Magnitude}";
        }
    }

    /// <summary>
    /// 修改器數值計算方式
    /// </summary>
    public enum ModifierMagnitudeType
    {
        /// <summary>
        /// 可縮放的固定數值
        /// </summary>
        ScalableFloat,

        /// <summary>
        /// 基於屬性計算
        /// </summary>
        AttributeBased,

        /// <summary>
        /// 自定義計算類
        /// </summary>
        CustomCalculation,

        /// <summary>
        /// 由呼叫方動態設定（透過 GameplayEffectSpec.SetSetByCallerMagnitude）
        /// </summary>
        SetByCaller
    }

    /// <summary>
    /// 屬性來源
    /// </summary>
    public enum ModifierAttributeSource
    {
        /// <summary>
        /// 來源 (施放者)
        /// </summary>
        Source,

        /// <summary>
        /// 目標 (受影響者)
        /// </summary>
        Target
    }
}
