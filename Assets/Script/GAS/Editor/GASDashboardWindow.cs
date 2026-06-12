#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GAS.Editor
{
    /// <summary>
    /// GAS 總覽面板 - 提供新手友善的可視化 GAS 系統導覽
    /// 包含系統架構圖、能力/效果/屬性/標籤瀏覽、傷害計算機、FAQ 等功能
    /// </summary>
    public class GASDashboardWindow : EditorWindow
    {
        #region 列舉與常量

        private enum DashboardTab
        {
            Overview,
            Abilities,
            Effects,
            Attributes,
            Tags,
            QuickLinks
        }

        private const float SIDEBAR_WIDTH = 180f;
        private const float LIST_WIDTH_RATIO = 0.35f;

        // 各子系統色彩
        private static readonly Color COLOR_ABILITY = new(0.8f, 0.6f, 0.2f);
        private static readonly Color COLOR_EFFECT = new(0.6f, 0.2f, 0.8f);
        private static readonly Color COLOR_ATTRIBUTE = new(0.2f, 0.6f, 0.2f);
        private static readonly Color COLOR_TAG = new(0.2f, 0.4f, 0.8f);
        private static readonly Color COLOR_CUE = new(0.2f, 0.7f, 0.7f);
        private static readonly Color COLOR_WEAPON = new(0.8f, 0.2f, 0.2f);

        #endregion

        #region 欄位

        private DashboardTab _currentTab = DashboardTab.Overview;
        private Vector2 _contentScrollPos;
        private Vector2 _listScrollPos;
        private Vector2 _detailScrollPos;

        // 快取資料
        private List<GameplayAbility> _cachedAbilities = new();
        private List<GameplayEffect> _cachedEffects = new();
        private List<WeaponData> _cachedWeapons = new();
        private List<GameplayCue> _cachedCues = new();
        private List<string> _cachedTags = new();
        private bool _cacheInitialized;

        // 選取狀態
        private int _selectedAbilityIndex = -1;
        private int _selectedEffectIndex = -1;

        // 搜尋/篩選
        private string _abilitySearch = "";
        private string _effectSearch = "";
        private string _tagSearch = "";
        private int _abilityTypeFilter; // 0=全部, 1=近戰, 2=遠程, 3=閃避, 4=其他
        private int _effectDurationFilter; // 0=全部, 1=即時, 2=持續, 3=永久

        // 傷害計算機
        private float _calcRawDamage = 100f;
        private float _calcDefense = 50f;
        private float _calcDamageReduction;

        // 摺疊狀態
        private readonly Dictionary<string, bool> _foldouts = new();

        // 屬性效果查詢
        private int _selectedAttributeForQuery;

        #endregion

        #region 屬性參考資料

        private struct AttributeInfo
        {
            public string Name;
            public float DefaultValue;
            public string Description;
            public string Category;
            public string Related;
        }

        private static readonly AttributeInfo[] ATTRIBUTE_INFOS =
        {
            new() { Name = "Health", DefaultValue = 100f, Description = "角色目前的生命值", Category = "生命值", Related = "上限: MaxHealth" },
            new() { Name = "MaxHealth", DefaultValue = 100f, Description = "生命值上限", Category = "生命值", Related = "Health ≤ MaxHealth" },
            new() { Name = "AttackPower", DefaultValue = 10f, Description = "基礎攻擊力，影響所有傷害計算", Category = "攻擊", Related = "" },
            new() { Name = "CriticalChance", DefaultValue = 0.05f, Description = "暴擊機率（0.05 = 5%）", Category = "攻擊", Related = "配合 CriticalDamage" },
            new() { Name = "CriticalDamage", DefaultValue = 1.5f, Description = "暴擊傷害倍率（1.5 = 150%）", Category = "攻擊", Related = "配合 CriticalChance" },
            new() { Name = "Defense", DefaultValue = 5f, Description = "防禦力，減少受到的傷害", Category = "防禦", Related = "公式: 100/(100+Defense)" },
            new() { Name = "DamageReduction", DefaultValue = 0f, Description = "百分比傷害減免（0.1 = 10%）", Category = "防禦", Related = "在 Defense 之前套用" },
            new() { Name = "MoveSpeed", DefaultValue = 5f, Description = "角色移動速度", Category = "移動", Related = "" },
            new() { Name = "DodgeCooldown", DefaultValue = 1f, Description = "閃避冷卻時間（秒）", Category = "移動", Related = "" },
            new() { Name = "Stamina", DefaultValue = 100f, Description = "體力值，閃避和衝刺消耗", Category = "體力", Related = "上限: MaxStamina" },
            new() { Name = "MaxStamina", DefaultValue = 100f, Description = "體力值上限", Category = "體力", Related = "Stamina ≤ MaxStamina" },
            new() { Name = "StaminaRegen", DefaultValue = 10f, Description = "每秒體力恢復量", Category = "體力", Related = "消耗後延遲1秒恢復" },
            new() { Name = "Mana", DefaultValue = 100f, Description = "魔力值，技能施放消耗", Category = "魔力", Related = "上限: MaxMana" },
            new() { Name = "MaxMana", DefaultValue = 100f, Description = "魔力值上限", Category = "魔力", Related = "Mana ≤ MaxMana" },
            new() { Name = "ManaRegen", DefaultValue = 5f, Description = "每秒魔力恢復量", Category = "魔力", Related = "消耗後延遲1.5秒恢復" },
            new() { Name = "AssistPoints", DefaultValue = 3f, Description = "支援點數（招架/迴避支援消耗）", Category = "支援點數", Related = "上限: MaxAssistPoints" },
            new() { Name = "MaxAssistPoints", DefaultValue = 3f, Description = "支援點數上限", Category = "支援點數", Related = "" },
            new() { Name = "IncomingDamage", DefaultValue = 0f, Description = "臨時屬性，傳遞傷害計算中間值", Category = "元屬性", Related = "不要直接修改" },
        };

        #endregion

        #region 生命週期

        [MenuItem("GAS/Dashboard %#g")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<GASDashboardWindow>();
            wnd.titleContent = new GUIContent("GAS 總覽面板");
            wnd.minSize = new Vector2(800, 550);
        }

        private void OnEnable()
        {
            RefreshAllCaches();
        }

        private void OnGUI()
        {
            if (!_cacheInitialized)
                RefreshAllCaches();
            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            DrawContent();
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 側邊欄

        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SIDEBAR_WIDTH));
            // 標題
            var titleRect = GUILayoutUtility.GetRect(SIDEBAR_WIDTH, 40);
            EditorGUI.DrawRect(titleRect, new Color(0.15f, 0.15f, 0.15f));
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                normal = { textColor = Color.white }
            };
            GUI.Label(titleRect, "GAS 總覽面板", titleStyle);
            EditorGUILayout.Space(5);
            // 分頁按鈕
            DrawSidebarTab("系統總覽", DashboardTab.Overview, new Color(0.3f, 0.5f, 0.7f));
            DrawSidebarTab("能力瀏覽", DashboardTab.Abilities, COLOR_ABILITY);
            DrawSidebarTab("效果百科", DashboardTab.Effects, COLOR_EFFECT);
            DrawSidebarTab("屬性參考", DashboardTab.Attributes, COLOR_ATTRIBUTE);
            DrawSidebarTab("標籤地圖", DashboardTab.Tags, COLOR_TAG);
            DrawSidebarTab("快速連結", DashboardTab.QuickLinks, new Color(0.5f, 0.5f, 0.5f));
            GUILayout.FlexibleSpace();
            // 重整按鈕
            if (GUILayout.Button("重新整理快取", GUILayout.Height(25)))
                RefreshAllCaches();
            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
            // 分隔線
            var sepRect = GUILayoutUtility.GetRect(1, 0, GUILayout.ExpandHeight(true), GUILayout.Width(1));
            EditorGUI.DrawRect(sepRect, new Color(0.3f, 0.3f, 0.3f));
        }

        private void DrawSidebarTab(string label, DashboardTab tab, Color color)
        {
            bool isActive = _currentTab == tab;
            Color bg = isActive ? color : new Color(0.22f, 0.22f, 0.22f);
            var rect = GUILayoutUtility.GetRect(SIDEBAR_WIDTH - 10, 32);
            EditorGUI.DrawRect(rect, bg);
            var style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                fontSize = 12,
                normal = { textColor = isActive ? Color.white : new Color(0.8f, 0.8f, 0.8f) }
            };
            GUI.Label(rect, label, style);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _currentTab = tab;
                _contentScrollPos = Vector2.zero;
                Event.current.Use();
                Repaint();
            }
            EditorGUILayout.Space(2);
        }

        #endregion

        #region 內容分發

        private void DrawContent()
        {
            EditorGUILayout.BeginVertical();
            switch (_currentTab)
            {
                case DashboardTab.Overview:
                    DrawOverviewTab();
                    break;
                case DashboardTab.Abilities:
                    DrawAbilitiesTab();
                    break;
                case DashboardTab.Effects:
                    DrawEffectsTab();
                    break;
                case DashboardTab.Attributes:
                    DrawAttributesTab();
                    break;
                case DashboardTab.Tags:
                    DrawTagsTab();
                    break;
                case DashboardTab.QuickLinks:
                    DrawQuickLinksTab();
                    break;
            }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Tab 1: 系統總覽

        private void DrawOverviewTab()
        {
            _contentScrollPos = EditorGUILayout.BeginScrollView(_contentScrollPos);
            DrawColoredHeader("系統總覽", new Color(0.3f, 0.5f, 0.7f));
            EditorGUILayout.Space(5);
            // 資產統計
            DrawSectionHeader("資產統計");
            EditorGUILayout.BeginHorizontal();
            DrawStatBox("能力", _cachedAbilities.Count, COLOR_ABILITY);
            DrawStatBox("效果", _cachedEffects.Count, COLOR_EFFECT);
            DrawStatBox("武器", _cachedWeapons.Count, COLOR_WEAPON);
            DrawStatBox("Cue", _cachedCues.Count, COLOR_CUE);
            DrawStatBox("標籤", _cachedTags.Count, COLOR_TAG);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);
            // 系統架構圖
            DrawSectionHeader("系統架構圖（點擊方塊跳轉）");
            DrawSystemDiagram();
            EditorGUILayout.Space(10);
            // 新手指南
            DrawBeginnerGuide();
            EditorGUILayout.EndScrollView();
        }

        private void DrawStatBox(string label, int count, Color color)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(80));
            var rect = GUILayoutUtility.GetRect(0, 4, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, color);
            EditorGUILayout.LabelField(count.ToString(), new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18
            }, GUILayout.Height(28));
            EditorGUILayout.LabelField(label, new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            });
            EditorGUILayout.EndVertical();
        }

        private void DrawSystemDiagram()
        {
            // 使用按鈕繪製簡化架構圖
            float boxW = 120f;
            float boxH = 40f;
            EditorGUILayout.Space(5);
            // 頂部行：標籤、Cue
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawDiagramBox("標籤 (Tags)", COLOR_TAG, boxW, boxH, DashboardTab.Tags);
            GUILayout.Space(40);
            DrawDiagramBox("Cue (回饋)", COLOR_CUE, boxW, boxH, DashboardTab.QuickLinks);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            // 箭頭提示
            DrawCenteredLabel("▲  查詢/觸發  ▲");
            EditorGUILayout.Space(2);
            // 中間行：能力 → ASC ← 效果
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawDiagramBox("能力 (Ability)", COLOR_ABILITY, boxW, boxH, DashboardTab.Abilities);
            DrawCenteredArrow(" → ");
            DrawDiagramBox("ASC\n(核心元件)", new Color(0.3f, 0.5f, 0.7f), boxW + 20, boxH + 10, null);
            DrawCenteredArrow(" ← ");
            DrawDiagramBox("效果 (Effect)", COLOR_EFFECT, boxW, boxH, DashboardTab.Effects);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            // 箭頭提示
            DrawCenteredLabel("▼  修改/裝備  ▼");
            EditorGUILayout.Space(2);
            // 底部行：屬性、武器
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawDiagramBox("屬性 (Attribute)", COLOR_ATTRIBUTE, boxW, boxH, DashboardTab.Attributes);
            GUILayout.Space(40);
            DrawDiagramBox("武器 (Weapon)", COLOR_WEAPON, boxW, boxH, DashboardTab.QuickLinks);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        private void DrawDiagramBox(string label, Color color, float w, float h, DashboardTab? targetTab)
        {
            var originalBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            var style = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            if (GUILayout.Button(label, style, GUILayout.Width(w), GUILayout.Height(h)))
            {
                if (targetTab.HasValue)
                {
                    _currentTab = targetTab.Value;
                    _contentScrollPos = Vector2.zero;
                }
            }
            GUI.backgroundColor = originalBg;
        }

        private void DrawCenteredArrow(string arrow)
        {
            GUILayout.Label(arrow, new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16
            }, GUILayout.Width(40), GUILayout.Height(40));
        }

        private void DrawCenteredLabel(string text)
        {
            EditorGUILayout.LabelField(text, new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            });
        }

        private void DrawBeginnerGuide()
        {
            bool show = GetFoldout("beginnerGuide", true);
            show = EditorGUILayout.Foldout(show, "新手指南：什麼是 GAS？", true, EditorStyles.foldoutHeader);
            SetFoldout("beginnerGuide", show);
            if (!show) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "GAS（Gameplay Ability System）是一套模組化的遊戲能力框架，" +
                "將戰鬥邏輯拆解為獨立的組件，讓設計師可以不寫程式碼就能調整遊戲性。",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(5);
            DrawSectionHeader("核心元件說明");
            DrawExplanation("AbilitySystemComponent (ASC)",
                "附加在角色上的核心元件，管理所有能力、效果和屬性。每個需要使用 GAS 的角色都需要一個 ASC。");
            DrawExplanation("GameplayAbility（能力）",
                "定義角色可以執行的動作（如攻擊、閃避、跳躍）。每個能力都是一個 ScriptableObject，包含啟動條件、冷卻、消耗等設定。");
            DrawExplanation("GameplayEffect（效果）",
                "定義如何修改屬性（如造成傷害、施加增益）。效果可以是即時的、持續一段時間的、或永久的。");
            DrawExplanation("GameplayAttribute（屬性）",
                "角色的數值屬性（如生命值、攻擊力、防禦力）。效果透過修改器（Modifier）來改變屬性值。");
            DrawExplanation("GameplayTag（標籤）",
                "階層式的狀態標記（如 State.Attacking、Effect.Buff.AttackUp）。用來控制能力的啟動條件和互相影響。");
            DrawExplanation("GameplayCue（回饋）",
                "視覺/音效回饋系統（粒子特效、音效）。與邏輯解耦，由標籤觸發。");
            EditorGUILayout.Space(5);
            DrawSectionHeader("完整資料流程");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var flowStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = 11
            };
            EditorGUILayout.LabelField(
                "<b>1.</b> 玩家按下按鍵 → <b>AbilityInputHandler</b> 接收輸入\n" +
                "<b>2.</b> InputHandler 呼叫 → <b>ASC.TryActivateAbility(tag)</b>\n" +
                "<b>3.</b> ASC 檢查標籤條件 → <b>GameplayAbility.CanActivate()</b>\n" +
                "<b>4.</b> 能力啟動 → 執行邏輯（播放動畫、移動等）\n" +
                "<b>5.</b> 能力套用 → <b>GameplayEffect</b>（傷害/增益/減益）\n" +
                "<b>6.</b> 效果修改 → <b>GameplayAttribute</b>（生命值、攻擊力等）\n" +
                "<b>7.</b> 效果觸發 → <b>GameplayCue</b>（粒子特效、音效回饋）",
                flowStyle, GUILayout.Height(120));
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Tab 2: 能力瀏覽

        private void DrawAbilitiesTab()
        {
            DrawColoredHeader("能力瀏覽", COLOR_ABILITY);
            EditorGUILayout.Space(3);
            // 搜尋與篩選
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜尋:", GUILayout.Width(40));
            _abilitySearch = EditorGUILayout.TextField(_abilitySearch);
            string[] typeFilters = { "全部", "近戰", "遠程", "閃避", "其他" };
            _abilityTypeFilter = EditorGUILayout.Popup(_abilityTypeFilter, typeFilters, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);
            // 主面板：左列表 + 右詳情
            EditorGUILayout.BeginHorizontal();
            // 左側列表
            float listW = Mathf.Max(200, (position.width - SIDEBAR_WIDTH) * LIST_WIDTH_RATIO);
            EditorGUILayout.BeginVertical(GUILayout.Width(listW));
            _listScrollPos = EditorGUILayout.BeginScrollView(_listScrollPos, GUILayout.ExpandHeight(true));
            var filtered = GetFilteredAbilities();
            for (int i = 0; i < filtered.Count; i++)
            {
                var ability = filtered[i];
                if (ability == null) continue;
                bool isSelected = _selectedAbilityIndex == i;
                DrawAbilityListItem(ability, i, isSelected);
            }
            if (filtered.Count == 0)
                EditorGUILayout.HelpBox("沒有找到符合條件的能力", MessageType.Info);
            EditorGUILayout.EndScrollView();
            // 底部按鈕
            if (GUILayout.Button("開啟創建嚮導", GUILayout.Height(25)))
                GASCreationWizard.ShowWindow();
            EditorGUILayout.EndVertical();
            // 分隔線
            var sep = GUILayoutUtility.GetRect(1, 0, GUILayout.ExpandHeight(true), GUILayout.Width(1));
            EditorGUI.DrawRect(sep, new Color(0.3f, 0.3f, 0.3f));
            // 右側詳情
            EditorGUILayout.BeginVertical();
            _detailScrollPos = EditorGUILayout.BeginScrollView(_detailScrollPos, GUILayout.ExpandHeight(true));
            var filteredList = GetFilteredAbilities();
            if (_selectedAbilityIndex >= 0 && _selectedAbilityIndex < filteredList.Count)
                DrawAbilityDetail(filteredList[_selectedAbilityIndex]);
            else
                EditorGUILayout.HelpBox("← 請從左側列表選擇一個能力查看詳情", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAbilityListItem(GameplayAbility ability, int index, bool isSelected)
        {
            Color bg = isSelected ? new Color(0.3f, 0.5f, 0.7f, 0.3f) : Color.clear;
            var rect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, bg);
            // 類型色條
            var typeColor = GetAbilityTypeColor(ability);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), typeColor);
            // 名稱
            var nameRect = new Rect(rect.x + 8, rect.y + 2, rect.width - 12, 18);
            string displayName = !string.IsNullOrEmpty(ability.AbilityName) ? ability.AbilityName : ability.name;
            GUI.Label(nameRect, displayName, EditorStyles.boldLabel);
            // Tag
            var tagRect = new Rect(rect.x + 8, rect.y + 18, rect.width - 12, 14);
            string tagText = ability.AbilityTag.IsValid ? ability.AbilityTag.TagName : "(無標籤)";
            GUI.Label(tagRect, tagText, EditorStyles.miniLabel);
            // 點擊選取
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selectedAbilityIndex = index;
                _detailScrollPos = Vector2.zero;
                Event.current.Use();
                Repaint();
            }
        }

        private void DrawAbilityDetail(GameplayAbility ability)
        {
            if (ability == null) return;
            // 技能卡片預覽
            GASAbilityWorkshop.DrawSkillCard(ability);
            EditorGUILayout.Space(3);
            // 在工作坊中編輯按鈕
            GUI.backgroundColor = new Color(0.8f, 0.6f, 0.2f);
            if (GUILayout.Button("在工作坊中編輯", GUILayout.Height(25)))
            {
                var workshop = EditorWindow.GetWindow<GASAbilityWorkshop>();
                workshop.titleContent = new GUIContent("能力工作坊");
                workshop.LoadAbility(ability);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space(5);
            // 標題
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string displayName = !string.IsNullOrEmpty(ability.AbilityName) ? ability.AbilityName : ability.name;
            EditorGUILayout.LabelField(displayName, new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            EditorGUILayout.LabelField($"類型: {ability.GetType().Name}", EditorStyles.miniLabel);
            if (ability.AbilityTag.IsValid)
                EditorGUILayout.LabelField($"標籤: {ability.AbilityTag.TagName}");
            if (!string.IsNullOrEmpty(ability.Description))
                EditorGUILayout.LabelField(ability.Description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField($"等級: {ability.AbilityLevel}");
            EditorGUILayout.LabelField($"可重複啟動: {(ability.CanReactivateWhileActive ? "是" : "否")}");
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
            // 啟動條件
            DrawSectionHeader("啟動條件");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawTagContainerInfo("必要標籤 (ActivationRequiredTags)", ability.ActivationRequiredTags,
                "角色必須擁有這些標籤才能啟動此能力");
            DrawTagContainerInfo("阻擋標籤 (ActivationBlockedTags)", ability.ActivationBlockedTags,
                "角色擁有任一標籤時，此能力無法啟動");
            DrawTagContainerInfo("賦予標籤 (ActivationOwnedTags)", ability.ActivationOwnedTags,
                "能力啟動時，會將這些標籤賦予角色（結束時移除）");
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
            // 取消與阻擋
            DrawSectionHeader("取消與阻擋");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawTagContainerInfo("阻止其他能力 (BlockAbilitiesWithTags)", ability.BlockAbilitiesWithTags,
                "此能力啟動時，會阻止帶有這些標籤的能力啟動");
            DrawTagContainerInfo("取消其他能力 (CancelAbilitiesWithTags)", ability.CancelAbilitiesWithTags,
                "此能力啟動時，會取消正在執行的帶有這些標籤的能力");
            DrawTagContainerInfo("被取消條件 (CancelledByTags)", ability.CancelledByTags,
                "帶有這些標籤的能力啟動時，會取消此能力");
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
            // 冷卻與消耗
            DrawSectionHeader("冷卻與消耗");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawEffectLink("冷卻效果 (CooldownEffect)", ability.CooldownEffect,
                "能力結束後套用的冷卻效果，冷卻期間無法再次使用");
            DrawEffectLink("消耗效果 (CostEffect)", ability.CostEffect,
                "啟動能力時扣除的資源（如體力、魔力）");
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
            // 引用此能力的武器
            DrawSectionHeader("使用此能力的武器");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool foundWeapon = false;
            foreach (var weapon in _cachedWeapons)
            {
                if (weapon == null) continue;
                if (weapon.AttackAbility == ability || weapon.HeavyAttackAbility == ability ||
                    (GameplayAbility)weapon.DodgeAbility == ability || weapon.ParryAssistAbility == ability ||
                    weapon.DodgeAssistAbility == ability)
                {
                    foundWeapon = true;
                    DrawClickableAsset(weapon, $"{weapon.name}");
                }
            }
            if (!foundWeapon)
                EditorGUILayout.LabelField("（尚無武器引用此能力）", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
            // 操作按鈕
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("在 Inspector 中查看", GUILayout.Height(25)))
            {
                Selection.activeObject = ability;
                EditorGUIUtility.PingObject(ability);
            }
            EditorGUILayout.EndHorizontal();
        }

        private List<GameplayAbility> GetFilteredAbilities()
        {
            return _cachedAbilities.Where(a =>
            {
                if (a == null) return false;
                // 名稱搜尋
                if (!string.IsNullOrEmpty(_abilitySearch))
                {
                    string search = _abilitySearch.ToLower();
                    bool nameMatch = (!string.IsNullOrEmpty(a.AbilityName) && a.AbilityName.ToLower().Contains(search))
                                     || a.name.ToLower().Contains(search);
                    bool tagMatch = a.AbilityTag.IsValid && a.AbilityTag.TagName.ToLower().Contains(search);
                    if (!nameMatch && !tagMatch) return false;
                }
                // 類型篩選
                if (_abilityTypeFilter != 0)
                {
                    string typeName = a.GetType().Name;
                    return _abilityTypeFilter switch
                    {
                        1 => typeName.Contains("Melee"),
                        2 => typeName.Contains("Ranged"),
                        3 => typeName.Contains("Dodge"),
                        _ => !typeName.Contains("Melee") && !typeName.Contains("Ranged") && !typeName.Contains("Dodge")
                    };
                }
                return true;
            }).ToList();
        }

        private Color GetAbilityTypeColor(GameplayAbility ability)
        {
            string typeName = ability.GetType().Name;
            if (typeName.Contains("Melee")) return new Color(0.8f, 0.3f, 0.3f);
            if (typeName.Contains("Ranged")) return new Color(0.3f, 0.5f, 0.8f);
            if (typeName.Contains("Dodge")) return new Color(0.3f, 0.8f, 0.3f);
            if (typeName.Contains("Jump") || typeName.Contains("Glide")) return new Color(0.8f, 0.8f, 0.3f);
            return new Color(0.6f, 0.6f, 0.6f);
        }

        #endregion

        #region Tab 3: 效果百科

        private void DrawEffectsTab()
        {
            DrawColoredHeader("效果百科", COLOR_EFFECT);
            EditorGUILayout.Space(3);
            // 搜尋與篩選
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜尋:", GUILayout.Width(40));
            _effectSearch = EditorGUILayout.TextField(_effectSearch);
            string[] durationFilters = { "全部", "即時", "持續", "永久" };
            _effectDurationFilter = EditorGUILayout.Popup(_effectDurationFilter, durationFilters, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);
            // 主面板
            EditorGUILayout.BeginHorizontal();
            // 左側列表
            float listW = Mathf.Max(200, (position.width - SIDEBAR_WIDTH) * LIST_WIDTH_RATIO);
            EditorGUILayout.BeginVertical(GUILayout.Width(listW));
            _listScrollPos = EditorGUILayout.BeginScrollView(_listScrollPos, GUILayout.ExpandHeight(true));
            var filtered = GetFilteredEffects();
            for (int i = 0; i < filtered.Count; i++)
            {
                var effect = filtered[i];
                if (effect == null) continue;
                bool isSelected = _selectedEffectIndex == i;
                DrawEffectListItem(effect, i, isSelected);
            }
            if (filtered.Count == 0)
                EditorGUILayout.HelpBox("沒有找到符合條件的效果", MessageType.Info);
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("開啟創建嚮導", GUILayout.Height(25)))
                GASCreationWizard.ShowWindow();
            EditorGUILayout.EndVertical();
            // 分隔線
            var sep = GUILayoutUtility.GetRect(1, 0, GUILayout.ExpandHeight(true), GUILayout.Width(1));
            EditorGUI.DrawRect(sep, new Color(0.3f, 0.3f, 0.3f));
            // 右側詳情
            EditorGUILayout.BeginVertical();
            _detailScrollPos = EditorGUILayout.BeginScrollView(_detailScrollPos, GUILayout.ExpandHeight(true));
            var filteredList = GetFilteredEffects();
            if (_selectedEffectIndex >= 0 && _selectedEffectIndex < filteredList.Count)
                DrawEffectDetail(filteredList[_selectedEffectIndex]);
            else
                EditorGUILayout.HelpBox("← 請從左側列表選擇一個效果查看詳情", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEffectListItem(GameplayEffect effect, int index, bool isSelected)
        {
            Color bg = isSelected ? new Color(0.5f, 0.2f, 0.6f, 0.3f) : Color.clear;
            var rect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, bg);
            // 持續時間色條
            Color durationColor = GetDurationColor(effect.DurationPolicy);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), durationColor);
            // 名稱
            var nameRect = new Rect(rect.x + 8, rect.y + 2, rect.width - 60, 18);
            string displayName = !string.IsNullOrEmpty(effect.EffectName) ? effect.EffectName : effect.name;
            GUI.Label(nameRect, displayName, EditorStyles.boldLabel);
            // 持續時間標記
            string durationLabel = GetDurationLabel(effect.DurationPolicy);
            var badgeRect = new Rect(rect.xMax - 50, rect.y + 4, 46, 16);
            EditorGUI.DrawRect(badgeRect, durationColor);
            GUI.Label(badgeRect, durationLabel, new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            });
            // 修改器數量
            var modRect = new Rect(rect.x + 8, rect.y + 18, rect.width - 12, 14);
            GUI.Label(modRect, $"修改器: {effect.Modifiers.Count} 個", EditorStyles.miniLabel);
            // 點擊選取
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selectedEffectIndex = index;
                _detailScrollPos = Vector2.zero;
                Event.current.Use();
                Repaint();
            }
        }

        private void DrawEffectDetail(GameplayEffect effect)
        {
            if (effect == null) return;
            // 標題
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string displayName = !string.IsNullOrEmpty(effect.EffectName) ? effect.EffectName : effect.name;
            EditorGUILayout.LabelField(displayName, new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            if (effect.EffectTag.IsValid)
                EditorGUILayout.LabelField($"標籤: {effect.EffectTag.TagName}");
            if (!string.IsNullOrEmpty(effect.Description))
                EditorGUILayout.LabelField(effect.Description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
            // 持續時間策略
            DrawSectionHeader("持續時間策略");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Color dColor = GetDurationColor(effect.DurationPolicy);
            var dRect = GUILayoutUtility.GetRect(0, 4, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(dRect, dColor);
            EditorGUILayout.LabelField($"策略: {GetDurationLabel(effect.DurationPolicy)}", EditorStyles.boldLabel);
            string durationExplanation = effect.DurationPolicy switch
            {
                DurationPolicy.Instant => "即時效果 — 立刻套用一次就消失（如：傷害、治療）",
                DurationPolicy.Duration => $"持續效果 — 持續 {effect.Duration} 秒後自動結束（如：增益/減益）",
                DurationPolicy.Infinite => "永久效果 — 除非手動移除，否則不會消失（如：被動技能、裝備加成）",
                _ => ""
            };
            EditorGUILayout.HelpBox(durationExplanation, MessageType.Info);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
            // 週期策略
            if (effect.PeriodicPolicy != PeriodicPolicy.None)
            {
                DrawSectionHeader("週期策略");
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                string periodicExplanation = effect.PeriodicPolicy switch
                {
                    PeriodicPolicy.ExecuteOnInterval => $"每 {effect.Period} 秒執行一次效果",
                    PeriodicPolicy.ExecuteOnStart => "僅在效果開始時執行一次",
                    PeriodicPolicy.ExecuteOnStartAndInterval => $"開始時立刻執行一次，之後每 {effect.Period} 秒再執行",
                    _ => ""
                };
                EditorGUILayout.LabelField($"策略: {effect.PeriodicPolicy}", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(periodicExplanation, MessageType.Info);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(3);
            }
            // 堆疊策略
            if (effect.StackingPolicy != StackingPolicy.None)
            {
                DrawSectionHeader("堆疊策略");
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                string stackExplanation = effect.StackingPolicy switch
                {
                    StackingPolicy.StackCount => $"可堆疊最多 {effect.MaxStacks} 層，每層數值倍率 ×{effect.StackMagnitudeMultiplier}",
                    StackingPolicy.RefreshDuration => "重新套用時刷新持續時間（不堆疊數值）",
                    StackingPolicy.StackAndRefresh => $"堆疊層數（最多 {effect.MaxStacks} 層）並刷新持續時間",
                    _ => ""
                };
                EditorGUILayout.LabelField($"策略: {effect.StackingPolicy}", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(stackExplanation, MessageType.Info);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(3);
            }
            // 修改器
            if (effect.Modifiers.Count > 0)
            {
                DrawSectionHeader($"屬性修改器（{effect.Modifiers.Count} 個）");
                foreach (var mod in effect.Modifiers)
                {
                    DrawModifierCard(mod);
                }
                EditorGUILayout.Space(3);
            }
            // 標籤操作
            DrawSectionHeader("標籤操作");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawTagContainerInfo("賦予標籤 (GrantedTags)", effect.GrantedTags,
                "效果生效時賦予目標的標籤");
            DrawTagContainerInfo("結束移除 (RemoveTagsOnEnd)", effect.RemoveTagsOnEnd,
                "效果結束時移除的標籤");
            DrawTagContainerInfo("套用必要 (ApplicationRequiredTags)", effect.ApplicationRequiredTags,
                "目標必須有這些標籤，效果才能套用");
            DrawTagContainerInfo("套用阻擋 (ApplicationBlockedTags)", effect.ApplicationBlockedTags,
                "目標有這些標籤時，效果無法套用");
            DrawTagContainerInfo("持續必要 (OngoingRequiredTags)", effect.OngoingRequiredTags,
                "目標失去這些標籤時，效果自動移除");
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
            // Cue 標籤
            if (effect.CueTags != null && effect.CueTags.Count > 0)
            {
                DrawSectionHeader("觸發的 Cue");
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                foreach (var cue in effect.CueTags)
                {
                    if (cue.IsValid)
                        EditorGUILayout.LabelField($"  {cue.TagName}");
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(3);
            }
            // 移除其他效果
            if (effect.RemoveEffectsWithTags != null && effect.RemoveEffectsWithTags.Count() > 0)
            {
                DrawSectionHeader("移除其他效果");
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.HelpBox("套用此效果時，會移除目標身上帶有以下標籤的效果", MessageType.Info);
                foreach (var tag in effect.RemoveEffectsWithTags)
                    EditorGUILayout.LabelField($"  {tag.TagName}");
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(3);
            }
            // 引用此效果的能力
            DrawSectionHeader("引用此效果的能力");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool foundAbility = false;
            foreach (var ability in _cachedAbilities)
            {
                if (ability == null) continue;
                if (ability.CooldownEffect == effect || ability.CostEffect == effect)
                {
                    foundAbility = true;
                    string role = ability.CooldownEffect == effect ? "[冷卻]" : "[消耗]";
                    DrawClickableAsset(ability, $"{role} {ability.name}");
                }
            }
            if (!foundAbility)
                EditorGUILayout.LabelField("（尚無能力引用此效果）", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
            // 操作按鈕
            if (GUILayout.Button("在 Inspector 中查看", GUILayout.Height(25)))
            {
                Selection.activeObject = effect;
                EditorGUIUtility.PingObject(effect);
            }
        }

        private void DrawModifierCard(GameplayModifier mod)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            // 操作類型色條
            Color opColor = mod.OperationType switch
            {
                ModifierOperationType.Additive => new Color(0.3f, 0.7f, 0.3f),
                ModifierOperationType.Multiplicative => new Color(0.7f, 0.5f, 0.2f),
                ModifierOperationType.Override => new Color(0.7f, 0.3f, 0.3f),
                _ => Color.gray
            };
            var colorRect = GUILayoutUtility.GetRect(0, 3, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(colorRect, opColor);
            // 摘要
            string opSymbol = mod.OperationType switch
            {
                ModifierOperationType.Additive => "+",
                ModifierOperationType.Multiplicative => "×",
                ModifierOperationType.Override => "=",
                _ => "?"
            };
            EditorGUILayout.LabelField($"{mod.AttributeName}  {opSymbol}  {mod.Magnitude}",
                new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 });
            // 說明
            string opExplanation = mod.OperationType switch
            {
                ModifierOperationType.Additive => $"加法修改：{mod.AttributeName} 的值增加 {mod.Magnitude}",
                ModifierOperationType.Multiplicative => $"乘法修改：{mod.AttributeName} 的值乘以 {mod.Magnitude}",
                ModifierOperationType.Override => $"覆蓋修改：{mod.AttributeName} 的值強制設為 {mod.Magnitude}",
                _ => ""
            };
            EditorGUILayout.LabelField(opExplanation, EditorStyles.wordWrappedLabel);
            // 數值計算方式
            string magTypeExplanation = mod.MagnitudeType switch
            {
                ModifierMagnitudeType.ScalableFloat => "計算方式: 固定數值 × 曲線縮放",
                ModifierMagnitudeType.AttributeBased => $"計算方式: 基於{(mod.AttributeSource == ModifierAttributeSource.Source ? "施放者" : "目標")}的 {mod.SourceAttributeName} × {mod.AttributeCoefficient}",
                ModifierMagnitudeType.CustomCalculation => "計算方式: 自定義計算邏輯",
                _ => ""
            };
            EditorGUILayout.LabelField(magTypeExplanation, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private List<GameplayEffect> GetFilteredEffects()
        {
            return _cachedEffects.Where(e =>
            {
                if (e == null) return false;
                if (!string.IsNullOrEmpty(_effectSearch))
                {
                    string search = _effectSearch.ToLower();
                    bool nameMatch = (!string.IsNullOrEmpty(e.EffectName) && e.EffectName.ToLower().Contains(search))
                                     || e.name.ToLower().Contains(search);
                    bool tagMatch = e.EffectTag.IsValid && e.EffectTag.TagName.ToLower().Contains(search);
                    if (!nameMatch && !tagMatch) return false;
                }
                if (_effectDurationFilter != 0)
                {
                    DurationPolicy requiredPolicy = _effectDurationFilter switch
                    {
                        1 => DurationPolicy.Instant,
                        2 => DurationPolicy.Duration,
                        3 => DurationPolicy.Infinite,
                        _ => DurationPolicy.Instant
                    };
                    if (e.DurationPolicy != requiredPolicy) return false;
                }
                return true;
            }).ToList();
        }

        private Color GetDurationColor(DurationPolicy policy)
        {
            return policy switch
            {
                DurationPolicy.Instant => new Color(0.3f, 0.7f, 0.3f),
                DurationPolicy.Duration => new Color(0.8f, 0.7f, 0.2f),
                DurationPolicy.Infinite => new Color(0.8f, 0.3f, 0.3f),
                _ => Color.gray
            };
        }

        private string GetDurationLabel(DurationPolicy policy)
        {
            return policy switch
            {
                DurationPolicy.Instant => "即時",
                DurationPolicy.Duration => "持續",
                DurationPolicy.Infinite => "永久",
                _ => "?"
            };
        }

        #endregion

        #region Tab 4: 屬性參考

        private void DrawAttributesTab()
        {
            _contentScrollPos = EditorGUILayout.BeginScrollView(_contentScrollPos);
            DrawColoredHeader("屬性參考", COLOR_ATTRIBUTE);
            EditorGUILayout.Space(5);
            // 屬性總表
            DrawSectionHeader("CombatAttributeSet 屬性總表");
            string currentCategory = "";
            foreach (var attr in ATTRIBUTE_INFOS)
            {
                if (attr.Category != currentCategory)
                {
                    currentCategory = attr.Category;
                    EditorGUILayout.Space(3);
                    var catColor = currentCategory switch
                    {
                        "生命值" => new Color(0.8f, 0.3f, 0.3f),
                        "攻擊" => new Color(0.8f, 0.5f, 0.2f),
                        "防禦" => new Color(0.3f, 0.5f, 0.8f),
                        "移動" => new Color(0.3f, 0.8f, 0.3f),
                        "體力" => new Color(0.8f, 0.8f, 0.3f),
                        "魔力" => new Color(0.4f, 0.3f, 0.8f),
                        "支援點數" => COLOR_CUE,
                        _ => Color.gray
                    };
                    DrawColoredHeader($"  {currentCategory}", catColor);
                }
                DrawAttributeRow(attr);
            }
            EditorGUILayout.Space(10);
            // 傷害公式計算機
            DrawDamageCalculator();
            EditorGUILayout.Space(10);
            // 效果影響查詢
            DrawAttributeEffectQuery();
            EditorGUILayout.EndScrollView();
        }

        private void DrawAttributeRow(AttributeInfo attr)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(attr.Name, EditorStyles.boldLabel, GUILayout.Width(140));
            EditorGUILayout.LabelField($"預設: {attr.DefaultValue}", GUILayout.Width(100));
            EditorGUILayout.LabelField(attr.Description, EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrEmpty(attr.Related))
                EditorGUILayout.LabelField(attr.Related, EditorStyles.miniLabel, GUILayout.Width(160));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDamageCalculator()
        {
            DrawSectionHeader("傷害公式計算機");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(
                "公式: 實際傷害 = 原始傷害 × (1 - DamageReduction) × (100 / (100 + Defense))",
                MessageType.Info);
            EditorGUILayout.Space(5);
            _calcRawDamage = EditorGUILayout.FloatField("原始傷害", _calcRawDamage);
            _calcDamageReduction = EditorGUILayout.Slider("傷害減免 (DamageReduction)", _calcDamageReduction, 0f, 0.99f);
            _calcDefense = EditorGUILayout.FloatField("防禦力 (Defense)", _calcDefense);
            EditorGUILayout.Space(5);
            // 計算
            float afterReduction = _calcRawDamage * (1f - _calcDamageReduction);
            float defenseMultiplier = 100f / (100f + Mathf.Max(0f, _calcDefense));
            float finalDamage = afterReduction * defenseMultiplier;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var resultStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField($"實際傷害: {finalDamage:F1}", resultStyle, GUILayout.Height(30));
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField($"計算過程:", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"  1. 減免後傷害 = {_calcRawDamage} × (1 - {_calcDamageReduction:F2}) = {afterReduction:F1}");
            EditorGUILayout.LabelField($"  2. 防禦倍率 = 100 / (100 + {_calcDefense}) = {defenseMultiplier:F3}");
            EditorGUILayout.LabelField($"  3. 最終傷害 = {afterReduction:F1} × {defenseMultiplier:F3} = {finalDamage:F1}");
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        private void DrawAttributeEffectQuery()
        {
            DrawSectionHeader("效果影響查詢");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox("選擇一個屬性，查看哪些 GameplayEffect 會修改它", MessageType.Info);
            string[] attrNames = ATTRIBUTE_INFOS.Select(a => a.Name).ToArray();
            _selectedAttributeForQuery = EditorGUILayout.Popup("查詢屬性", _selectedAttributeForQuery, attrNames);
            string targetAttr = attrNames[_selectedAttributeForQuery];
            EditorGUILayout.Space(3);
            bool found = false;
            foreach (var effect in _cachedEffects)
            {
                if (effect == null) continue;
                foreach (var mod in effect.Modifiers)
                {
                    if (mod.AttributeName == targetAttr)
                    {
                        found = true;
                        string opSymbol = mod.OperationType switch
                        {
                            ModifierOperationType.Additive => "+",
                            ModifierOperationType.Multiplicative => "×",
                            ModifierOperationType.Override => "=",
                            _ => "?"
                        };
                        EditorGUILayout.BeginHorizontal();
                        DrawClickableAsset(effect, effect.EffectName ?? effect.name);
                        EditorGUILayout.LabelField($"  {opSymbol} {mod.Magnitude}", GUILayout.Width(80));
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            if (!found)
                EditorGUILayout.LabelField($"（目前沒有效果修改 {targetAttr}）", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Tab 5: 標籤地圖

        private void DrawTagsTab()
        {
            _contentScrollPos = EditorGUILayout.BeginScrollView(_contentScrollPos);
            DrawColoredHeader("標籤地圖", COLOR_TAG);
            EditorGUILayout.Space(5);
            // 搜尋
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜尋:", GUILayout.Width(40));
            _tagSearch = EditorGUILayout.TextField(_tagSearch);
            if (GUILayout.Button("開啟標籤瀏覽器", GUILayout.Width(120)))
                GameplayTagBrowser.ShowWindow();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
            // 標籤流程圖
            DrawTagFlowDiagram();
            EditorGUILayout.Space(5);
            // 分類群組
            var groups = GroupTagsByCategory();
            var categoryColors = new Dictionary<string, Color>
            {
                { "Ability", COLOR_ABILITY },
                { "State", new Color(0.7f, 0.4f, 0.2f) },
                { "Effect", COLOR_EFFECT },
                { "Cue", COLOR_CUE },
                { "Event", new Color(0.5f, 0.5f, 0.5f) },
                { "Assist", new Color(0.2f, 0.7f, 0.5f) },
            };
            foreach (var group in groups.OrderBy(g => g.Key))
            {
                var filtered = group.Value
                    .Where(t => string.IsNullOrEmpty(_tagSearch) || t.ToLower().Contains(_tagSearch.ToLower()))
                    .ToList();
                if (filtered.Count == 0) continue;
                Color headerColor = categoryColors.GetValueOrDefault(group.Key, Color.gray);
                DrawTagGroup(group.Key, filtered, headerColor);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTagFlowDiagram()
        {
            bool show = GetFoldout("tagFlow", true);
            show = EditorGUILayout.Foldout(show, "標籤運作流程", true, EditorStyles.foldoutHeader);
            SetFoldout("tagFlow", show);
            if (!show) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var flowStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = 11
            };
            EditorGUILayout.LabelField("<b>能力啟動時的標籤流程：</b>", flowStyle);
            EditorGUILayout.LabelField(
                "1. ASC 檢查 <b>ActivationRequiredTags</b> → 角色必須有這些標籤\n" +
                "2. ASC 檢查 <b>ActivationBlockedTags</b> → 角色不能有這些標籤\n" +
                "3. 通過檢查後，賦予 <b>ActivationOwnedTags</b>（如 State.Attacking）\n" +
                "4. 同時檢查 <b>BlockAbilitiesWithTags</b> → 阻止其他能力\n" +
                "5. 檢查 <b>CancelAbilitiesWithTags</b> → 取消正在執行的能力\n" +
                "6. 能力結束時，移除 ActivationOwnedTags",
                flowStyle, GUILayout.Height(100));
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("<b>效果套用時的標籤流程：</b>", flowStyle);
            EditorGUILayout.LabelField(
                "1. 檢查 <b>ApplicationRequiredTags</b> → 目標必須有\n" +
                "2. 檢查 <b>ApplicationBlockedTags</b> → 目標不能有\n" +
                "3. 通過後，賦予 <b>GrantedTags</b>\n" +
                "4. 持續期間檢查 <b>OngoingRequiredTags</b>（失去則移除效果）\n" +
                "5. 效果結束時，移除 <b>RemoveTagsOnEnd</b> 中的標籤",
                flowStyle, GUILayout.Height(85));
            EditorGUILayout.EndVertical();
        }

        private void DrawTagGroup(string category, List<string> tags, Color headerColor)
        {
            string foldoutKey = $"tagGroup_{category}";
            bool show = GetFoldout(foldoutKey, true);
            // 自訂色彩標頭
            var headerRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, headerColor);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontSize = 12
            };
            string arrow = show ? "▼" : "▶";
            GUI.Label(headerRect, $"  {arrow}  {category}（{tags.Count} 個標籤）", headerStyle);
            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                SetFoldout(foldoutKey, !show);
                Event.current.Use();
                Repaint();
            }
            if (!show) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            foreach (var tag in tags)
            {
                EditorGUILayout.BeginHorizontal();
                // 階層縮進
                int depth = tag.Split('.').Length - 1;
                GUILayout.Space(depth * 16);
                EditorGUILayout.LabelField(tag, GUILayout.ExpandWidth(true));
                // 複製按鈕
                if (GUILayout.Button("複製", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    EditorGUIUtility.systemCopyBuffer = tag;
                }
                // 引用計數
                int refCount = CountTagReferences(tag);
                EditorGUILayout.LabelField($"引用: {refCount}", EditorStyles.miniLabel, GUILayout.Width(55));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private Dictionary<string, List<string>> GroupTagsByCategory()
        {
            var groups = new Dictionary<string, List<string>>();
            foreach (var tag in _cachedTags)
            {
                string category = tag.Contains('.') ? tag.Split('.')[0] : tag;
                if (!groups.ContainsKey(category))
                    groups[category] = new List<string>();
                groups[category].Add(tag);
            }
            return groups;
        }

        private int CountTagReferences(string tagName)
        {
            int count = 0;
            foreach (var ability in _cachedAbilities)
            {
                if (ability == null) continue;
                if (ability.AbilityTag.IsValid && ability.AbilityTag.TagName == tagName) count++;
                count += CountInContainer(ability.ActivationRequiredTags, tagName);
                count += CountInContainer(ability.ActivationBlockedTags, tagName);
                count += CountInContainer(ability.ActivationOwnedTags, tagName);
                count += CountInContainer(ability.BlockAbilitiesWithTags, tagName);
                count += CountInContainer(ability.CancelAbilitiesWithTags, tagName);
                count += CountInContainer(ability.CancelledByTags, tagName);
            }
            foreach (var effect in _cachedEffects)
            {
                if (effect == null) continue;
                if (effect.EffectTag.IsValid && effect.EffectTag.TagName == tagName) count++;
                count += CountInContainer(effect.GrantedTags, tagName);
                count += CountInContainer(effect.RemoveTagsOnEnd, tagName);
                count += CountInContainer(effect.ApplicationRequiredTags, tagName);
                count += CountInContainer(effect.ApplicationBlockedTags, tagName);
                count += CountInContainer(effect.OngoingRequiredTags, tagName);
                count += CountInContainer(effect.RemoveEffectsWithTags, tagName);
            }
            return count;
        }

        private int CountInContainer(GameplayTagContainer container, string tagName)
        {
            if (container == null) return 0;
            foreach (var tag in container)
            {
                if (tag.TagName == tagName) return 1;
            }
            return 0;
        }

        #endregion

        #region Tab 6: 快速連結

        private void DrawQuickLinksTab()
        {
            _contentScrollPos = EditorGUILayout.BeginScrollView(_contentScrollPos);
            DrawColoredHeader("快速連結", new Color(0.5f, 0.5f, 0.5f));
            EditorGUILayout.Space(5);
            // 編輯器啟動按鈕
            DrawSectionHeader("GAS 編輯器工具");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            DrawLaunchButton("攻擊數據編輯器", "編輯近戰/遠程攻擊的時間軸、命中框、連招",
                GASAttackDataEditorWindow.ShowWindow);
            DrawLaunchButton("資產創建嚮導", "快速建立能力、效果、攻擊資料等資產", GASCreationWizard.ShowWindow);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawLaunchButton("能力工作坊", "一站式能力編輯與建立，含技能卡片預覽", GASAbilityWorkshop.ShowWindow);
            DrawLaunchButton("標籤瀏覽器", "樹狀結構管理所有 GameplayTag", GameplayTagBrowser.ShowWindow);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawLaunchButton("運行時調試器", "Play Mode 中即時檢視角色的標籤、能力、效果", GASDebugWindow.ShowWindow);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);
            // 常見問題
            DrawFAQSection();
            EditorGUILayout.Space(10);
            // 腳本速查表
            DrawScriptReference();
            EditorGUILayout.EndScrollView();
        }

        private void DrawLaunchButton(string title, string description, Action onClick)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(200), GUILayout.Height(70));
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("開啟", GUILayout.Height(22)))
                onClick?.Invoke();
            EditorGUILayout.EndVertical();
        }

        private void DrawFAQSection()
        {
            DrawSectionHeader("常見問題 (FAQ)");
            DrawFAQItem("faq_activate", "為什麼我的能力無法啟動？",
                "請依序檢查以下條件：\n" +
                "1. 角色是否有 AbilitySystemComponent？\n" +
                "2. 該能力是否已透過 GiveAbility() 授予角色？\n" +
                "3. ActivationRequiredTags — 角色是否擁有所有必要標籤？\n" +
                "4. ActivationBlockedTags — 角色是否擁有任何阻擋標籤（如 State.Dead、State.Stunned）？\n" +
                "5. CooldownEffect — 是否還在冷卻中？\n" +
                "6. CostEffect — 資源是否足夠（體力/魔力）？\n" +
                "7. 其他能力的 BlockAbilitiesWithTags 是否阻止了此能力？\n\n" +
                "提示：開啟「運行時調試器」可以即時查看角色的標籤和能力狀態。");
            DrawFAQItem("faq_buff", "如何添加一個新的 Buff 效果？",
                "步驟：\n" +
                "1. 在 Project 視窗右鍵 → Create → GAS → Gameplay Effect\n" +
                "2. 設定 EffectName 和 EffectTag（如 Effect.Buff.MyBuff）\n" +
                "3. DurationPolicy 設為 Duration，填入持續秒數\n" +
                "4. 在 Modifiers 中添加修改器（如 AttackPower + 10）\n" +
                "5. 可選：在 GrantedTags 添加狀態標籤\n" +
                "6. 透過能力或程式碼呼叫 ASC.ApplyEffectToSelf(buffEffect) 套用\n\n" +
                "也可以使用「資產創建嚮導」快速建立。");
            DrawFAQItem("faq_damage", "傷害公式如何運作？",
                "傷害計算分兩步：\n" +
                "1. 百分比減免：傷害 × (1 - DamageReduction)\n" +
                "2. 防禦減免：傷害 × (100 / (100 + Defense))\n\n" +
                "例如：100 傷害，DamageReduction=0.1，Defense=50\n" +
                "→ 100 × 0.9 × (100/150) = 60 傷害\n\n" +
                "前往「屬性參考」分頁可使用互動式計算機。");
            DrawFAQItem("faq_cooldown", "如何設定能力的冷卻時間？",
                "步驟：\n" +
                "1. 建立一個 GameplayEffect（冷卻效果）\n" +
                "2. DurationPolicy 設為 Duration\n" +
                "3. Duration 填入冷卻秒數（如 2.0）\n" +
                "4. EffectTag 設為唯一標籤（如 Effect.Cooldown.MyAbility）\n" +
                "5. 不需要添加任何 Modifier（冷卻效果只用來計時）\n" +
                "6. 將此效果拖到能力的 CooldownEffect 欄位\n\n" +
                "能力結束時會自動套用冷卻，冷卻期間 CanActivate() 回傳 false。");
            DrawFAQItem("faq_tag", "如何新增自定義的 Tag？",
                "方法一：使用標籤瀏覽器\n" +
                "1. 開啟 GAS → Tag Browser\n" +
                "2. 在底部輸入新標籤名稱（如 State.MyCustomState）\n" +
                "3. 點擊 Add 新增\n\n" +
                "方法二：在 GameplayTagLibrary 資產中手動添加\n" +
                "1. 在 Project 中找到 GameplayTagLibrary 資產\n" +
                "2. 在 Inspector 中的 Tag Definitions 列表添加新項目\n\n" +
                "方法三：直接在 Inspector 欄位中輸入\n" +
                "任何 GameplayTag 欄位都可以直接輸入標籤名稱（如 Custom.MyTag）。");
        }

        private void DrawFAQItem(string key, string question, string answer)
        {
            bool show = GetFoldout(key, false);
            show = EditorGUILayout.Foldout(show, question, true, EditorStyles.foldoutHeader);
            SetFoldout(key, show);
            if (show)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(answer, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawScriptReference()
        {
            bool show = GetFoldout("scriptRef", false);
            show = EditorGUILayout.Foldout(show, "GAS 腳本速查表", true, EditorStyles.foldoutHeader);
            SetFoldout("scriptRef", show);
            if (!show) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawScriptEntry("Core/AbilitySystemComponent", "核心元件，管理能力、效果、屬性");
            DrawScriptEntry("Core/GameplayAbility", "能力基類 (ScriptableObject)");
            DrawScriptEntry("Core/GameplayAbilitySpec", "能力的運行時實例");
            DrawScriptEntry("Core/AbilityTask", "非同步能力任務（延遲、動畫、輸入等待）");
            DrawScriptEntry("Core/GASDamageReceiver", "傷害接收元件");
            DrawScriptEntry("Effects/GameplayEffect", "效果定義 (ScriptableObject)");
            DrawScriptEntry("Effects/GameplayEffectSpec", "效果的運行時實例");
            DrawScriptEntry("Effects/Modifiers/GameplayModifier", "屬性修改器");
            DrawScriptEntry("Attributes/GameplayAttribute", "單一屬性（基值 + 修改器）");
            DrawScriptEntry("Attributes/AttributeSet", "屬性集合基類");
            DrawScriptEntry("Attributes/Sets/CombatAttributeSet", "戰鬥屬性集（生命、攻擊、防禦等）");
            DrawScriptEntry("Tags/GameplayTag", "階層式標籤結構");
            DrawScriptEntry("Tags/GameplayTagContainer", "標籤集合");
            DrawScriptEntry("Tags/GameplayTagLibrary", "標籤庫 (ScriptableObject)");
            DrawScriptEntry("Cues/GameplayCue", "視覺/音效回饋基類");
            DrawScriptEntry("Cues/GameplayCueManager", "Cue 管理器（全域單例）");
            DrawScriptEntry("Weapon/WeaponData", "武器資料 (ScriptableObject)");
            DrawScriptEntry("Weapon/WeaponManager", "武器切換管理器");
            DrawScriptEntry("Input/AbilityInputHandler", "輸入→能力橋接（含緩衝系統）");
            DrawScriptEntry("Targeting/TargetingSystem", "目標偵測 + 鎖定系統");
            DrawScriptEntry("Projectile/ProjectileBehaviour", "投射物行為（追蹤、穿透、區域傷害）");
            DrawScriptEntry("Assist/AssistTriggerDetector", "招架/迴避支援偵測");
            EditorGUILayout.EndVertical();
        }

        private void DrawScriptEntry(string path, string description)
        {
            EditorGUILayout.BeginHorizontal();
            // 嘗試 Ping 到腳本
            if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                string fullPath = $"Assets/Script/GAS/{path}.cs";
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fullPath);
                if (obj != null)
                    EditorGUIUtility.PingObject(obj);
            }
            EditorGUILayout.LabelField(path, EditorStyles.boldLabel, GUILayout.Width(280));
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 共用工具方法

        private void RefreshAllCaches()
        {
            _cachedAbilities = FindAllAssets<GameplayAbility>();
            _cachedEffects = FindAllAssets<GameplayEffect>();
            _cachedWeapons = FindAllAssets<WeaponData>();
            _cachedCues = FindAllAssets<GameplayCue>();
            _cachedTags = CollectAllTags();
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

        private List<string> CollectAllTags()
        {
            var tags = new HashSet<string>();
            // 從 TagLibrary 收集
            string[] guids = AssetDatabase.FindAssets("t:GameplayTagLibrary");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var library = AssetDatabase.LoadAssetAtPath<GameplayTagLibrary>(path);
                if (library != null && library.TagDefinitions != null)
                {
                    foreach (var def in library.TagDefinitions)
                    {
                        if (!string.IsNullOrEmpty(def.TagName))
                            tags.Add(def.TagName);
                    }
                }
            }
            // 添加預定義標籤
            AddPredefinedTags(tags);
            // 從能力和效果中收集
            foreach (var ability in _cachedAbilities)
            {
                if (ability == null) continue;
                if (ability.AbilityTag.IsValid) tags.Add(ability.AbilityTag.TagName);
                CollectFromContainer(tags, ability.ActivationRequiredTags);
                CollectFromContainer(tags, ability.ActivationBlockedTags);
                CollectFromContainer(tags, ability.ActivationOwnedTags);
                CollectFromContainer(tags, ability.BlockAbilitiesWithTags);
                CollectFromContainer(tags, ability.CancelAbilitiesWithTags);
                CollectFromContainer(tags, ability.CancelledByTags);
            }
            foreach (var effect in _cachedEffects)
            {
                if (effect == null) continue;
                if (effect.EffectTag.IsValid) tags.Add(effect.EffectTag.TagName);
                CollectFromContainer(tags, effect.GrantedTags);
                CollectFromContainer(tags, effect.RemoveTagsOnEnd);
                CollectFromContainer(tags, effect.ApplicationRequiredTags);
                CollectFromContainer(tags, effect.ApplicationBlockedTags);
                CollectFromContainer(tags, effect.OngoingRequiredTags);
                CollectFromContainer(tags, effect.RemoveEffectsWithTags);
                if (effect.CueTags != null)
                {
                    foreach (var cue in effect.CueTags)
                    {
                        if (cue.IsValid) tags.Add(cue.TagName);
                    }
                }
            }
            return tags.OrderBy(t => t).ToList();
        }

        private void AddPredefinedTags(HashSet<string> tags)
        {
            // 能力標籤
            tags.Add("Ability");
            tags.Add("Ability.Attack"); tags.Add("Ability.Attack.Melee"); tags.Add("Ability.Attack.Ranged");
            tags.Add("Ability.Attack.Light"); tags.Add("Ability.Attack.Heavy");
            tags.Add("Ability.Attack.Ranged.Light"); tags.Add("Ability.Attack.Ranged.Heavy");
            tags.Add("Ability.Movement"); tags.Add("Ability.Movement.Dodge"); tags.Add("Ability.Movement.Dash");
            tags.Add("Ability.Movement.Jump"); tags.Add("Ability.Movement.Glide");
            tags.Add("Ability.Skill"); tags.Add("Ability.Weapon.Switch");
            tags.Add("Ability.Assist"); tags.Add("Ability.Assist.Parry"); tags.Add("Ability.Assist.Dodge");
            // 狀態標籤
            tags.Add("State"); tags.Add("State.Attacking"); tags.Add("State.Dodging");
            tags.Add("State.Stunned"); tags.Add("State.Dead"); tags.Add("State.Invincible");
            tags.Add("State.CannotMove"); tags.Add("State.CannotAttack");
            tags.Add("State.Jumping"); tags.Add("State.Gliding");
            tags.Add("State.Switching"); tags.Add("State.AfterImage");
            tags.Add("State.Aiming"); tags.Add("State.Charging");
            tags.Add("State.Parrying"); tags.Add("State.BulletTime"); tags.Add("State.AssistWindow");
            // 效果標籤
            tags.Add("Effect"); tags.Add("Effect.Damage"); tags.Add("Effect.Damage.Physical");
            tags.Add("Effect.Damage.Magical"); tags.Add("Effect.Damage.Fire"); tags.Add("Effect.Damage.Ice");
            tags.Add("Effect.Buff"); tags.Add("Effect.Buff.AttackUp"); tags.Add("Effect.Buff.DefenseUp"); tags.Add("Effect.Buff.SpeedUp");
            tags.Add("Effect.Debuff"); tags.Add("Effect.Debuff.AttackDown"); tags.Add("Effect.Debuff.DefenseDown"); tags.Add("Effect.Debuff.Slow");
            // Cue / Event
            tags.Add("Cue"); tags.Add("Cue.HitImpact"); tags.Add("Cue.Attack"); tags.Add("Cue.Dodge");
            tags.Add("Event"); tags.Add("Event.Montage"); tags.Add("Event.HitWindow");
        }

        private void CollectFromContainer(HashSet<string> tags, GameplayTagContainer container)
        {
            if (container == null) return;
            foreach (var tag in container)
            {
                if (tag.IsValid) tags.Add(tag.TagName);
            }
        }

        private void DrawColoredHeader(string title, Color color)
        {
            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, color);
            GUI.Label(rect, $"  {title}", new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            });
        }

        private void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(title, new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            });
        }

        private void DrawExplanation(string title, string content)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(content, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawTagContainerInfo(string label, GameplayTagContainer container, string tooltip)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"  說明: {tooltip}", EditorStyles.wordWrappedMiniLabel);
            if (container != null && container.Count() > 0)
            {
                foreach (var tag in container)
                {
                    EditorGUILayout.LabelField($"    • {tag.TagName}");
                }
            }
            else
            {
                EditorGUILayout.LabelField("    （無）", EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(3);
        }

        private void DrawEffectLink(string label, GameplayEffect effect, string tooltip)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"  說明: {tooltip}", EditorStyles.wordWrappedMiniLabel);
            if (effect != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                DrawClickableAsset(effect, effect.EffectName ?? effect.name);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("    （未設定）", EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(3);
        }

        private void DrawClickableAsset(UnityEngine.Object asset, string label)
        {
            if (GUILayout.Button(label, EditorStyles.linkLabel))
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        private bool GetFoldout(string key, bool defaultValue)
        {
            if (!_foldouts.ContainsKey(key))
                _foldouts[key] = defaultValue;
            return _foldouts[key];
        }

        private void SetFoldout(string key, bool value)
        {
            _foldouts[key] = value;
        }

        #endregion
    }
}
#endif
