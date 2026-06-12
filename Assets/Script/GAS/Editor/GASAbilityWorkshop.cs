#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GAS.Editor
{
    /// <summary>
    /// 能力工作坊 - 一站式能力編輯與建立工具
    /// 支援讀取/編輯現有能力、建立新能力、即時技能卡片預覽
    /// </summary>
    public class GASAbilityWorkshop : EditorWindow
    {
        #region 列舉與常量

        private enum WorkshopMode
        {
            Edit,
            Create
        }

        private enum AbilityTemplate
        {
            MeleeAttack,
            RangedAttack,
            Dodge,
            Buff,
            Debuff
        }

        private const float LIST_WIDTH = 200f;
        private const float CARD_WIDTH = 280f;
        private const float MIN_EDITOR_WIDTH = 350f;

        // 可拖動分隔線
        private const float SPLITTER_WIDTH = 5f;
        private const float MIN_LEFT_WIDTH = 140f;
        private const float MAX_LEFT_WIDTH = 400f;
        private const float MIN_RIGHT_WIDTH = 180f;
        private const float MAX_RIGHT_WIDTH = 450f;

        // 色彩系統 (與 Dashboard 一致)
        private static readonly Color COLOR_ABILITY = new(0.8f, 0.6f, 0.2f);
        private static readonly Color COLOR_EFFECT = new(0.6f, 0.2f, 0.8f);
        private static readonly Color COLOR_ATTRIBUTE = new(0.2f, 0.6f, 0.2f);
        private static readonly Color COLOR_TAG = new(0.2f, 0.4f, 0.8f);
        private static readonly Color COLOR_WEAPON = new(0.8f, 0.2f, 0.2f);
        private static readonly Color COLOR_MELEE = new(0.9f, 0.4f, 0.2f);
        private static readonly Color COLOR_RANGED = new(0.2f, 0.6f, 0.9f);
        private static readonly Color COLOR_DODGE = new(0.2f, 0.8f, 0.4f);
        private static readonly Color COLOR_CARD_BG = new(0.16f, 0.16f, 0.18f);
        private static readonly Color COLOR_CARD_HEADER = new(0.75f, 0.55f, 0.15f);
        private static readonly Color COLOR_CARD_SECTION = new(0.22f, 0.22f, 0.25f);
        private static readonly Color COLOR_CARD_BORDER = new(0.4f, 0.35f, 0.2f);
        private static readonly Color COLOR_SAVE_BTN = new(0.3f, 0.75f, 0.3f);
        private static readonly Color COLOR_CREATE_BTN = new(0.3f, 0.6f, 0.9f);
        private static readonly Color COLOR_DANGER = new(0.9f, 0.3f, 0.3f);

        #endregion

        #region 欄位

        // 模式
        private WorkshopMode _mode = WorkshopMode.Edit;
        private AbilityTemplate _selectedTemplate;
        private bool _showTemplateSelector;

        // 三欄 Scroll
        private Vector2 _listScroll;
        private Vector2 _editorScroll;
        private Vector2 _cardScroll;

        // 可拖動面板寬度
        private float _leftPanelWidth = LIST_WIDTH;
        private float _rightPanelWidth = CARD_WIDTH;
        private bool _isDraggingLeftSplitter;
        private bool _isDraggingRightSplitter;

        // 左欄 - 能力列表
        private List<GameplayAbility> _cachedAbilities = new();
        private List<GameplayEffect> _cachedEffects = new();
        private List<WeaponData> _cachedWeapons = new();
        private string _searchString = "";
        private int _typeFilter; // 0=全部, 1=近戰, 2=遠程, 3=閃避, 4=其他
        private bool _cacheInitialized;

        // 中欄 - 編輯狀態
        private GameplayAbility _editingAbility;
        private SerializedObject _serializedAbility;

        // 摺疊狀態
        private readonly Dictionary<string, bool> _foldouts = new();

        // 建立模式暫存
        private string _newName = "NewAbility";
        private string _newTag = "Ability.";
        private string _newDescription = "";
        private string _outputPath = "Assets/Data/GAS/Abilities";
        private bool _createCooldown = true;
        private float _newCooldownDuration = 2f;
        private bool _createCost = true;
        private string _costAttributeName = "Stamina";
        private float _newCostAmount = 20f;

        // 附帶效果編輯
        private readonly List<AttachedEffectEntry> _attachedEffects = new();

        // 常用標籤勾選
        private readonly Dictionary<string, bool> _blockedTagToggles = new();
        private readonly Dictionary<string, bool> _grantedTagToggles = new();
        private readonly Dictionary<string, bool> _cancelTagToggles = new();

        // 自訂標籤
        private string _customBlockedTag = "";
        private string _customGrantedTag = "";
        private string _customCancelTag = "";

        #endregion

        #region 內部類

        [Serializable]
        private class AttachedEffectEntry
        {
            public GameplayEffect Effect;
            public EffectTriggerTiming Timing = EffectTriggerTiming.OnActivate;
        }

        private enum EffectTriggerTiming
        {
            OnActivate,
            OnHit,
            OnEnd
        }

        #endregion

        #region 生命週期

        [MenuItem("GAS/Ability Workshop")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<GASAbilityWorkshop>();
            wnd.titleContent = new GUIContent("能力工作坊");
            wnd.minSize = new Vector2(900, 550);
        }

        /// <summary>
        /// 從外部載入能力 (供 Dashboard 呼叫)
        /// </summary>
        public void LoadAbility(GameplayAbility ability)
        {
            if (ability == null) return;
            _mode = WorkshopMode.Edit;
            _showTemplateSelector = false;
            SelectAbility(ability);
            Repaint();
        }

        private void OnEnable()
        {
            RefreshCache();
        }

        private void OnGUI()
        {
            if (!_cacheInitialized)
                RefreshCache();
            HandleSplitterEvents();
            float centerWidth = position.width - _leftPanelWidth - _rightPanelWidth - 2 * SPLITTER_WIDTH;
            centerWidth = Mathf.Max(centerWidth, MIN_EDITOR_WIDTH);
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawDraggableSplitter(true);
            DrawCenterPanel(centerWidth);
            DrawDraggableSplitter(false);
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 左欄 - 能力列表

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth));

            // 標題
            DrawPanelHeader("能力列表", COLOR_ABILITY);

            // 搜尋列
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchString = EditorGUILayout.TextField(_searchString, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.miniButton, GUILayout.Width(18)))
            {
                _searchString = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            // 類型篩選
            string[] filterLabels = { "全部", "近戰", "遠程", "閃避", "其他" };
            _typeFilter = GUILayout.Toolbar(_typeFilter, filterLabels, EditorStyles.miniButton);
            EditorGUILayout.Space(3);

            // 能力列表
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            var filtered = GetFilteredAbilities();
            foreach (var ability in filtered)
            {
                if (ability == null) continue;
                DrawAbilityListItem(ability);
            }
            if (filtered.Count == 0)
            {
                EditorGUILayout.LabelField("（無匹配的能力）", EditorStyles.centeredGreyMiniLabel);
            }
            EditorGUILayout.EndScrollView();

            // 底部按鈕
            EditorGUILayout.Space(3);
            GUI.backgroundColor = COLOR_CREATE_BTN;
            if (GUILayout.Button("+ 新建能力", GUILayout.Height(28)))
            {
                _mode = WorkshopMode.Create;
                _showTemplateSelector = true;
                _editingAbility = null;
                _serializedAbility = null;
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(2);
            if (GUILayout.Button("重新整理", GUILayout.Height(22)))
                RefreshCache();

            EditorGUILayout.Space(3);
            EditorGUILayout.EndVertical();
        }

        private void DrawAbilityListItem(GameplayAbility ability)
        {
            bool isSelected = _editingAbility == ability;
            Color typeColor = GetAbilityTypeColor(ability);
            Color bgColor = isSelected ? typeColor * 0.8f : new Color(0.22f, 0.22f, 0.22f);
            var rect = GUILayoutUtility.GetRect(_leftPanelWidth - 10, 36);
            EditorGUI.DrawRect(rect, bgColor);

            // 類型色條
            var colorBar = new Rect(rect.x, rect.y, 4, rect.height);
            EditorGUI.DrawRect(colorBar, typeColor);

            // 名稱
            var nameRect = new Rect(rect.x + 8, rect.y + 2, rect.width - 12, 18);
            var nameStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = isSelected ? Color.white : new Color(0.85f, 0.85f, 0.85f) },
                fontSize = 11
            };
            GUI.Label(nameRect, ability.AbilityName ?? ability.name, nameStyle);

            // 類型標籤
            var typeRect = new Rect(rect.x + 8, rect.y + 18, rect.width - 12, 14);
            var typeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = typeColor * 1.2f },
                fontSize = 9
            };
            GUI.Label(typeRect, GetAbilityTypeName(ability), typeStyle);

            // 點擊選擇
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _mode = WorkshopMode.Edit;
                _showTemplateSelector = false;
                SelectAbility(ability);
                Event.current.Use();
                Repaint();
            }
            EditorGUILayout.Space(1);
        }

        private List<GameplayAbility> GetFilteredAbilities()
        {
            return _cachedAbilities.Where(a =>
            {
                if (a == null) return false;
                // 搜尋篩選
                if (!string.IsNullOrEmpty(_searchString))
                {
                    string search = _searchString.ToLower();
                    string aName = (a.AbilityName ?? a.name).ToLower();
                    string aTag = a.AbilityTag.IsValid ? a.AbilityTag.TagName.ToLower() : "";
                    if (!aName.Contains(search) && !aTag.Contains(search))
                        return false;
                }
                // 類型篩選
                return _typeFilter switch
                {
                    1 => a is GA_MeleeAttack,
                    2 => a is GA_RangedAttack,
                    3 => a is GA_Dodge,
                    4 => a is not GA_MeleeAttack and not GA_RangedAttack and not GA_Dodge,
                    _ => true
                };
            }).ToList();
        }

        #endregion

        #region 中欄 - 編輯器

        private void DrawCenterPanel(float centerWidth)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(centerWidth));

            if (_mode == WorkshopMode.Create && _showTemplateSelector)
            {
                DrawTemplateSelector();
            }
            else if (_mode == WorkshopMode.Create)
            {
                DrawCreateMode();
            }
            else if (_editingAbility != null)
            {
                DrawEditMode();
            }
            else
            {
                DrawEmptyState();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("從左側列表選擇一個能力進行編輯", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField("或點擊「+ 新建能力」開始建立", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
        }

        #region 模板選擇

        private void DrawTemplateSelector()
        {
            DrawPanelHeader("選擇能力模板", COLOR_CREATE_BTN);
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("選擇一個模板來快速建立能力：", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);

            DrawTemplateButton("近戰攻擊", "建立近戰攻擊能力 (GA_MeleeAttack)\n包含攻擊數據、命中視窗、連招設定",
                AbilityTemplate.MeleeAttack, COLOR_MELEE);
            DrawTemplateButton("遠程攻擊", "建立遠程攻擊能力 (GA_RangedAttack)\n包含投射物、AoE、蓄力設定",
                AbilityTemplate.RangedAttack, COLOR_RANGED);
            DrawTemplateButton("閃避", "建立閃避能力 (GA_Dodge)\n包含閃避距離、無敵時間、體力消耗",
                AbilityTemplate.Dodge, COLOR_DODGE);
            DrawTemplateButton("增益效果", "建立增益效果型能力\n用於提升屬性 (攻擊力、防禦力等)",
                AbilityTemplate.Buff, COLOR_ATTRIBUTE);
            DrawTemplateButton("減益效果", "建立減益效果型能力\n用於降低敵人屬性或施加 DOT",
                AbilityTemplate.Debuff, COLOR_EFFECT);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("取消", GUILayout.Height(25)))
            {
                _showTemplateSelector = false;
                _mode = WorkshopMode.Edit;
            }
        }

        private void DrawTemplateButton(string title, string desc, AbilityTemplate template, Color color)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            // 色條
            var colorRect = GUILayoutUtility.GetRect(6, 50, GUILayout.Width(6));
            EditorGUI.DrawRect(colorRect, color);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(title, new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 });
            EditorGUILayout.LabelField(desc, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            GUI.backgroundColor = color;
            if (GUILayout.Button("選擇", GUILayout.Width(60), GUILayout.Height(40)))
            {
                _selectedTemplate = template;
                _showTemplateSelector = false;
                InitCreateDefaults();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        private void InitCreateDefaults()
        {
            _attachedEffects.Clear();
            switch (_selectedTemplate)
            {
                case AbilityTemplate.MeleeAttack:
                    _newName = "GA_NewMeleeAttack";
                    _newTag = "Ability.Attack.Melee";
                    _newDescription = "近戰攻擊能力";
                    _createCooldown = false;
                    _createCost = false;
                    break;
                case AbilityTemplate.RangedAttack:
                    _newName = "GA_NewRangedAttack";
                    _newTag = "Ability.Attack.Ranged";
                    _newDescription = "遠程攻擊能力";
                    _createCooldown = false;
                    _createCost = false;
                    break;
                case AbilityTemplate.Dodge:
                    _newName = "GA_NewDodge";
                    _newTag = "Ability.Movement.Dodge";
                    _newDescription = "閃避能力";
                    _createCooldown = false;
                    _createCost = true;
                    _costAttributeName = "Stamina";
                    _newCostAmount = 20f;
                    break;
                case AbilityTemplate.Buff:
                    _newName = "GE_NewBuff";
                    _newTag = "Effect.Buff";
                    _newDescription = "增益效果";
                    _createCooldown = true;
                    _newCooldownDuration = 10f;
                    _createCost = true;
                    _costAttributeName = "Mana";
                    _newCostAmount = 30f;
                    break;
                case AbilityTemplate.Debuff:
                    _newName = "GE_NewDebuff";
                    _newTag = "Effect.Debuff";
                    _newDescription = "減益效果";
                    _createCooldown = true;
                    _newCooldownDuration = 8f;
                    _createCost = true;
                    _costAttributeName = "Mana";
                    _newCostAmount = 25f;
                    break;
            }
        }

        #endregion

        #region 建立模式

        private void DrawCreateMode()
        {
            string templateName = _selectedTemplate switch
            {
                AbilityTemplate.MeleeAttack => "近戰攻擊",
                AbilityTemplate.RangedAttack => "遠程攻擊",
                AbilityTemplate.Dodge => "閃避",
                AbilityTemplate.Buff => "增益效果",
                AbilityTemplate.Debuff => "減益效果",
                _ => "未知"
            };
            DrawPanelHeader($"建立新能力 - {templateName}", COLOR_CREATE_BTN);

            _editorScroll = EditorGUILayout.BeginScrollView(_editorScroll);

            // 基本設定
            DrawFoldoutSection("create_basic", "基本設定", () =>
            {
                _newName = EditorGUILayout.TextField("資產名稱", _newName);
                _newTag = EditorGUILayout.TextField("能力標籤", _newTag);
                EditorGUILayout.LabelField("描述");
                _newDescription = EditorGUILayout.TextArea(_newDescription, GUILayout.Height(50));
                _outputPath = EditorGUILayout.TextField("輸出路徑", _outputPath);
            });

            // 冷卻設定
            DrawFoldoutSection("create_cooldown", "冷卻設定", () =>
            {
                _createCooldown = EditorGUILayout.Toggle("啟用冷卻", _createCooldown);
                if (_createCooldown)
                {
                    _newCooldownDuration = EditorGUILayout.Slider("冷卻時間 (秒)", _newCooldownDuration, 0.1f, 30f);
                }
            });

            // 消耗設定
            DrawFoldoutSection("create_cost", "消耗設定", () =>
            {
                _createCost = EditorGUILayout.Toggle("啟用消耗", _createCost);
                if (_createCost)
                {
                    string[] costAttributes = { "Stamina", "Mana", "AssistPoints" };
                    string[] costLabels = { "體力 (Stamina)", "魔力 (Mana)", "支援點數 (AssistPoints)" };
                    int costIdx = Array.IndexOf(costAttributes, _costAttributeName);
                    if (costIdx < 0) costIdx = 0;
                    costIdx = EditorGUILayout.Popup("資源類型", costIdx, costLabels);
                    _costAttributeName = costAttributes[costIdx];
                    _newCostAmount = EditorGUILayout.Slider("消耗量", _newCostAmount, 1f, 200f);
                }
            });

            // 模板專屬提示
            DrawFoldoutSection("create_note", "建立說明", () =>
            {
                switch (_selectedTemplate)
                {
                    case AbilityTemplate.MeleeAttack:
                        EditorGUILayout.HelpBox(
                            "將建立：\n" +
                            "  1. GA_MeleeAttack 能力資產\n" +
                            "  2. MeleeAttackData 攻擊數據 (含預設命中視窗)\n" +
                            "  3. 冷卻/消耗效果 (如啟用)\n\n" +
                            "建立後可在工作坊中進一步編輯所有參數。",
                            MessageType.Info);
                        break;
                    case AbilityTemplate.RangedAttack:
                        EditorGUILayout.HelpBox(
                            "將建立：\n" +
                            "  1. GA_RangedAttack 能力資產\n" +
                            "  2. RangedAttackData 攻擊數據\n" +
                            "  3. 冷卻/消耗效果 (如啟用)\n\n" +
                            "建立後可在工作坊中進一步編輯所有參數。",
                            MessageType.Info);
                        break;
                    case AbilityTemplate.Dodge:
                        EditorGUILayout.HelpBox(
                            "將建立：\n" +
                            "  1. GA_Dodge 閃避能力資產 (含預設移動參數)\n" +
                            "  2. 冷卻/消耗效果 (如啟用)\n\n" +
                            "建立後可在工作坊中調整閃避距離、無敵時間等。",
                            MessageType.Info);
                        break;
                    case AbilityTemplate.Buff:
                    case AbilityTemplate.Debuff:
                        EditorGUILayout.HelpBox(
                            "將建立：\n" +
                            "  1. GameplayEffect 效果資產 (含預設修改器)\n" +
                            "  2. 冷卻/消耗效果 (如啟用)\n\n" +
                            "注意：增益/減益是效果 (Effect)，不是能力 (Ability)。\n" +
                            "如需透過能力觸發，請建立後指派到能力的附帶效果中。",
                            MessageType.Info);
                        break;
                }
            });

            EditorGUILayout.EndScrollView();

            // 底部按鈕
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("返回模板選擇", GUILayout.Height(30)))
            {
                _showTemplateSelector = true;
            }
            GUILayout.FlexibleSpace();
            GUI.backgroundColor = COLOR_SAVE_BTN;
            if (GUILayout.Button("建立所有資產", GUILayout.Height(30), GUILayout.Width(150)))
            {
                ExecuteCreateAssets();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);
        }

        #endregion

        #region 編輯模式

        private void DrawEditMode()
        {
            // 標題列
            DrawPanelHeader($"編輯: {_editingAbility.AbilityName ?? _editingAbility.name}", GetAbilityTypeColor(_editingAbility));

            _editorScroll = EditorGUILayout.BeginScrollView(_editorScroll);

            _serializedAbility?.Update();

            // 1. 基本設定
            DrawFoldoutSection("edit_basic", "基本設定", DrawEditBasicSection);

            // 2. 冷卻/消耗
            DrawFoldoutSection("edit_cooldown_cost", "冷卻 / 消耗", DrawEditCooldownCostSection);

            // 3. 戰鬥數值 (依類型動態顯示)
            DrawCombatSection();

            // 4. 附帶效果
            DrawFoldoutSection("edit_effects", "附帶效果", DrawEditAttachedEffectsSection);

            // 5. 標籤條件
            DrawFoldoutSection("edit_tags", "標籤條件", DrawEditTagsSection);

            // 6. 武器指派
            DrawFoldoutSection("edit_weapon", "武器指派", DrawEditWeaponSection);

            EditorGUILayout.EndScrollView();

            // 儲存按鈕
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
            if (GUILayout.Button("在 Inspector 中查看", GUILayout.Height(28)))
            {
                Selection.activeObject = _editingAbility;
                EditorGUIUtility.PingObject(_editingAbility);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
            GUI.backgroundColor = COLOR_SAVE_BTN;
            if (GUILayout.Button("儲存變更", GUILayout.Height(28), GUILayout.Width(120)))
            {
                SaveChanges();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);
        }

        private void DrawEditBasicSection()
        {
            if (_serializedAbility == null) return;

            var propName = _serializedAbility.FindProperty("AbilityName");
            var propTag = _serializedAbility.FindProperty("AbilityTag").FindPropertyRelative("_tagName");
            var propDesc = _serializedAbility.FindProperty("Description");
            var propLevel = _serializedAbility.FindProperty("AbilityLevel");
            var propReactivate = _serializedAbility.FindProperty("CanReactivateWhileActive");

            if (propName != null)
                EditorGUILayout.PropertyField(propName, new GUIContent("能力名稱"));
            if (propTag != null)
                EditorGUILayout.PropertyField(propTag, new GUIContent("能力標籤"));
            if (propDesc != null)
            {
                EditorGUILayout.LabelField("描述");
                propDesc.stringValue = EditorGUILayout.TextArea(propDesc.stringValue, GUILayout.Height(50));
            }
            if (propLevel != null)
                EditorGUILayout.PropertyField(propLevel, new GUIContent("能力等級"));
            if (propReactivate != null)
                EditorGUILayout.PropertyField(propReactivate, new GUIContent("可重複啟動"));

            _serializedAbility.ApplyModifiedProperties();
        }

        private void DrawEditCooldownCostSection()
        {
            if (_serializedAbility == null) return;

            // 冷卻效果
            DrawSubSectionLabel("冷卻效果");
            var propCooldown = _serializedAbility.FindProperty("CooldownEffect");
            if (propCooldown != null)
            {
                EditorGUILayout.PropertyField(propCooldown, new GUIContent("冷卻效果"));
            }
            if (_editingAbility.CooldownEffect != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"持續時間: {_editingAbility.CooldownEffect.Duration:F1} 秒",
                    EditorStyles.miniLabel);
                if (GUILayout.Button("編輯冷卻效果", EditorStyles.miniButton))
                {
                    Selection.activeObject = _editingAbility.CooldownEffect;
                    EditorGUIUtility.PingObject(_editingAbility.CooldownEffect);
                }
                EditorGUI.indentLevel--;
            }
            else
            {
                if (GUILayout.Button("自動建立冷卻效果", EditorStyles.miniButton))
                {
                    CreateAndAssignCooldownEffect();
                }
            }

            EditorGUILayout.Space(5);

            // 消耗效果
            DrawSubSectionLabel("消耗效果");
            var propCost = _serializedAbility.FindProperty("CostEffect");
            if (propCost != null)
            {
                EditorGUILayout.PropertyField(propCost, new GUIContent("消耗效果"));
            }
            if (_editingAbility.CostEffect != null)
            {
                EditorGUI.indentLevel++;
                foreach (var mod in _editingAbility.CostEffect.Modifiers)
                {
                    EditorGUILayout.LabelField($"{mod.AttributeName}: {mod.Magnitude:F1}",
                        EditorStyles.miniLabel);
                }
                if (GUILayout.Button("編輯消耗效果", EditorStyles.miniButton))
                {
                    Selection.activeObject = _editingAbility.CostEffect;
                    EditorGUIUtility.PingObject(_editingAbility.CostEffect);
                }
                EditorGUI.indentLevel--;
            }
            else
            {
                if (GUILayout.Button("自動建立消耗效果", EditorStyles.miniButton))
                {
                    CreateAndAssignCostEffect();
                }
            }

            _serializedAbility.ApplyModifiedProperties();
        }

        private void DrawCombatSection()
        {
            if (_editingAbility is GA_MeleeAttack melee)
            {
                DrawFoldoutSection("edit_combat", "戰鬥數值 (近戰)", () => DrawMeleeCombatSection(melee));
            }
            else if (_editingAbility is GA_RangedAttack ranged)
            {
                DrawFoldoutSection("edit_combat", "戰鬥數值 (遠程)", () => DrawRangedCombatSection(ranged));
            }
            else if (_editingAbility is GA_Dodge dodge)
            {
                DrawFoldoutSection("edit_combat", "戰鬥數值 (閃避)", () => DrawDodgeCombatSection(dodge));
            }
        }

        private void DrawMeleeCombatSection(GA_MeleeAttack melee)
        {
            if (_serializedAbility == null) return;

            var propFirstAttack = _serializedAbility.FindProperty("FirstAttackData");
            var propEnemyLayer = _serializedAbility.FindProperty("EnemyLayer");
            var propObstacleLayer = _serializedAbility.FindProperty("ObstacleLayer");
            var propFallback = _serializedAbility.FindProperty("FallbackFirstAttack");
            var propCrossTag = _serializedAbility.FindProperty("CrossTypeAbilityTag");

            if (propFirstAttack != null)
                EditorGUILayout.PropertyField(propFirstAttack, new GUIContent("初始攻擊數據"));
            if (propFallback != null)
                EditorGUILayout.PropertyField(propFallback, new GUIContent("回退第一擊"));
            if (propEnemyLayer != null)
                EditorGUILayout.PropertyField(propEnemyLayer, new GUIContent("敵人圖層"));
            if (propObstacleLayer != null)
                EditorGUILayout.PropertyField(propObstacleLayer, new GUIContent("障礙物圖層"));
            if (propCrossTag != null)
                EditorGUILayout.PropertyField(propCrossTag, new GUIContent("跨類型連招標籤"));

            // 攻擊數據快速預覽
            if (melee.FirstAttackData != null)
            {
                EditorGUILayout.Space(5);
                DrawSubSectionLabel("攻擊數據預覽");
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"命中視窗數: {melee.FirstAttackData.HitWindows.Count}");
                EditorGUILayout.LabelField($"允許輸入時間: {melee.FirstAttackData.AllowInputTime:F2}s");
                EditorGUILayout.LabelField($"連招重置時間: {melee.FirstAttackData.ComboResetTime:F2}s");
                EditorGUILayout.LabelField($"連招分支數: {melee.FirstAttackData.NextCombos.Count}");

                foreach (var hw in melee.FirstAttackData.HitWindows)
                {
                    EditorGUILayout.LabelField(
                        $"  [{hw.StartTime:F2}~{hw.EndTime:F2}] 傷害: {hw.BaseDamage} x{hw.DamageMultiplier:F1}",
                        EditorStyles.miniLabel);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(3);
                if (GUILayout.Button("在攻擊數據編輯器中開啟", EditorStyles.miniButton))
                {
                    GASAttackDataEditorWindow.ShowWindow();
                }
            }

            _serializedAbility.ApplyModifiedProperties();
        }

        private void DrawRangedCombatSection(GA_RangedAttack ranged)
        {
            if (_serializedAbility == null) return;

            var propFirstAttack = _serializedAbility.FindProperty("FirstAttackData");
            var propEnemyLayer = _serializedAbility.FindProperty("EnemyLayer");
            var propObstacleLayer = _serializedAbility.FindProperty("ObstacleLayer");
            var propFallback = _serializedAbility.FindProperty("FallbackFirstAttack");
            var propCrossTag = _serializedAbility.FindProperty("CrossTypeAbilityTag");

            if (propFirstAttack != null)
                EditorGUILayout.PropertyField(propFirstAttack, new GUIContent("初始攻擊數據"));
            if (propFallback != null)
                EditorGUILayout.PropertyField(propFallback, new GUIContent("回退第一擊"));
            if (propEnemyLayer != null)
                EditorGUILayout.PropertyField(propEnemyLayer, new GUIContent("敵人圖層"));
            if (propObstacleLayer != null)
                EditorGUILayout.PropertyField(propObstacleLayer, new GUIContent("障礙物圖層"));
            if (propCrossTag != null)
                EditorGUILayout.PropertyField(propCrossTag, new GUIContent("跨類型連招標籤"));

            // 攻擊數據快速預覽
            if (ranged.FirstAttackData != null)
            {
                EditorGUILayout.Space(5);
                DrawSubSectionLabel("攻擊數據預覽");
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"攻擊類型: {ranged.FirstAttackData.AttackType}");
                EditorGUILayout.LabelField($"蓄力模式: {ranged.FirstAttackData.Charge}");
                EditorGUILayout.LabelField($"基礎傷害: {ranged.FirstAttackData.BaseDamage}");
                EditorGUILayout.LabelField($"發射時間: {ranged.FirstAttackData.FireTime:F2}s");
                EditorGUILayout.LabelField($"連招分支數: {ranged.FirstAttackData.NextCombos.Count}");
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(3);
                if (GUILayout.Button("在攻擊數據編輯器中開啟", EditorStyles.miniButton))
                {
                    GASAttackDataEditorWindow.ShowWindow();
                }
            }

            _serializedAbility.ApplyModifiedProperties();
        }

        private void DrawDodgeCombatSection(GA_Dodge _)
        {
            if (_serializedAbility == null) return;

            var propDodgeAnim = _serializedAbility.FindProperty("DodgeAnimation");
            var propBackstepAnim = _serializedAbility.FindProperty("BackstepAnimation");
            var propDistance = _serializedAbility.FindProperty("DodgeDistance");
            var propDuration = _serializedAbility.FindProperty("DodgeDuration");
            var propCurve = _serializedAbility.FindProperty("DodgeCurve");
            var propInvincEffect = _serializedAbility.FindProperty("InvincibilityEffect");
            var propInvincStart = _serializedAbility.FindProperty("InvincibilityStartTime");
            var propInvincDuration = _serializedAbility.FindProperty("InvincibilityDuration");
            var propStaminaCost = _serializedAbility.FindProperty("StaminaCost");
            var propStartCue = _serializedAbility.FindProperty("DodgeStartCue");
            var propEndCue = _serializedAbility.FindProperty("DodgeEndCue");

            DrawSubSectionLabel("動畫");
            if (propDodgeAnim != null)
                EditorGUILayout.PropertyField(propDodgeAnim, new GUIContent("前衝動畫"));
            if (propBackstepAnim != null)
                EditorGUILayout.PropertyField(propBackstepAnim, new GUIContent("後撤動畫"));

            EditorGUILayout.Space(3);
            DrawSubSectionLabel("移動");
            if (propDistance != null)
                EditorGUILayout.PropertyField(propDistance, new GUIContent("閃避距離"));
            if (propDuration != null)
                EditorGUILayout.PropertyField(propDuration, new GUIContent("閃避持續時間"));
            if (propCurve != null)
                EditorGUILayout.PropertyField(propCurve, new GUIContent("移動曲線"));

            EditorGUILayout.Space(3);
            DrawSubSectionLabel("無敵");
            if (propInvincEffect != null)
                EditorGUILayout.PropertyField(propInvincEffect, new GUIContent("無敵效果"));
            if (propInvincStart != null)
                EditorGUILayout.PropertyField(propInvincStart, new GUIContent("無敵開始時間"));
            if (propInvincDuration != null)
                EditorGUILayout.PropertyField(propInvincDuration, new GUIContent("無敵持續時間"));

            EditorGUILayout.Space(3);
            DrawSubSectionLabel("消耗 / Cue");
            if (propStaminaCost != null)
                EditorGUILayout.PropertyField(propStaminaCost, new GUIContent("體力消耗"));
            if (propStartCue != null)
                EditorGUILayout.PropertyField(propStartCue, new GUIContent("閃避開始 Cue"));
            if (propEndCue != null)
                EditorGUILayout.PropertyField(propEndCue, new GUIContent("閃避結束 Cue"));

            _serializedAbility.ApplyModifiedProperties();
        }

        private void DrawEditAttachedEffectsSection()
        {
            EditorGUILayout.LabelField("此能力觸發時附帶的額外效果：", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(3);

            for (int i = 0; i < _attachedEffects.Count; i++)
            {
                var entry = _attachedEffects[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                // 效果欄位
                entry.Effect = (GameplayEffect)EditorGUILayout.ObjectField(
                    entry.Effect, typeof(GameplayEffect), false, GUILayout.MinWidth(120));

                // 觸發時機
                string[] timingLabels = { "啟動時", "命中時", "結束時" };
                entry.Timing = (EffectTriggerTiming)EditorGUILayout.Popup(
                    (int)entry.Timing, timingLabels, GUILayout.Width(70));

                // 移除按鈕
                GUI.backgroundColor = COLOR_DANGER;
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    _attachedEffects.RemoveAt(i);
                    i--;
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 新增效果"))
            {
                _attachedEffects.Add(new AttachedEffectEntry());
            }
            if (GUILayout.Button("+ 快速建立效果"))
            {
                QuickCreateEffect();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEditTagsSection()
        {
            if (_serializedAbility == null) return;

            // 啟動阻擋標籤
            DrawSubSectionLabel("啟動阻擋標籤 (擁有這些標籤時不能啟動)");
            DrawTagCheckboxGroup(_serializedAbility.FindProperty("ActivationBlockedTags"),
                _blockedTagToggles, ref _customBlockedTag,
                new[] { "State.Dead", "State.Stunned", "State.Switching", "State.CannotAttack" });

            EditorGUILayout.Space(5);

            // 啟動賦予標籤
            DrawSubSectionLabel("啟動賦予標籤 (啟動時獲得這些標籤)");
            DrawTagCheckboxGroup(_serializedAbility.FindProperty("ActivationOwnedTags"),
                _grantedTagToggles, ref _customGrantedTag,
                new[] { "State.Attacking", "State.Dodging", "State.Aiming", "State.Charging" });

            EditorGUILayout.Space(5);

            // 取消其他能力
            DrawSubSectionLabel("取消其他能力 (啟動時取消帶有這些標籤的能力)");
            DrawTagCheckboxGroup(_serializedAbility.FindProperty("CancelAbilitiesWithTags"),
                _cancelTagToggles, ref _customCancelTag,
                new[] { "State.Attacking", "State.Aiming", "State.Charging" });

            EditorGUILayout.Space(5);

            // 其他標籤容器 (使用 PropertyField)
            DrawSubSectionLabel("其他標籤設定");
            var propRequired = _serializedAbility.FindProperty("ActivationRequiredTags");
            var propBlock = _serializedAbility.FindProperty("BlockAbilitiesWithTags");
            var propCancelledBy = _serializedAbility.FindProperty("CancelledByTags");
            if (propRequired != null)
                EditorGUILayout.PropertyField(propRequired, new GUIContent("啟動所需標籤"));
            if (propBlock != null)
                EditorGUILayout.PropertyField(propBlock, new GUIContent("阻擋其他能力標籤"));
            if (propCancelledBy != null)
                EditorGUILayout.PropertyField(propCancelledBy, new GUIContent("可被取消標籤"));

            _serializedAbility.ApplyModifiedProperties();
        }

        private void DrawTagCheckboxGroup(SerializedProperty containerProp, Dictionary<string, bool> _,
            ref string customTag, string[] commonTags)
        {
            if (containerProp == null) return;

            // 讀取目前容器中的標籤
            var tagsProp = containerProp.FindPropertyRelative("_tags");
            HashSet<string> currentTags = new();
            if (tagsProp != null)
            {
                for (int i = 0; i < tagsProp.arraySize; i++)
                {
                    string tagName = tagsProp.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("_tagName")?.stringValue ?? "";
                    if (!string.IsNullOrEmpty(tagName))
                        currentTags.Add(tagName);
                }
            }

            // 常用標籤勾選
            EditorGUI.indentLevel++;
            foreach (string tag in commonTags)
            {
                bool wasOn = currentTags.Contains(tag);
                bool isOn = EditorGUILayout.ToggleLeft(tag, wasOn);
                if (isOn != wasOn)
                {
                    if (isOn)
                        AddTagToContainer(tagsProp, tag);
                    else
                        RemoveTagFromContainer(tagsProp, tag);
                    _serializedAbility.ApplyModifiedProperties();
                }
            }
            EditorGUI.indentLevel--;

            // 自訂標籤輸入
            EditorGUILayout.BeginHorizontal();
            customTag = EditorGUILayout.TextField(customTag);
            if (GUILayout.Button("新增", GUILayout.Width(50)) && !string.IsNullOrEmpty(customTag))
            {
                if (!currentTags.Contains(customTag))
                {
                    AddTagToContainer(tagsProp, customTag);
                    _serializedAbility.ApplyModifiedProperties();
                }
                customTag = "";
            }
            EditorGUILayout.EndHorizontal();

            // 顯示非常用的自訂標籤
            var commonSet = new HashSet<string>(commonTags);
            foreach (string tag in currentTags)
            {
                if (commonSet.Contains(tag)) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"  [自訂] {tag}", EditorStyles.miniLabel);
                GUI.backgroundColor = COLOR_DANGER;
                if (GUILayout.Button("X", GUILayout.Width(20), GUILayout.Height(16)))
                {
                    RemoveTagFromContainer(tagsProp, tag);
                    _serializedAbility.ApplyModifiedProperties();
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void AddTagToContainer(SerializedProperty tagsProp, string tagName)
        {
            if (tagsProp == null) return;
            int idx = tagsProp.arraySize;
            tagsProp.InsertArrayElementAtIndex(idx);
            var elem = tagsProp.GetArrayElementAtIndex(idx);
            var nameProp = elem.FindPropertyRelative("_tagName");
            if (nameProp != null)
                nameProp.stringValue = tagName;
        }

        private void RemoveTagFromContainer(SerializedProperty tagsProp, string tagName)
        {
            if (tagsProp == null) return;
            for (int i = tagsProp.arraySize - 1; i >= 0; i--)
            {
                string existing = tagsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("_tagName")?.stringValue ?? "";
                if (existing == tagName)
                {
                    tagsProp.DeleteArrayElementAtIndex(i);
                    return;
                }
            }
        }

        private void DrawEditWeaponSection()
        {
            EditorGUILayout.LabelField("此能力目前被以下武器使用：", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(3);

            bool found = false;
            foreach (var weapon in _cachedWeapons)
            {
                if (weapon == null) continue;
                bool isAssigned = weapon.AttackAbility == _editingAbility ||
                                  weapon.HeavyAttackAbility == _editingAbility ||
                                  (GameplayAbility)weapon.DodgeAbility == _editingAbility ||
                                  weapon.ParryAssistAbility == _editingAbility ||
                                  weapon.DodgeAssistAbility == _editingAbility;
                if (isAssigned)
                {
                    found = true;
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    string slotName = GetWeaponSlotName(weapon, _editingAbility);
                    EditorGUILayout.LabelField($"{weapon.WeaponName ?? weapon.name}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"[{slotName}]", EditorStyles.miniLabel, GUILayout.Width(100));
                    if (GUILayout.Button("選擇", GUILayout.Width(50)))
                    {
                        Selection.activeObject = weapon;
                        EditorGUIUtility.PingObject(weapon);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (!found)
            {
                EditorGUILayout.LabelField("（尚未被任何武器使用）", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(5);
            DrawSubSectionLabel("指派到武器");

            // 武器選擇下拉
            if (_cachedWeapons.Count > 0)
            {
                string[] weaponNames = _cachedWeapons
                    .Where(w => w != null)
                    .Select(w => w.WeaponName ?? w.name)
                    .ToArray();
                string[] slotNames = { "輕攻擊", "重攻擊", "閃避", "招架支援", "迴避支援" };

                EditorGUILayout.BeginHorizontal();
                int weaponIdx = EditorGUILayout.Popup("武器", 0, weaponNames);
                int slotIdx = EditorGUILayout.Popup("槽位", 0, slotNames);
                if (GUILayout.Button("指派", GUILayout.Width(50)))
                {
                    AssignToWeapon(weaponIdx, slotIdx);
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("（未找到任何武器資產）", EditorStyles.miniLabel);
            }
        }

        #endregion

        #endregion

        #region 右欄 - 技能卡片預覽

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_rightPanelWidth));
            DrawPanelHeader("技能卡片預覽", new Color(0.6f, 0.5f, 0.2f));

            _cardScroll = EditorGUILayout.BeginScrollView(_cardScroll);

            if (_mode == WorkshopMode.Edit && _editingAbility != null)
            {
                DrawSkillCard(_editingAbility, _rightPanelWidth);
            }
            else if (_mode == WorkshopMode.Create && !_showTemplateSelector)
            {
                DrawCreateModePreviewCard();
            }
            else
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.LabelField("選擇或建立能力後\n將在此處顯示預覽",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true });
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 繪製技能卡片 (可供 Dashboard 呼叫)
        /// </summary>
        public static void DrawSkillCard(GameplayAbility ability, float panelWidth = CARD_WIDTH)
        {
            if (ability == null) return;

            float cardWidth = panelWidth - 20;
            Color typeColor = GetAbilityTypeColorStatic(ability);

            // 外框
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space(5);

            // === 標題列 ===
            var headerRect = GUILayoutUtility.GetRect(cardWidth, 30);
            EditorGUI.DrawRect(headerRect, COLOR_CARD_HEADER);
            // 邊框
            DrawCardBorder(headerRect, COLOR_CARD_BORDER);

            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            };
            var levelStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(1f, 1f, 0.8f) },
                fontSize = 11,
                alignment = TextAnchor.MiddleRight
            };
            GUI.Label(new Rect(headerRect.x + 8, headerRect.y, headerRect.width - 60, headerRect.height),
                ability.AbilityName ?? ability.name, headerStyle);
            GUI.Label(new Rect(headerRect.x + headerRect.width - 55, headerRect.y, 50, headerRect.height),
                $"Lv.{ability.AbilityLevel}", levelStyle);

            // === 類型 + 標籤 ===
            var typeRect = GUILayoutUtility.GetRect(cardWidth, 32);
            EditorGUI.DrawRect(typeRect, COLOR_CARD_BG);

            var typeNameStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = typeColor },
                fontSize = 11
            };
            var tagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.6f, 0.7f) },
                fontSize = 9
            };
            GUI.Label(new Rect(typeRect.x + 8, typeRect.y + 1, typeRect.width - 12, 16),
                $"類型: {GetAbilityTypeNameStatic(ability)}", typeNameStyle);
            string tagText = ability.AbilityTag.IsValid ? ability.AbilityTag.TagName : "(未設定)";
            GUI.Label(new Rect(typeRect.x + 8, typeRect.y + 16, typeRect.width - 12, 14),
                $"標籤: {tagText}", tagStyle);

            // === 描述 ===
            if (!string.IsNullOrEmpty(ability.Description))
            {
                DrawCardSection(cardWidth, () =>
                {
                    var descStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                    {
                        normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
                        padding = new RectOffset(8, 8, 4, 4)
                    };
                    EditorGUILayout.LabelField(ability.Description, descStyle);
                });
            }

            // === 冷卻 / 消耗 ===
            bool hasCooldown = ability.CooldownEffect != null;
            bool hasCost = ability.CostEffect != null;
            if (hasCooldown || hasCost)
            {
                DrawCardSection(cardWidth, () =>
                {
                    if (hasCooldown)
                    {
                        DrawCardIconLine("[冷卻]",
                            $"{ability.CooldownEffect.Duration:F1} 秒",
                            new Color(0.4f, 0.7f, 1f));
                    }
                    if (hasCost)
                    {
                        foreach (var mod in ability.CostEffect.Modifiers)
                        {
                            string attrDisplay = GetAttributeDisplayName(mod.AttributeName);
                            DrawCardIconLine("[消耗]",
                                $"{attrDisplay} {Mathf.Abs(mod.Magnitude):F0}",
                                new Color(1f, 0.85f, 0.3f));
                        }
                    }
                });
            }

            // === 戰鬥數值 ===
            DrawCombatValueCard(ability, cardWidth);

            // === 標籤條件 ===
            DrawTagConditionsCard(ability, cardWidth);

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private static void DrawCombatValueCard(GameplayAbility ability, float cardWidth)
        {
            if (ability is GA_MeleeAttack melee && melee.FirstAttackData != null)
            {
                DrawCardSection(cardWidth, () =>
                {
                    DrawCardSectionTitle("戰鬥數值");
                    foreach (var hw in melee.FirstAttackData.HitWindows)
                    {
                        float damage = hw.BaseDamage * hw.DamageMultiplier;
                        DrawCardIconLine("[傷害]", $"基礎: {hw.BaseDamage} x{hw.DamageMultiplier:F1} = {damage:F0}",
                            new Color(1f, 0.4f, 0.3f));
                    }
                    DrawCardIconLine("[時間]",
                        $"允許輸入: {melee.FirstAttackData.AllowInputTime:F2}s",
                        new Color(0.7f, 0.7f, 0.7f));
                    DrawCardIconLine("[連招]",
                        $"分支: {melee.FirstAttackData.NextCombos.Count}",
                        new Color(0.7f, 0.7f, 0.7f));
                });
            }
            else if (ability is GA_RangedAttack ranged && ranged.FirstAttackData != null)
            {
                DrawCardSection(cardWidth, () =>
                {
                    DrawCardSectionTitle("戰鬥數值");
                    DrawCardIconLine("[傷害]", $"基礎: {ranged.FirstAttackData.BaseDamage}",
                        new Color(1f, 0.4f, 0.3f));
                    DrawCardIconLine("[類型]", $"{ranged.FirstAttackData.AttackType}",
                        new Color(0.4f, 0.7f, 1f));
                    if (ranged.FirstAttackData.Charge != ChargeMode.None)
                    {
                        DrawCardIconLine("[蓄力]", $"{ranged.FirstAttackData.Charge}",
                            new Color(0.9f, 0.6f, 0.2f));
                    }
                    DrawCardIconLine("[連招]",
                        $"分支: {ranged.FirstAttackData.NextCombos.Count}",
                        new Color(0.7f, 0.7f, 0.7f));
                });
            }
            else if (ability is GA_Dodge dodge)
            {
                DrawCardSection(cardWidth, () =>
                {
                    DrawCardSectionTitle("閃避數值");
                    DrawCardIconLine("[距離]", $"{dodge.DodgeDistance:F1}",
                        COLOR_DODGE);
                    DrawCardIconLine("[時間]", $"{dodge.DodgeDuration:F2}s",
                        new Color(0.7f, 0.7f, 0.7f));
                    if (dodge.InvincibilityDuration > 0)
                    {
                        DrawCardIconLine("[無敵]",
                            $"{dodge.InvincibilityStartTime:F2}~{dodge.InvincibilityStartTime + dodge.InvincibilityDuration:F2}s",
                            new Color(1f, 0.85f, 0.3f));
                    }
                    if (dodge.CostEffect != null && dodge.CostEffect.Modifiers.Count > 0)
                    {
                        float costValue = Mathf.Abs(dodge.CostEffect.Modifiers[0].Magnitude);
                        DrawCardIconLine("[體力]", $"消耗: {costValue:F0}",
                            new Color(0.3f, 0.9f, 0.3f));
                    }
                });
            }
        }

        private static void DrawTagConditionsCard(GameplayAbility ability, float cardWidth)
        {
            bool hasConditions = !ability.ActivationBlockedTags.IsEmpty ||
                                 !ability.ActivationOwnedTags.IsEmpty ||
                                 !ability.ActivationRequiredTags.IsEmpty;
            if (!hasConditions) return;

            DrawCardSection(cardWidth, () =>
            {
                DrawCardSectionTitle("啟動條件");
                if (!ability.ActivationRequiredTags.IsEmpty)
                {
                    DrawCardIconLine("[需要]", TagContainerToString(ability.ActivationRequiredTags),
                        new Color(0.3f, 0.8f, 0.3f));
                }
                else
                {
                    DrawCardIconLine("[需要]", "(無)", new Color(0.5f, 0.5f, 0.5f));
                }
                if (!ability.ActivationBlockedTags.IsEmpty)
                {
                    DrawCardIconLine("[阻擋]", TagContainerToString(ability.ActivationBlockedTags),
                        new Color(1f, 0.4f, 0.3f));
                }
                if (!ability.ActivationOwnedTags.IsEmpty)
                {
                    DrawCardIconLine("[賦予]", TagContainerToString(ability.ActivationOwnedTags),
                        COLOR_TAG);
                }
            });
        }

        private void DrawCreateModePreviewCard()
        {
            float cardWidth = _rightPanelWidth - 20;
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space(5);

            // 標題
            var headerRect = GUILayoutUtility.GetRect(cardWidth, 30);
            EditorGUI.DrawRect(headerRect, COLOR_CARD_HEADER);
            DrawCardBorder(headerRect, COLOR_CARD_BORDER);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            };
            GUI.Label(new Rect(headerRect.x + 8, headerRect.y, headerRect.width - 12, headerRect.height),
                _newName, headerStyle);

            // 類型
            var typeRect = GUILayoutUtility.GetRect(cardWidth, 20);
            EditorGUI.DrawRect(typeRect, COLOR_CARD_BG);
            string templateName = _selectedTemplate switch
            {
                AbilityTemplate.MeleeAttack => "近戰攻擊",
                AbilityTemplate.RangedAttack => "遠程攻擊",
                AbilityTemplate.Dodge => "閃避",
                AbilityTemplate.Buff => "增益效果",
                AbilityTemplate.Debuff => "減益效果",
                _ => "未知"
            };
            GUI.Label(new Rect(typeRect.x + 8, typeRect.y + 1, typeRect.width - 12, 18),
                $"類型: {templateName} | 標籤: {_newTag}",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.6f, 0.7f) } });

            // 描述
            if (!string.IsNullOrEmpty(_newDescription))
            {
                DrawCardSection(cardWidth, () =>
                {
                    EditorGUILayout.LabelField(_newDescription,
                        new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                        {
                            normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
                            padding = new RectOffset(8, 8, 4, 4)
                        });
                });
            }

            // 冷卻 / 消耗
            if (_createCooldown || _createCost)
            {
                DrawCardSection(cardWidth, () =>
                {
                    if (_createCooldown)
                        DrawCardIconLine("[冷卻]", $"{_newCooldownDuration:F1} 秒", new Color(0.4f, 0.7f, 1f));
                    if (_createCost)
                        DrawCardIconLine("[消耗]",
                            $"{GetAttributeDisplayName(_costAttributeName)} {_newCostAmount:F0}",
                            new Color(1f, 0.85f, 0.3f));
                });
            }

            // 建立資產列表
            DrawCardSection(cardWidth, () =>
            {
                DrawCardSectionTitle("將建立的資產");
                DrawCardIconLine("[主體]", $"{_newName}.asset", new Color(0.8f, 0.8f, 0.8f));
                if (_selectedTemplate == AbilityTemplate.MeleeAttack)
                    DrawCardIconLine("[數據]", $"{_newName}_AttackData.asset", new Color(0.8f, 0.8f, 0.8f));
                if (_selectedTemplate == AbilityTemplate.RangedAttack)
                    DrawCardIconLine("[數據]", $"{_newName}_AttackData.asset", new Color(0.8f, 0.8f, 0.8f));
                if (_createCooldown)
                    DrawCardIconLine("[冷卻]", $"{_newName}_Cooldown.asset", new Color(0.8f, 0.8f, 0.8f));
                if (_createCost)
                    DrawCardIconLine("[消耗]", $"{_newName}_Cost.asset", new Color(0.8f, 0.8f, 0.8f));
            });

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region 資產建立

        private void ExecuteCreateAssets()
        {
            if (string.IsNullOrEmpty(_newName))
            {
                EditorUtility.DisplayDialog("錯誤", "請輸入資產名稱！", "確定");
                return;
            }

            // 確保輸出路徑存在
            if (!Directory.Exists(_outputPath))
            {
                Directory.CreateDirectory(_outputPath);
                AssetDatabase.Refresh();
            }

            GameplayEffect cooldownEffect = null;
            GameplayEffect costEffect = null;

            // 建立冷卻效果
            if (_createCooldown)
            {
                cooldownEffect = ScriptableObject.CreateInstance<GameplayEffect>();
                cooldownEffect.EffectName = $"{_newName} Cooldown";
                cooldownEffect.EffectTag = new GameplayTag($"Effect.Cooldown.{_newName}");
                cooldownEffect.DurationPolicy = DurationPolicy.Duration;
                cooldownEffect.Duration = _newCooldownDuration;
                AssetDatabase.CreateAsset(cooldownEffect, $"{_outputPath}/{_newName}_Cooldown.asset");
            }

            // 建立消耗效果
            if (_createCost)
            {
                costEffect = ScriptableObject.CreateInstance<GameplayEffect>();
                costEffect.EffectName = $"{_newName} Cost";
                costEffect.EffectTag = new GameplayTag($"Effect.Cost.{_newName}");
                costEffect.DurationPolicy = DurationPolicy.Instant;
                costEffect.Modifiers.Add(new GameplayModifier
                {
                    AttributeName = _costAttributeName,
                    OperationType = ModifierOperationType.Additive,
                    Magnitude = -_newCostAmount,
                    MagnitudeType = ModifierMagnitudeType.ScalableFloat
                });
                AssetDatabase.CreateAsset(costEffect, $"{_outputPath}/{_newName}_Cost.asset");
            }

            // 根據模板建立主資產
            switch (_selectedTemplate)
            {
                case AbilityTemplate.MeleeAttack:
                    CreateMeleeAbility(cooldownEffect, costEffect);
                    break;
                case AbilityTemplate.RangedAttack:
                    CreateRangedAbility(cooldownEffect, costEffect);
                    break;
                case AbilityTemplate.Dodge:
                    CreateDodgeAbility(cooldownEffect, costEffect);
                    break;
                case AbilityTemplate.Buff:
                case AbilityTemplate.Debuff:
                    CreateEffectAsset(cooldownEffect, costEffect);
                    break;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshCache();

            EditorUtility.DisplayDialog("建立完成",
                $"所有資產已建立於：\n{_outputPath}", "確定");

            // 切換到編輯模式並載入新建的能力
            _mode = WorkshopMode.Edit;
            _showTemplateSelector = false;
        }

        private void CreateMeleeAbility(GameplayEffect cooldown, GameplayEffect cost)
        {
            // 建立攻擊數據
            var attackData = ScriptableObject.CreateInstance<MeleeAttackData>();
            attackData.AllowInputTime = 0.3f;
            attackData.ComboResetTime = 0.6f;
            attackData.AllowCancelTime = 0.4f;
            attackData.HitWindows.Add(new MeleeHitWindow
            {
                StartTime = 0.1f,
                EndTime = 0.25f,
                Shape = HitboxShape.Box,
                Offset = new Vector3(0, 1, 1),
                Size = Vector3.one,
                BaseDamage = 25,
                DamageMultiplier = 1f,
                HitStopDuration = 0.08f,
                ScreenShakeForce = 1f
            });
            AssetDatabase.CreateAsset(attackData, $"{_outputPath}/{_newName}_AttackData.asset");

            // 建立能力
            var ability = ScriptableObject.CreateInstance<GA_MeleeAttack>();
            ability.AbilityName = _newName;
            ability.AbilityTag = new GameplayTag(_newTag);
            ability.Description = _newDescription;
            ability.CooldownEffect = cooldown;
            ability.CostEffect = cost;
            ability.FirstAttackData = attackData;
            ability.ActivationBlockedTags.AddTag(new GameplayTag("State.Dead"));
            ability.ActivationBlockedTags.AddTag(new GameplayTag("State.Stunned"));
            ability.ActivationOwnedTags.AddTag(new GameplayTag("State.Attacking"));
            AssetDatabase.CreateAsset(ability, $"{_outputPath}/{_newName}.asset");

            SelectAbility(ability);
        }

        private void CreateRangedAbility(GameplayEffect cooldown, GameplayEffect cost)
        {
            // 建立攻擊數據
            var attackData = ScriptableObject.CreateInstance<RangedAttackData>();
            attackData.AllowInputTime = 0.3f;
            attackData.ComboResetTime = 0.6f;
            attackData.AllowCancelTime = 0.4f;
            attackData.BaseDamage = 20;
            attackData.FireTime = 0.3f;
            AssetDatabase.CreateAsset(attackData, $"{_outputPath}/{_newName}_AttackData.asset");

            // 建立能力
            var ability = ScriptableObject.CreateInstance<GA_RangedAttack>();
            ability.AbilityName = _newName;
            ability.AbilityTag = new GameplayTag(_newTag);
            ability.Description = _newDescription;
            ability.CooldownEffect = cooldown;
            ability.CostEffect = cost;
            ability.FirstAttackData = attackData;
            ability.ActivationBlockedTags.AddTag(new GameplayTag("State.Dead"));
            ability.ActivationBlockedTags.AddTag(new GameplayTag("State.Stunned"));
            ability.ActivationOwnedTags.AddTag(new GameplayTag("State.Attacking"));
            AssetDatabase.CreateAsset(ability, $"{_outputPath}/{_newName}.asset");

            SelectAbility(ability);
        }

        private void CreateDodgeAbility(GameplayEffect cooldown, GameplayEffect cost)
        {
            var ability = ScriptableObject.CreateInstance<GA_Dodge>();
            ability.AbilityName = _newName;
            ability.AbilityTag = new GameplayTag(_newTag);
            ability.Description = _newDescription;
            ability.CooldownEffect = cooldown;
            ability.CostEffect = cost;
            ability.DodgeDistance = 5f;
            ability.DodgeDuration = 0.4f;
            ability.InvincibilityStartTime = 0f;
            ability.InvincibilityDuration = 0.3f;
            ability.ActivationBlockedTags.AddTag(new GameplayTag("State.Dead"));
            ability.ActivationBlockedTags.AddTag(new GameplayTag("State.Stunned"));
            ability.ActivationOwnedTags.AddTag(new GameplayTag("State.Dodging"));
            ability.CancelAbilitiesWithTags.AddTag(new GameplayTag("State.Attacking"));
            AssetDatabase.CreateAsset(ability, $"{_outputPath}/{_newName}.asset");

            SelectAbility(ability);
        }

        private void CreateEffectAsset(GameplayEffect _, GameplayEffect __)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.EffectName = _newName;
            effect.EffectTag = new GameplayTag(_newTag);
            effect.Description = _newDescription;

            if (_selectedTemplate == AbilityTemplate.Buff)
            {
                effect.DurationPolicy = DurationPolicy.Duration;
                effect.Duration = 10f;
                effect.Modifiers.Add(new GameplayModifier
                {
                    AttributeName = "AttackPower",
                    OperationType = ModifierOperationType.Additive,
                    Magnitude = 10f,
                    MagnitudeType = ModifierMagnitudeType.ScalableFloat
                });
                effect.GrantedTags.AddTag(new GameplayTag("State.Buffed"));
            }
            else
            {
                effect.DurationPolicy = DurationPolicy.Duration;
                effect.Duration = 8f;
                effect.Modifiers.Add(new GameplayModifier
                {
                    AttributeName = "Defense",
                    OperationType = ModifierOperationType.Additive,
                    Magnitude = -5f,
                    MagnitudeType = ModifierMagnitudeType.ScalableFloat
                });
                effect.GrantedTags.AddTag(new GameplayTag("State.Debuffed"));
            }

            AssetDatabase.CreateAsset(effect, $"{_outputPath}/{_newName}.asset");
            Selection.activeObject = effect;
            EditorGUIUtility.PingObject(effect);
        }

        private void CreateAndAssignCooldownEffect()
        {
            if (_editingAbility == null) return;
            string abilityName = _editingAbility.AbilityName ?? _editingAbility.name;
            string path = AssetDatabase.GetAssetPath(_editingAbility);
            string dir = string.IsNullOrEmpty(path) ? "Assets/Data/GAS" : Path.GetDirectoryName(path);

            var cdEffect = ScriptableObject.CreateInstance<GameplayEffect>();
            cdEffect.EffectName = $"{abilityName} Cooldown";
            cdEffect.EffectTag = new GameplayTag($"Effect.Cooldown.{abilityName}");
            cdEffect.DurationPolicy = DurationPolicy.Duration;
            cdEffect.Duration = 2f;

            string cdPath = $"{dir}/{abilityName}_Cooldown.asset";
            AssetDatabase.CreateAsset(cdEffect, cdPath);

            _editingAbility.CooldownEffect = cdEffect;
            EditorUtility.SetDirty(_editingAbility);
            AssetDatabase.SaveAssets();

            _serializedAbility = new SerializedObject(_editingAbility);
            RefreshCache();
        }

        private void CreateAndAssignCostEffect()
        {
            if (_editingAbility == null) return;
            string abilityName = _editingAbility.AbilityName ?? _editingAbility.name;
            string path = AssetDatabase.GetAssetPath(_editingAbility);
            string dir = string.IsNullOrEmpty(path) ? "Assets/Data/GAS" : Path.GetDirectoryName(path);

            var costEffect = ScriptableObject.CreateInstance<GameplayEffect>();
            costEffect.EffectName = $"{abilityName} Cost";
            costEffect.EffectTag = new GameplayTag($"Effect.Cost.{abilityName}");
            costEffect.DurationPolicy = DurationPolicy.Instant;
            costEffect.Modifiers.Add(new GameplayModifier
            {
                AttributeName = "Stamina",
                OperationType = ModifierOperationType.Additive,
                Magnitude = -20f,
                MagnitudeType = ModifierMagnitudeType.ScalableFloat
            });

            string costPath = $"{dir}/{abilityName}_Cost.asset";
            AssetDatabase.CreateAsset(costEffect, costPath);

            _editingAbility.CostEffect = costEffect;
            EditorUtility.SetDirty(_editingAbility);
            AssetDatabase.SaveAssets();

            _serializedAbility = new SerializedObject(_editingAbility);
            RefreshCache();
        }

        private void QuickCreateEffect()
        {
            if (_editingAbility == null) return;
            string abilityName = _editingAbility.AbilityName ?? _editingAbility.name;
            string path = AssetDatabase.GetAssetPath(_editingAbility);
            string dir = string.IsNullOrEmpty(path) ? "Assets/Data/GAS" : Path.GetDirectoryName(path);

            int idx = _attachedEffects.Count + 1;
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.EffectName = $"{abilityName}_Effect_{idx}";
            effect.EffectTag = new GameplayTag($"Effect.{abilityName}.{idx}");
            effect.DurationPolicy = DurationPolicy.Duration;
            effect.Duration = 5f;

            string effectPath = $"{dir}/{effect.EffectName}.asset";
            AssetDatabase.CreateAsset(effect, effectPath);
            AssetDatabase.SaveAssets();

            _attachedEffects.Add(new AttachedEffectEntry { Effect = effect });
            RefreshCache();
        }

        #endregion

        #region 儲存 / 工具

        private new void SaveChanges()
        {
            if (_editingAbility == null) return;

            _serializedAbility?.ApplyModifiedProperties();

            EditorUtility.SetDirty(_editingAbility);

            // 同時標記相關效果為 dirty
            if (_editingAbility.CooldownEffect != null)
                EditorUtility.SetDirty(_editingAbility.CooldownEffect);
            if (_editingAbility.CostEffect != null)
                EditorUtility.SetDirty(_editingAbility.CostEffect);

            AssetDatabase.SaveAssets();
            Repaint();

            // 簡短提示
            Debug.Log($"[能力工作坊] 已儲存: {_editingAbility.AbilityName ?? _editingAbility.name}");
        }

        private void SelectAbility(GameplayAbility ability)
        {
            _editingAbility = ability;
            _serializedAbility = ability != null ? new SerializedObject(ability) : null;
            _editorScroll = Vector2.zero;

            // 重建附帶效果列表 (從能力的現有狀態推斷)
            _attachedEffects.Clear();

            // 初始化標籤勾選狀態
            _blockedTagToggles.Clear();
            _grantedTagToggles.Clear();
            _cancelTagToggles.Clear();

            Repaint();
        }

        private void AssignToWeapon(int weaponIdx, int slotIdx)
        {
            var validWeapons = _cachedWeapons.Where(w => w != null).ToList();
            if (weaponIdx < 0 || weaponIdx >= validWeapons.Count || _editingAbility == null) return;

            var weapon = validWeapons[weaponIdx];
            var so = new SerializedObject(weapon);

            switch (slotIdx)
            {
                case 0: // 輕攻擊
                    so.FindProperty("AttackAbility").objectReferenceValue = _editingAbility;
                    break;
                case 1: // 重攻擊
                    so.FindProperty("HeavyAttackAbility").objectReferenceValue = _editingAbility;
                    break;
                case 2: // 閃避
                    if (_editingAbility is GA_Dodge)
                        so.FindProperty("DodgeAbility").objectReferenceValue = _editingAbility;
                    else
                        EditorUtility.DisplayDialog("類型不符", "閃避槽位只能指派 GA_Dodge 類型的能力。", "確定");
                    break;
                case 3: // 招架支援
                    so.FindProperty("ParryAssistAbility").objectReferenceValue = _editingAbility;
                    break;
                case 4: // 迴避支援
                    so.FindProperty("DodgeAssistAbility").objectReferenceValue = _editingAbility;
                    break;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(weapon);
            AssetDatabase.SaveAssets();
            Repaint();
        }

        private void RefreshCache()
        {
            _cachedAbilities = FindAllAssets<GameplayAbility>();
            _cachedEffects = FindAllAssets<GameplayEffect>();
            _cachedWeapons = FindAllAssets<WeaponData>();
            _cacheInitialized = true;
            Repaint();
        }

        private List<T> FindAllAssets<T>() where T : ScriptableObject
        {
            var results = new List<T>();
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                    results.Add(asset);
            }
            return results;
        }

        #endregion

        #region UI 輔助

        private void DrawPanelHeader(string title, Color color)
        {
            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, color);
            GUI.Label(rect, $"  {title}", new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            });
        }

        private void DrawDraggableSplitter(bool isLeftSplitter)
        {
            var rect = GUILayoutUtility.GetRect(SPLITTER_WIDTH, 0,
                GUILayout.ExpandHeight(true), GUILayout.Width(SPLITTER_WIDTH));
            bool isDragging = isLeftSplitter ? _isDraggingLeftSplitter : _isDraggingRightSplitter;
            Color bgColor = isDragging ? new Color(0.35f, 0.35f, 0.4f) : new Color(0.18f, 0.18f, 0.2f);
            EditorGUI.DrawRect(rect, bgColor);
            // 中線握把提示
            float cx = rect.x + (SPLITTER_WIDTH - 1f) * 0.5f;
            EditorGUI.DrawRect(new Rect(cx, rect.y, 1, rect.height),
                isDragging ? new Color(0.6f, 0.6f, 0.7f) : new Color(0.4f, 0.4f, 0.45f));
            // 游標
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            // 滑鼠按下
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (isLeftSplitter)
                    _isDraggingLeftSplitter = true;
                else
                    _isDraggingRightSplitter = true;
                Event.current.Use();
            }
        }

        private void HandleSplitterEvents()
        {
            var e = Event.current;
            if (e.type == EventType.MouseDrag)
            {
                if (_isDraggingLeftSplitter)
                {
                    _leftPanelWidth += e.delta.x;
                    _leftPanelWidth = Mathf.Clamp(_leftPanelWidth, MIN_LEFT_WIDTH, MAX_LEFT_WIDTH);
                    float centerWidth = position.width - _leftPanelWidth - _rightPanelWidth - 2 * SPLITTER_WIDTH;
                    if (centerWidth < MIN_EDITOR_WIDTH)
                        _leftPanelWidth = position.width - _rightPanelWidth - 2 * SPLITTER_WIDTH - MIN_EDITOR_WIDTH;
                    e.Use();
                    Repaint();
                }
                else if (_isDraggingRightSplitter)
                {
                    _rightPanelWidth -= e.delta.x;
                    _rightPanelWidth = Mathf.Clamp(_rightPanelWidth, MIN_RIGHT_WIDTH, MAX_RIGHT_WIDTH);
                    float centerWidth = position.width - _leftPanelWidth - _rightPanelWidth - 2 * SPLITTER_WIDTH;
                    if (centerWidth < MIN_EDITOR_WIDTH)
                        _rightPanelWidth = position.width - _leftPanelWidth - 2 * SPLITTER_WIDTH - MIN_EDITOR_WIDTH;
                    e.Use();
                    Repaint();
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                if (_isDraggingLeftSplitter || _isDraggingRightSplitter)
                {
                    _isDraggingLeftSplitter = false;
                    _isDraggingRightSplitter = false;
                    e.Use();
                }
            }
        }

        private void DrawFoldoutSection(string key, string title, Action drawContent)
        {
            if (!_foldouts.ContainsKey(key))
                _foldouts[key] = true;

            EditorGUILayout.Space(3);
            var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.25f));

            var foldoutStyle = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                normal = { textColor = new Color(0.9f, 0.85f, 0.7f) },
                onNormal = { textColor = new Color(0.9f, 0.85f, 0.7f) }
            };
            _foldouts[key] = EditorGUI.Foldout(
                new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4),
                _foldouts[key], title, true, foldoutStyle);

            if (_foldouts[key])
            {
                EditorGUI.indentLevel++;
                drawContent();
                EditorGUI.indentLevel--;
            }
        }

        private static void DrawSubSectionLabel(string title)
        {
            EditorGUILayout.LabelField(title, new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = new Color(0.7f, 0.8f, 0.9f) }
            });
        }

        private static void DrawCardSection(float cardWidth, Action drawContent)
        {
            var sectionRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(sectionRect, COLOR_CARD_BG);
            // 頂部分隔線
            var sepRect = GUILayoutUtility.GetRect(cardWidth, 1);
            EditorGUI.DrawRect(sepRect, COLOR_CARD_SECTION);
            drawContent();
            EditorGUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        private static void DrawCardSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                padding = new RectOffset(8, 0, 2, 0)
            });
        }

        private static void DrawCardIconLine(string icon, string text, Color iconColor)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            var iconStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = iconColor },
                fontStyle = FontStyle.Bold,
                fontSize = 10
            };
            var textStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                fontSize = 10
            };
            EditorGUILayout.LabelField(icon, iconStyle, GUILayout.Width(42));
            EditorGUILayout.LabelField(text, textStyle);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawCardBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }

        private static Color GetAbilityTypeColorStatic(GameplayAbility ability)
        {
            return ability switch
            {
                GA_MeleeAttack => COLOR_MELEE,
                GA_RangedAttack => COLOR_RANGED,
                GA_Dodge => COLOR_DODGE,
                _ => COLOR_ABILITY
            };
        }

        private static Color GetAbilityTypeColor(GameplayAbility ability)
        {
            return GetAbilityTypeColorStatic(ability);
        }

        private static string GetAbilityTypeNameStatic(GameplayAbility ability)
        {
            return ability switch
            {
                GA_MeleeAttack => "近戰攻擊",
                GA_RangedAttack => "遠程攻擊",
                GA_Dodge => "閃避",
                _ => ability.GetType().Name
            };
        }

        private static string GetAbilityTypeName(GameplayAbility ability)
        {
            return GetAbilityTypeNameStatic(ability);
        }

        private static string GetWeaponSlotName(WeaponData weapon, GameplayAbility ability)
        {
            if (weapon.AttackAbility == ability) return "輕攻擊";
            if (weapon.HeavyAttackAbility == ability) return "重攻擊";
            if ((GameplayAbility)weapon.DodgeAbility == ability) return "閃避";
            if (weapon.ParryAssistAbility == ability) return "招架支援";
            if (weapon.DodgeAssistAbility == ability) return "迴避支援";
            return "?";
        }

        private static string GetAttributeDisplayName(string attrName)
        {
            return attrName switch
            {
                "Health" => "生命值",
                "MaxHealth" => "最大生命值",
                "AttackPower" => "攻擊力",
                "CriticalChance" => "暴擊率",
                "CriticalDamage" => "暴擊傷害",
                "Defense" => "防禦力",
                "DamageReduction" => "傷害減免",
                "MoveSpeed" => "移動速度",
                "DodgeCooldown" => "閃避冷卻",
                "Stamina" => "體力",
                "MaxStamina" => "最大體力",
                "StaminaRegen" => "體力恢復",
                "Mana" => "魔力",
                "MaxMana" => "最大魔力",
                "ManaRegen" => "魔力恢復",
                "AssistPoints" => "支援點數",
                "MaxAssistPoints" => "最大支援點數",
                "IncomingDamage" => "受到傷害",
                _ => attrName
            };
        }

        private static string TagContainerToString(GameplayTagContainer container)
        {
            if (container == null || container.IsEmpty) return "(無)";
            var tags = new List<string>();
            foreach (var tag in container)
            {
                if (tag.IsValid)
                {
                    // 取最後一段作為簡短顯示
                    string name = tag.TagName;
                    int lastDot = name.LastIndexOf('.');
                    tags.Add(lastDot >= 0 ? name[(lastDot + 1)..] : name);
                }
            }
            return string.Join(", ", tags);
        }

        #endregion
    }
}
#endif
