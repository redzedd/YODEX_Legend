#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Item;

namespace Cook.Editor
{
    /// <summary>
    /// 食譜可視化編輯器：圖示化材料格、食材庫一鍵加入、成品預覽、衝突偵測與料理模擬測試。
    /// </summary>
    public class RecipeEditorWindow : EditorWindow
    {
        private const float LIST_WIDTH = 250f;
        private const float ICON = 46f;
        private const string DEFAULT_FOLDER = "Assets/Script/Cook/Recipe";

        private readonly List<RecipeData> _allRecipes = new();
        private readonly List<ItemData> _allIngredients = new();
        private readonly List<ItemData> _allItems = new();

        private RecipeData _selected;
        private string _listFilter = "";

        private Vector2 _listScroll;
        private Vector2 _editScroll;

        // 料理模擬測試用的暫存材料
        private readonly List<ItemData> _simIngredients = new();
        private bool _showSimulator;

        [MenuItem("Cooking/食譜編輯器")]
        public static void ShowWindow()
        {
            RecipeEditorWindow wnd = GetWindow<RecipeEditorWindow>();
            wnd.titleContent = new GUIContent("食譜編輯器");
            wnd.minSize = new Vector2(880, 520);
        }

        private void OnEnable()
        {
            RefreshCache();
        }

        private void RefreshCache()
        {
            _allRecipes.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:RecipeData"))
            {
                RecipeData r = AssetDatabase.LoadAssetAtPath<RecipeData>(AssetDatabase.GUIDToAssetPath(guid));
                if (r != null) _allRecipes.Add(r);
            }
            _allRecipes.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));

            _allItems.Clear();
            _allIngredients.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
            {
                ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null) continue;
                _allItems.Add(item);
                if (item.category == GAS.UI.Inventory.InventoryDisplay.Category.Ingredients)
                    _allIngredients.Add(item);
            }
            _allItems.Sort((a, b) => a.itemID.CompareTo(b.itemID));
            _allIngredients.Sort((a, b) => a.itemID.CompareTo(b.itemID));
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawListPanel();
            DrawEditPanel();
            EditorGUILayout.EndHorizontal();
        }

        // ---------- 左側：食譜清單 ----------

        private void DrawListPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LIST_WIDTH));

            EditorGUILayout.LabelField("食譜清單", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 新增食譜", GUILayout.Height(24))) CreateNewRecipe();
            if (GUILayout.Button("⟳", GUILayout.Width(28), GUILayout.Height(24))) RefreshCache();
            EditorGUILayout.EndHorizontal();

            _listFilter = EditorGUILayout.TextField(_listFilter, EditorStyles.toolbarSearchField);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUI.skin.box);
            foreach (RecipeData recipe in _allRecipes)
            {
                if (recipe == null) continue;
                if (!string.IsNullOrEmpty(_listFilter) &&
                    recipe.name.IndexOf(_listFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                DrawListRow(recipe);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawListRow(RecipeData recipe)
        {
            bool isSelected = recipe == _selected;
            Color bg = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);

            EditorGUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(ICON));
            GUI.backgroundColor = bg;

            Texture icon = GetIcon(recipe.resultItem != null ? recipe.resultItem.icon : null);
            GUILayout.Label(icon, GUILayout.Width(ICON), GUILayout.Height(ICON));

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(recipe.name, EditorStyles.boldLabel);
            string result = recipe.resultItem != null ? recipe.resultItem.itemName : "（未設定產物）";
            EditorGUILayout.LabelField($"產物：{result} ×{recipe.resultAmount}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"材料 {recipe.requirements.Count} 格", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            Rect rowRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
            {
                _selected = recipe;
                if (Event.current.clickCount == 2) EditorGUIUtility.PingObject(recipe);
                Event.current.Use();
                Repaint();
            }
        }

        // ---------- 右側：編輯區 ----------

        private void DrawEditPanel()
        {
            EditorGUILayout.BeginVertical();

            if (_selected == null)
            {
                EditorGUILayout.HelpBox("從左側選擇一個食譜，或點「＋ 新增食譜」開始建立。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _editScroll = EditorGUILayout.BeginScrollView(_editScroll);

            // 標題列
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_selected.name, EditorStyles.largeLabel);
            if (GUILayout.Button("在 Project 中選取", GUILayout.Width(120))) EditorGUIUtility.PingObject(_selected);
            if (GUILayout.Button("複製", GUILayout.Width(50))) DuplicateRecipe(_selected);
            if (GUILayout.Button("刪除", GUILayout.Width(50))) DeleteRecipe(_selected);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            DrawConflictWarning();
            DrawManagerRegistration();

            // 成品
            EditorGUILayout.LabelField("產出物", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(GetIcon(_selected.resultItem != null ? _selected.resultItem.icon : null),
                GUILayout.Width(ICON), GUILayout.Height(ICON));
            EditorGUILayout.BeginVertical();
            EditorGUI.BeginChangeCheck();
            ItemData newResult = (ItemData)EditorGUILayout.ObjectField("成品", _selected.resultItem, typeof(ItemData), false);
            int newAmount = EditorGUILayout.IntField("數量", _selected.resultAmount);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_selected, "編輯食譜產物");
                _selected.resultItem = newResult;
                _selected.resultAmount = Mathf.Max(1, newAmount);
                MarkDirty();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            // 材料需求
            EditorGUILayout.LabelField($"材料需求（{_selected.requirements.Count}/5）", EditorStyles.boldLabel);
            DrawRequirements();
            EditorGUILayout.Space();

            // 食材庫
            DrawIngredientPalette();
            EditorGUILayout.Space();

            // 備註
            EditorGUILayout.LabelField("備註", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string note = EditorGUILayout.TextArea(_selected.recipeNote, GUILayout.MinHeight(40));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_selected, "編輯食譜備註");
                _selected.recipeNote = note;
                MarkDirty();
            }

            EditorGUILayout.Space();
            DrawSimulator();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRequirements()
        {
            int removeIndex = -1;
            for (int i = 0; i < _selected.requirements.Count; i++)
            {
                RecipeData.RecipeSlotRequirement req = _selected.requirements[i];
                EditorGUILayout.BeginHorizontal(GUI.skin.box);

                Texture icon = GetIcon(req.requiredItemData != null ? req.requiredItemData.icon : null);
                GUILayout.Label(icon, GUILayout.Width(ICON), GUILayout.Height(ICON));

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField($"材料格 {i + 1}", EditorStyles.miniBoldLabel);

                EditorGUI.BeginChangeCheck();
                ItemData newItem = (ItemData)EditorGUILayout.ObjectField("指定食材", req.requiredItemData, typeof(ItemData), false);
                IngredientType newType = (IngredientType)EditorGUILayout.EnumPopup("食材類型", req.requiredType);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_selected, "編輯材料需求");
                    req.requiredItemData = newItem;
                    req.requiredType = newType;
                    MarkDirty();
                }

                EditorGUILayout.LabelField(DescribeRequirement(req), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(ICON))) removeIndex = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0)
            {
                Undo.RecordObject(_selected, "移除材料格");
                _selected.requirements.RemoveAt(removeIndex);
                MarkDirty();
            }

            using (new EditorGUI.DisabledScope(_selected.requirements.Count >= 5))
            {
                if (GUILayout.Button("＋ 新增空白材料格"))
                {
                    Undo.RecordObject(_selected, "新增材料格");
                    _selected.requirements.Add(new RecipeData.RecipeSlotRequirement());
                    MarkDirty();
                }
            }
        }

        private void DrawIngredientPalette()
        {
            EditorGUILayout.LabelField("食材庫（點一下加入材料格）", EditorStyles.boldLabel);
            if (_allIngredients.Count == 0)
            {
                EditorGUILayout.HelpBox("找不到任何分類為 Ingredients 的 ItemData。", MessageType.None);
                return;
            }

            bool full = _selected.requirements.Count >= 5;
            using (new EditorGUI.DisabledScope(full))
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                float available = Mathf.Max(position.width - LIST_WIDTH - 60f, ICON + 10f);
                int columns = Mathf.Max(1, Mathf.FloorToInt(available / (ICON + 8f)));
                int col = 0;
                EditorGUILayout.BeginHorizontal();
                foreach (ItemData ing in _allIngredients)
                {
                    GUIContent content = new GUIContent(GetIcon(ing.icon), ing.itemName);
                    if (GUILayout.Button(content, GUILayout.Width(ICON), GUILayout.Height(ICON)))
                        AddIngredientSlot(ing);
                    if (++col % columns == 0)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            if (full) EditorGUILayout.HelpBox("已達 5 格上限，移除一格後才能再加入。", MessageType.Info);
        }

        private void AddIngredientSlot(ItemData ingredient)
        {
            if (_selected.requirements.Count >= 5) return;
            Undo.RecordObject(_selected, "加入食材");
            _selected.requirements.Add(new RecipeData.RecipeSlotRequirement { requiredItemData = ingredient });
            MarkDirty();
        }

        // ---------- 衝突偵測 ----------

        private void DrawConflictWarning()
        {
            List<RecipeData> conflicts = _allRecipes
                .Where(r => r != null && r != _selected && SameSignature(r, _selected))
                .ToList();

            if (conflicts.Count == 0) return;

            string names = string.Join("、", conflicts.Select(r => r.name));
            EditorGUILayout.HelpBox(
                $"⚠ 此食譜的材料需求與下列食譜相同，料理時只會配對到其中一個（依清單順序）：{names}",
                MessageType.Warning);
        }

        // 兩食譜是否擁有相同的材料需求簽章（格數相同且每格條件可一一對應）
        private static bool SameSignature(RecipeData a, RecipeData b)
        {
            if (a.requirements.Count != b.requirements.Count) return false;
            List<string> sa = a.requirements.Select(DescribeRequirement).OrderBy(s => s).ToList();
            List<string> sb = b.requirements.Select(DescribeRequirement).OrderBy(s => s).ToList();
            for (int i = 0; i < sa.Count; i++)
                if (sa[i] != sb[i]) return false;
            return true;
        }

        private static string DescribeRequirement(RecipeData.RecipeSlotRequirement req)
        {
            bool hasItem = req.requiredItemData != null;
            bool hasType = req.requiredType != IngredientType.None;
            if (hasItem && hasType) return $"指定:{req.requiredItemData.itemName}+類型:{req.requiredType}";
            if (hasItem) return $"指定:{req.requiredItemData.itemName}";
            if (hasType) return $"類型:{req.requiredType}";
            return "任意食材";
        }

        // ---------- RecipeManager 註冊提示 ----------

        private void DrawManagerRegistration()
        {
            RecipeManager manager = Object.FindFirstObjectByType<RecipeManager>();
            if (manager == null) return;
            if (manager.allRecipes.Contains(_selected)) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox("此食譜尚未加入場景中的 RecipeManager，遊戲執行時不會生效。", MessageType.Warning);
            if (GUILayout.Button("加入 RecipeManager", GUILayout.Width(140), GUILayout.Height(38)))
            {
                Undo.RecordObject(manager, "註冊食譜");
                manager.allRecipes.Add(_selected);
                EditorUtility.SetDirty(manager);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ---------- 料理模擬測試 ----------

        private void DrawSimulator()
        {
            _showSimulator = EditorGUILayout.Foldout(_showSimulator, "料理模擬測試", true);
            if (!_showSimulator) return;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("放入材料模擬一次料理，驗證會配對到哪個食譜：", EditorStyles.miniLabel);

            int removeIndex = -1;
            for (int i = 0; i < _simIngredients.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _simIngredients[i] = (ItemData)EditorGUILayout.ObjectField(_simIngredients[i], typeof(ItemData), false);
                if (GUILayout.Button("✕", GUILayout.Width(24))) removeIndex = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removeIndex >= 0) _simIngredients.RemoveAt(removeIndex);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_simIngredients.Count >= 5))
                if (GUILayout.Button("＋ 加入材料")) _simIngredients.Add(null);
            if (GUILayout.Button("清空")) _simIngredients.Clear();
            EditorGUILayout.EndHorizontal();

            List<ItemData> valid = _simIngredients.Where(i => i != null).ToList();
            if (valid.Count > 0)
            {
                RecipeData match = SimulateMatch(valid);
                if (match != null)
                    EditorGUILayout.HelpBox($"✅ 會配對到：{match.name} → 產出 {match.resultItem?.itemName} ×{match.resultAmount}", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("❌ 沒有符合的食譜，遊戲中會煮出失敗料理。", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        // 複製 RecipeManager 的配對邏輯（依需求格數多到少，逐一貪婪配對）
        private RecipeData SimulateMatch(List<ItemData> inputs)
        {
            List<RecipeData> sorted = new List<RecipeData>(_allRecipes);
            sorted.Sort((a, b) => b.requirements.Count.CompareTo(a.requirements.Count));
            foreach (RecipeData recipe in sorted)
                if (IsMatch(recipe, inputs)) return recipe;
            return null;
        }

        private static bool IsMatch(RecipeData recipe, List<ItemData> inputs)
        {
            if (inputs.Count != recipe.requirements.Count) return false;
            List<ItemData> available = new List<ItemData>(inputs);
            foreach (RecipeData.RecipeSlotRequirement req in recipe.requirements)
            {
                bool found = false;
                for (int i = 0; i < available.Count; i++)
                {
                    ItemData item = available[i];
                    bool nameMatch = req.requiredItemData == null || item == req.requiredItemData;
                    bool typeMatch = req.requiredType == IngredientType.None || item.ingredientType == req.requiredType;
                    if (nameMatch && typeMatch)
                    {
                        available.RemoveAt(i);
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        // ---------- 資產操作 ----------

        private void CreateNewRecipe()
        {
            if (!AssetDatabase.IsValidFolder(DEFAULT_FOLDER))
                System.IO.Directory.CreateDirectory(DEFAULT_FOLDER);

            string path = EditorUtility.SaveFilePanelInProject("建立新食譜", "NewRecipe", "asset", "請輸入食譜檔名", DEFAULT_FOLDER);
            if (string.IsNullOrEmpty(path)) return;

            RecipeData recipe = CreateInstance<RecipeData>();
            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();
            RefreshCache();
            _selected = recipe;
            EditorGUIUtility.PingObject(recipe);
        }

        private void DuplicateRecipe(RecipeData source)
        {
            string srcPath = AssetDatabase.GetAssetPath(source);
            string newPath = AssetDatabase.GenerateUniqueAssetPath(srcPath);
            if (AssetDatabase.CopyAsset(srcPath, newPath))
            {
                AssetDatabase.SaveAssets();
                RefreshCache();
                _selected = AssetDatabase.LoadAssetAtPath<RecipeData>(newPath);
                EditorGUIUtility.PingObject(_selected);
            }
        }

        private void DeleteRecipe(RecipeData recipe)
        {
            if (!EditorUtility.DisplayDialog("刪除食譜", $"確定要刪除「{recipe.name}」嗎？此操作無法復原。", "刪除", "取消"))
                return;
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(recipe));
            _selected = null;
            RefreshCache();
        }

        private void MarkDirty()
        {
            EditorUtility.SetDirty(_selected);
        }

        private static Texture GetIcon(Sprite sprite)
        {
            if (sprite == null) return null;
            Texture preview = AssetPreview.GetAssetPreview(sprite);
            return preview != null ? preview : sprite.texture;
        }
    }
}
#endif
