#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace GAS.Editor
{
    /// <summary>
    /// 閃避數據遷移工具 - 將 GA_Dodge 舊欄位遷移到新的 DodgeData ScriptableObject
    /// </summary>
    public static class DodgeDataMigrationTool
    {
        private const string OUTPUT_PATH = "Assets/Script/GAS/Abilities/Data/Converted/";

        [MenuItem("GAS/Tools/Migrate Dodge Data")]
        public static void MigrateAll()
        {
            // 搜尋所有 GA_Dodge 資產
            var guids = AssetDatabase.FindAssets("t:GA_Dodge");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[DodgeDataMigration] 找不到任何 GA_Dodge 資產。");
                return;
            }
            int migratedCount = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var dodgeAbility = AssetDatabase.LoadAssetAtPath<GA_Dodge>(path);
                if (dodgeAbility == null) continue;
                // 如果已經有 DodgeData，跳過
                if (dodgeAbility.DodgeData != null)
                {
                    Debug.Log($"[DodgeDataMigration] {dodgeAbility.name} 已有 DodgeData，跳過。");
                    continue;
                }
                MigrateSingle(dodgeAbility, path);
                migratedCount++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[DodgeDataMigration] 遷移完成！共遷移 {migratedCount} 個閃避資產。");
        }

        private static void MigrateSingle(GA_Dodge dodgeAbility, string abilityPath)
        {
            // 建立新的 DodgeData
            var dodgeData = ScriptableObject.CreateInstance<DodgeData>();
            // 從舊欄位讀取（透過 SerializedObject 確保讀取到序列化值）
            var so = new SerializedObject(dodgeAbility);
            // 動畫
            CopyProperty(so, "DodgeAnimation", dodgeData, "DodgeClip");
            CopyProperty(so, "BackstepAnimation", dodgeData, "BackstepClip");
            // Dodge 移動參數
            dodgeData.DodgeDistance = so.FindProperty("DodgeDistance").floatValue;
            dodgeData.DodgeDuration = so.FindProperty("DodgeDuration").floatValue;
            dodgeData.DodgeCurve = GetAnimationCurve(so, "DodgeCurve");
            // Backstep 移動參數
            dodgeData.BackstepDistance = so.FindProperty("BackstepDistance").floatValue;
            dodgeData.BackstepDuration = so.FindProperty("BackstepDuration").floatValue;
            dodgeData.BackstepCurve = GetAnimationCurve(so, "BackstepCurve");
            // 無敵
            var invEffectProp = so.FindProperty("InvincibilityEffect");
            dodgeData.InvincibilityEffect = invEffectProp.objectReferenceValue as GameplayEffect;
            dodgeData.InvincibilityStartTime = so.FindProperty("InvincibilityStartTime").floatValue;
            dodgeData.InvincibilityDuration = so.FindProperty("InvincibilityDuration").floatValue;
            // Cues（GameplayTag 是結構體，需要特殊處理）
            CopyGameplayTag(so, "DodgeStartCue", dodgeData, nameof(DodgeData.DodgeStartCue));
            CopyGameplayTag(so, "DodgeEndCue", dodgeData, nameof(DodgeData.DodgeEndCue));
            // Timing 設定（新欄位，使用合理的預設值）
            dodgeData.AllowInputTime = 0.2f;
            dodgeData.AllowCancelTime = 0.2f;
            dodgeData.SheatheCancelTime = -1f;
            // 儲存 DodgeData 資產
            string dataName = dodgeAbility.name.Replace("GA_", "DodgeData_").Replace("_Dodge", "");
            if (!dataName.StartsWith("DodgeData_")) dataName = "DodgeData_" + dataName;
            string outputPath = OUTPUT_PATH + dataName + ".asset";
            // 確保路徑不重複
            outputPath = AssetDatabase.GenerateUniqueAssetPath(outputPath);
            AssetDatabase.CreateAsset(dodgeData, outputPath);
            // 設定 GA_Dodge 的 _dodgeData 引用
            var dodgeSo = new SerializedObject(dodgeAbility);
            var dodgeDataProp = dodgeSo.FindProperty("_dodgeData");
            dodgeDataProp.objectReferenceValue = dodgeData;
            dodgeSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(dodgeAbility);
            Debug.Log($"[DodgeDataMigration] 已遷移 {dodgeAbility.name} → {outputPath}");
        }

        /// <summary>
        /// 複製 ClipTransition 屬性（透過 SerializedObject 深度複製）
        /// </summary>
        private static void CopyProperty(SerializedObject sourceSo, string sourceName, DodgeData target, string targetName)
        {
            var targetSo = new SerializedObject(target);
            var sourceProp = sourceSo.FindProperty(sourceName);
            var targetProp = targetSo.FindProperty(targetName);
            if (sourceProp != null && targetProp != null)
            {
                // 使用 SerializedProperty 的深度複製
                targetSo.CopyFromSerializedProperty(sourceProp);
                targetSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// 複製 GameplayTag（結構體欄位）
        /// </summary>
        private static void CopyGameplayTag(SerializedObject sourceSo, string sourceName, DodgeData target, string targetName)
        {
            var sourceProp = sourceSo.FindProperty(sourceName);
            if (sourceProp == null) return;
            var targetSo = new SerializedObject(target);
            var targetProp = targetSo.FindProperty(targetName);
            if (targetProp == null) return;
            // GameplayTag 內部有 TagName 字串欄位
            var sourceTagName = sourceProp.FindPropertyRelative("TagName");
            var targetTagName = targetProp.FindPropertyRelative("TagName");
            if (sourceTagName != null && targetTagName != null)
            {
                targetTagName.stringValue = sourceTagName.stringValue;
                targetSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// 取得 AnimationCurve 的深度複製
        /// </summary>
        private static AnimationCurve GetAnimationCurve(SerializedObject so, string propName)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) return AnimationCurve.EaseInOut(0, 0, 1, 1);
            return prop.animationCurveValue ?? AnimationCurve.EaseInOut(0, 0, 1, 1);
        }
    }
}
#endif
