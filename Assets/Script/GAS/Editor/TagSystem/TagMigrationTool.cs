#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GAS.Editor.TagSystem
{
    /// <summary>
    /// [A1] Tag 一次性遷移工具 — 把 GameplayTags 靜態類所有 Tag 灌進 GameplayTagLibrary.asset。
    /// 流程: 反射 GameplayTags 收集 Tag → 從 GameplayTagLibrary.cs 抽取 XML doc 作為 Description
    ///       → 備份現有 Library → 清空並寫入 → SetDirty 存檔。
    /// </summary>
    public static class TagMigrationTool
    {
        private const string LIBRARY_PATH = "Assets/Resources/GameplayTagLibrary.asset";
        private const string BACKUP_FOLDER = "Assets/Resources/GameplayTagLibrary_Backups";
        private const string TAGS_SOURCE_FILE = "Assets/Script/GAS/Tags/GameplayTagLibrary.cs";

        [MenuItem("GAS/Tag System/[A1] Migrate Tags from Code to Library", priority = 1)]
        public static void RunMigration()
        {
            GameplayTagLibrary library = AssetDatabase.LoadAssetAtPath<GameplayTagLibrary>(LIBRARY_PATH);
            if (library == null)
            {
                EditorUtility.DisplayDialog(
                    "找不到 Tag Library",
                    $"預期路徑: {LIBRARY_PATH}\n\n請先建立 GameplayTagLibrary 資產於該路徑後再執行。",
                    "知道了");
                return;
            }

            List<TagEntry> tagsFromCode = CollectFromStaticClass();
            if (tagsFromCode.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "沒找到 Tag",
                    "反射 GameplayTags 靜態類沒有取到任何 Tag,請確認程式碼有編譯。",
                    "知道了");
                return;
            }

            Dictionary<string, string> docMap = ExtractXmlDocs();
            int withDocCount = 0;
            foreach (TagEntry entry in tagsFromCode)
            {
                if (docMap.TryGetValue(entry.TagName, out string desc))
                {
                    entry.Description = desc;
                    withDocCount++;
                }
            }

            int existingCount = library.TagDefinitions != null ? library.TagDefinitions.Count : 0;
            string preview = BuildPreviewText(tagsFromCode, maxLines: 12);
            string msg =
                $"準備從 GameplayTags 靜態類遷移 {tagsFromCode.Count} 個 Tag。\n" +
                $"其中 {withDocCount} 個帶有原始 XML 文件說明。\n\n" +
                $"現有 Library 共 {existingCount} 個 Tag,將被完整替換。\n" +
                $"執行前會自動備份到 {BACKUP_FOLDER}/。\n\n" +
                $"前 12 個 Tag 預覽:\n{preview}\n\n" +
                $"確定要繼續?";

            if (!EditorUtility.DisplayDialog("Tag Migration (A1)", msg, "確定遷移", "取消"))
            {
                return;
            }

            string backupPath = CreateBackup();
            if (string.IsNullOrEmpty(backupPath))
            {
                EditorUtility.DisplayDialog(
                    "備份失敗",
                    "無法建立 Library 備份,為避免資料遺失已中止遷移。請檢查 Resources 資料夾權限。",
                    "知道了");
                return;
            }

            ApplyMigration(library, tagsFromCode);

            Debug.Log($"[TagMigrationTool] 已遷移 {tagsFromCode.Count} 個 Tag (含 {withDocCount} 個帶文件說明)。備份: {backupPath}");

            EditorUtility.DisplayDialog(
                "遷移完成",
                $"成功遷移 {tagsFromCode.Count} 個 Tag 到 Library。\n" +
                $"含文件說明: {withDocCount} 個\n\n" +
                $"備份位置:\n{backupPath}\n\n" +
                $"下一步: 開啟 Library asset 確認內容,然後可以執行 A2 自動生成 GameplayTags.cs。",
                "好");

            Selection.activeObject = library;
            EditorGUIUtility.PingObject(library);
        }

        // ====================================================================

        private sealed class TagEntry
        {
            public string TagName;
            public string Description;
        }

        /// <summary>
        /// 反射 GameplayTags 靜態類所有 GameplayTag 欄位(含巢狀類),保留首見順序,自動去重。
        /// </summary>
        private static List<TagEntry> CollectFromStaticClass()
        {
            HashSet<string> seen = new();
            List<TagEntry> result = new();
            CollectRecursive(typeof(GameplayTags), seen, result);
            return result;
        }

        private static void CollectRecursive(Type type, HashSet<string> seen, List<TagEntry> result)
        {
            const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Static;
            foreach (FieldInfo field in type.GetFields(FLAGS))
            {
                if (field.FieldType != typeof(GameplayTag))
                {
                    continue;
                }
                GameplayTag tag = (GameplayTag)field.GetValue(null);
                if (!tag.IsValid)
                {
                    continue;
                }
                if (seen.Add(tag.TagName))
                {
                    result.Add(new TagEntry { TagName = tag.TagName, Description = string.Empty });
                }
            }
            foreach (Type nested in type.GetNestedTypes(FLAGS))
            {
                CollectRecursive(nested, seen, result);
            }
        }

        /// <summary>
        /// 從 GameplayTagLibrary.cs 抽取 /// summary 對應到下方 GameplayTag = new("...") 的字串。
        /// 用於保留設計師寫過的文件說明,A2 自動生成時會把這些放回程式碼。
        /// 比對方式: 先抓「連續 /// 行區塊 + 緊接 GameplayTag 宣告」,再從區塊中抽 summary,
        /// 避免 singleline 模式下 . 跨多個 summary 區塊造成污染。
        /// </summary>
        private static Dictionary<string, string> ExtractXmlDocs()
        {
            Dictionary<string, string> docMap = new();
            if (!File.Exists(TAGS_SOURCE_FILE))
            {
                return docMap;
            }
            string content = File.ReadAllText(TAGS_SOURCE_FILE);
            // 抓: 一段連續 /// 行(doc block) + 緊接的 GameplayTag 宣告。^/$ 以 Multiline 為基準
            const string BLOCK_PATTERN =
                @"(?<doc>(?:^[ \t]*///[^\n]*\r?\n)+)" +
                @"[ \t]*public\s+static\s+readonly\s+GameplayTag\s+\w+\s*=\s*new\s*\(\s*""(?<tag>[^""]+)""\s*\)";
            MatchCollection matches = Regex.Matches(content, BLOCK_PATTERN, RegexOptions.Multiline);
            foreach (Match m in matches)
            {
                string docBlock = m.Groups["doc"].Value;
                string tagName = m.Groups["tag"].Value;
                // 從 doc block 內抽 <summary>...</summary>(這裡才用 Singleline 跨行)
                Match sm = Regex.Match(docBlock, @"<summary>(?<text>.*?)</summary>", RegexOptions.Singleline);
                if (!sm.Success)
                {
                    continue;
                }
                string summary = sm.Groups["text"].Value;
                // 移掉每行開頭的 /// 標記
                summary = Regex.Replace(summary, @"^\s*///\s?", "", RegexOptions.Multiline);
                // 合併換行與多餘空白為單一空格
                summary = Regex.Replace(summary, @"\s+", " ").Trim();
                if (!string.IsNullOrEmpty(summary))
                {
                    docMap[tagName] = summary;
                }
            }
            return docMap;
        }

        private static string CreateBackup()
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(BACKUP_FOLDER))
                {
                    Directory.CreateDirectory(BACKUP_FOLDER);
                    AssetDatabase.Refresh();
                }
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = $"{BACKUP_FOLDER}/GameplayTagLibrary_Backup_{timestamp}.asset";
                bool ok = AssetDatabase.CopyAsset(LIBRARY_PATH, backupPath);
                return ok ? backupPath : null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TagMigrationTool] 備份失敗: {e}");
                return null;
            }
        }

        private static void ApplyMigration(GameplayTagLibrary library, List<TagEntry> tags)
        {
            SerializedObject so = new(library);
            SerializedProperty listProp = so.FindProperty("_tagDefinitions");
            listProp.ClearArray();
            for (int i = 0; i < tags.Count; i++)
            {
                listProp.InsertArrayElementAtIndex(i);
                SerializedProperty element = listProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("TagName").stringValue = tags[i].TagName;
                element.FindPropertyRelative("Description").stringValue = tags[i].Description ?? string.Empty;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
        }

        private static string BuildPreviewText(List<TagEntry> tags, int maxLines)
        {
            System.Text.StringBuilder sb = new();
            int show = Mathf.Min(tags.Count, maxLines);
            for (int i = 0; i < show; i++)
            {
                sb.Append("  • ").AppendLine(tags[i].TagName);
            }
            if (tags.Count > maxLines)
            {
                sb.Append($"  ... 還有 {tags.Count - maxLines} 個");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
#endif
