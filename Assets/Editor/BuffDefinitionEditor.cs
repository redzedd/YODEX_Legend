using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BuffDefinition))]
public class BuffDefinitionEditor : Editor
{
    SerializedProperty buffIdProp, affectTypeProp, specialEffectProp;
    SerializedProperty tier1Prop, tier2Prop, tier3Prop;

    // Foldout 狀態（預設收合）
    static bool foldTier1 = false;
    static bool foldTier2 = false;
    static bool foldTier3 = false;

    void OnEnable()
    {
        buffIdProp = serializedObject.FindProperty("buffId");
        affectTypeProp = serializedObject.FindProperty("affectType");
        specialEffectProp = serializedObject.FindProperty("specialEffect");
        tier1Prop = serializedObject.FindProperty("tier1");
        tier2Prop = serializedObject.FindProperty("tier2");
        tier3Prop = serializedObject.FindProperty("tier3");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(buffIdProp);
        EditorGUILayout.PropertyField(affectTypeProp);
        EditorGUILayout.PropertyField(specialEffectProp);

        var fx = (SpecialEffectType)specialEffectProp.enumValueIndex;

        // 快捷鍵：全部展開 / 全部收合
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("全部展開", GUILayout.Height(22)))
            {
                foldTier1 = foldTier2 = foldTier3 = true;
            }
            if (GUILayout.Button("全部收合", GUILayout.Height(22)))
            {
                foldTier1 = foldTier2 = foldTier3 = false;
            }
        }

        DrawTierFoldout(ref foldTier1, tier1Prop, fx, "Tier 1");
        DrawTierFoldout(ref foldTier2, tier2Prop, fx, "Tier 2");
        DrawTierFoldout(ref foldTier3, tier3Prop, fx, "Tier 3");

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawTierFoldout(ref bool fold, SerializedProperty tierProp, SpecialEffectType fx, string title)
    {
        if (tierProp == null) return;

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // Foldout 標題列
            var barRect = GUILayoutUtility.GetRect(20, 20);
            fold = EditorGUI.Foldout(barRect, fold, title, true, EditorStyles.foldout);

            if (!fold) return;

            EditorGUI.indentLevel++;
            // 內容
            var levelProp = tierProp.FindPropertyRelative("level");
            var iconProp = tierProp.FindPropertyRelative("icon");

            EditorGUILayout.PropertyField(levelProp, new GUIContent("Level"));
            EditorGUILayout.PropertyField(iconProp, new GUIContent("Icon"));

            // duration：大多數效果都會用到（Regeneration 也需要）
            EditorGUILayout.PropertyField(tierProp.FindPropertyRelative("duration"), new GUIContent("Duration (sec)"));

            if (fx == SpecialEffectType.Regeneration)
            {
                EditorGUILayout.PropertyField(tierProp.FindPropertyRelative("tickInterval"), new GUIContent("Tick Interval (sec)"));
                EditorGUILayout.PropertyField(tierProp.FindPropertyRelative("percentPerTick"), new GUIContent("Percent Per Tick (%)"));
                // magnitude 對再生無用 → 不顯示
            }
            else
            {
                // 其他增益顯示 magnitude；隱藏 Regeneration 專用欄位
                EditorGUILayout.PropertyField(tierProp.FindPropertyRelative("magnitude"), new GUIContent("Magnitude"));
            }
            EditorGUI.indentLevel--;
        }
    }
}
