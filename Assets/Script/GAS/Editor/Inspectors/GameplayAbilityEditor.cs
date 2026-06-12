#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace GAS.Editor
{
    /// <summary>
    /// GameplayAbility 的自定義 Inspector
    /// 提供更直觀的能力配置界面
    /// </summary>
    [CustomEditor(typeof(GameplayAbility), true)] // true = 支援子類
    public class GameplayAbilityEditor : UnityEditor.Editor
    {
        private SerializedProperty _abilityName;
        private SerializedProperty _abilityTag;
        private SerializedProperty _description;
        private SerializedProperty _abilityLevel;
        private SerializedProperty _canReactivateWhileActive;
        private SerializedProperty _cooldownEffect;
        private SerializedProperty _costEffect;
        private SerializedProperty _activationRequiredTags;
        private SerializedProperty _activationBlockedTags;
        private SerializedProperty _activationOwnedTags;
        private SerializedProperty _blockAbilitiesWithTags;
        private SerializedProperty _cancelAbilitiesWithTags;
        private SerializedProperty _cancelledByTags;

        private bool _showBasicInfo = true;
        private bool _showActivation = true;
        private bool _showCostAndCooldown = true;
        private bool _showTagRequirements = true;
        private bool _showBlockingAndCancel = true;
        private bool _showSubclassProperties = true;

        private void OnEnable()
        {
            _abilityName = serializedObject.FindProperty("AbilityName");
            _abilityTag = serializedObject.FindProperty("AbilityTag");
            _description = serializedObject.FindProperty("Description");
            _abilityLevel = serializedObject.FindProperty("AbilityLevel");
            _canReactivateWhileActive = serializedObject.FindProperty("CanReactivateWhileActive");
            _cooldownEffect = serializedObject.FindProperty("CooldownEffect");
            _costEffect = serializedObject.FindProperty("CostEffect");
            _activationRequiredTags = serializedObject.FindProperty("ActivationRequiredTags");
            _activationBlockedTags = serializedObject.FindProperty("ActivationBlockedTags");
            _activationOwnedTags = serializedObject.FindProperty("ActivationOwnedTags");
            _blockAbilitiesWithTags = serializedObject.FindProperty("BlockAbilitiesWithTags");
            _cancelAbilitiesWithTags = serializedObject.FindProperty("CancelAbilitiesWithTags");
            _cancelledByTags = serializedObject.FindProperty("CancelledByTags");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 標題
            EditorGUILayout.LabelField("Gameplay Ability", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 能力預覽卡片
            DrawAbilityPreview();
            EditorGUILayout.Space(10);

            // 基本資訊
            _showBasicInfo = DrawSection("Basic Info", _showBasicInfo, () =>
            {
                EditorGUILayout.PropertyField(_abilityName);
                EditorGUILayout.PropertyField(_abilityTag);
                EditorGUILayout.PropertyField(_description);
            });

            // 啟動設定
            _showActivation = DrawSection("Activation", _showActivation, () =>
            {
                EditorGUILayout.PropertyField(_abilityLevel);
                EditorGUILayout.PropertyField(_canReactivateWhileActive);
            });

            // 消耗和冷卻
            _showCostAndCooldown = DrawSection("Cost & Cooldown", _showCostAndCooldown, () =>
            {
                EditorGUILayout.PropertyField(_costEffect, new GUIContent("Cost Effect"));
                
                if (_costEffect.objectReferenceValue != null)
                {
                    EditorGUI.indentLevel++;
                    DrawEffectPreview(_costEffect.objectReferenceValue as GameplayEffect, "Cost");
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(_cooldownEffect, new GUIContent("Cooldown Effect"));
                
                if (_cooldownEffect.objectReferenceValue != null)
                {
                    EditorGUI.indentLevel++;
                    DrawEffectPreview(_cooldownEffect.objectReferenceValue as GameplayEffect, "Cooldown");
                    EditorGUI.indentLevel--;
                }
            });

            // 標籤需求
            _showTagRequirements = DrawSection("Tag Requirements", _showTagRequirements, () =>
            {
                DrawTagRequirementDiagram();
                
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Activation Conditions", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_activationRequiredTags, new GUIContent("Required Tags", "Owner must have ALL these tags"));
                EditorGUILayout.PropertyField(_activationBlockedTags, new GUIContent("Blocked Tags", "Owner must NOT have ANY of these tags"));
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Tags Granted During Activation", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_activationOwnedTags, new GUIContent("Owned Tags", "Tags added to owner while ability is active"));
                EditorGUI.indentLevel--;
            });

            // 阻止和取消
            _showBlockingAndCancel = DrawSection("Blocking & Cancellation", _showBlockingAndCancel, () =>
            {
                EditorGUILayout.LabelField("This ability affects others:", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_blockAbilitiesWithTags, new GUIContent("Blocks", "Prevents abilities with these tags from activating"));
                EditorGUILayout.PropertyField(_cancelAbilitiesWithTags, new GUIContent("Cancels", "Cancels active abilities with these tags"));
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("This ability is affected by:", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_cancelledByTags, new GUIContent("Cancelled By", "Abilities with these tags can cancel this one"));
                EditorGUI.indentLevel--;
            });

            // 子類特定屬性
            DrawSubclassProperties();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAbilityPreview()
        {
            var ability = target as GameplayAbility;
            if (ability == null) return;

            Rect rect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 背景顏色根據能力類型
            Color bgColor = GetAbilityTypeColor(ability);
            GUI.DrawTexture(rect, MakeColorTexture(bgColor));

            EditorGUILayout.BeginHorizontal();

            // 左側：名稱和標籤
            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            string displayName = string.IsNullOrEmpty(ability.AbilityName) ? "(Unnamed Ability)" : ability.AbilityName;
            EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(ability.AbilityTag.TagName, EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Level: {ability.AbilityLevel}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            // 右側：狀態指示
            EditorGUILayout.BeginVertical();
            
            // 冷卻資訊
            if (ability.CooldownEffect != null)
            {
                var cd = ability.CooldownEffect;
                EditorGUILayout.LabelField($"CD: {cd.Duration}s", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("No Cooldown", EditorStyles.miniLabel);
            }

            // 消耗資訊
            if (ability.CostEffect != null)
            {
                EditorGUILayout.LabelField("Has Cost", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("No Cost", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            // 描述
            if (!string.IsNullOrEmpty(ability.Description))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField(ability.Description, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTagRequirementDiagram()
        {
            var ability = target as GameplayAbility;
            if (ability == null) return;

            // 簡化的流程圖
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Activation Flow:", EditorStyles.miniBoldLabel);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(20);
            EditorGUILayout.BeginVertical();
            
            // 檢查步驟
            DrawFlowStep("1. Check Required Tags", ability.ActivationRequiredTags.Count > 0);
            DrawFlowStep("2. Check Blocked Tags", ability.ActivationBlockedTags.Count > 0);
            DrawFlowStep("3. Check Cost", ability.CostEffect != null);
            DrawFlowStep("4. Check Cooldown", ability.CooldownEffect != null);
            DrawFlowStep("5. Activate → Add Owned Tags", ability.ActivationOwnedTags.Count > 0);
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }

        private void DrawFlowStep(string text, bool hasContent)
        {
            EditorGUILayout.BeginHorizontal();
            
            Color dotColor = hasContent ? Color.green : Color.gray;
            Rect dotRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
            EditorGUI.DrawRect(new Rect(dotRect.x + 2, dotRect.y + 2, 8, 8), dotColor);
            
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
            if (!hasContent) style.normal.textColor = Color.gray;
            
            EditorGUILayout.LabelField(text, style);
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEffectPreview(GameplayEffect effect, string label)
        {
            if (effect == null) return;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{label}:", GUILayout.Width(60));
            EditorGUILayout.LabelField(effect.EffectName);
            
            if (effect.HasDuration)
            {
                EditorGUILayout.LabelField($"{effect.Duration}s", GUILayout.Width(50));
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSubclassProperties()
        {
            // 繪製子類特有的屬性
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            
            // 跳過已繪製的基類屬性
            var basePropertyNames = new[]
            {
                "m_Script", "AbilityName", "AbilityTag", "Description", "AbilityLevel",
                "CanReactivateWhileActive", "CooldownEffect", "CostEffect",
                "ActivationRequiredTags", "ActivationBlockedTags", "ActivationOwnedTags",
                "BlockAbilitiesWithTags", "CancelAbilitiesWithTags", "CancelledByTags"
            };

            bool hasSubclassProps = false;
            var subclassProps = new System.Collections.Generic.List<SerializedProperty>();

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                
                if (System.Array.IndexOf(basePropertyNames, iterator.name) < 0)
                {
                    subclassProps.Add(iterator.Copy());
                    hasSubclassProps = true;
                }
            }

            if (hasSubclassProps)
            {
                _showSubclassProperties = DrawSection($"{target.GetType().Name} Settings", _showSubclassProperties, () =>
                {
                    foreach (var prop in subclassProps)
                    {
                        EditorGUILayout.PropertyField(prop, true);
                    }
                });
            }
        }

        private bool DrawSection(string title, bool isExpanded, System.Action drawContent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            isExpanded = EditorGUILayout.Foldout(isExpanded, title, true, EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();

            if (isExpanded)
            {
                EditorGUILayout.Space(2);
                drawContent();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);

            return isExpanded;
        }

        private Color GetAbilityTypeColor(GameplayAbility ability)
        {
            string tagName = ability.AbilityTag.TagName;
            
            if (tagName.Contains("Attack"))
                return new Color(0.7f, 0.3f, 0.3f, 0.3f);
            if (tagName.Contains("Movement") || tagName.Contains("Dodge"))
                return new Color(0.3f, 0.5f, 0.7f, 0.3f);
            if (tagName.Contains("Skill"))
                return new Color(0.6f, 0.4f, 0.7f, 0.3f);
            if (tagName.Contains("Buff"))
                return new Color(0.3f, 0.7f, 0.3f, 0.3f);
                
            return new Color(0.4f, 0.4f, 0.4f, 0.3f);
        }

        private Texture2D MakeColorTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
#endif
