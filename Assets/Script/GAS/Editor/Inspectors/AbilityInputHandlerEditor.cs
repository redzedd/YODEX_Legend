#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace GAS.Editor
{
    /// <summary>
    /// AbilityInputHandler 自訂 Inspector
    /// 提供輸入映射可視化和 Runtime 輸入監控
    /// </summary>
    [CustomEditor(typeof(AbilityInputHandler))]
    public class AbilityInputHandlerEditor : UnityEditor.Editor
    {
        private bool _showCombatInputs = true;
        private bool _showSystemInputs = false;
        private bool _showWeaponSwitch = true;
        private bool _showBufferSettings = true;
        private bool _showRuntime = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var handler = (AbilityInputHandler)target;

            // Runtime 監控（僅 Play Mode）
            if (Application.isPlaying)
            {
                _showRuntime = DrawColorSection("Runtime Monitor", _showRuntime,
                    new Color(0.1f, 0.4f, 0.2f, 0.3f), () => DrawRuntimeMonitor(handler));
            }

            _showCombatInputs = DrawColorSection("Combat Inputs", _showCombatInputs,
                new Color(0.5f, 0.2f, 0.2f, 0.2f), DrawCombatInputs);

            _showSystemInputs = DrawColorSection("System Inputs", _showSystemInputs,
                new Color(0.3f, 0.3f, 0.3f, 0.2f), DrawSystemInputs);

            _showWeaponSwitch = DrawColorSection("Weapon Switch", _showWeaponSwitch,
                new Color(0.4f, 0.3f, 0.1f, 0.2f), DrawWeaponSwitch);

            _showBufferSettings = DrawColorSection("Buffer Settings", _showBufferSettings,
                new Color(0.3f, 0.3f, 0.3f, 0.2f), DrawBufferSettings);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawRuntimeMonitor(AbilityInputHandler handler)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 輸入緩衝狀態
            EditorGUILayout.LabelField("-- Input Buffer --", EditorStyles.centeredGreyMiniLabel);

            bool hasInput = handler.HasInput();
            var bufferColor = hasInput ? Color.yellow : Color.gray;
            var origColor = GUI.color;
            GUI.color = bufferColor;
            EditorGUILayout.LabelField("Has Buffered Input", hasInput.ToString());
            GUI.color = origColor;

            if (hasInput)
            {
                var nextInput = handler.PeekInput();
                EditorGUILayout.LabelField("Next Input", nextInput.ToString());
            }

            // 按住狀態
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("-- Held Inputs --", EditorStyles.centeredGreyMiniLabel);

            DrawHeldState(handler, MeleeInputType.LightAttack, "Light Attack");
            DrawHeldState(handler, MeleeInputType.HeavyAttack, "Heavy Attack");

            EditorGUILayout.EndVertical();
        }

        private void DrawHeldState(AbilityInputHandler handler, MeleeInputType type, string label)
        {
            bool isHeld = handler.IsInputHeld(type);
            var origColor = GUI.color;
            GUI.color = isHeld ? Color.green : Color.gray;
            EditorGUILayout.LabelField(label, isHeld ? "HELD" : "---");
            GUI.color = origColor;
        }

        private void DrawCombatInputs()
        {
            DrawInputActionField("LightAttackAction", "Light Attack", GameplayTags.Ability.Attack.Light);
            DrawInputActionField("HeavyAttackAction", "Heavy Attack", GameplayTags.Ability.Attack.Heavy);
        }

        private void DrawSystemInputs()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("InteractAction"));
        }

        private void DrawWeaponSwitch()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("WeaponSwitchAction"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PreselectionAction"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("WeaponSwitchAbilityTag"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoTriggerAssistSwitch"));
        }

        private void DrawBufferSettings()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("BufferTime"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MaxBufferSize"));
        }

        private void DrawInputActionField(string propertyName, string label, GameplayTag defaultTag)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName),
                new GUIContent(label));

            // 顯示對應的默認 Tag
            if (defaultTag.IsValid)
            {
                var miniStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(0.6f, 0.8f, 0.6f) }
                };
                EditorGUILayout.LabelField($"-> {defaultTag.TagName}", miniStyle, GUILayout.Width(150));
            }

            EditorGUILayout.EndHorizontal();
        }

        // === Helper ===

        private bool DrawColorSection(string title, bool isExpanded, Color bgColor,
            System.Action drawContent)
        {
            var headerRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.foldoutHeader);
            EditorGUI.DrawRect(headerRect, bgColor);

            isExpanded = EditorGUI.Foldout(headerRect, isExpanded, " " + title,
                true, EditorStyles.foldoutHeader);

            if (isExpanded)
            {
                EditorGUI.indentLevel++;
                drawContent();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }

            return isExpanded;
        }
    }
}
#endif
