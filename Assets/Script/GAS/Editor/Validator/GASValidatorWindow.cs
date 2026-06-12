#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GAS.Editor.Validation
{
    /// <summary>
    /// [C] GAS Validator UI — 視覺化呈現 GASValidator 找到的問題,支援跳轉與一鍵修復。
    /// 手動觸發 (按 Validate Now),不打擾日常工作。
    /// </summary>
    public class GASValidatorWindow : EditorWindow
    {
        private List<GASValidator.Issue> _issues = new();
        private DateTime? _lastScanTime;
        private Vector2 _scroll;
        private readonly Dictionary<GASValidator.Category, bool> _categoryFolds = new();

        private static readonly (GASValidator.Category cat, string label, Color color)[] CATEGORY_META =
        {
            (GASValidator.Category.TagReference,        "Tag 引用斷裂",         new Color(1f, 0.4f, 0.4f)),
            (GASValidator.Category.AttributeName,       "AttributeName 拼錯",   new Color(1f, 0.55f, 0.3f)),
            (GASValidator.Category.HitEffectNull,       "HitEffect 為空",       new Color(1f, 0.75f, 0.3f)),
            (GASValidator.Category.SetByCallerMissing,  "缺 SetByCaller",       new Color(0.85f, 0.85f, 0.3f)),
            (GASValidator.Category.CueAssetMissing,     "Cue 資產缺失",         new Color(0.6f, 0.85f, 1f)),
            (GASValidator.Category.AbilityTagDuplicate, "AbilityTag 重複",      new Color(1f, 0.5f, 0.85f)),
        };

        [MenuItem("GAS/Validator", priority = 100)]
        public static void Open()
        {
            GASValidatorWindow w = GetWindow<GASValidatorWindow>();
            w.titleContent = new GUIContent("GAS Validator");
            w.minSize = new Vector2(640, 480);
            w.Show();
        }

        // ====================================================================

        private void OnGUI()
        {
            DrawToolbar();
            DrawSummary();
            DrawIssuesList();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Validate Now", EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                RunValidation();
            }
            GUILayout.Space(8);
            if (_lastScanTime.HasValue)
            {
                GUILayout.Label($"最近掃描: {_lastScanTime.Value:HH:mm:ss}", EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.Label("尚未執行掃描", EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            int unknownTagCount = _issues.Count(i => i.Category == GASValidator.Category.TagReference);
            using (new EditorGUI.DisabledScope(unknownTagCount == 0))
            {
                Color prev = GUI.backgroundColor;
                if (unknownTagCount > 0) GUI.backgroundColor = new Color(0.45f, 0.85f, 0.45f);
                if (GUILayout.Button($"📥 補 {unknownTagCount} Tag 進 Library", EditorStyles.toolbarButton, GUILayout.Width(180)))
                {
                    BatchAddUnknownTagsToLibrary();
                }
                GUI.backgroundColor = prev;
            }
            using (new EditorGUI.DisabledScope(_issues.Count == 0))
            {
                if (GUILayout.Button("修復所有可修復項", EditorStyles.toolbarButton, GUILayout.Width(140)))
                {
                    AutoFixAll();
                }
                if (GUILayout.Button("清除結果", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    _issues.Clear();
                    _lastScanTime = null;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void BatchAddUnknownTagsToLibrary()
        {
            const string LIBRARY_PATH = "Assets/Resources/GameplayTagLibrary.asset";
            GameplayTagLibrary lib = AssetDatabase.LoadAssetAtPath<GameplayTagLibrary>(LIBRARY_PATH);
            if (lib == null)
            {
                EditorUtility.DisplayDialog("找不到 Library", $"預期路徑: {LIBRARY_PATH}", "知道了");
                return;
            }
            HashSet<string> existing = new(System.StringComparer.Ordinal);
            foreach (GameplayTagLibrary.TagDefinition d in lib.TagDefinitions)
            {
                existing.Add(d.TagName);
            }
            List<string> toAdd = _issues
                .Where(i => i.Category == GASValidator.Category.TagReference && !string.IsNullOrEmpty(i.OffendingValue))
                .Select(i => i.OffendingValue)
                .Distinct()
                .Where(t => !existing.Contains(t))
                .OrderBy(t => t)
                .ToList();
            if (toAdd.Count == 0)
            {
                EditorUtility.DisplayDialog("沒有要加的 Tag", "所有未知 Tag 已存在於 Library 中。", "好");
                return;
            }
            System.Text.StringBuilder preview = new();
            int show = System.Math.Min(toAdd.Count, 15);
            for (int i = 0; i < show; i++)
            {
                preview.Append("  • ").AppendLine(toAdd[i]);
            }
            if (toAdd.Count > show)
            {
                preview.Append($"  ... 還有 {toAdd.Count - show} 個");
            }
            string msg =
                $"將以下 {toAdd.Count} 個 Tag 加入 Library:\n\n" +
                preview.ToString() + "\n\n" +
                "加入後 GameplayTags.cs 會自動 regen,A5 紅字也會立即消失。\n\n確認?";
            if (!EditorUtility.DisplayDialog("批次補 Tag 進 Library", msg, "確認加入", "取消"))
            {
                return;
            }
            Undo.RecordObject(lib, "Batch Add Unknown Tags");
            SerializedObject so = new(lib);
            SerializedProperty list = so.FindProperty("_tagDefinitions");
            foreach (string tag in toAdd)
            {
                int idx = list.arraySize;
                list.InsertArrayElementAtIndex(idx);
                SerializedProperty el = list.GetArrayElementAtIndex(idx);
                el.FindPropertyRelative("TagName").stringValue = tag;
                el.FindPropertyRelative("Description").stringValue = "由 Validator 批次補入 (原 .asset 已引用)";
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssetIfDirty(lib);
            ShowNotification(new GUIContent($"已加入 {toAdd.Count} 個 Tag"));
            // 重跑掃描 — 此時這些 Tag 已存在,TagReference 錯誤會消失
            RunValidation();
        }

        private void DrawSummary()
        {
            if (_lastScanTime == null)
            {
                EditorGUILayout.HelpBox(
                    "按上方 Validate Now 開始掃描專案。\n" +
                    "會檢查: Tag 引用斷裂 / AttributeName 拼錯 / HitEffect 為空 / 缺 SetByCaller / Cue 資產缺失 / AbilityTag 重複。",
                    MessageType.Info);
                return;
            }
            int errors = _issues.Count(i => i.Severity == GASValidator.Severity.Error);
            int warnings = _issues.Count(i => i.Severity == GASValidator.Severity.Warning);
            int fixable = _issues.Count(i => i.CanAutoFix);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"❌ {errors} 個錯誤", GUILayout.Width(120));
            EditorGUILayout.LabelField($"⚠ {warnings} 個警告", GUILayout.Width(120));
            EditorGUILayout.LabelField($"🔧 {fixable} 個可一鍵修復", GUILayout.Width(160));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        private void DrawIssuesList()
        {
            if (_issues.Count == 0 && _lastScanTime != null)
            {
                EditorGUILayout.HelpBox("🎉 沒找到任何問題,設定都健康!", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach ((GASValidator.Category cat, string label, Color color) in CATEGORY_META)
            {
                List<GASValidator.Issue> inCat = _issues.Where(i => i.Category == cat).ToList();
                if (inCat.Count == 0) continue;

                if (!_categoryFolds.ContainsKey(cat))
                {
                    _categoryFolds[cat] = true;
                }
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                Rect header = EditorGUILayout.BeginHorizontal();
                EditorGUI.DrawRect(header, new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f));
                Color prev = GUI.contentColor;
                GUI.contentColor = color;
                _categoryFolds[cat] = EditorGUILayout.Foldout(_categoryFolds[cat], $"  {label}  ({inCat.Count})", true, EditorStyles.foldoutHeader);
                GUI.contentColor = prev;
                EditorGUILayout.EndHorizontal();

                if (_categoryFolds[cat])
                {
                    foreach (GASValidator.Issue issue in inCat)
                    {
                        DrawIssueRow(issue);
                    }
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawIssueRow(GASValidator.Issue issue)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            string sevIcon = issue.Severity == GASValidator.Severity.Error ? "❌" : "⚠";
            EditorGUILayout.LabelField($"{sevIcon}  {issue.Description}", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"  📄 {System.IO.Path.GetFileName(issue.AssetPath)}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"  → {issue.PropertyHint}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("跳到", GUILayout.Width(50)))
            {
                PingIssue(issue);
            }
            using (new EditorGUI.DisabledScope(!issue.CanAutoFix))
            {
                Color prev = GUI.backgroundColor;
                if (issue.CanAutoFix) GUI.backgroundColor = new Color(0.5f, 0.85f, 0.5f);
                if (GUILayout.Button(issue.CanAutoFix ? "🔧 修復" : "—", GUILayout.Width(60)))
                {
                    issue.AutoFix?.Invoke();
                    _issues.Remove(issue);
                    return;
                }
                GUI.backgroundColor = prev;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static void PingIssue(GASValidator.Issue issue)
        {
            if (issue.Asset == null) return;
            Selection.activeObject = issue.Asset;
            EditorGUIUtility.PingObject(issue.Asset);
        }

        // ====================================================================

        private void RunValidation()
        {
            _issues = GASValidator.Run();
            _lastScanTime = DateTime.Now;
            Repaint();
        }

        private void AutoFixAll()
        {
            int count = 0;
            List<GASValidator.Issue> fixable = _issues.Where(i => i.CanAutoFix).ToList();
            foreach (GASValidator.Issue i in fixable)
            {
                try
                {
                    i.AutoFix();
                    _issues.Remove(i);
                    count++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GASValidator] AutoFix 失敗 ({i.AssetPath}): {e.Message}");
                }
            }
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent($"已修復 {count} 個問題"));
        }
    }
}
#endif
