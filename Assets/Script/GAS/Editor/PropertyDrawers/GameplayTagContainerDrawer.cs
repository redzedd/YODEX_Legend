#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Linq;

namespace GAS.Editor
{
    /// <summary>
    /// GameplayTagContainer 的自定義 PropertyDrawer
    /// 提供可重排序的標籤列表和便捷的添加/刪除功能
    /// 共用 GameplayTagDrawer 的分類式選單與 Custom Tag 掃描機制
    /// </summary>
    [CustomPropertyDrawer(typeof(GameplayTagContainer))]
    public class GameplayTagContainerDrawer : PropertyDrawer
    {
        private Dictionary<string, ReorderableList> _listCache = new();
        private Dictionary<string, bool> _foldoutState = new();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var tagsProperty = property.FindPropertyRelative("_tags");
            string key = property.propertyPath;
            if (!_foldoutState.ContainsKey(key))
                _foldoutState[key] = true;
            Rect headerRect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            _foldoutState[key] = EditorGUI.Foldout(
                new Rect(headerRect.x, headerRect.y, headerRect.width - 60, headerRect.height),
                _foldoutState[key],
                $"{label.text} ({tagsProperty.arraySize})",
                true);
            if (GUI.Button(new Rect(headerRect.xMax - 55, headerRect.y, 55, headerRect.height), "Add"))
            {
                ShowAddTagMenu(tagsProperty);
            }
            if (_foldoutState[key])
            {
                EditorGUI.indentLevel++;
                float yOffset = headerRect.height + 2;
                if (!_listCache.TryGetValue(key, out var list) ||
                    list.serializedProperty.serializedObject != property.serializedObject)
                {
                    list = CreateReorderableList(tagsProperty);
                    _listCache[key] = list;
                }
                list.serializedProperty = tagsProperty;
                Rect listRect = new(position.x, position.y + yOffset, position.width, list.GetHeight());
                list.DoList(listRect);
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight + 2;
            string key = property.propertyPath;
            if (_foldoutState.TryGetValue(key, out bool isExpanded) && isExpanded)
            {
                var tagsProperty = property.FindPropertyRelative("_tags");
                if (_listCache.TryGetValue(key, out var list))
                {
                    height += list.GetHeight();
                }
                else
                {
                    height += (tagsProperty.arraySize + 3) * (EditorGUIUtility.singleLineHeight + 2);
                }
            }
            return height;
        }

        private ReorderableList CreateReorderableList(SerializedProperty tagsProperty)
        {
            var list = new ReorderableList(
                tagsProperty.serializedObject, tagsProperty,
                true, true, true, true);
            list.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Tags");
            };
            list.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= tagsProperty.arraySize) return;
                var element = tagsProperty.GetArrayElementAtIndex(index);
                var tagNameProp = element.FindPropertyRelative("_tagName");
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                // 使用共用的 DrawTagDropdown — 未知 Tag 會自動紅底警告
                string currentValue = tagNameProp.stringValue;
                bool unknown = !GameplayTagDrawer.IsKnownTag(currentValue);
                Color prevBg = GUI.backgroundColor;
                Color prevContent = GUI.contentColor;
                GUIContent content;
                if (unknown)
                {
                    GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                    GUI.contentColor = new Color(1f, 0.92f, 0.92f);
                    content = new GUIContent(
                        "⚠ " + currentValue,
                        $"未知 Tag: '{currentValue}'\n可能已被重命名、刪除,或未加入 GameplayTagLibrary。");
                }
                else
                {
                    content = new GUIContent(string.IsNullOrEmpty(currentValue) ? "(None)" : currentValue);
                }
                if (EditorGUI.DropdownButton(rect, content, FocusType.Keyboard))
                {
                    ShowTagSelectorMenu(tagNameProp, rect);
                }
                GUI.backgroundColor = prevBg;
                GUI.contentColor = prevContent;
            };
            list.onAddCallback = (ReorderableList l) =>
            {
                ShowAddTagMenu(tagsProperty);
            };
            list.elementHeight = EditorGUIUtility.singleLineHeight + 4;
            return list;
        }

        /// <summary>
        /// 新增標籤的分類式選單（Add 按鈕 / ReorderableList 的 + 按鈕）
        /// </summary>
        private void ShowAddTagMenu(SerializedProperty tagsProperty)
        {
            var menu = new GenericMenu();
            var knownTags = GameplayTagDrawer.GetAllKnownTags();
            var customTags = GameplayTagDrawer.GetAllCustomTags();
            var parentSet = BuildParentSet(knownTags);
            // 已知標籤（按分類展開）
            foreach (var tag in knownTags)
            {
                string menuPath = tag.Replace('.', '/');
                bool isParent = parentSet.Contains(tag);
                if (isParent)
                {
                    string lastPart = tag.Contains('.')
                        ? tag.Substring(tag.LastIndexOf('.') + 1) : tag;
                    menuPath += $"/(Select) {lastPart}";
                }
                string capturedTag = tag;
                menu.AddItem(new GUIContent(menuPath), false, () =>
                {
                    tagsProperty.arraySize++;
                    var newElem = tagsProperty.GetArrayElementAtIndex(tagsProperty.arraySize - 1);
                    newElem.FindPropertyRelative("_tagName").stringValue = capturedTag;
                    tagsProperty.serializedObject.ApplyModifiedProperties();
                });
            }
            // Custom 標籤
            if (customTags != null && customTags.Count > 0)
            {
                menu.AddSeparator("");
                foreach (var tag in customTags)
                {
                    string capturedTag = tag;
                    menu.AddItem(new GUIContent("Custom/" + tag.Replace('.', '/')), false, () =>
                    {
                        tagsProperty.arraySize++;
                        var newElem = tagsProperty.GetArrayElementAtIndex(tagsProperty.arraySize - 1);
                        newElem.FindPropertyRelative("_tagName").stringValue = capturedTag;
                        tagsProperty.serializedObject.ApplyModifiedProperties();
                    });
                }
            }
            // 手動輸入
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enter Custom Tag\u2026"), false, () =>
            {
                tagsProperty.arraySize++;
                var newElem = tagsProperty.GetArrayElementAtIndex(tagsProperty.arraySize - 1);
                newElem.FindPropertyRelative("_tagName").stringValue = "";
                tagsProperty.serializedObject.ApplyModifiedProperties();
            });
            menu.ShowAsContext();
        }

        /// <summary>
        /// 單一標籤的分類式選取選單（點擊列表項目時的 DropdownButton）
        /// </summary>
        private void ShowTagSelectorMenu(SerializedProperty tagNameProp, Rect rect)
        {
            var menu = new GenericMenu();
            string currentValue = tagNameProp.stringValue;
            var knownTags = GameplayTagDrawer.GetAllKnownTags();
            var customTags = GameplayTagDrawer.GetAllCustomTags();
            var parentSet = BuildParentSet(knownTags);
            // (None)
            menu.AddItem(new GUIContent("(None)"),
                string.IsNullOrEmpty(currentValue),
                () =>
                {
                    tagNameProp.stringValue = string.Empty;
                    tagNameProp.serializedObject.ApplyModifiedProperties();
                });
            menu.AddSeparator("");
            // 已知標籤
            foreach (var tag in knownTags)
            {
                string menuPath = tag.Replace('.', '/');
                bool isParent = parentSet.Contains(tag);
                if (isParent)
                {
                    string lastPart = tag.Contains('.')
                        ? tag.Substring(tag.LastIndexOf('.') + 1) : tag;
                    menuPath += $"/(Select) {lastPart}";
                }
                string capturedTag = tag;
                menu.AddItem(new GUIContent(menuPath),
                    currentValue == capturedTag,
                    () =>
                    {
                        tagNameProp.stringValue = capturedTag;
                        tagNameProp.serializedObject.ApplyModifiedProperties();
                    });
            }
            // Custom 標籤
            if (customTags != null && customTags.Count > 0)
            {
                menu.AddSeparator("");
                foreach (var tag in customTags)
                {
                    string capturedTag = tag;
                    menu.AddItem(new GUIContent("Custom/" + tag.Replace('.', '/')),
                        currentValue == capturedTag,
                        () =>
                        {
                            tagNameProp.stringValue = capturedTag;
                            tagNameProp.serializedObject.ApplyModifiedProperties();
                        });
                }
            }
            // 手動輸入
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enter Custom Tag\u2026"), false, () =>
            {
                var window = EditorWindow.GetWindow<CustomTagInputWindow>(true, "Enter Custom Tag", true);
                window.Initialize(tagNameProp, currentValue);
                window.ShowUtility();
            });
            menu.DropDown(rect);
        }

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
    }
}
#endif
