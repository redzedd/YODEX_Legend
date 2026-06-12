#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace GAS.Editor
{
    /// <summary>
    /// GameplayModifier 的自定義 PropertyDrawer
    /// 提供更直觀的屬性選擇和預覽
    /// </summary>
    [CustomPropertyDrawer(typeof(GameplayModifier))]
    public class GameplayModifierDrawer : PropertyDrawer
    {
        private static readonly List<string> _attributeNames = new()
        {
            // 來自 CombatAttributes
            CombatAttributes.Health,
            CombatAttributes.MaxHealth,
            CombatAttributes.AttackPower,
            CombatAttributes.CriticalChance,
            CombatAttributes.CriticalDamage,
            CombatAttributes.Defense,
            CombatAttributes.DamageReduction,
            CombatAttributes.MoveSpeed,
            CombatAttributes.DodgeCooldown,
            CombatAttributes.Stamina,
            CombatAttributes.MaxStamina,
            CombatAttributes.StaminaRegen,
            CombatAttributes.IncomingDamage
        };

        // 使用 Dictionary 儲存每個屬性的展開狀態
        private static Dictionary<string, bool> _expandedStates = new Dictionary<string, bool>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var attributeNameProp = property.FindPropertyRelative("AttributeName");
            var operationTypeProp = property.FindPropertyRelative("OperationType");
            var magnitudeProp = property.FindPropertyRelative("Magnitude");
            var magnitudeTypeProp = property.FindPropertyRelative("MagnitudeType");

            float lineHeight = EditorGUIUtility.singleLineHeight + 2;
            Rect currentRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // 獲取唯一識別符（使用 property path）
            string propertyKey = property.propertyPath;
            if (!_expandedStates.ContainsKey(propertyKey))
            {
                _expandedStates[propertyKey] = true; // 預設展開
            }

            // 繪製摺疊標題 (顯示摘要)
            string summary = GetModifierSummary(attributeNameProp.stringValue, operationTypeProp.enumValueIndex, magnitudeProp.floatValue);
            
            // 背景框
            Rect boxRect = new Rect(position.x, position.y, position.width, GetPropertyHeight(property, label));
            EditorGUI.DrawRect(boxRect, new Color(0, 0, 0, 0.1f));
            
            // 繪製邊框
            EditorGUI.DrawRect(new Rect(boxRect.x, boxRect.y, 3, boxRect.height), GetOperationColor(operationTypeProp.enumValueIndex));

            // 標題行
            currentRect.x += 5;
            currentRect.width -= 5;
            bool isExpanded = _expandedStates[propertyKey];
            bool newExpanded = EditorGUI.Foldout(currentRect, isExpanded, summary, true);
            if (newExpanded != isExpanded)
            {
                _expandedStates[propertyKey] = newExpanded;
            }
            currentRect.y += lineHeight;

            if (newExpanded)
            {
                EditorGUI.indentLevel++;

                const float fieldSpacing = 4f; // 每個欄位之間的間距

                // 屬性選擇 (下拉選單)
                float attrHeight = EditorGUIUtility.singleLineHeight;
                currentRect.height = attrHeight;
                DrawAttributeSelector(currentRect, attributeNameProp);
                currentRect.y += attrHeight + fieldSpacing;

                // 操作類型
                float opHeight = EditorGUI.GetPropertyHeight(operationTypeProp, true);
                currentRect.height = opHeight;
                EditorGUI.PropertyField(currentRect, operationTypeProp, true);
                currentRect.y += opHeight + fieldSpacing;

                // 數值
                float magHeight = EditorGUI.GetPropertyHeight(magnitudeProp, true);
                currentRect.height = magHeight;
                EditorGUI.PropertyField(currentRect, magnitudeProp, true);
                currentRect.y += magHeight + fieldSpacing;

                // 數值類型
                float magTypeHeight = EditorGUI.GetPropertyHeight(magnitudeTypeProp, true);
                currentRect.height = magTypeHeight;
                EditorGUI.PropertyField(currentRect, magnitudeTypeProp, true);
                currentRect.y += magTypeHeight + fieldSpacing;

                // 根據 MagnitudeType 顯示額外設定
                var magnitudeType = (ModifierMagnitudeType)magnitudeTypeProp.enumValueIndex;
                
                if (magnitudeType == ModifierMagnitudeType.ScalableFloat)
                {
                    var scalingCurveProp = property.FindPropertyRelative("ScalingCurve");
                    float curveHeight = EditorGUI.GetPropertyHeight(scalingCurveProp, true);
                    currentRect.height = curveHeight;
                    EditorGUI.PropertyField(currentRect, scalingCurveProp, true);
                    currentRect.y += curveHeight + fieldSpacing;
                }
                else if (magnitudeType == ModifierMagnitudeType.AttributeBased)
                {
                    var attrSourceProp = property.FindPropertyRelative("AttributeSource");
                    float srcHeight = EditorGUI.GetPropertyHeight(attrSourceProp, true);
                    currentRect.height = srcHeight;
                    EditorGUI.PropertyField(currentRect, attrSourceProp, true);
                    currentRect.y += srcHeight + fieldSpacing;

                    var sourceAttrNameProp = property.FindPropertyRelative("SourceAttributeName");
                    float srcAttrHeight = EditorGUIUtility.singleLineHeight;
                    currentRect.height = srcAttrHeight;
                    DrawAttributeSelector(currentRect, sourceAttrNameProp, "Source Attribute");
                    currentRect.y += srcAttrHeight + fieldSpacing;

                    var coeffProp = property.FindPropertyRelative("AttributeCoefficient");
                    float coeffHeight = EditorGUI.GetPropertyHeight(coeffProp, true);
                    currentRect.height = coeffHeight;
                    EditorGUI.PropertyField(currentRect, coeffProp, true);
                    currentRect.y += coeffHeight + fieldSpacing;
                }
                else if (magnitudeType == ModifierMagnitudeType.SetByCaller)
                {
                    var dataTagProp = property.FindPropertyRelative("SetByCallerDataTag");
                    float tagHeight = EditorGUIUtility.singleLineHeight;
                    currentRect.height = tagHeight;
                    DrawSetByCallerTagSelector(currentRect, dataTagProp);
                    currentRect.y += tagHeight + fieldSpacing;

                    // 顯示 Magnitude 作為 Fallback 提示
                    EditorGUI.BeginDisabledGroup(false);
                    currentRect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.PropertyField(currentRect, magnitudeProp, new GUIContent("Fallback Value"));
                    currentRect.y += EditorGUIUtility.singleLineHeight + fieldSpacing;
                    EditorGUI.EndDisabledGroup();
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            const float fieldSpacing = 4f;
            float lineHeight = EditorGUIUtility.singleLineHeight + 2;
            float height = lineHeight; // 標題行

            // 獲取展開狀態
            string propertyKey = property.propertyPath;
            bool isExpanded = _expandedStates.ContainsKey(propertyKey) ? _expandedStates[propertyKey] : true;

            if (isExpanded)
            {
                // 使用各屬性的實際高度，避免擠壓
                var attributeNameProp = property.FindPropertyRelative("AttributeName");
                var operationTypeProp = property.FindPropertyRelative("OperationType");
                var magnitudeProp = property.FindPropertyRelative("Magnitude");
                var magnitudeTypeProp = property.FindPropertyRelative("MagnitudeType");

                height += EditorGUIUtility.singleLineHeight + fieldSpacing; // Attribute
                height += EditorGUI.GetPropertyHeight(operationTypeProp, true) + fieldSpacing; // Operation
                height += EditorGUI.GetPropertyHeight(magnitudeProp, true) + fieldSpacing; // Magnitude
                height += EditorGUI.GetPropertyHeight(magnitudeTypeProp, true) + fieldSpacing; // MagnitudeType

                var magnitudeType = (ModifierMagnitudeType)magnitudeTypeProp.enumValueIndex;

                if (magnitudeType == ModifierMagnitudeType.ScalableFloat)
                {
                    var scalingCurveProp = property.FindPropertyRelative("ScalingCurve");
                    height += EditorGUI.GetPropertyHeight(scalingCurveProp, true) + fieldSpacing; // AnimationCurve 需要較大高度
                }
                else if (magnitudeType == ModifierMagnitudeType.AttributeBased)
                {
                    var attrSourceProp = property.FindPropertyRelative("AttributeSource");
                    var sourceAttrNameProp = property.FindPropertyRelative("SourceAttributeName");
                    var coeffProp = property.FindPropertyRelative("AttributeCoefficient");

                    height += EditorGUI.GetPropertyHeight(attrSourceProp, true) + fieldSpacing;
                    height += EditorGUIUtility.singleLineHeight + fieldSpacing; // Source Attribute
                    height += EditorGUI.GetPropertyHeight(coeffProp, true) + fieldSpacing;
                }
                else if (magnitudeType == ModifierMagnitudeType.SetByCaller)
                {
                    height += EditorGUIUtility.singleLineHeight + fieldSpacing; // Data Tag
                    height += EditorGUIUtility.singleLineHeight + fieldSpacing; // Fallback Value
                }
            }

            return height + 8; // 底部 padding 確保完整顯示
        }

        private void DrawAttributeSelector(Rect rect, SerializedProperty prop, string label = "Attribute")
        {
            string currentValue = prop.stringValue;
            
            // 建立選項列表
            var options = new List<string> { "(None)", "(Custom...)" };
            options.AddRange(_attributeNames);

            // 找到當前索引
            int currentIndex = 0;
            if (!string.IsNullOrEmpty(currentValue))
            {
                int foundIndex = _attributeNames.IndexOf(currentValue);
                if (foundIndex >= 0)
                    currentIndex = foundIndex + 2;
                else
                    currentIndex = 1; // Custom
            }

            // 如果是自定義值，顯示文字欄位
            if (currentIndex == 1)
            {
                float labelWidth = EditorGUIUtility.labelWidth;
                float popupWidth = (rect.width - labelWidth) * 0.4f;
                float textWidth = (rect.width - labelWidth) * 0.6f - 2;

                Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
                Rect popupRect = new Rect(rect.x + labelWidth, rect.y, popupWidth, rect.height);
                Rect textRect = new Rect(popupRect.xMax + 2, rect.y, textWidth, rect.height);

                EditorGUI.LabelField(labelRect, label);
                
                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUI.Popup(popupRect, currentIndex, options.ToArray());
                if (EditorGUI.EndChangeCheck())
                {
                    if (newIndex == 0)
                        prop.stringValue = string.Empty;
                    else if (newIndex > 1)
                        prop.stringValue = _attributeNames[newIndex - 2];
                }

                EditorGUI.BeginChangeCheck();
                string newValue = EditorGUI.TextField(textRect, currentValue);
                if (EditorGUI.EndChangeCheck())
                {
                    prop.stringValue = newValue;
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUI.Popup(rect, label, currentIndex, options.ToArray());
                if (EditorGUI.EndChangeCheck())
                {
                    if (newIndex == 0)
                        prop.stringValue = string.Empty;
                    else if (newIndex == 1)
                    {
                        // Keep current value for editing
                    }
                    else
                        prop.stringValue = _attributeNames[newIndex - 2];
                }
            }
        }

        private static readonly List<string> _setByCallerTags = new()
        {
            SetByCallerTags.DAMAGE,
            SetByCallerTags.HEAL
        };

        private void DrawSetByCallerTagSelector(Rect rect, SerializedProperty prop)
        {
            string currentValue = prop.stringValue;
            var options = new List<string> { "(Custom...)" };
            options.AddRange(_setByCallerTags);

            int currentIndex = 0;
            if (!string.IsNullOrEmpty(currentValue))
            {
                int foundIndex = _setByCallerTags.IndexOf(currentValue);
                if (foundIndex >= 0)
                    currentIndex = foundIndex + 1;
            }

            if (currentIndex == 0 && !string.IsNullOrEmpty(currentValue))
            {
                // 自定義值：下拉 + 文字欄位
                float labelWidth = EditorGUIUtility.labelWidth;
                float popupWidth = (rect.width - labelWidth) * 0.4f;
                float textWidth = (rect.width - labelWidth) * 0.6f - 2;
                Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
                Rect popupRect = new Rect(rect.x + labelWidth, rect.y, popupWidth, rect.height);
                Rect textRect = new Rect(popupRect.xMax + 2, rect.y, textWidth, rect.height);
                EditorGUI.LabelField(labelRect, "Data Tag");
                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUI.Popup(popupRect, 0, options.ToArray());
                if (EditorGUI.EndChangeCheck() && newIndex > 0)
                {
                    prop.stringValue = _setByCallerTags[newIndex - 1];
                }
                EditorGUI.BeginChangeCheck();
                string newValue = EditorGUI.TextField(textRect, currentValue);
                if (EditorGUI.EndChangeCheck())
                {
                    prop.stringValue = newValue;
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUI.Popup(rect, "Data Tag", currentIndex, options.ToArray());
                if (EditorGUI.EndChangeCheck())
                {
                    prop.stringValue = newIndex == 0 ? currentValue : _setByCallerTags[newIndex - 1];
                }
            }
        }

        private string GetModifierSummary(string attrName, int opIndex, float magnitude)
        {
            if (string.IsNullOrEmpty(attrName))
                return "Empty Modifier";

            string op = opIndex switch
            {
                0 => magnitude >= 0 ? "+" : "",
                1 => "×",
                2 => "=",
                _ => "?"
            };

            string valueStr = opIndex == 1 ? $"{magnitude:P0}" : magnitude.ToString("F1");
            
            return $"{attrName} {op}{valueStr}";
        }

        private Color GetOperationColor(int opIndex)
        {
            return opIndex switch
            {
                0 => new Color(0.2f, 0.8f, 0.2f), // Additive - Green
                1 => new Color(0.2f, 0.6f, 0.8f), // Multiplicative - Blue
                2 => new Color(0.8f, 0.6f, 0.2f), // Override - Orange
                _ => Color.gray
            };
        }
    }
}
#endif
