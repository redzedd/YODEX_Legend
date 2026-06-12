#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GAS;
using GAS.UI.Inventory;

namespace Item.Editor
{
    /// <summary>
    /// ItemData 可視化編輯器：清單瀏覽、分類篩選、圖示預覽、自動指派 ID、重複 ID 偵測與一鍵註冊到 ItemDatabase。
    /// </summary>
    public class ItemDataEditorWindow : EditorWindow
    {
        private const float LIST_WIDTH = 270f;
        private const float ICON = 46f;
        private const float BIG_ICON = 96f;
        private const string DEFAULT_FOLDER = "Assets/Script/Item/ItemData";

        private readonly List<ItemData> _allItems = new();
        private ItemData _selected;

        private string _listFilter = "";
        private int _categoryFilter = -1; // -1 = 全部

        private Vector2 _listScroll;
        private Vector2 _editScroll;

        [MenuItem("Inventory/物品編輯器")]
        public static void ShowWindow()
        {
            ItemDataEditorWindow wnd = GetWindow<ItemDataEditorWindow>();
            wnd.titleContent = new GUIContent("物品編輯器");
            wnd.minSize = new Vector2(900, 560);
        }

        private void OnEnable()
        {
            RefreshCache();
        }

        private void RefreshCache()
        {
            _allItems.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
            {
                ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null) _allItems.Add(item);
            }
            _allItems.Sort((a, b) => a.itemID.CompareTo(b.itemID));
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawListPanel();
            DrawEditPanel();
            EditorGUILayout.EndHorizontal();
        }

        // ---------- 左側：物品清單 ----------

        private void DrawListPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LIST_WIDTH));

            EditorGUILayout.LabelField("物品清單", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 新增物品", GUILayout.Height(24))) CreateNewItem();
            if (GUILayout.Button("⟳", GUILayout.Width(28), GUILayout.Height(24))) RefreshCache();
            EditorGUILayout.EndHorizontal();

            _listFilter = EditorGUILayout.TextField(_listFilter, EditorStyles.toolbarSearchField);

            string[] catNames = { "全部分類", "Ingredients", "Food", "Tool", "KeyItem" };
            int popupValue = _categoryFilter + 1;
            popupValue = EditorGUILayout.Popup(popupValue, catNames);
            _categoryFilter = popupValue - 1;

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUI.skin.box);
            foreach (ItemData item in _allItems)
            {
                if (item == null) continue;
                if (_categoryFilter >= 0 && (int)item.category != _categoryFilter) continue;
                if (!string.IsNullOrEmpty(_listFilter) && !MatchesFilter(item)) continue;
                DrawListRow(item);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private bool MatchesFilter(ItemData item)
        {
            if (item.itemName != null && item.itemName.IndexOf(_listFilter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (item.name.IndexOf(_listFilter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return item.itemID.ToString() == _listFilter.Trim();
        }

        private void DrawListRow(ItemData item)
        {
            bool isSelected = item == _selected;
            Color bg = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);

            EditorGUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(ICON));
            GUI.backgroundColor = bg;

            GUILayout.Label(GetIcon(item.icon), GUILayout.Width(ICON), GUILayout.Height(ICON));

            EditorGUILayout.BeginVertical();
            string displayName = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;
            EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"ID {item.itemID}・{item.category}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            Rect rowRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
            {
                _selected = item;
                if (Event.current.clickCount == 2) EditorGUIUtility.PingObject(item);
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
                EditorGUILayout.HelpBox("從左側選擇一個物品，或點「＋ 新增物品」開始建立。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _editScroll = EditorGUILayout.BeginScrollView(_editScroll);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_selected.name, EditorStyles.largeLabel);
            if (GUILayout.Button("在 Project 中選取", GUILayout.Width(120))) EditorGUIUtility.PingObject(_selected);
            if (GUILayout.Button("複製", GUILayout.Width(50))) DuplicateItem(_selected);
            if (GUILayout.Button("刪除", GUILayout.Width(50))) DeleteItem(_selected);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            DrawDuplicateIdWarning();
            DrawDatabaseRegistration();

            // 圖示預覽
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            EditorGUILayout.BeginVertical(GUILayout.Width(BIG_ICON));
            EditorGUILayout.LabelField("圖示", EditorStyles.miniBoldLabel);
            GUILayout.Label(GetIcon(_selected.icon), GUILayout.Width(BIG_ICON), GUILayout.Height(BIG_ICON));
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical(GUILayout.Width(BIG_ICON));
            EditorGUILayout.LabelField("大圖", EditorStyles.miniBoldLabel);
            GUILayout.Label(GetIcon(_selected.fullSizeImage), GUILayout.Width(BIG_ICON), GUILayout.Height(BIG_ICON));
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();

            // 基本資料
            EditorGUILayout.LabelField("基本資料", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            int newID = EditorGUILayout.IntField(new GUIContent("物品 ID", "供 ItemDatabase 查找的唯一編號，不可重複。"), _selected.itemID);
            if (GUILayout.Button("自動指派", GUILayout.Width(80))) newID = NextFreeId();
            EditorGUILayout.EndHorizontal();
            string newName = EditorGUILayout.TextField(new GUIContent("物品名稱", "顯示給玩家的名稱。"), _selected.itemName);
            Sprite newIcon = (Sprite)EditorGUILayout.ObjectField("圖示 Icon", _selected.icon, typeof(Sprite), false);
            Sprite newFull = (Sprite)EditorGUILayout.ObjectField("大圖 FullSize", _selected.fullSizeImage, typeof(Sprite), false);

            // 分類
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("分類", EditorStyles.boldLabel);
            InventoryDisplay.Category newCategory = (InventoryDisplay.Category)EditorGUILayout.EnumPopup(
                new GUIContent("分類", "Ingredients=食材、Food=料理、Tool=工具、KeyItem=關鍵道具。食材庫只抓 Ingredients。"), _selected.category);
            IngredientType newIngType;
            using (new EditorGUI.DisabledScope(newCategory != InventoryDisplay.Category.Ingredients))
            {
                newIngType = (IngredientType)EditorGUILayout.EnumPopup(
                    new GUIContent("食材類型", "僅在分類為 Ingredients 時有意義，供食譜以類型配對。"), _selected.ingredientType);
            }
            RareLevel newRare = (RareLevel)EditorGUILayout.EnumPopup(
                new GUIContent("稀有度", "影響取得字卡的演出效果。"), _selected.rareLevel);

            // 文字
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("說明文字", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(new GUIContent("描述", "物品的一般說明。"), EditorStyles.miniLabel);
            string newDesc = EditorGUILayout.TextArea(_selected.description, GUILayout.MinHeight(40));
            EditorGUILayout.LabelField(new GUIContent("效果描述", "食用或使用後的效果說明。"), EditorStyles.miniLabel);
            string newEffectDesc = EditorGUILayout.TextArea(_selected.effectDescription, GUILayout.MinHeight(40));

            // 效果數值
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("效果數值", EditorStyles.boldLabel);
            int newHeal = EditorGUILayout.IntField(new GUIContent("立即回復量", "食用時立即回復的生命值，0 代表不回復。"), _selected.healAmount);
            BuffDefinition newBuff = (BuffDefinition)EditorGUILayout.ObjectField(
                new GUIContent("Buff 定義", "食用後套用的增益效果，可留空。"), _selected.buffDefinition, typeof(BuffDefinition), false);
            int newTier = EditorGUILayout.IntField(new GUIContent("效果等級 Tier", "Buff 強度等級，範圍 1~3。"), _selected.effectTier);
            newTier = Mathf.Clamp(newTier, 1, 3);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_selected, "編輯物品");
                _selected.itemID = newID;
                _selected.itemName = newName;
                _selected.icon = newIcon;
                _selected.fullSizeImage = newFull;
                _selected.category = newCategory;
                _selected.ingredientType = newIngType;
                _selected.rareLevel = newRare;
                _selected.description = newDesc;
                _selected.effectDescription = newEffectDesc;
                _selected.healAmount = newHeal;
                _selected.buffDefinition = newBuff;
                _selected.effectTier = newTier;
                MarkDirty();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ---------- 重複 ID 偵測 ----------

        private void DrawDuplicateIdWarning()
        {
            List<ItemData> dupes = _allItems
                .Where(i => i != null && i != _selected && i.itemID == _selected.itemID)
                .ToList();
            if (dupes.Count == 0) return;

            string names = string.Join("、", dupes.Select(i => i.name));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox($"⚠ 物品 ID {_selected.itemID} 與下列物品重複，ItemDatabase 查找會出錯：{names}", MessageType.Error);
            if (GUILayout.Button("改用可用 ID", GUILayout.Width(110), GUILayout.Height(38)))
            {
                Undo.RecordObject(_selected, "修正物品 ID");
                _selected.itemID = NextFreeId();
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ---------- ItemDatabase 註冊提示 ----------

        private void DrawDatabaseRegistration()
        {
            ItemDatabase db = Object.FindFirstObjectByType<ItemDatabase>();
            if (db == null) return;

            SerializedObject so = new SerializedObject(db);
            SerializedProperty listProp = so.FindProperty("_allItemDataList");
            if (listProp == null) return;

            bool contained = false;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                if (listProp.GetArrayElementAtIndex(i).objectReferenceValue == _selected)
                {
                    contained = true;
                    break;
                }
            }
            if (contained) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox("此物品尚未加入場景中的 ItemDatabase，依 ID 查找時會找不到。", MessageType.Warning);
            if (GUILayout.Button("加入 ItemDatabase", GUILayout.Width(140), GUILayout.Height(38)))
            {
                listProp.arraySize++;
                listProp.GetArrayElementAtIndex(listProp.arraySize - 1).objectReferenceValue = _selected;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(db);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ---------- 資產操作 ----------

        private int NextFreeId()
        {
            int max = 0;
            foreach (ItemData item in _allItems)
                if (item != null && item != _selected && item.itemID > max) max = item.itemID;
            return max + 1;
        }

        private void CreateNewItem()
        {
            if (!AssetDatabase.IsValidFolder(DEFAULT_FOLDER))
                System.IO.Directory.CreateDirectory(DEFAULT_FOLDER);

            string path = EditorUtility.SaveFilePanelInProject("建立新物品", "NewItemData", "asset", "請輸入物品檔名", DEFAULT_FOLDER);
            if (string.IsNullOrEmpty(path)) return;

            ItemData item = CreateInstance<ItemData>();
            item.itemID = NextFreeId();
            item.itemName = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(item, path);
            AssetDatabase.SaveAssets();
            RefreshCache();
            _selected = item;
            EditorGUIUtility.PingObject(item);
        }

        private void DuplicateItem(ItemData source)
        {
            string srcPath = AssetDatabase.GetAssetPath(source);
            string newPath = AssetDatabase.GenerateUniqueAssetPath(srcPath);
            if (!AssetDatabase.CopyAsset(srcPath, newPath)) return;

            AssetDatabase.SaveAssets();
            RefreshCache();
            ItemData copy = AssetDatabase.LoadAssetAtPath<ItemData>(newPath);
            if (copy != null)
            {
                copy.itemID = NextFreeId();
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssets();
            }
            _selected = copy;
            EditorGUIUtility.PingObject(copy);
        }

        private void DeleteItem(ItemData item)
        {
            if (!EditorUtility.DisplayDialog("刪除物品", $"確定要刪除「{item.name}」嗎？此操作無法復原。", "刪除", "取消"))
                return;
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(item));
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
