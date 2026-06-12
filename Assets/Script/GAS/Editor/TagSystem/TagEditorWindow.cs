#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GAS.Editor.TagSystem
{
    /// <summary>
    /// [A3] Tag 編輯器視窗 — 取代直接編輯 GameplayTagLibrary.asset 的 Inspector。
    /// 樹狀檢視 + 內嵌新增/重命名/刪除/搜尋。
    /// 任何寫入操作後自動 SaveAssets → 觸發 GameplayTagAssetPostprocessor → 自動 regen GameplayTags.cs。
    /// </summary>
    public class TagEditorWindow : EditorWindow
    {
        private const string LIBRARY_PATH = "Assets/Resources/GameplayTagLibrary.asset";
        private const string LIBRARY_BACKUP_FOLDER = "Assets/Resources/GameplayTagLibrary_Backups";

        private GameplayTagLibrary _library;
        private TagNode _root;
        private string _searchText = string.Empty;
        private string _selectedFullPath;
        private Vector2 _treeScroll;
        private Vector2 _detailScroll;

        private readonly HashSet<string> _expandedPaths = new(StringComparer.Ordinal);
        private bool _showAddTopLevelInline;
        private string _addTopLevelInput = string.Empty;
        private string _addChildParentPath;
        private string _addChildInput = string.Empty;

        // 詳情面板暫存值
        private string _detailRenameSegment = string.Empty;
        private string _detailDescription = string.Empty;
        private bool _detailValuesLoaded;

        // 樣式
        private static readonly Color COLOR_TAG_LEAF = new(0.9f, 0.9f, 0.9f);
        private static readonly Color COLOR_TAG_BRANCH = new(0.7f, 0.85f, 1f);
        private static readonly Color COLOR_TAG_BRANCH_AND_LEAF = new(1f, 0.9f, 0.5f);
        private static readonly Color COLOR_MATCH_BG = new(0.3f, 0.5f, 0.2f, 0.35f);
        private static readonly Color COLOR_SELECT_BG = new(0.25f, 0.45f, 0.7f, 0.5f);

        [MenuItem("GAS/Tag System/Tag Editor", priority = 0)]
        public static void Open()
        {
            TagEditorWindow w = GetWindow<TagEditorWindow>();
            w.titleContent = new GUIContent("Tag Editor");
            w.minSize = new Vector2(640, 420);
            w.Show();
        }

        // ====================================================================

        private void OnEnable()
        {
            ReloadLibrary();
        }

        private void ReloadLibrary()
        {
            _library = AssetDatabase.LoadAssetAtPath<GameplayTagLibrary>(LIBRARY_PATH);
            RebuildTree();
        }

        private void RebuildTree()
        {
            _root = new TagNode { Segment = string.Empty, FullPath = string.Empty };
            if (_library == null || _library.TagDefinitions == null)
            {
                return;
            }
            foreach (GameplayTagLibrary.TagDefinition d in _library.TagDefinitions)
            {
                if (string.IsNullOrWhiteSpace(d?.TagName))
                {
                    continue;
                }
                InsertIntoTree(_root, d.TagName, d.Description ?? string.Empty);
            }
        }

        private static void InsertIntoTree(TagNode root, string fullPath, string description)
        {
            string[] segs = fullPath.Split('.');
            TagNode current = root;
            for (int i = 0; i < segs.Length; i++)
            {
                string seg = segs[i];
                if (!current.Children.TryGetValue(seg, out TagNode child))
                {
                    child = new TagNode
                    {
                        Segment = seg,
                        FullPath = i == 0 ? seg : current.FullPath + "." + seg
                    };
                    current.Children[seg] = child;
                }
                current = child;
            }
            current.IsTag = true;
            current.Description = description;
        }

        // ====================================================================
        // OnGUI 主流程
        // ====================================================================

        private void OnGUI()
        {
            if (_library == null)
            {
                EditorGUILayout.HelpBox(
                    $"找不到 Library: {LIBRARY_PATH}\n請先建立 GameplayTagLibrary 資產於該路徑。",
                    MessageType.Error);
                if (GUILayout.Button("重新載入"))
                {
                    ReloadLibrary();
                }
                return;
            }

            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawTreePanel();
            DrawSeparator();
            DrawDetailPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Library:", GUILayout.Width(50));
            EditorGUILayout.ObjectField(_library, typeof(GameplayTagLibrary), false, GUILayout.Width(220));
            GUILayout.Space(8);
            GUILayout.Label("搜尋:", GUILayout.Width(36));
            string newSearch = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
            }
            if (GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                _searchText = string.Empty;
                GUI.FocusControl(null);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("全部展開", EditorStyles.toolbarButton))
            {
                ExpandAll(_root);
            }
            if (GUILayout.Button("全部摺疊", EditorStyles.toolbarButton))
            {
                _expandedPaths.Clear();
            }
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
            {
                ReloadLibrary();
                _detailValuesLoaded = false;
            }
            if (GUILayout.Button("Regenerate now", EditorStyles.toolbarButton))
            {
                ForceRegenerateNow();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ====================================================================
        // 左半: 樹狀面板
        // ====================================================================

        private void DrawTreePanel()
        {
            float treeWidth = Mathf.Max(280f, position.width * 0.55f);
            EditorGUILayout.BeginVertical(GUILayout.Width(treeWidth));

            // 頂部「新增頂層」按鈕列
            EditorGUILayout.BeginHorizontal();
            int totalCount = _library.TagDefinitions != null ? _library.TagDefinitions.Count : 0;
            GUILayout.Label($"共 {totalCount} 個 Tag", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_showAddTopLevelInline ? "× 取消新增" : "+ 新增頂層 Tag", GUILayout.Width(120)))
            {
                _showAddTopLevelInline = !_showAddTopLevelInline;
                _addTopLevelInput = string.Empty;
                _addChildParentPath = null;
            }
            EditorGUILayout.EndHorizontal();

            if (_showAddTopLevelInline)
            {
                DrawInlineAddTop();
            }

            EditorGUILayout.Space(2);

            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll);
            foreach (TagNode child in _root.Children.Values)
            {
                DrawTreeNode(child, 0);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawInlineAddTop()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("新頂層 Tag 名稱 (例: MyCategory)", EditorStyles.miniLabel);
            _addTopLevelInput = EditorGUILayout.TextField(_addTopLevelInput);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrWhiteSpace(_addTopLevelInput);
            if (GUILayout.Button("新增"))
            {
                TryAddTag(_addTopLevelInput.Trim());
                _addTopLevelInput = string.Empty;
                _showAddTopLevelInline = false;
            }
            GUI.enabled = true;
            if (GUILayout.Button("取消"))
            {
                _addTopLevelInput = string.Empty;
                _showAddTopLevelInline = false;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawTreeNode(TagNode node, int depth)
        {
            bool matchSelf = MatchesSearch(node);
            bool matchSubtree = SubtreeMatches(node);
            if (!matchSelf && !matchSubtree)
            {
                return;
            }
            bool selected = _selectedFullPath == node.FullPath;
            bool expanded = IsExpanded(node) || (!string.IsNullOrEmpty(_searchText) && matchSubtree);

            // 主列
            Rect rowRect = EditorGUILayout.BeginHorizontal();
            if (selected)
            {
                EditorGUI.DrawRect(rowRect, COLOR_SELECT_BG);
            }
            else if (matchSelf && !string.IsNullOrEmpty(_searchText))
            {
                EditorGUI.DrawRect(rowRect, COLOR_MATCH_BG);
            }

            GUILayout.Space(depth * 14f);

            // foldout 三角(若有子節點才畫)
            if (node.Children.Count > 0)
            {
                bool newExpanded = EditorGUILayout.Foldout(expanded, GUIContent.none, true, EditorStyles.foldout);
                if (newExpanded != expanded && string.IsNullOrEmpty(_searchText))
                {
                    SetExpanded(node, newExpanded);
                    expanded = newExpanded;
                }
                else if (string.IsNullOrEmpty(_searchText))
                {
                    expanded = newExpanded;
                }
            }
            else
            {
                GUILayout.Space(14f);
            }

            // 圖示: 葉子 / 分支 / 兩者
            string icon;
            Color iconColor;
            if (node.Children.Count == 0)
            {
                icon = "•";
                iconColor = COLOR_TAG_LEAF;
            }
            else if (node.IsTag)
            {
                icon = "▣";
                iconColor = COLOR_TAG_BRANCH_AND_LEAF;
            }
            else
            {
                icon = "□";
                iconColor = COLOR_TAG_BRANCH;
            }
            Color prev = GUI.contentColor;
            GUI.contentColor = iconColor;
            GUILayout.Label(icon, GUILayout.Width(16));
            GUI.contentColor = prev;

            // 名稱 + 子數
            string label = node.Segment;
            if (node.Children.Count > 0)
            {
                int leafCount = CountLeaves(node);
                label += $"  ({leafCount})";
            }
            GUIStyle nameStyle = new(EditorStyles.label)
            {
                fontStyle = node.IsTag ? FontStyle.Normal : FontStyle.Italic
            };
            if (GUILayout.Button(label, nameStyle, GUILayout.MinWidth(80)))
            {
                SelectNode(node);
            }

            GUILayout.FlexibleSpace();

            // 「+」按鈕: 新增子 Tag
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                _addChildParentPath = node.FullPath;
                _addChildInput = string.Empty;
                _showAddTopLevelInline = false;
                _expandedPaths.Add(node.FullPath); // 順手展開
            }

            EditorGUILayout.EndHorizontal();

            // 內嵌「新增子 Tag」輸入
            if (_addChildParentPath == node.FullPath)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space((depth + 1) * 14f + 16f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"新增子 Tag 於 [{node.FullPath}]", EditorStyles.miniLabel);
                _addChildInput = EditorGUILayout.TextField("子段名稱", _addChildInput);
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = !string.IsNullOrWhiteSpace(_addChildInput);
                if (GUILayout.Button("新增"))
                {
                    string fullPath = node.FullPath + "." + _addChildInput.Trim();
                    TryAddTag(fullPath);
                    _addChildParentPath = null;
                    _addChildInput = string.Empty;
                }
                GUI.enabled = true;
                if (GUILayout.Button("取消"))
                {
                    _addChildParentPath = null;
                    _addChildInput = string.Empty;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }

            // 子節點
            if (expanded)
            {
                foreach (TagNode child in node.Children.Values)
                {
                    DrawTreeNode(child, depth + 1);
                }
            }
        }

        private static int CountLeaves(TagNode node)
        {
            int count = node.IsTag ? 1 : 0;
            foreach (TagNode c in node.Children.Values)
            {
                count += CountLeaves(c);
            }
            return count;
        }

        private bool MatchesSearch(TagNode node)
        {
            if (string.IsNullOrEmpty(_searchText))
            {
                return true;
            }
            return node.FullPath.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool SubtreeMatches(TagNode node)
        {
            if (MatchesSearch(node))
            {
                return true;
            }
            foreach (TagNode c in node.Children.Values)
            {
                if (SubtreeMatches(c))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsExpanded(TagNode node)
        {
            return _expandedPaths.Contains(node.FullPath);
        }

        private void SetExpanded(TagNode node, bool expanded)
        {
            if (expanded)
            {
                _expandedPaths.Add(node.FullPath);
            }
            else
            {
                _expandedPaths.Remove(node.FullPath);
            }
        }

        private void ExpandAll(TagNode node)
        {
            if (node.FullPath.Length > 0)
            {
                _expandedPaths.Add(node.FullPath);
            }
            foreach (TagNode c in node.Children.Values)
            {
                ExpandAll(c);
            }
        }

        private void SelectNode(TagNode node)
        {
            _selectedFullPath = node.FullPath;
            _detailValuesLoaded = false; // 重新載入詳情值
        }

        private void DrawSeparator()
        {
            Rect r = GUILayoutUtility.GetRect(2, position.height, GUILayout.Width(2), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(r, new Color(0.15f, 0.15f, 0.15f));
        }

        // ====================================================================
        // 右半: 詳情面板
        // ====================================================================

        private void DrawDetailPanel()
        {
            EditorGUILayout.BeginVertical();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            if (string.IsNullOrEmpty(_selectedFullPath))
            {
                EditorGUILayout.HelpBox("從左側樹中選一個 Tag,即可在此編輯。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            TagNode node = FindNode(_root, _selectedFullPath);
            if (node == null)
            {
                EditorGUILayout.HelpBox("選中的 Tag 已不存在,可能剛被重命名或刪除。", MessageType.Warning);
                _selectedFullPath = null;
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            if (!_detailValuesLoaded)
            {
                _detailRenameSegment = node.Segment;
                _detailDescription = node.Description;
                _detailValuesLoaded = true;
            }

            EditorGUILayout.LabelField("選中 Tag", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(node.FullPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            EditorGUILayout.Space(6);

            // === 此節點是否本身就是個 Tag(有 Description) ===
            bool isTag = node.IsTag;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("類型:", GUILayout.Width(50));
            if (node.Children.Count == 0)
            {
                EditorGUILayout.LabelField("葉子 Tag (•)");
            }
            else if (isTag)
            {
                EditorGUILayout.LabelField("既是 Tag 也是父節點 (▣)");
            }
            else
            {
                EditorGUILayout.LabelField("純路徑節點 (□) — 自己不是 Tag,只是子 Tag 的階層");
            }
            EditorGUILayout.EndHorizontal();

            // 若不是 Tag,提供「將此節點升級為 Tag」按鈕
            if (!isTag)
            {
                if (GUILayout.Button($"將 [{node.FullPath}] 也設為一個 Tag (加入 Library)"))
                {
                    TryAddTag(node.FullPath);
                }
            }

            EditorGUILayout.Space(8);

            // === 描述 ===
            using (new EditorGUI.DisabledScope(!isTag))
            {
                EditorGUILayout.LabelField("描述 (XML doc)", EditorStyles.boldLabel);
                _detailDescription = EditorGUILayout.TextArea(_detailDescription ?? string.Empty, GUILayout.MinHeight(60));
                if (GUILayout.Button("儲存描述", GUILayout.Width(120)))
                {
                    SaveDescription(node.FullPath, _detailDescription);
                }
            }

            EditorGUILayout.Space(12);

            // === 重命名 ===
            EditorGUILayout.LabelField("重命名 (含所有子節點)", EditorStyles.boldLabel);
            string parentPath = node.FullPath.Contains('.')
                ? node.FullPath.Substring(0, node.FullPath.LastIndexOf('.'))
                : string.Empty;
            EditorGUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(parentPath))
            {
                EditorGUILayout.LabelField(parentPath + " .", GUILayout.Width(parentPath.Length * 8 + 16));
            }
            _detailRenameSegment = EditorGUILayout.TextField(_detailRenameSegment, GUILayout.MinWidth(120));
            EditorGUILayout.EndHorizontal();

            int subtreeAffected = CountAffectedByRename(node);
            EditorGUILayout.LabelField(
                subtreeAffected > 1
                    ? $"將同時改寫 {subtreeAffected} 個 Tag 的前綴(此 Tag + 子節點 Tags)。"
                    : "僅改寫此 Tag。",
                EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_detailRenameSegment) || _detailRenameSegment == node.Segment))
            {
                if (GUILayout.Button("執行重命名", GUILayout.Width(120)))
                {
                    TryRenameSubtree(node, _detailRenameSegment.Trim());
                }
            }

            EditorGUILayout.Space(12);

            // === 刪除 ===
            EditorGUILayout.LabelField("刪除", EditorStyles.boldLabel);
            int deleteCount = CountAffectedByDelete(node);
            EditorGUILayout.LabelField(
                deleteCount > 1
                    ? $"將同時刪除 {deleteCount} 個 Tag (此 Tag + 所有子節點 Tag)。"
                    : "僅刪除此 Tag。",
                EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(
                "刪除不會掃描專案中其他資產的引用 — 若有 .asset 引用了被刪的 Tag,GameplayTagDrawer 會顯示警告 (A5 完成後)。完整掃描將由 A4 與 C 模組驗證器處理。",
                MessageType.Info);
            Color bg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
            if (GUILayout.Button($"刪除 {deleteCount} 個 Tag", GUILayout.Width(180)))
            {
                if (EditorUtility.DisplayDialog(
                        "確認刪除",
                        $"確定要刪除 {deleteCount} 個 Tag?\n(此 Tag + 所有子節點)\n\n操作不可復原 (但下次重命名/刪除前 Library 仍保有 Unity Undo 能力)。",
                        "刪除",
                        "取消"))
                {
                    TryDeleteSubtree(node);
                }
            }
            GUI.backgroundColor = bg;

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ====================================================================
        // 操作: 新增 / 重命名 / 刪除 / 描述
        // ====================================================================

        private void TryAddTag(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }
            string error;
            if (!ValidateFullPath(fullPath, out error))
            {
                EditorUtility.DisplayDialog("無效的 Tag 名稱", error, "知道了");
                return;
            }
            // 檢查重複
            if (LibraryHasTag(fullPath))
            {
                EditorUtility.DisplayDialog("Tag 已存在", $"'{fullPath}' 已存在於 Library。", "知道了");
                return;
            }
            Undo.RecordObject(_library, "Add Tag");
            SerializedObject so = new(_library);
            SerializedProperty list = so.FindProperty("_tagDefinitions");
            int idx = list.arraySize;
            list.InsertArrayElementAtIndex(idx);
            SerializedProperty newEl = list.GetArrayElementAtIndex(idx);
            newEl.FindPropertyRelative("TagName").stringValue = fullPath;
            newEl.FindPropertyRelative("Description").stringValue = string.Empty;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(_library);
            AssetDatabase.SaveAssetIfDirty(_library);
            RebuildTree();
            _selectedFullPath = fullPath;
            _detailValuesLoaded = false;
        }

        private void SaveDescription(string fullPath, string newDesc)
        {
            int i = FindLibraryIndex(fullPath);
            if (i < 0)
            {
                return;
            }
            Undo.RecordObject(_library, "Edit Tag Description");
            SerializedObject so = new(_library);
            SerializedProperty list = so.FindProperty("_tagDefinitions");
            SerializedProperty el = list.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("Description").stringValue = newDesc ?? string.Empty;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(_library);
            AssetDatabase.SaveAssetIfDirty(_library);
            RebuildTree();
            ShowNotification(new GUIContent("已儲存描述"));
        }

        private void TryRenameSubtree(TagNode node, string newSegment)
        {
            string error;
            if (!ValidateSegment(newSegment, out error))
            {
                EditorUtility.DisplayDialog("無效的段名稱", error, "知道了");
                return;
            }
            string parentPath = node.FullPath.Contains('.')
                ? node.FullPath.Substring(0, node.FullPath.LastIndexOf('.'))
                : string.Empty;
            string newFullPath = string.IsNullOrEmpty(parentPath) ? newSegment : parentPath + "." + newSegment;

            // 衝突檢查 part 1: 目標路徑不能與現有 Tag 衝突
            if (newFullPath != node.FullPath && LibraryHasTag(newFullPath))
            {
                EditorUtility.DisplayDialog(
                    "重命名衝突",
                    $"'{newFullPath}' 已存在,無法重命名。",
                    "知道了");
                return;
            }

            // 計算 Library 內所有受影響的 Tag(此 Tag + 子節點 Tag)
            List<(int idx, string oldPath, string newPath)> changes = new();
            for (int i = 0; i < _library.TagDefinitions.Count; i++)
            {
                string p = _library.TagDefinitions[i].TagName;
                if (p == node.FullPath)
                {
                    changes.Add((i, p, newFullPath));
                }
                else if (p.StartsWith(node.FullPath + ".", StringComparison.Ordinal))
                {
                    string suffix = p.Substring(node.FullPath.Length); // 含開頭的 "."
                    changes.Add((i, p, newFullPath + suffix));
                }
            }

            if (changes.Count == 0)
            {
                EditorUtility.DisplayDialog("找不到 Tag", "Library 中找不到此節點,重命名取消。", "知道了");
                return;
            }

            // 衝突檢查 part 2: 改後的 newPath 不能與其他未受改的現有 Tag 衝突
            HashSet<string> changedOldPaths = new(changes.Select(c => c.oldPath), StringComparer.Ordinal);
            foreach ((int _, string _, string newPath) in changes)
            {
                if (changedOldPaths.Contains(newPath))
                {
                    continue;
                }
                if (LibraryHasTag(newPath))
                {
                    EditorUtility.DisplayDialog(
                        "重命名衝突",
                        $"重命名後的路徑 '{newPath}' 已存在於 Library,操作取消。",
                        "知道了");
                    return;
                }
            }

            // === A4: 掃描全專案 .asset / .prefab + .cs 引用 ===
            Dictionary<string, string> mapping = changes.ToDictionary(c => c.oldPath, c => c.newPath, StringComparer.Ordinal);
            TagReferenceScanner.ScanResult assetScan = TagReferenceScanner.Scan(node.FullPath, mapping);
            TagReferenceScanner.CodeScanResult codeScan = TagReferenceScanner.ScanCodeReferences(mapping);

            // === 顯示確認對話框(含預覽) ===
            string previewBody = BuildRenamePreview(changes, assetScan, codeScan);
            string confirmMsg =
                $"準備重命名:\n" +
                $"  • Library 內 {changes.Count} 個 Tag\n" +
                $"  • {assetScan.AssetCount} 個資產檔的 {assetScan.ReferenceCount} 處 _tagName 引用\n" +
                $"  • {codeScan.FileCount} 個 .cs 檔的 {codeScan.ReferenceCount} 處程式碼引用\n\n" +
                $"執行前會自動備份 Library 到:\n  {LIBRARY_BACKUP_FOLDER}/\n\n" +
                previewBody;

            if (!EditorUtility.DisplayDialog("確認重命名", confirmMsg, "確認執行", "取消"))
            {
                return;
            }

            // === 備份 Library ===
            string backupPath = TagReferenceScanner.BackupLibrarySnapshot(LIBRARY_PATH, LIBRARY_BACKUP_FOLDER);

            // === 用 Undo group 包裹整個操作(限 Object 變更,.cs 檔改寫不支援 Undo) ===
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Rename Tag {node.FullPath} → {newFullPath}");

            // === 改 Library 內字串 ===
            Undo.RecordObject(_library, "Rename Tag Subtree");
            SerializedObject so = new(_library);
            SerializedProperty list = so.FindProperty("_tagDefinitions");
            foreach ((int idx, string _, string newPath) in changes)
            {
                SerializedProperty el = list.GetArrayElementAtIndex(idx);
                el.FindPropertyRelative("TagName").stringValue = newPath;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(_library);
            AssetDatabase.SaveAssetIfDirty(_library);

            // === 改全專案 .asset / .prefab 引用 ===
            int changedAssetCount = TagReferenceScanner.ApplyRename(mapping, out int totalPropChanges);

            // === 改全專案 .cs 引用 ===
            int changedCodeFiles = TagReferenceScanner.ApplyCodeRename(mapping, out int totalCodeReplacements);

            // === Refresh: Unity 會重新編譯被改的 .cs ===
            AssetDatabase.Refresh();

            // 立即清除 Drawer cache,讓 Inspector 紅字標示即時反映新 Tag 集合
            GameplayTagDrawer.ClearCache();

            Undo.CollapseUndoOperations(undoGroup);
            RebuildTree();
            _selectedFullPath = newFullPath;
            _detailValuesLoaded = false;

            string resultMsg =
                $"重命名完成:\n" +
                $"  • Library: {changes.Count} 個 Tag\n" +
                $"  • 資產引用: {changedAssetCount} 個資產的 {totalPropChanges} 處\n" +
                $"  • 程式碼引用: {changedCodeFiles} 個 .cs 檔的 {totalCodeReplacements} 處 (Unity 將重新編譯)\n";
            if (!string.IsNullOrEmpty(backupPath))
            {
                resultMsg += $"  • 備份: {backupPath}";
            }
            EditorUtility.DisplayDialog("完成", resultMsg, "好");
            ShowNotification(new GUIContent($"已重命名 {changes.Count} Tag + {changedAssetCount + changedCodeFiles} 檔"));
        }

        private static string BuildRenamePreview(
            List<(int idx, string oldPath, string newPath)> changes,
            TagReferenceScanner.ScanResult assetScan,
            TagReferenceScanner.CodeScanResult codeScan)
        {
            System.Text.StringBuilder sb = new();
            sb.AppendLine("── 路徑變更預覽 (最多 8 筆) ──");
            int show = Mathf.Min(changes.Count, 8);
            for (int i = 0; i < show; i++)
            {
                sb.AppendLine($"  {changes[i].oldPath}");
                sb.AppendLine($"    → {changes[i].newPath}");
            }
            if (changes.Count > show)
            {
                sb.AppendLine($"  ... 還有 {changes.Count - show} 個");
            }

            // 資產引用預覽
            if (assetScan.AssetCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine("── 受影響資產 (最多 6 個) ──");
                int idx = 0;
                foreach (string path in assetScan.AssetPaths)
                {
                    if (idx >= 6)
                    {
                        sb.AppendLine($"  ... 還有 {assetScan.AssetCount - 6} 個");
                        break;
                    }
                    sb.Append("  • ").AppendLine(System.IO.Path.GetFileName(path));
                    idx++;
                }
            }

            // 程式碼引用預覽
            if (codeScan.FileCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine("── 受影響程式碼檔 (最多 6 個) ──");
                int idx = 0;
                foreach (string path in codeScan.FilePaths)
                {
                    if (idx >= 6)
                    {
                        sb.AppendLine($"  ... 還有 {codeScan.FileCount - 6} 個");
                        break;
                    }
                    sb.Append("  • ").AppendLine(System.IO.Path.GetFileName(path));
                    idx++;
                }
                sb.AppendLine("  (.cs 改寫不支援 Undo,如需復原請靠 Git / 備份。)");
            }

            if (assetScan.AssetCount == 0 && codeScan.FileCount == 0)
            {
                sb.AppendLine();
                sb.AppendLine("(專案內沒有任何資產或程式碼引用此 Tag,純粹改名 Library。)");
            }
            return sb.ToString().TrimEnd();
        }

        private void TryDeleteSubtree(TagNode node)
        {
            // 收集所有要刪的 index(由大到小排序方便 RemoveAt)
            List<int> indicesToRemove = new();
            for (int i = 0; i < _library.TagDefinitions.Count; i++)
            {
                string p = _library.TagDefinitions[i].TagName;
                if (p == node.FullPath || p.StartsWith(node.FullPath + ".", StringComparison.Ordinal))
                {
                    indicesToRemove.Add(i);
                }
            }
            if (indicesToRemove.Count == 0)
            {
                return;
            }

            // === A4: 掃描全專案 .asset / .prefab 引用 ===
            TagReferenceScanner.ScanResult scan = TagReferenceScanner.Scan(node.FullPath);
            if (scan.ReferenceCount > 0)
            {
                System.Text.StringBuilder sb = new();
                sb.AppendLine($"⚠ 警告: 有 {scan.AssetCount} 個資產的 {scan.ReferenceCount} 處引用了即將刪除的 Tag。");
                sb.AppendLine("刪除後這些引用將變成「未知 Tag」(A5 完成後 Drawer 會以紅字標出)。");
                sb.AppendLine();
                sb.AppendLine("── 受影響資產 (最多 8 個) ──");
                int idx = 0;
                foreach (string path in scan.AssetPaths)
                {
                    if (idx >= 8)
                    {
                        sb.AppendLine($"  ... 還有 {scan.AssetCount - 8} 個");
                        break;
                    }
                    sb.Append("  • ").AppendLine(System.IO.Path.GetFileName(path));
                    idx++;
                }
                sb.AppendLine();
                sb.AppendLine("仍要刪除?");
                if (!EditorUtility.DisplayDialog("刪除前確認 — 偵測到引用", sb.ToString(), "仍要刪除", "取消"))
                {
                    return;
                }
            }

            // === 備份 Library ===
            string backupPath = TagReferenceScanner.BackupLibrarySnapshot(LIBRARY_PATH, LIBRARY_BACKUP_FOLDER);

            Undo.RecordObject(_library, "Delete Tag Subtree");
            SerializedObject so = new(_library);
            SerializedProperty list = so.FindProperty("_tagDefinitions");
            indicesToRemove.Sort((a, b) => b.CompareTo(a)); // 大到小
            foreach (int i in indicesToRemove)
            {
                list.DeleteArrayElementAtIndex(i);
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(_library);
            AssetDatabase.SaveAssetIfDirty(_library);
            // 立即清 Drawer cache,讓 Inspector 中引用已刪 Tag 的欄位即時顯示紅字
            GameplayTagDrawer.ClearCache();
            RebuildTree();
            _selectedFullPath = null;
            ShowNotification(new GUIContent($"已刪除 {indicesToRemove.Count} 個 Tag"));
            if (!string.IsNullOrEmpty(backupPath))
            {
                Debug.Log($"[TagEditor] 刪除前已備份 Library 到 {backupPath}");
            }
        }

        private void ForceRegenerateNow()
        {
            if (_library == null)
            {
                return;
            }
            GameplayTagCodeGenerator.GenerationResult r = GameplayTagCodeGenerator.RegenerateSilent(_library);
            ShowNotification(new GUIContent(r.Success ? "已重生 GameplayTags.cs" : "生成失敗 (見 Console)"));
        }

        // ====================================================================
        // 驗證 / 輔助
        // ====================================================================

        private static bool ValidateSegment(string seg, out string error)
        {
            if (string.IsNullOrWhiteSpace(seg))
            {
                error = "段名稱不可為空。";
                return false;
            }
            if (seg.Contains('.'))
            {
                error = "段名稱不可含 '.' (這是階層分隔符,應只填單一段)。";
                return false;
            }
            char first = seg[0];
            if (!(char.IsLetter(first) || first == '_'))
            {
                error = "段名稱必須以字母或底線開頭。";
                return false;
            }
            for (int i = 1; i < seg.Length; i++)
            {
                char c = seg[i];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    error = $"段名稱含非法字元 '{c}' — 僅允許字母、數字、底線。";
                    return false;
                }
            }
            error = null;
            return true;
        }

        private static bool ValidateFullPath(string fullPath, out string error)
        {
            string[] segs = fullPath.Split('.');
            foreach (string s in segs)
            {
                if (!ValidateSegment(s, out error))
                {
                    return false;
                }
            }
            error = null;
            return true;
        }

        private bool LibraryHasTag(string fullPath)
        {
            return FindLibraryIndex(fullPath) >= 0;
        }

        private int FindLibraryIndex(string fullPath)
        {
            if (_library?.TagDefinitions == null)
            {
                return -1;
            }
            for (int i = 0; i < _library.TagDefinitions.Count; i++)
            {
                if (_library.TagDefinitions[i].TagName == fullPath)
                {
                    return i;
                }
            }
            return -1;
        }

        private int CountAffectedByRename(TagNode node)
        {
            int count = 0;
            if (_library?.TagDefinitions == null)
            {
                return 0;
            }
            foreach (GameplayTagLibrary.TagDefinition d in _library.TagDefinitions)
            {
                if (d.TagName == node.FullPath || d.TagName.StartsWith(node.FullPath + ".", StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        private int CountAffectedByDelete(TagNode node)
        {
            return CountAffectedByRename(node); // 同樣的範圍
        }

        private static TagNode FindNode(TagNode root, string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return null;
            }
            string[] segs = fullPath.Split('.');
            TagNode current = root;
            foreach (string seg in segs)
            {
                if (!current.Children.TryGetValue(seg, out TagNode child))
                {
                    return null;
                }
                current = child;
            }
            return current;
        }

        // ====================================================================
        // 資料模型
        // ====================================================================

        private sealed class TagNode
        {
            public string Segment;
            public string FullPath;
            public string Description = string.Empty;
            public bool IsTag;
            public SortedDictionary<string, TagNode> Children = new(StringComparer.Ordinal);
        }
    }
}
#endif
