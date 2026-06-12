#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GAS.Editor.TagSystem
{
    /// <summary>
    /// [A4] Tag 引用掃描器 — 找出全專案中所有引用指定 Tag 的位置(包含子 Tag 前綴匹配),
    /// 並提供批次改寫 API。掃描範圍: ScriptableObject (.asset) + Prefab (.prefab)。
    /// 場景內 (.unity) 的引用本期不掃,留給 C 模組驗證器處理。
    /// </summary>
    public static class TagReferenceScanner
    {
        public sealed class Reference
        {
            public string AssetPath;
            public string PropertyPath;
            public string CurrentValue;
            public string NewValueAfterRename;
        }

        public sealed class ScanResult
        {
            public List<Reference> References = new();
            public HashSet<string> AssetPaths = new(StringComparer.Ordinal);
            public int AssetCount => AssetPaths.Count;
            public int ReferenceCount => References.Count;
        }

        // ====================================================================
        // 掃描
        // ====================================================================

        /// <summary>
        /// 掃描全專案,找出值等於 fullPath 或開頭為 fullPath+"." 的 _tagName 屬性。
        /// renameMapping 若提供,會同時填入 Reference.NewValueAfterRename(便於預覽變動內容)。
        /// </summary>
        public static ScanResult Scan(string fullPath, Dictionary<string, string> renameMapping = null)
        {
            ScanResult result = new();
            if (string.IsNullOrEmpty(fullPath))
            {
                return result;
            }
            string prefixWithDot = fullPath + ".";

            List<string> paths = CollectScannablePaths();
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    if (i % 16 == 0)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "掃描 Tag 引用",
                                $"{i + 1}/{paths.Count}: {path}",
                                (float)(i + 1) / paths.Count))
                        {
                            break;
                        }
                    }
                    ScanAssetForTagReferences(path, fullPath, prefixWithDot, renameMapping, result);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return result;
        }

        private static List<string> CollectScannablePaths()
        {
            HashSet<string> paths = new(StringComparer.Ordinal);
            string[] soGuids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" });
            foreach (string g in soGuids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(g));
            }
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (string g in prefabGuids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(g));
            }
            // 過濾: 跳過 Library 本身與 Library 備份
            return paths
                .Where(p => !p.Contains("/GameplayTagLibrary.asset"))
                .Where(p => !p.Contains("GameplayTagLibrary_Backups/"))
                .Where(p => !p.StartsWith("Packages/", StringComparison.Ordinal))
                .ToList();
        }

        private static void ScanAssetForTagReferences(
            string path, string fullPath, string prefixWithDot,
            Dictionary<string, string> renameMapping, ScanResult result)
        {
            UnityEngine.Object[] objs = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (UnityEngine.Object o in objs)
            {
                if (o == null)
                {
                    continue;
                }
                SerializedObject so = new(o);
                SerializedProperty iter = so.GetIterator();
                bool enterChildren = true;
                while (iter.NextVisible(enterChildren))
                {
                    enterChildren = true;
                    if (iter.propertyType != SerializedPropertyType.String)
                    {
                        continue;
                    }
                    if (iter.name != "_tagName")
                    {
                        continue;
                    }
                    string val = iter.stringValue;
                    if (string.IsNullOrEmpty(val))
                    {
                        continue;
                    }
                    bool matches = val == fullPath
                                   || val.StartsWith(prefixWithDot, StringComparison.Ordinal);
                    if (!matches)
                    {
                        continue;
                    }
                    string newVal = null;
                    if (renameMapping != null && renameMapping.TryGetValue(val, out string mapped))
                    {
                        newVal = mapped;
                    }
                    else if (renameMapping != null)
                    {
                        // 子 Tag: 用前綴替換
                        newVal = ComputeRenamed(val, renameMapping);
                    }
                    Reference r = new()
                    {
                        AssetPath = path,
                        PropertyPath = iter.propertyPath,
                        CurrentValue = val,
                        NewValueAfterRename = newVal
                    };
                    result.References.Add(r);
                    result.AssetPaths.Add(path);
                }
            }
        }

        private static string ComputeRenamed(string oldValue, Dictionary<string, string> renameMapping)
        {
            foreach (KeyValuePair<string, string> kv in renameMapping)
            {
                if (oldValue == kv.Key)
                {
                    return kv.Value;
                }
                string prefix = kv.Key + ".";
                if (oldValue.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return kv.Value + oldValue.Substring(kv.Key.Length);
                }
            }
            return oldValue;
        }

        // ====================================================================
        // 批次改寫
        // ====================================================================

        /// <summary>
        /// 依 renameMapping 批次改寫所有 .asset / .prefab 中的 _tagName。
        /// mapping key 為「來源完整路徑」(可能是分支 Tag,如 "Ability.Attack"),
        /// 對應 value 為「目標完整路徑」(例如 "Ability.MeleeAttack")。
        /// 所有以 key+"." 開頭的子 Tag 自動跟著替換前綴。
        /// 回傳實際改寫的「資產數」。
        /// </summary>
        public static int ApplyRename(Dictionary<string, string> renameMapping, out int totalPropChanges)
        {
            totalPropChanges = 0;
            int changedAssetCount = 0;
            if (renameMapping == null || renameMapping.Count == 0)
            {
                return 0;
            }
            List<string> paths = CollectScannablePaths();
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    EditorUtility.DisplayProgressBar(
                        "套用 Tag 重命名",
                        $"{i + 1}/{paths.Count}: {path}",
                        (float)(i + 1) / paths.Count);
                    int propChangesInAsset = RenameOneAsset(path, renameMapping);
                    if (propChangesInAsset > 0)
                    {
                        changedAssetCount++;
                        totalPropChanges += propChangesInAsset;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (changedAssetCount > 0)
            {
                AssetDatabase.SaveAssets();
            }
            return changedAssetCount;
        }

        private static int RenameOneAsset(string path, Dictionary<string, string> renameMapping)
        {
            int changes = 0;
            UnityEngine.Object[] objs = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (UnityEngine.Object o in objs)
            {
                if (o == null)
                {
                    continue;
                }
                SerializedObject so = new(o);
                SerializedProperty iter = so.GetIterator();
                bool enterChildren = true;
                bool soDirty = false;
                while (iter.NextVisible(enterChildren))
                {
                    enterChildren = true;
                    if (iter.propertyType != SerializedPropertyType.String)
                    {
                        continue;
                    }
                    if (iter.name != "_tagName")
                    {
                        continue;
                    }
                    string val = iter.stringValue;
                    if (string.IsNullOrEmpty(val))
                    {
                        continue;
                    }
                    string newVal = ComputeRenamed(val, renameMapping);
                    if (newVal != null && newVal != val)
                    {
                        Undo.RecordObject(o, "Tag Rename");
                        iter.stringValue = newVal;
                        soDirty = true;
                        changes++;
                    }
                }
                if (soDirty)
                {
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(o);
                }
            }
            return changes;
        }

        // ====================================================================
        // C# 程式碼引用 (GameplayTags.xxx)
        // ====================================================================

        public sealed class CodeReference
        {
            public string FilePath;
            public int LineNumber;
            public string LineText;
            public string OldCSharpPath;
            public string NewCSharpPath;
        }

        public sealed class CodeScanResult
        {
            public List<CodeReference> References = new();
            public HashSet<string> FilePaths = new(StringComparer.Ordinal);
            public int FileCount => FilePaths.Count;
            public int ReferenceCount => References.Count;
        }

        /// <summary>
        /// 掃描全專案 .cs 檔,找出含 GameplayTags.{oldPath} 引用的位置(供重命名預覽用)。
        /// 排除自動生成檔(*.generated.cs)、TagSystem 編輯器自身、Packages/。
        /// </summary>
        public static CodeScanResult ScanCodeReferences(Dictionary<string, string> renameMapping)
        {
            CodeScanResult result = new();
            if (renameMapping == null || renameMapping.Count == 0)
            {
                return result;
            }
            // 由長到短處理,避免子串誤匹配(雖然 negative lookahead 已防,但雙保險)
            List<KeyValuePair<string, string>> ordered = renameMapping
                .OrderByDescending(kv => kv.Key.Length)
                .ToList();
            List<string> csFiles = CollectCodeFilePaths();
            try
            {
                for (int i = 0; i < csFiles.Count; i++)
                {
                    string file = csFiles[i];
                    if (i % 32 == 0)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "掃描程式碼引用",
                                $"{i + 1}/{csFiles.Count}: {file}",
                                (float)(i + 1) / csFiles.Count))
                        {
                            break;
                        }
                    }
                    ScanOneCodeFile(file, ordered, result);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return result;
        }

        private static void ScanOneCodeFile(
            string filePath,
            List<KeyValuePair<string, string>> ordered,
            CodeScanResult result)
        {
            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch
            {
                return;
            }
            // 早期過濾: 沒提及 GameplayTags 就跳過
            if (text.IndexOf("GameplayTags.", StringComparison.Ordinal) < 0)
            {
                return;
            }
            // 對每個 mapping,找出所有匹配位置
            string[] lines = null;
            foreach (KeyValuePair<string, string> kv in ordered)
            {
                string oldCs = "GameplayTags." + kv.Key;
                string newCs = "GameplayTags." + kv.Value;
                // negative lookahead: 確保不是 LightAttack 之類更長識別子的前綴
                string pattern = Regex.Escape(oldCs) + @"(?![A-Za-z0-9_])";
                MatchCollection matches = Regex.Matches(text, pattern);
                if (matches.Count == 0)
                {
                    continue;
                }
                lines ??= text.Split('\n');
                foreach (Match m in matches)
                {
                    int lineNum = CountLinesUpTo(text, m.Index);
                    string lineText = lineNum >= 0 && lineNum < lines.Length ? lines[lineNum].TrimEnd('\r') : string.Empty;
                    result.References.Add(new CodeReference
                    {
                        FilePath = filePath,
                        LineNumber = lineNum + 1,
                        LineText = lineText.Trim(),
                        OldCSharpPath = oldCs,
                        NewCSharpPath = newCs
                    });
                    result.FilePaths.Add(filePath);
                }
            }
        }

        private static int CountLinesUpTo(string text, int charIndex)
        {
            int n = 0;
            int max = Math.Min(charIndex, text.Length);
            for (int i = 0; i < max; i++)
            {
                if (text[i] == '\n')
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// 套用程式碼重命名: 依 renameMapping 改寫所有 .cs 檔內含 GameplayTags.{old} 的引用。
        /// 回傳改寫的檔案數,out 出總替換次數。
        /// </summary>
        public static int ApplyCodeRename(Dictionary<string, string> renameMapping, out int totalReplacements)
        {
            totalReplacements = 0;
            int changedFiles = 0;
            if (renameMapping == null || renameMapping.Count == 0)
            {
                return 0;
            }
            List<KeyValuePair<string, string>> ordered = renameMapping
                .OrderByDescending(kv => kv.Key.Length)
                .ToList();
            List<string> csFiles = CollectCodeFilePaths();
            try
            {
                for (int i = 0; i < csFiles.Count; i++)
                {
                    string file = csFiles[i];
                    EditorUtility.DisplayProgressBar(
                        "改寫程式碼引用",
                        $"{i + 1}/{csFiles.Count}: {file}",
                        (float)(i + 1) / csFiles.Count);
                    int replacements = RenameOneCodeFile(file, ordered);
                    if (replacements > 0)
                    {
                        changedFiles++;
                        totalReplacements += replacements;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return changedFiles;
        }

        private static int RenameOneCodeFile(string filePath, List<KeyValuePair<string, string>> ordered)
        {
            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch
            {
                return 0;
            }
            if (text.IndexOf("GameplayTags.", StringComparison.Ordinal) < 0)
            {
                return 0;
            }
            int totalReplacements = 0;
            string newText = text;
            foreach (KeyValuePair<string, string> kv in ordered)
            {
                string oldCs = "GameplayTags." + kv.Key;
                string newCs = "GameplayTags." + kv.Value;
                string pattern = Regex.Escape(oldCs) + @"(?![A-Za-z0-9_])";
                int countBefore = totalReplacements;
                newText = Regex.Replace(newText, pattern, _ =>
                {
                    totalReplacements++;
                    return newCs;
                });
            }
            if (totalReplacements > 0 && newText != text)
            {
                try
                {
                    File.WriteAllText(filePath, newText);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TagReferenceScanner] 寫入失敗 {filePath}: {e}");
                    return 0;
                }
            }
            return totalReplacements;
        }

        private static List<string> CollectCodeFilePaths()
        {
            string[] guids = AssetDatabase.FindAssets("t:Script", new[] { "Assets" });
            HashSet<string> set = new(StringComparer.Ordinal);
            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(p) || !p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // 排除自動生成檔
                if (p.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // 排除 TagSystem 編輯器自身 — 內含 "GameplayTags." 純文字字串(例如錯誤訊息),不該被改
                if (p.IndexOf("/GAS/Editor/TagSystem/", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }
                if (p.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    continue;
                }
                set.Add(p);
            }
            return set.ToList();
        }

        // ====================================================================
        // Library 備份
        // ====================================================================

        public static string BackupLibrarySnapshot(string libraryPath, string backupFolder)
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                    AssetDatabase.Refresh();
                }
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = $"{backupFolder}/GameplayTagLibrary_Rename_{ts}.asset";
                if (AssetDatabase.CopyAsset(libraryPath, backupPath))
                {
                    return backupPath;
                }
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TagReferenceScanner] 備份失敗: {e}");
                return null;
            }
        }
    }
}
#endif
