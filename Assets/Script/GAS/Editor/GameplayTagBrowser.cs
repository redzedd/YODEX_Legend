#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;
using System.Linq;

namespace GAS.Editor
{
    /// <summary>
    /// GameplayTag 瀏覽器視窗
    /// 提供樹狀結構的標籤管理界面
    /// </summary>
    public class GameplayTagBrowser : EditorWindow
    {
        private TreeViewState _treeViewState;
        private TagTreeView _treeView;
        private SearchField _searchField;
        private string _searchString = "";
        
        private GameplayTagLibrary _selectedLibrary;
        private Vector2 _detailScrollPos;
        
        private string _newTagName = "";
        private string _newTagDescription = "";

        [MenuItem("GAS/Tag Browser")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<GameplayTagBrowser>();
            wnd.titleContent = new GUIContent("Tag Browser");
            wnd.minSize = new Vector2(500, 400);
        }

        private void OnEnable()
        {
            _treeViewState = new TreeViewState();
            _searchField = new SearchField();
            
            FindTagLibrary();
            RefreshTreeView();
        }

        private void FindTagLibrary()
        {
            // 嘗試找到 GameplayTagLibrary
            string[] guids = AssetDatabase.FindAssets("t:GameplayTagLibrary");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _selectedLibrary = AssetDatabase.LoadAssetAtPath<GameplayTagLibrary>(path);
            }
        }

        private void RefreshTreeView()
        {
            var allTags = CollectAllTags();
            _treeView = new TagTreeView(_treeViewState, allTags);
            _treeView.OnTagSelected += OnTagSelected;
            _treeView.Reload();
        }

        private List<string> CollectAllTags()
        {
            var tags = new HashSet<string>();

            // 從 Library 獲取
            if (_selectedLibrary != null && _selectedLibrary.TagDefinitions != null)
            {
                foreach (var def in _selectedLibrary.TagDefinitions)
                {
                    if (!string.IsNullOrEmpty(def.TagName))
                        tags.Add(def.TagName);
                }
            }

            // 添加預定義標籤
            AddPredefinedTags(tags);

            return tags.OrderBy(t => t).ToList();
        }

        private void AddPredefinedTags(HashSet<string> tags)
        {
            string[] predefined = {
                "Ability", "Ability.Attack", "Ability.Attack.Melee", "Ability.Attack.Ranged",
                "Ability.Attack.Light", "Ability.Attack.Heavy",
                "Ability.Movement", "Ability.Movement.Dodge", "Ability.Movement.Dash", "Ability.Movement.Jump",
                "Ability.Skill",
                "State", "State.Attacking", "State.Dodging", "State.Stunned", "State.Dead",
                "State.Invincible", "State.CannotMove", "State.CannotAttack",
                "Effect", "Effect.Damage", "Effect.Damage.Physical", "Effect.Damage.Magical",
                "Effect.Damage.Fire", "Effect.Damage.Ice",
                "Effect.Buff", "Effect.Buff.AttackUp", "Effect.Buff.DefenseUp", "Effect.Buff.SpeedUp",
                "Effect.Debuff", "Effect.Debuff.AttackDown", "Effect.Debuff.DefenseDown", "Effect.Debuff.Slow",
                "Cue", "Cue.HitImpact", "Cue.Attack", "Cue.Dodge",
                "Event", "Event.Montage", "Event.HitWindow"
            };

            foreach (var tag in predefined)
                tags.Add(tag);
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            // Library 選擇器
            EditorGUI.BeginChangeCheck();
            _selectedLibrary = (GameplayTagLibrary)EditorGUILayout.ObjectField(
                _selectedLibrary, typeof(GameplayTagLibrary), false, GUILayout.Width(200));
            if (EditorGUI.EndChangeCheck())
            {
                RefreshTreeView();
            }

            // 搜索欄
            GUILayout.Space(10);
            EditorGUI.BeginChangeCheck();
            _searchString = _searchField.OnToolbarGUI(_searchString);
            if (EditorGUI.EndChangeCheck())
            {
                _treeView.searchString = _searchString;
            }

            // 刷新按鈕
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                GameplayTagDrawer.ClearCache();
                RefreshTreeView();
            }

            EditorGUILayout.EndHorizontal();

            // 主要內容區
            EditorGUILayout.BeginHorizontal();

            // 左側：標籤樹
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.6f));
            
            Rect treeRect = GUILayoutUtility.GetRect(0, position.height - 120, GUILayout.ExpandWidth(true));
            _treeView?.OnGUI(treeRect);

            EditorGUILayout.EndVertical();

            // 右側：詳情面板
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(position.width * 0.4f - 10));
            DrawDetailPanel();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            // 底部：新增標籤區
            DrawAddTagSection();
        }

        private void DrawDetailPanel()
        {
            _detailScrollPos = EditorGUILayout.BeginScrollView(_detailScrollPos);

            EditorGUILayout.LabelField("Tag Details", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            if (_treeView != null && _treeView.HasSelection())
            {
                var selection = _treeView.GetSelection();
                if (selection.Count > 0)
                {
                    var item = _treeView.FindItem(selection[0]);
                    if (item != null)
                    {
                        DrawTagDetails(item.TagName);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Select a tag to view details", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTagDetails(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return;

            // 標籤名稱
            EditorGUILayout.LabelField("Name:", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(tagName, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            // 階層
            var tag = new GameplayTag(tagName);
            EditorGUILayout.LabelField("Depth:", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(tag.GetDepth().ToString());

            // 父標籤
            var parent = tag.GetParentTag();
            if (parent.IsValid)
            {
                EditorGUILayout.LabelField("Parent:", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(parent.TagName);
            }

            EditorGUILayout.Space(10);

            // 使用情況
            EditorGUILayout.LabelField("Usage", EditorStyles.boldLabel);
            DrawTagUsage(tagName);

            EditorGUILayout.Space(10);

            // 操作按鈕
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Copy Tag Name"))
            {
                EditorGUIUtility.systemCopyBuffer = tagName;
                Debug.Log($"Copied: {tagName}");
            }

            if (_selectedLibrary != null)
            {
                if (GUILayout.Button("Add to Library"))
                {
                    _selectedLibrary.AddTagDefinition(tagName);
                    EditorUtility.SetDirty(_selectedLibrary);
                    AssetDatabase.SaveAssets();
                }
            }

            EditorGUILayout.EndHorizontal();

            // 刪除按鈕
            if (_selectedLibrary != null)
            {
                EditorGUILayout.Space(5);
                GUI.color = new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button("Remove from Library"))
                {
                    if (EditorUtility.DisplayDialog("Confirm Remove", 
                        $"Remove tag '{tagName}' from library?", "Remove", "Cancel"))
                    {
                        _selectedLibrary.RemoveTagDefinition(tagName);
                        EditorUtility.SetDirty(_selectedLibrary);
                        AssetDatabase.SaveAssets();
                        RefreshTreeView();
                    }
                }
                GUI.color = Color.white;
            }
        }

        private void DrawTagUsage(string tagName)
        {
            int usageCount = 0;

            // 搜索使用此標籤的 Ability
            string[] abilityGuids = AssetDatabase.FindAssets("t:GameplayAbility");
            foreach (var guid in abilityGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ability = AssetDatabase.LoadAssetAtPath<GameplayAbility>(path);
                if (ability != null && ability.AbilityTag.TagName == tagName)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Ability:", GUILayout.Width(60));
                    if (GUILayout.Button(ability.name, EditorStyles.linkLabel))
                    {
                        Selection.activeObject = ability;
                        EditorGUIUtility.PingObject(ability);
                    }
                    EditorGUILayout.EndHorizontal();
                    usageCount++;
                }
            }

            // 搜索使用此標籤的 Effect
            string[] effectGuids = AssetDatabase.FindAssets("t:GameplayEffect");
            foreach (var guid in effectGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var effect = AssetDatabase.LoadAssetAtPath<GameplayEffect>(path);
                if (effect != null && effect.EffectTag.TagName == tagName)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Effect:", GUILayout.Width(60));
                    if (GUILayout.Button(effect.name, EditorStyles.linkLabel))
                    {
                        Selection.activeObject = effect;
                        EditorGUIUtility.PingObject(effect);
                    }
                    EditorGUILayout.EndHorizontal();
                    usageCount++;
                }
            }

            if (usageCount == 0)
            {
                EditorGUILayout.LabelField("No direct usage found", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void DrawAddTagSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Add New Tag", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField("Name:", GUILayout.Width(50));
            _newTagName = EditorGUILayout.TextField(_newTagName);

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_newTagName) || _selectedLibrary == null);
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                if (_selectedLibrary != null)
                {
                    _selectedLibrary.AddTagDefinition(_newTagName, _newTagDescription);
                    EditorUtility.SetDirty(_selectedLibrary);
                    AssetDatabase.SaveAssets();
                    RefreshTreeView();
                    _newTagName = "";
                    _newTagDescription = "";
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            if (_selectedLibrary == null)
            {
                EditorGUILayout.HelpBox("Select a GameplayTagLibrary to add tags", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void OnTagSelected(string tagName)
        {
            Repaint();
        }
    }

    /// <summary>
    /// 標籤樹狀視圖
    /// </summary>
    public class TagTreeView : TreeView
    {
        private List<string> _allTags;
        private Dictionary<int, TagTreeViewItem> _itemMap = new();
        
        public event System.Action<string> OnTagSelected;

        public TagTreeView(TreeViewState state, List<string> tags) : base(state)
        {
            _allTags = tags;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            _itemMap.Clear();

            // 建立階層結構
            var nodeMap = new Dictionary<string, TagTreeViewItem>();
            int id = 1;

            foreach (var tagName in _allTags)
            {
                string[] parts = tagName.Split('.');
                string currentPath = "";

                TagTreeViewItem parent = null;
                
                for (int i = 0; i < parts.Length; i++)
                {
                    currentPath = i == 0 ? parts[i] : currentPath + "." + parts[i];
                    
                    if (!nodeMap.TryGetValue(currentPath, out var node))
                    {
                        node = new TagTreeViewItem
                        {
                            id = id++,
                            depth = i,
                            displayName = parts[i],
                            TagName = currentPath,
                            IsLeaf = (i == parts.Length - 1)
                        };
                        nodeMap[currentPath] = node;
                        _itemMap[node.id] = node;

                        if (parent != null)
                        {
                            parent.AddChild(node);
                        }
                        else
                        {
                            root.AddChild(node);
                        }
                    }
                    
                    parent = node;
                }
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item as TagTreeViewItem;
            
            // 繪製圖標
            Rect iconRect = args.rowRect;
            iconRect.x += GetContentIndent(args.item);
            iconRect.width = 16;

            Color iconColor = item.IsLeaf ? new Color(0.2f, 0.8f, 0.4f) : new Color(0.8f, 0.6f, 0.2f);
            EditorGUI.DrawRect(new Rect(iconRect.x, iconRect.y + 4, 8, 8), iconColor);

            // 繪製標籤
            Rect labelRect = args.rowRect;
            labelRect.x += GetContentIndent(args.item) + 12;
            
            GUI.Label(labelRect, args.item.displayName);
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);
            
            if (selectedIds.Count > 0 && _itemMap.TryGetValue(selectedIds[0], out var item))
            {
                OnTagSelected?.Invoke(item.TagName);
            }
        }

        protected override void DoubleClickedItem(int id)
        {
            if (_itemMap.TryGetValue(id, out var item))
            {
                // 複製標籤名稱到剪貼板
                EditorGUIUtility.systemCopyBuffer = item.TagName;
                Debug.Log($"Copied tag: {item.TagName}");
            }
        }

        public TagTreeViewItem FindItem(int id)
        {
            _itemMap.TryGetValue(id, out var item);
            return item;
        }
    }

    public class TagTreeViewItem : TreeViewItem
    {
        public string TagName;
        public bool IsLeaf;
    }
}
#endif
