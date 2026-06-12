#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace GAS.Editor
{
    /// <summary>
    /// VFX Cue 遷移工具
    /// 用於將舊的 VFX Cue 更新為新版本（支援 TimeLineEvent Transform 設定）
    /// </summary>
    public class VFXCueMigrationTool : EditorWindow
    {
        private Vector2 _scrollPosition;
        private List<VFXCue> _foundCues = new List<VFXCue>();
        private bool _hasScanned = false;

        [MenuItem("GAS/Tools/VFX Cue Migration Tool")]
        public static void ShowWindow()
        {
            var window = GetWindow<VFXCueMigrationTool>();
            window.titleContent = new GUIContent("VFX Cue Migration");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "此工具會掃描專案中所有的 VFX Cue 資源，並檢查是否需要遷移。\n\n" +
                "舊版本的 VFX Cue 使用 PositionOffset/RotationOffset/Scale 欄位。\n" +
                "新版本重命名為 AdditionalPositionOffset/AdditionalRotationOffset/AdditionalScale，\n" +
                "並添加了 UseParameterTransform 選項，以支援從 TimeLineEvent 傳入 Transform 設定。",
                MessageType.Info);

            GUILayout.Space(10);

            if (GUILayout.Button("掃描專案中的 VFX Cue", GUILayout.Height(30)))
            {
                ScanForVFXCues();
            }

            if (_hasScanned)
            {
                GUILayout.Space(10);
                EditorGUILayout.LabelField($"找到 {_foundCues.Count} 個 VFX Cue 資源", EditorStyles.boldLabel);
                
                if (_foundCues.Count > 0)
                {
                    GUILayout.Space(5);
                    
                    EditorGUILayout.HelpBox(
                        "以下列出的 VFX Cue 會被更新：\n" +
                        "- UseParameterTransform 將被設為 true（使用 TimeLineEvent 的 Transform）\n" +
                        "- 如果您不希望某些 Cue 使用 TimeLineEvent 的設定，請稍後手動設為 false",
                        MessageType.Warning);

                    GUILayout.Space(5);

                    _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));
                    
                    foreach (var cue in _foundCues)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.ObjectField(cue, typeof(VFXCue), false);
                        EditorGUILayout.LabelField($"UseParameterTransform: {GetUseParameterTransformValue(cue)}", GUILayout.Width(200));
                        EditorGUILayout.EndHorizontal();
                    }
                    
                    EditorGUILayout.EndScrollView();

                    GUILayout.Space(10);

                    if (GUILayout.Button("更新所有 VFX Cue", GUILayout.Height(40)))
                    {
                        MigrateAllCues();
                    }
                }
                else
                {
                    GUILayout.Space(10);
                    EditorGUILayout.HelpBox("沒有找到需要遷移的 VFX Cue。", MessageType.Info);
                }
            }
        }

        private void ScanForVFXCues()
        {
            _foundCues.Clear();
            
            // 搜尋所有 VFX Cue 資源
            string[] guids = AssetDatabase.FindAssets("t:VFXCue");
            
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var cue = AssetDatabase.LoadAssetAtPath<VFXCue>(path);
                
                if (cue != null)
                {
                    _foundCues.Add(cue);
                }
            }

            _hasScanned = true;
            
            Debug.Log($"[VFX Cue Migration] 掃描完成，找到 {_foundCues.Count} 個 VFX Cue");
        }

        private string GetUseParameterTransformValue(VFXCue cue)
        {
            var so = new SerializedObject(cue);
            var prop = so.FindProperty("UseParameterTransform");
            
            if (prop != null)
            {
                return prop.boolValue ? "true" : "false";
            }
            
            return "未知";
        }

        private void MigrateAllCues()
        {
            if (_foundCues.Count == 0)
            {
                EditorUtility.DisplayDialog("錯誤", "沒有找到需要遷移的 VFX Cue", "確定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "確認遷移",
                $"即將更新 {_foundCues.Count} 個 VFX Cue。\n\n" +
                "這個操作會將所有 VFX Cue 的 UseParameterTransform 設為 true。\n\n" +
                "建議在操作前先備份專案。\n\n確定要繼續嗎？",
                "確定",
                "取消"))
            {
                return;
            }

            int successCount = 0;
            int failCount = 0;

            foreach (var cue in _foundCues)
            {
                try
                {
                    var so = new SerializedObject(cue);
                    var prop = so.FindProperty("UseParameterTransform");
                    
                    if (prop != null)
                    {
                        prop.boolValue = true;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(cue);
                        successCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[VFX Cue Migration] 找不到 UseParameterTransform 欄位: {cue.name}");
                        failCount++;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[VFX Cue Migration] 更新失敗: {cue.name}, 錯誤: {e.Message}");
                    failCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string message = $"遷移完成！\n\n" +
                           $"成功: {successCount}\n" +
                           $"失敗: {failCount}\n\n" +
                           $"所有 VFX Cue 的 UseParameterTransform 已設為 true。\n" +
                           $"如果某些 Cue 不需要使用 TimeLineEvent 的 Transform 設定，\n" +
                           $"請手動將它們的 UseParameterTransform 設為 false。";

            EditorUtility.DisplayDialog("遷移完成", message, "確定");
            
            Debug.Log($"[VFX Cue Migration] 遷移完成 - 成功: {successCount}, 失敗: {failCount}");
        }
    }
}
#endif
