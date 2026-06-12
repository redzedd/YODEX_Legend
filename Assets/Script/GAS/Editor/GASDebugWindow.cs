#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace GAS.Editor
{
    /// <summary>
    /// GAS 運行時調試視窗
    /// 顯示選中角色的能力、效果、屬性和標籤狀態
    /// </summary>
    public class GASDebugWindow : EditorWindow
    {
        private AbilitySystemComponent _selectedASC;
        private VisualElement _contentContainer;
        private Label _statusLabel;
        
        // 各區塊的摺疊狀態
        private bool _showTags = true;
        private bool _showAbilities = true;
        private bool _showEffects = true;
        private bool _showAttributes = true;

        // 更新間隔
        private double _lastUpdateTime;
        private const double UPDATE_INTERVAL = 0.1; // 100ms

        [MenuItem("GAS/Debug Window")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<GASDebugWindow>();
            wnd.titleContent = new GUIContent("GAS Debug");
            wnd.minSize = new Vector2(400, 500);
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Selection.selectionChanged -= OnSelectionChanged;
        }

        public void CreateGUI()
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Script/GAS/Editor/GASEditorStyles.uss");
            if (styleSheet != null) rootVisualElement.styleSheets.Add(styleSheet);

            rootVisualElement.style.paddingTop = 5;
            rootVisualElement.style.paddingBottom = 5;
            rootVisualElement.style.paddingLeft = 5;
            rootVisualElement.style.paddingRight = 5;

            // 標題和狀態
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 10 } };
            header.Add(new Label("GAS Debug Monitor") { style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold } });
            _statusLabel = new Label("No ASC Selected") { style = { color = Color.gray } };
            header.Add(_statusLabel);
            rootVisualElement.Add(header);

            // ASC 選擇器
            var selectorRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 10 } };
            selectorRow.Add(new Label("Target:") { style = { width = 50 } });
            
            var selectButton = new Button(SelectASCFromScene) { text = "Select from Scene", style = { flexGrow = 1 } };
            selectorRow.Add(selectButton);
            
            var refreshButton = new Button(ForceRefresh) { text = "Refresh", style = { width = 60 } };
            selectorRow.Add(refreshButton);
            rootVisualElement.Add(selectorRow);

            // 內容滾動區
            var scrollView = new ScrollView();
            scrollView.style.flexGrow = 1;
            _contentContainer = new VisualElement();
            scrollView.Add(_contentContainer);
            rootVisualElement.Add(scrollView);

            // 底部工具欄
            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 10, paddingTop = 5, borderTopWidth = 1, borderTopColor = Color.gray } };
            
            var applyEffectBtn = new Button(ShowApplyEffectMenu) { text = "Apply Effect", style = { flexGrow = 1 } };
            toolbar.Add(applyEffectBtn);
            
            var activateAbilityBtn = new Button(ShowActivateAbilityMenu) { text = "Activate Ability", style = { flexGrow = 1 } };
            toolbar.Add(activateAbilityBtn);
            
            var addTagBtn = new Button(ShowAddTagMenu) { text = "Add Tag", style = { flexGrow = 1 } };
            toolbar.Add(addTagBtn);
            
            rootVisualElement.Add(toolbar);

            OnSelectionChanged();
        }

        private void OnSelectionChanged()
        {
            // 檢查選中的物件
            if (Selection.activeGameObject != null)
            {
                var asc = Selection.activeGameObject.GetComponent<AbilitySystemComponent>();
                if (asc != null)
                {
                    _selectedASC = asc;
                }
            }
            
            ForceRefresh();
        }

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying) return;
            if (_selectedASC == null) return;

            // 限制更新頻率
            if (EditorApplication.timeSinceStartup - _lastUpdateTime < UPDATE_INTERVAL)
                return;

            _lastUpdateTime = EditorApplication.timeSinceStartup;
            RefreshContent();
        }

        private void ForceRefresh()
        {
            RefreshContent();
            Repaint();
        }

        private void RefreshContent()
        {
            if (_contentContainer == null) return;
            _contentContainer.Clear();

            if (_selectedASC == null)
            {
                _statusLabel.text = "No ASC Selected";
                _statusLabel.style.color = Color.gray;
                _contentContainer.Add(new Label("Select a GameObject with AbilitySystemComponent to debug.") { style = { color = new Color(0.7f, 0.7f, 0.7f) } });
                return;
            }

            _statusLabel.text = _selectedASC.gameObject.name;
            _statusLabel.style.color = Application.isPlaying ? Color.green : Color.yellow;

            if (!Application.isPlaying)
            {
                _contentContainer.Add(new Label("Enter Play Mode to see runtime data.") { style = { color = Color.yellow, marginBottom = 10 } });
            }

            // 標籤區塊
            DrawTagsSection();

            // 能力區塊
            DrawAbilitiesSection();

            // 效果區塊
            DrawEffectsSection();

            // 屬性區塊
            DrawAttributesSection();
        }

        #region Sections

        private void DrawTagsSection()
        {
            var section = CreateSection("Owned Tags", _showTags, new Color(0.2f, 0.6f, 0.8f), expanded => _showTags = expanded);
            if (!_showTags) return;

            var tagsContainer = section.Q<VisualElement>("content");

            if (_selectedASC.OwnedTags == null || _selectedASC.OwnedTags.Count == 0)
            {
                tagsContainer.Add(new Label("No tags") { style = { color = new Color(0.5f, 0.5f, 0.5f), unityFontStyleAndWeight = FontStyle.Italic } });
                return;
            }

            foreach (var tag in _selectedASC.OwnedTags)
            {
                var tagRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, paddingLeft = 5, paddingRight = 5, paddingTop = 2, paddingBottom = 2 } };
                tagRow.AddToClassList("debug-item");
                
                tagRow.Add(new Label(tag.TagName) { style = { flexGrow = 1 } });
                
                if (Application.isPlaying)
                {
                    var removeBtn = new Button(() => RemoveTag(tag)) { text = "X", style = { width = 20, height = 18, fontSize = 10 } };
                    tagRow.Add(removeBtn);
                }
                
                tagsContainer.Add(tagRow);
            }
        }

        private void DrawAbilitiesSection()
        {
            var section = CreateSection("Abilities", _showAbilities, new Color(0.8f, 0.6f, 0.2f), expanded => _showAbilities = expanded);
            if (!_showAbilities) return;

            var abilitiesContainer = section.Q<VisualElement>("content");
            var abilities = _selectedASC.GetAllAbilities();

            if (abilities == null || abilities.Count == 0)
            {
                abilitiesContainer.Add(new Label("No abilities granted") { style = { color = new Color(0.5f, 0.5f, 0.5f), unityFontStyleAndWeight = FontStyle.Italic } });
                return;
            }

            foreach (var spec in abilities)
            {
                var abilityRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 5, paddingRight = 5, paddingTop = 3, paddingBottom = 3 } };
                abilityRow.AddToClassList("debug-item");

                // 狀態指示
                Color statusColor = spec.IsActive ? Color.green : (IsOnCooldown(spec) ? Color.yellow : Color.gray);
                var statusDot = new VisualElement { style = { width = 8, height = 8, backgroundColor = statusColor, borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4, marginRight = 5 } };
                abilityRow.Add(statusDot);

                // 名稱
                var nameLabel = new Label(spec.AbilityDef.AbilityName) { style = { flexGrow = 1, unityFontStyleAndWeight = spec.IsActive ? FontStyle.Bold : FontStyle.Normal } };
                abilityRow.Add(nameLabel);

                // 狀態文字
                string statusText = spec.IsActive ? "Active" : (IsOnCooldown(spec) ? "Cooldown" : "Ready");
                var statusLabel = new Label(statusText) { style = { width = 60, color = statusColor, unityTextAlign = TextAnchor.MiddleRight } };
                abilityRow.Add(statusLabel);

                // 操作按鈕
                if (Application.isPlaying)
                {
                    if (spec.IsActive)
                    {
                        var cancelBtn = new Button(() => CancelAbility(spec)) { text = "Cancel", style = { width = 50, height = 18, fontSize = 10 } };
                        abilityRow.Add(cancelBtn);
                    }
                    else if (!IsOnCooldown(spec))
                    {
                        var activateBtn = new Button(() => ActivateAbility(spec)) { text = "Activate", style = { width = 50, height = 18, fontSize = 10 } };
                        abilityRow.Add(activateBtn);
                    }
                }

                abilitiesContainer.Add(abilityRow);

                // 展開詳細資訊
                var details = new VisualElement { style = { marginLeft = 20, marginTop = 2, display = DisplayStyle.None } };
                details.Add(new Label($"Tag: {spec.AbilityDef.AbilityTag}") { style = { fontSize = 10, color = Color.gray } });
                details.Add(new Label($"Level: {spec.Level}") { style = { fontSize = 10, color = Color.gray } });
                abilitiesContainer.Add(details);
            }
        }

        private void DrawEffectsSection()
        {
            var section = CreateSection("Active Effects", _showEffects, new Color(0.6f, 0.2f, 0.8f), expanded => _showEffects = expanded);
            if (!_showEffects) return;

            var effectsContainer = section.Q<VisualElement>("content");
            var effects = _selectedASC.ActiveEffects?.GetAllEffects();

            if (effects == null || !effects.Any())
            {
                effectsContainer.Add(new Label("No active effects") { style = { color = new Color(0.5f, 0.5f, 0.5f), unityFontStyleAndWeight = FontStyle.Italic } });
                return;
            }

            foreach (var spec in effects)
            {
                var effectRow = new VisualElement { style = { flexDirection = FlexDirection.Column, paddingLeft = 5, paddingRight = 5, paddingTop = 3, paddingBottom = 3 } };
                effectRow.AddToClassList("debug-item");

                // 標題行
                var headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };
                
                var nameLabel = new Label(spec.EffectDef.EffectName) { style = { unityFontStyleAndWeight = FontStyle.Bold } };
                headerRow.Add(nameLabel);

                // 持續時間
                if (spec.EffectDef.HasDuration)
                {
                    float remaining = spec.RemainingDuration;
                    float total = spec.EffectDef.Duration;
                    var durationLabel = new Label($"{remaining:F1}s / {total:F1}s") { style = { color = Color.cyan } };
                    headerRow.Add(durationLabel);
                }
                else if (spec.EffectDef.IsInfinite)
                {
                    headerRow.Add(new Label("Infinite") { style = { color = Color.yellow } });
                }

                effectRow.Add(headerRow);

                // 堆疊數
                if (spec.StackCount > 1)
                {
                    effectRow.Add(new Label($"Stacks: {spec.StackCount}") { style = { fontSize = 10, color = Color.gray } });
                }

                // 移除按鈕
                if (Application.isPlaying)
                {
                    var removeBtn = new Button(() => RemoveEffect(spec)) { text = "Remove", style = { width = 50, height = 16, fontSize = 9, alignSelf = Align.FlexEnd } };
                    effectRow.Add(removeBtn);
                }

                effectsContainer.Add(effectRow);
            }
        }

        private void DrawAttributesSection()
        {
            var section = CreateSection("Attributes", _showAttributes, new Color(0.2f, 0.8f, 0.4f), expanded => _showAttributes = expanded);
            if (!_showAttributes) return;

            var attributesContainer = section.Q<VisualElement>("content");
            var attrSet = _selectedASC.GetAttributeSet();

            if (attrSet == null)
            {
                attributesContainer.Add(new Label("No attribute set") { style = { color = new Color(0.5f, 0.5f, 0.5f), unityFontStyleAndWeight = FontStyle.Italic } });
                return;
            }

            var attributes = attrSet.GetAllAttributes();
            foreach (var attr in attributes)
            {
                var attrRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 5, paddingRight = 5, paddingTop = 2, paddingBottom = 2 } };
                attrRow.AddToClassList("debug-item");

                // 名稱
                var nameLabel = new Label(attr.AttributeName) { style = { width = 120 } };
                attrRow.Add(nameLabel);

                // 基礎值
                var baseLabel = new Label($"Base: {attr.BaseValue:F1}") { style = { width = 80, color = Color.gray } };
                attrRow.Add(baseLabel);

                // 當前值
                Color valueColor = attr.CurrentValue < attr.BaseValue ? Color.red : (attr.CurrentValue > attr.BaseValue ? Color.green : Color.white);
                var currentLabel = new Label($"Current: {attr.CurrentValue:F1}") { style = { flexGrow = 1, color = valueColor } };
                attrRow.Add(currentLabel);

                // 進度條 (如果有最大值)
                if (attr.AttributeName == CombatAttributes.Health || attr.AttributeName == CombatAttributes.Stamina)
                {
                    float maxValue = attr.AttributeName == CombatAttributes.Health ? 
                        (attrSet.GetAttribute(CombatAttributes.MaxHealth)?.CurrentValue ?? 100f) :
                        (attrSet.GetAttribute(CombatAttributes.MaxStamina)?.CurrentValue ?? 100f);
                    
                    float percent = maxValue > 0 ? attr.CurrentValue / maxValue : 0;
                    var progressBg = new VisualElement { style = { width = 60, height = 6, backgroundColor = new Color(0.2f, 0.2f, 0.2f), marginLeft = 5 } };
                    var progressFill = new VisualElement { style = { width = percent * 60, height = 6, backgroundColor = valueColor } };
                    progressBg.Add(progressFill);
                    attrRow.Add(progressBg);
                }

                attributesContainer.Add(attrRow);
            }
        }

        private VisualElement CreateSection(string title, bool isExpanded, Color headerColor, System.Action<bool> onExpandedChanged)
        {
            var section = new VisualElement { style = { marginBottom = 10, backgroundColor = new Color(0.15f, 0.15f, 0.15f), borderTopLeftRadius = 5, borderTopRightRadius = 5, borderBottomLeftRadius = 5, borderBottomRightRadius = 5 } };
            section.AddToClassList("debug-section");

            // 標題欄
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, backgroundColor = headerColor * 0.5f, paddingLeft = 10, paddingRight = 10, paddingTop = 5, paddingBottom = 5, borderTopLeftRadius = 5, borderTopRightRadius = 5 } };
            
            var foldout = new Label(isExpanded ? "▼" : "▶") { style = { width = 15 } };
            header.Add(foldout);
            
            header.Add(new Label(title) { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });

            header.RegisterCallback<MouseDownEvent>(evt =>
            {
                bool newExpanded = !isExpanded;
                onExpandedChanged?.Invoke(newExpanded);
                foldout.text = newExpanded ? "▼" : "▶";
                var content = section.Q<VisualElement>("content");
                if (content != null)
                    content.style.display = newExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            });

            section.Add(header);

            // 內容區
            var content = new VisualElement { name = "content", style = { paddingLeft = 5, paddingRight = 5, paddingTop = 5, paddingBottom = 5, display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None } };
            section.Add(content);

            _contentContainer.Add(section);
            return section;
        }

        #endregion

        #region Actions

        private void SelectASCFromScene()
        {
            var menu = new GenericMenu();
            
            var allASCs = Object.FindObjectsByType<AbilitySystemComponent>(FindObjectsSortMode.None);
            
            if (allASCs.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No AbilitySystemComponent in scene"));
            }
            else
            {
                foreach (var asc in allASCs)
                {
                    string name = asc.gameObject.name;
                    menu.AddItem(new GUIContent(name), _selectedASC == asc, () =>
                    {
                        _selectedASC = asc;
                        Selection.activeGameObject = asc.gameObject;
                        ForceRefresh();
                    });
                }
            }
            
            menu.ShowAsContext();
        }

        private void ShowApplyEffectMenu()
        {
            if (_selectedASC == null || !Application.isPlaying) return;

            var menu = new GenericMenu();
            
            // 查找所有 GameplayEffect 資產
            string[] guids = AssetDatabase.FindAssets("t:GameplayEffect");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var effect = AssetDatabase.LoadAssetAtPath<GameplayEffect>(path);
                if (effect != null)
                {
                    string name = effect.EffectName;
                    menu.AddItem(new GUIContent(name), false, () =>
                    {
                        _selectedASC.ApplyEffectToSelf(effect);
                    });
                }
            }
            
            if (guids.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No GameplayEffect assets found"));
            }
            
            menu.ShowAsContext();
        }

        private void ShowActivateAbilityMenu()
        {
            if (_selectedASC == null || !Application.isPlaying) return;

            var menu = new GenericMenu();
            
            foreach (var spec in _selectedASC.GetAllAbilities())
            {
                string name = spec.AbilityDef.AbilityName;
                bool isActive = spec.IsActive;
                
                if (!isActive && !IsOnCooldown(spec))
                {
                    menu.AddItem(new GUIContent(name), false, () => ActivateAbility(spec));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent($"{name} ({(isActive ? "Active" : "Cooldown")})"));
                }
            }
            
            menu.ShowAsContext();
        }

        private void ShowAddTagMenu()
        {
            if (_selectedASC == null || !Application.isPlaying) return;

            var menu = new GenericMenu();
            
            var commonTags = new[]
            {
                "State.Attacking", "State.Dodging", "State.Stunned", "State.Invincible",
                "State.CannotMove", "State.CannotAttack", "State.Dead"
            };
            
            foreach (var tag in commonTags)
            {
                bool hasTag = _selectedASC.OwnedTags.HasTagExact(new GameplayTag(tag));
                menu.AddItem(new GUIContent(tag.Replace(".", "/")), hasTag, () =>
                {
                    if (hasTag)
                        _selectedASC.OwnedTags.RemoveTag(new GameplayTag(tag));
                    else
                        _selectedASC.OwnedTags.AddTag(new GameplayTag(tag));
                    ForceRefresh();
                });
            }
            
            menu.ShowAsContext();
        }

        private void RemoveTag(GameplayTag tag)
        {
            if (_selectedASC == null || !Application.isPlaying) return;
            _selectedASC.OwnedTags.RemoveTag(tag);
            ForceRefresh();
        }

        private void ActivateAbility(GameplayAbilitySpec spec)
        {
            if (_selectedASC == null || !Application.isPlaying) return;
            spec.TryActivate();
            ForceRefresh();
        }

        private void CancelAbility(GameplayAbilitySpec spec)
        {
            if (_selectedASC == null || !Application.isPlaying) return;
            spec.CancelAbility();
            ForceRefresh();
        }

        private void RemoveEffect(GameplayEffectSpec spec)
        {
            if (_selectedASC == null || !Application.isPlaying) return;
            _selectedASC.ActiveEffects.RemoveEffect(spec);
            ForceRefresh();
        }

        private bool IsOnCooldown(GameplayAbilitySpec spec)
        {
            if (spec?.AbilityDef?.CooldownEffect == null) return false;
            return _selectedASC.ActiveEffects.HasEffectWithTag(spec.AbilityDef.CooldownEffect.EffectTag);
        }

        #endregion
    }
}
#endif
