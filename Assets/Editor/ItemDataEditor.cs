using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ItemData))]
public class ItemDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 基本欄位
        EditorGUILayout.PropertyField(serializedObject.FindProperty("itemID"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("itemName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("category"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("effectDescription"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("fullSizeImage"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("rareLevel"));

        var categoryProp = serializedObject.FindProperty("category");
        var cat = (InventoryDisplay.Category)categoryProp.enumValueIndex;

        // Ingredients
        if (cat == InventoryDisplay.Category.Ingredients)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("回復效果", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("healAmount"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("食材屬性", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ingredientType"));
        }

        // Food：只顯示 healAmount + BuffDefinition + 等級
        if (cat == InventoryDisplay.Category.Food)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("回復效果", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("healAmount"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Buff（由 BuffDefinition 驅動）", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("buffDefinition"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("effectTier"));

            var buffDefProp = serializedObject.FindProperty("buffDefinition");
            if (buffDefProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("若需要特殊效果，請指定對應的 BuffDefinition（在其中設定各等級的固定數值）。", MessageType.Info);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
