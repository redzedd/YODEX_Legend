#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GAS.Editor
{
    /// <summary>
    /// GameplayTag 的自定義 PropertyDrawer
    /// 提供分類式 GenericMenu 選擇標籤（按 Ability、Cue、State 等分類）
    /// 自動掃描專案中已使用的 Custom Tag，可直接選取不需重新輸入
    /// </summary>
    [CustomPropertyDrawer(typeof(GameplayTag))]
    public class GameplayTagDrawer : PropertyDrawer
    {
        private static List<string> _cachedKnownTags;
        private static HashSet<string> _cachedKnownTagSet;
        private static List<string> _cachedCustomTags;
        private static double _lastCacheTime;
        // Cache 從 5 秒延長至 60 秒 — Library 變動會由 AssetPostprocessor 主動 ClearCache,
        // 不再依賴定時失效。Inspector 反覆 Repaint 時不必反覆重建 cache → 效能大幅改善。
        private const double CACHE_DURATION = 60.0;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var tagNameProp = property.FindPropertyRelative("_tagName");
            string currentValue = tagNameProp.stringValue;
            float buttonWidth = 20f;
            float mainWidth = position.width - EditorGUIUtility.labelWidth - buttonWidth - 4f;
            Rect labelRect = new(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            Rect mainRect = new(position.x + EditorGUIUtility.labelWidth, position.y, mainWidth, position.height);
            Rect buttonRect = new(mainRect.xMax + 2f, position.y, buttonWidth, position.height);
            EditorGUI.LabelField(labelRect, label);
            // 顯示分類式下拉選單按鈕,未知 Tag → 紅底 + 警告 icon
            DrawTagDropdown(mainRect, tagNameProp, currentValue);
            // 瀏覽器按鈕
            if (GUI.Button(buttonRect, "\u2026"))
            {
                ShowTagBrowserPopup(tagNameProp, position);
            }
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 繪製 Tag 下拉按鈕,並在 Tag 值不在 Library 中時加紅底警告(供 GameplayTagContainerDrawer 共用)。
        /// </summary>
        public static void DrawTagDropdown(Rect rect, SerializedProperty tagNameProp, string currentValue)
        {
            bool unknown = !IsKnownTag(currentValue);
            Color prevBg = GUI.backgroundColor;
            Color prevContent = GUI.contentColor;
            GUIContent content;
            if (unknown)
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                GUI.contentColor = new Color(1f, 0.92f, 0.92f);
                content = new GUIContent(
                    "⚠ " + currentValue,
                    $"未知 Tag: '{currentValue}'\n可能已被重命名、刪除,或未加入 GameplayTagLibrary。\n請在 GAS/Tag System/Tag Editor 中檢查。");
            }
            else
            {
                content = new GUIContent(string.IsNullOrEmpty(currentValue) ? "(None)" : currentValue);
            }
            if (EditorGUI.DropdownButton(rect, content, FocusType.Keyboard))
            {
                ShowCategorizedMenuStatic(tagNameProp, rect);
            }
            GUI.backgroundColor = prevBg;
            GUI.contentColor = prevContent;
        }

        /// <summary>
        /// 此 Tag 字串是否存在於 Library / GameplayTags 中。空字串視為「已知」(代表 None)。
        /// </summary>
        public static bool IsKnownTag(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return true;
            }
            EnsureCache();
            return _cachedKnownTagSet != null && _cachedKnownTagSet.Contains(tagName);
        }

        private static void ShowCategorizedMenuStatic(SerializedProperty tagNameProp, Rect rect)
        {
            new GameplayTagDrawer().ShowCategorizedMenu(tagNameProp, rect);
        }

        /// <summary>
        /// 顯示分類式 GenericMenu
        /// 按 Ability、Cue、State 等分類組織標籤，以子選單形式展開
        /// Custom 區塊列出專案中已使用的非預定義標籤
        /// </summary>
        private void ShowCategorizedMenu(SerializedProperty tagNameProp, Rect rect)
        {
            EnsureCache();
            var menu = new GenericMenu();
            string currentValue = tagNameProp.stringValue;
            // (None)
            menu.AddItem(
                new GUIContent("(None)"),
                string.IsNullOrEmpty(currentValue),
                () => SetTagValue(tagNameProp, ""));
            menu.AddSeparator("");
            // 判定哪些標籤是父節點（有子標籤）
            var parentSet = BuildParentSet(_cachedKnownTags);
            // 依分類添加已知標籤（自動形成子選單階層）
            foreach (var tag in _cachedKnownTags)
            {
                string menuPath = tag.Replace('.', '/');
                bool isParent = parentSet.Contains(tag);
                if (isParent)
                {
                    // 父節點同時也是可選標籤 → 加入 (Select) 子項
                    string lastPart = tag.Contains('.')
                        ? tag.Substring(tag.LastIndexOf('.') + 1)
                        : tag;
                    menuPath += $"/(Select) {lastPart}";
                }
                string capturedTag = tag;
                menu.AddItem(
                    new GUIContent(menuPath),
                    currentValue == capturedTag,
                    () => SetTagValue(tagNameProp, capturedTag));
            }
            // Custom 標籤（專案中已使用但不在預定義清單中的標籤）
            if (_cachedCustomTags != null && _cachedCustomTags.Count > 0)
            {
                menu.AddSeparator("");
                foreach (var tag in _cachedCustomTags)
                {
                    string capturedTag = tag;
                    // Custom 標籤也按階層展開
                    string menuPath = "Custom/" + tag.Replace('.', '/');
                    menu.AddItem(
                        new GUIContent(menuPath),
                        currentValue == capturedTag,
                        () => SetTagValue(tagNameProp, capturedTag));
                }
            }
            // 手動輸入新 Custom Tag
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enter Custom Tag\u2026"), false, () =>
            {
                ShowCustomTagInput(tagNameProp, currentValue);
            });
            menu.DropDown(rect);
        }

        /// <summary>
        /// 建立父節點集合，用於判斷哪些標籤下方還有子標籤
        /// </summary>
        private static HashSet<string> BuildParentSet(List<string> tags)
        {
            var parentSet = new HashSet<string>();
            foreach (var tag in tags)
            {
                string[] parts = tag.Split('.');
                for (int depth = 1; depth < parts.Length; depth++)
                {
                    parentSet.Add(string.Join(".", parts, 0, depth));
                }
            }
            return parentSet;
        }

        private static void SetTagValue(SerializedProperty tagNameProp, string value)
        {
            tagNameProp.stringValue = value;
            tagNameProp.serializedObject.ApplyModifiedProperties();
        }

        #region 快取管理

        private static void EnsureCache()
        {
            if (_cachedKnownTags != null &&
                EditorApplication.timeSinceStartup - _lastCacheTime < CACHE_DURATION)
            {
                return;
            }
            _cachedKnownTagSet = CollectKnownTags();
            _cachedKnownTags = _cachedKnownTagSet.OrderBy(t => t).ToList();
            // A5 之後 Library 即為單一真實來源 — 任何不在 Library 的 Tag 都由 Drawer 紅字呈現,
            // 不再需要把專案內已用的 Custom Tag 列入下拉選單(那是慢的全專案掃描)。
            // 若未來想恢復,呼叫 RefreshCustomTags() 顯式觸發。
            _cachedCustomTags = new List<string>();
            _lastCacheTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// 顯式觸發全專案掃描以收集 Custom Tag。預設 EnsureCache 不再自動跑,
        /// 若需要 (例如除錯) 可從外部呼叫。
        /// </summary>
        public static void RefreshCustomTags()
        {
            EnsureCache();
            _cachedCustomTags = ScanProjectForCustomTags(_cachedKnownTagSet);
        }

        /// <summary>
        /// 收集所有已知標籤（從 GameplayTags 靜態類反射 + GameplayTagLibrary 資產）
        /// </summary>
        private static HashSet<string> CollectKnownTags()
        {
            var tags = new HashSet<string>();
            // 透過反射從 GameplayTags 靜態類收集所有 GameplayTag 常量
            CollectTagsViaReflection(typeof(GameplayTags), tags);
            // 從 GameplayTagLibrary 資產收集
            string[] guids = AssetDatabase.FindAssets("t:GameplayTagLibrary");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var lib = AssetDatabase.LoadAssetAtPath<GameplayTagLibrary>(path);
                if (lib?.TagDefinitions == null) continue;
                foreach (var def in lib.TagDefinitions)
                {
                    if (!string.IsNullOrEmpty(def.TagName))
                        tags.Add(def.TagName);
                }
            }
            return tags;
        }

        /// <summary>
        /// 遞迴反射收集指定類型及其巢狀類型中所有 static readonly GameplayTag 欄位
        /// 確保 GameplayTags 新增標籤時自動同步
        /// </summary>
        private static void CollectTagsViaReflection(System.Type type, HashSet<string> tags)
        {
            const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Static;
            foreach (var field in type.GetFields(FLAGS))
            {
                if (field.FieldType != typeof(GameplayTag)) continue;
                var tag = (GameplayTag)field.GetValue(null);
                if (tag.IsValid)
                    tags.Add(tag.TagName);
            }
            foreach (var nested in type.GetNestedTypes(FLAGS))
            {
                CollectTagsViaReflection(nested, tags);
            }
        }

        /// <summary>
        /// 掃描專案中 ScriptableObject 資產，收集已使用但不在預定義清單中的 Custom Tag
        /// </summary>
        private static List<string> ScanProjectForCustomTags(HashSet<string> knownTags)
        {
            var customTags = new HashSet<string>();
            string[] searchFolders = { "Assets" };
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", searchFolders);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/")) continue;
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (obj == null) continue;
                var so = new SerializedObject(obj);
                var iterator = so.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = true;
                    if (iterator.name == "_tagName" &&
                        iterator.propertyType == SerializedPropertyType.String)
                    {
                        string val = iterator.stringValue;
                        if (!string.IsNullOrEmpty(val) && !knownTags.Contains(val))
                        {
                            customTags.Add(val);
                        }
                    }
                }
            }
            return customTags.OrderBy(t => t).ToList();
        }

        /// <summary>
        /// 清除標籤快取，強制重新載入
        /// </summary>
        public static void ClearCache()
        {
            _cachedKnownTags = null;
            _cachedKnownTagSet = null;
            _cachedCustomTags = null;
        }

        /// <summary>
        /// 取得所有已知標籤（供外部使用）
        /// </summary>
        public static List<string> GetAllKnownTags()
        {
            EnsureCache();
            return _cachedKnownTags;
        }

        /// <summary>
        /// 取得所有已掃描的 Custom 標籤（供外部使用）
        /// </summary>
        public static List<string> GetAllCustomTags()
        {
            EnsureCache();
            return _cachedCustomTags;
        }

        #endregion

        #region 輔助視窗

        private void ShowCustomTagInput(SerializedProperty tagNameProp, string currentValue)
        {
            var window = EditorWindow.GetWindow<CustomTagInputWindow>(true, "Enter Custom Tag", true);
            window.Initialize(tagNameProp, currentValue);
            window.ShowUtility();
        }

        private void ShowTagBrowserPopup(SerializedProperty tagNameProp, Rect position)
        {
            var popup = new TagBrowserPopup(tagNameProp);
            PopupWindow.Show(position, popup);
        }

        #endregion
    }

    /// <summary>
    /// 自定義標籤輸入視窗
    /// </summary>
    public class CustomTagInputWindow : EditorWindow
    {
        private SerializedProperty _property;
        private string _tagValue;

        public void Initialize(SerializedProperty property, string currentValue)
        {
            _property = property;
            _tagValue = currentValue ?? string.Empty;
            minSize = new Vector2(300, 80);
            maxSize = new Vector2(400, 80);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("輸入標籤名稱（使用 . 分隔階層）：", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            _tagValue = EditorGUILayout.TextField("Tag", _tagValue);
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("取消"))
            {
                Close();
            }
            if (GUILayout.Button("套用"))
            {
                if (_property != null)
                {
                    _property.stringValue = _tagValue;
                    _property.serializedObject.ApplyModifiedProperties();
                    GameplayTagDrawer.ClearCache();
                }
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// 標籤瀏覽器彈出視窗
    /// 支援分類篩選（All / Ability / State / Cue / Effect / Event / Custom）
    /// 階層式顯示並附帶搜尋功能
    /// </summary>
    public class TagBrowserPopup : PopupWindowContent
    {
        private SerializedProperty _property;
        private string _searchFilter = "";
        private int _selectedCategory;
        private Vector2 _scrollPos;
        private List<string> _knownTags;
        private List<string> _customTags;
        private List<string> _filteredTags;

        private static readonly string[] CATEGORIES =
            { "All", "Ability", "State", "Effect", "Cue", "Event", "Custom" };
        private static readonly Color[] CATEGORY_COLORS =
        {
            new(0.7f, 0.7f, 0.7f),
            new(0.4f, 0.8f, 0.4f),
            new(0.8f, 0.8f, 0.3f),
            new(0.8f, 0.4f, 0.4f),
            new(0.3f, 0.7f, 1f),
            new(0.9f, 0.6f, 0.3f),
            new(0.7f, 0.5f, 0.9f)
        };

        public TagBrowserPopup(SerializedProperty property)
        {
            _property = property;
            RefreshTags();
        }

        public override Vector2 GetWindowSize() => new(360, 480);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.BeginVertical();
            // 搜尋列
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                FilterTags();
            }
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton")))
            {
                _searchFilter = "";
                FilterTags();
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            // 分類篩選按鈕
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < CATEGORIES.Length; i++)
            {
                bool isSelected = _selectedCategory == i;
                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = isSelected ? CATEGORY_COLORS[i] : Color.white;
                if (GUILayout.Button(CATEGORIES[i], EditorStyles.miniButton, GUILayout.MinWidth(36)))
                {
                    _selectedCategory = i;
                    FilterTags();
                }
                GUI.backgroundColor = prevColor;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
            // 標籤列表
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            // (None) 選項
            if (DrawTagButton("(None)", "", 0, false))
            {
                _property.stringValue = string.Empty;
                _property.serializedObject.ApplyModifiedProperties();
                editorWindow.Close();
                return;
            }
            // 按分類繪製標籤
            string lastCategory = "";
            foreach (var tag in _filteredTags)
            {
                string category = GetTopCategory(tag);
                if (category != lastCategory)
                {
                    lastCategory = category;
                    EditorGUILayout.Space(4);
                    // 分類標頭
                    int catIdx = System.Array.IndexOf(CATEGORIES, category);
                    Color headerColor = catIdx >= 0 ? CATEGORY_COLORS[catIdx] : Color.white;
                    bool isCustom = _customTags != null && _customTags.Contains(tag);
                    if (isCustom) headerColor = CATEGORY_COLORS[6]; // Custom 色
                    DrawCategoryHeader(isCustom ? "Custom" : category, headerColor);
                }
                int depth = tag.Count(c => c == '.');
                string currentValue = _property.stringValue;
                bool selected = currentValue == tag;
                if (DrawTagButton(tag, tag, depth, selected))
                {
                    _property.stringValue = tag;
                    _property.serializedObject.ApplyModifiedProperties();
                    editorWindow.Close();
                    return;
                }
            }
            EditorGUILayout.EndScrollView();
            // 底部按鈕列
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ New Custom Tag"))
            {
                var window = EditorWindow.GetWindow<CustomTagInputWindow>(true, "New Tag", true);
                window.Initialize(_property, "");
                window.ShowUtility();
            }
            if (GUILayout.Button("Refresh"))
            {
                GameplayTagDrawer.ClearCache();
                RefreshTags();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static void DrawCategoryHeader(string category, Color color)
        {
            var prevColor = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField($"\u25B6 {category}", EditorStyles.boldLabel);
            GUI.contentColor = prevColor;
        }

        private static bool DrawTagButton(string displayTag, string fullTag, int depth, bool selected)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 14f);
            // 取得顯示名稱（最後一段）
            string displayName = displayTag;
            if (fullTag.Contains('.'))
            {
                displayName = fullTag.Substring(fullTag.LastIndexOf('.') + 1);
            }
            // 選中標記
            var style = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal
            };
            bool clicked = GUILayout.Button(
                selected ? $"\u2714 {displayName}" : $"  {displayName}",
                style);
            // Tooltip 顯示完整路徑
            if (!string.IsNullOrEmpty(fullTag))
            {
                Rect last = GUILayoutUtility.GetLastRect();
                EditorGUI.LabelField(
                    new Rect(last.xMax - 150, last.y, 148, last.height),
                    fullTag,
                    new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
                    });
            }
            EditorGUILayout.EndHorizontal();
            return clicked;
        }

        private static string GetTopCategory(string tag)
        {
            int dot = tag.IndexOf('.');
            return dot > 0 ? tag.Substring(0, dot) : tag;
        }

        private void RefreshTags()
        {
            _knownTags = GameplayTagDrawer.GetAllKnownTags();
            _customTags = GameplayTagDrawer.GetAllCustomTags();
            FilterTags();
        }

        private void FilterTags()
        {
            // 合併已知 + Custom 標籤
            var allTags = new List<string>(_knownTags);
            if (_customTags != null && _customTags.Count > 0)
            {
                allTags.AddRange(_customTags);
            }
            // 分類篩選
            string catFilter = _selectedCategory > 0 ? CATEGORIES[_selectedCategory] : "";
            _filteredTags = allTags
                .Where(t =>
                {
                    // 分類篩選
                    if (!string.IsNullOrEmpty(catFilter))
                    {
                        if (catFilter == "Custom")
                            return _customTags != null && _customTags.Contains(t);
                        return t.StartsWith(catFilter + ".", System.StringComparison.Ordinal)
                            || t == catFilter;
                    }
                    return true;
                })
                .Where(t =>
                {
                    // 搜尋篩選
                    if (!string.IsNullOrEmpty(_searchFilter))
                        return t.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    return true;
                })
                .Distinct()
                .OrderBy(t => t)
                .ToList();
        }
    }
}
#endif
