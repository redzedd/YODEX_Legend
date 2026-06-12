#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace GAS.Editor
{
    /// <summary>
    /// GAS 資產創建嚮導
    /// 提供快速創建能力和效果的模板系統
    /// </summary>
    public class GASCreationWizard : EditorWindow
    {
        private enum WizardPage
        {
            SelectType,
            ConfigureAsset,
            Review
        }

        private enum AssetType
        {
            None,
            MeleeAbility,
            RangedAbility,
            DodgeAbility,
            BuffEffect,
            DebuffEffect,
            DamageEffect,
            HealEffect,
            CooldownEffect,
            MeleeAttackData
        }

        private WizardPage _currentPage = WizardPage.SelectType;
        private AssetType _selectedType = AssetType.None;
        
        // 通用設定
        private string _assetName = "";
        private string _outputPath = "Assets/Script/GAS/Generated";
        private string _tagName = "";
        private string _description = "";

        // Ability 設定
        private float _cooldownDuration = 1f;
        private bool _createCooldownEffect = true;
        private float _staminaCost = 0f;
        private bool _createCostEffect = false;

        // Effect 設定
        private DurationPolicy _durationPolicy = DurationPolicy.Instant;
        private float _effectDuration = 5f;
        private float _effectMagnitude = 10f;
        private string _targetAttribute = CombatAttributes.Health;
        private ModifierOperationType _operationType = ModifierOperationType.Additive;

        // Attack Data 設定
        private AnimationClip _attackAnimation;
        private float _allowInputTime = 0.3f;
        private float _comboResetTime = 0.8f;
        private int _hitWindowCount = 1;
        private float _baseDamage = 10f;

        private Vector2 _scrollPos;

        [MenuItem("GAS/Creation Wizard")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<GASCreationWizard>();
            wnd.titleContent = new GUIContent("GAS Creation Wizard");
            wnd.minSize = new Vector2(500, 600);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            // 頁面標題
            DrawPageHeader();

            EditorGUILayout.Space(10);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_currentPage)
            {
                case WizardPage.SelectType:
                    DrawSelectTypePage();
                    break;
                case WizardPage.ConfigureAsset:
                    DrawConfigurePage();
                    break;
                case WizardPage.Review:
                    DrawReviewPage();
                    break;
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            // 導航按鈕
            DrawNavigationButtons();
        }

        private void DrawPageHeader()
        {
            EditorGUILayout.BeginHorizontal();
            
            // 步驟指示器
            DrawStepIndicator(1, "Select Type", _currentPage == WizardPage.SelectType);
            DrawStepIndicator(2, "Configure", _currentPage == WizardPage.ConfigureAsset);
            DrawStepIndicator(3, "Review", _currentPage == WizardPage.Review);
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStepIndicator(int step, string label, bool isActive)
        {
            Color bgColor = isActive ? new Color(0.2f, 0.6f, 0.8f) : new Color(0.3f, 0.3f, 0.3f);
            
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width / 3 - 10));
            
            Rect rect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, bgColor);
            
            GUI.Label(rect, $"{step}. {label}", new GUIStyle(EditorStyles.label) 
            { 
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = Color.white }
            });
            
            EditorGUILayout.EndVertical();
        }

        #region Page 1: Select Type

        private void DrawSelectTypePage()
        {
            EditorGUILayout.LabelField("What would you like to create?", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // Abilities
            EditorGUILayout.LabelField("Abilities", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            DrawTypeButton(AssetType.MeleeAbility, "Melee Attack", "Close-range attack ability");
            DrawTypeButton(AssetType.RangedAbility, "Ranged Attack", "Projectile-based attack");
            DrawTypeButton(AssetType.DodgeAbility, "Dodge/Dash", "Evasive movement ability");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Effects
            EditorGUILayout.LabelField("Effects", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            DrawTypeButton(AssetType.DamageEffect, "Damage", "Instant damage effect");
            DrawTypeButton(AssetType.HealEffect, "Heal", "Health restoration");
            DrawTypeButton(AssetType.CooldownEffect, "Cooldown", "Ability cooldown timer");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawTypeButton(AssetType.BuffEffect, "Buff", "Positive stat modifier");
            DrawTypeButton(AssetType.DebuffEffect, "Debuff", "Negative stat modifier");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Data Assets
            EditorGUILayout.LabelField("Data Assets", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            DrawTypeButton(AssetType.MeleeAttackData, "Attack Data", "Melee attack configuration");
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTypeButton(AssetType type, string label, string tooltip)
        {
            bool isSelected = _selectedType == type;
            
            GUIStyle style = new GUIStyle(GUI.skin.button)
            {
                fixedHeight = 60,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal
            };

            Color originalBg = GUI.backgroundColor;
            if (isSelected)
                GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);

            if (GUILayout.Button(new GUIContent(label, tooltip), style, GUILayout.Width(150)))
            {
                _selectedType = type;
                AutoGenerateName();
            }

            GUI.backgroundColor = originalBg;
        }

        private void AutoGenerateName()
        {
            string prefix = _selectedType switch
            {
                AssetType.MeleeAbility => "GA_MeleeAttack",
                AssetType.RangedAbility => "GA_RangedAttack",
                AssetType.DodgeAbility => "GA_Dodge",
                AssetType.BuffEffect => "GE_Buff",
                AssetType.DebuffEffect => "GE_Debuff",
                AssetType.DamageEffect => "GE_Damage",
                AssetType.HealEffect => "GE_Heal",
                AssetType.CooldownEffect => "GE_Cooldown",
                AssetType.MeleeAttackData => "MAD_Attack",
                _ => "New"
            };

            _assetName = prefix + "_" + System.DateTime.Now.ToString("HHmmss");
            _tagName = GetDefaultTag();
        }

        private string GetDefaultTag()
        {
            return _selectedType switch
            {
                AssetType.MeleeAbility => "Ability.Attack.Melee",
                AssetType.RangedAbility => "Ability.Attack.Ranged",
                AssetType.DodgeAbility => "Ability.Movement.Dodge",
                AssetType.BuffEffect => "Effect.Buff",
                AssetType.DebuffEffect => "Effect.Debuff",
                AssetType.DamageEffect => "Effect.Damage",
                AssetType.HealEffect => "Effect.Heal",
                AssetType.CooldownEffect => "Effect.Cooldown",
                _ => ""
            };
        }

        #endregion

        #region Page 2: Configure

        private void DrawConfigurePage()
        {
            // 通用設定
            EditorGUILayout.LabelField("Basic Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            _assetName = EditorGUILayout.TextField("Asset Name", _assetName);
            _tagName = EditorGUILayout.TextField("Tag", _tagName);
            _description = EditorGUILayout.TextField("Description", _description);

            EditorGUILayout.BeginHorizontal();
            _outputPath = EditorGUILayout.TextField("Output Path", _outputPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                        _outputPath = "Assets" + path.Substring(Application.dataPath.Length);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 類型特定設定
            switch (_selectedType)
            {
                case AssetType.MeleeAbility:
                case AssetType.RangedAbility:
                case AssetType.DodgeAbility:
                    DrawAbilityConfig();
                    break;

                case AssetType.BuffEffect:
                case AssetType.DebuffEffect:
                case AssetType.DamageEffect:
                case AssetType.HealEffect:
                case AssetType.CooldownEffect:
                    DrawEffectConfig();
                    break;

                case AssetType.MeleeAttackData:
                    DrawAttackDataConfig();
                    break;
            }
        }

        private void DrawAbilityConfig()
        {
            EditorGUILayout.LabelField("Ability Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 冷卻
            _createCooldownEffect = EditorGUILayout.Toggle("Create Cooldown Effect", _createCooldownEffect);
            if (_createCooldownEffect)
            {
                EditorGUI.indentLevel++;
                _cooldownDuration = EditorGUILayout.FloatField("Cooldown Duration", _cooldownDuration);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5);

            // 消耗
            _createCostEffect = EditorGUILayout.Toggle("Create Cost Effect", _createCostEffect);
            if (_createCostEffect)
            {
                EditorGUI.indentLevel++;
                _staminaCost = EditorGUILayout.FloatField("Stamina Cost", _staminaCost);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEffectConfig()
        {
            EditorGUILayout.LabelField("Effect Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _durationPolicy = (DurationPolicy)EditorGUILayout.EnumPopup("Duration Policy", _durationPolicy);

            if (_durationPolicy == DurationPolicy.Duration)
            {
                _effectDuration = EditorGUILayout.FloatField("Duration (seconds)", _effectDuration);
            }

            EditorGUILayout.Space(5);

            // 修改器設定
            EditorGUILayout.LabelField("Modifier", EditorStyles.miniBoldLabel);
            
            // 屬性下拉選單
            var attrOptions = new List<string>
            {
                CombatAttributes.Health, CombatAttributes.MaxHealth,
                CombatAttributes.AttackPower, CombatAttributes.Defense,
                CombatAttributes.MoveSpeed, CombatAttributes.Stamina,
                CombatAttributes.IncomingDamage
            };
            int attrIndex = attrOptions.IndexOf(_targetAttribute);
            if (attrIndex < 0) attrIndex = 0;
            attrIndex = EditorGUILayout.Popup("Target Attribute", attrIndex, attrOptions.ToArray());
            _targetAttribute = attrOptions[attrIndex];

            _operationType = (ModifierOperationType)EditorGUILayout.EnumPopup("Operation", _operationType);
            _effectMagnitude = EditorGUILayout.FloatField("Magnitude", _effectMagnitude);

            EditorGUILayout.EndVertical();
        }

        private void DrawAttackDataConfig()
        {
            EditorGUILayout.LabelField("Attack Data Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _attackAnimation = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", _attackAnimation, typeof(AnimationClip), false);
            _allowInputTime = EditorGUILayout.FloatField("Allow Input Time", _allowInputTime);
            _comboResetTime = EditorGUILayout.FloatField("Combo Reset Time", _comboResetTime);

            EditorGUILayout.Space(5);

            _hitWindowCount = EditorGUILayout.IntSlider("Hit Window Count", _hitWindowCount, 1, 5);
            _baseDamage = EditorGUILayout.FloatField("Base Damage", _baseDamage);

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Page 3: Review

        private void DrawReviewPage()
        {
            EditorGUILayout.LabelField("Review & Create", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("Summary", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"Type: {_selectedType}");
            EditorGUILayout.LabelField($"Name: {_assetName}");
            EditorGUILayout.LabelField($"Tag: {_tagName}");
            EditorGUILayout.LabelField($"Path: {_outputPath}/{_assetName}.asset");

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 將要創建的檔案列表
            EditorGUILayout.LabelField("Files to Create:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawFilePreview($"{_assetName}.asset", GetMainAssetDescription());

            // 附帶的效果
            if (IsAbilityType() && _createCooldownEffect)
            {
                DrawFilePreview($"{_assetName}_Cooldown.asset", "Cooldown Effect");
            }
            if (IsAbilityType() && _createCostEffect)
            {
                DrawFilePreview($"{_assetName}_Cost.asset", "Cost Effect");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFilePreview(string filename, string description)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("•", GUILayout.Width(15));
            EditorGUILayout.LabelField(filename, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"({description})", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private string GetMainAssetDescription()
        {
            return _selectedType switch
            {
                AssetType.MeleeAbility => "Melee Attack Ability",
                AssetType.RangedAbility => "Ranged Attack Ability",
                AssetType.DodgeAbility => "Dodge Ability",
                AssetType.BuffEffect => "Buff Effect",
                AssetType.DebuffEffect => "Debuff Effect",
                AssetType.DamageEffect => "Damage Effect",
                AssetType.HealEffect => "Heal Effect",
                AssetType.CooldownEffect => "Cooldown Effect",
                AssetType.MeleeAttackData => "Melee Attack Data",
                _ => "Asset"
            };
        }

        private bool IsAbilityType()
        {
            return _selectedType == AssetType.MeleeAbility ||
                   _selectedType == AssetType.RangedAbility ||
                   _selectedType == AssetType.DodgeAbility;
        }

        #endregion

        #region Navigation

        private void DrawNavigationButtons()
        {
            EditorGUILayout.BeginHorizontal();

            // 返回按鈕
            EditorGUI.BeginDisabledGroup(_currentPage == WizardPage.SelectType);
            if (GUILayout.Button("← Back", GUILayout.Height(30)))
            {
                _currentPage--;
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            // 下一步/創建按鈕
            if (_currentPage == WizardPage.Review)
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                if (GUILayout.Button("Create Assets", GUILayout.Height(30), GUILayout.Width(150)))
                {
                    CreateAssets();
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                EditorGUI.BeginDisabledGroup(_selectedType == AssetType.None);
                if (GUILayout.Button("Next →", GUILayout.Height(30), GUILayout.Width(100)))
                {
                    _currentPage++;
                }
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Create Assets

        private void CreateAssets()
        {
            // 確保輸出資料夾存在
            if (!Directory.Exists(_outputPath))
            {
                Directory.CreateDirectory(_outputPath);
            }

            switch (_selectedType)
            {
                case AssetType.MeleeAbility:
                case AssetType.RangedAbility:
                case AssetType.DodgeAbility:
                    CreateAbility();
                    break;

                case AssetType.BuffEffect:
                case AssetType.DebuffEffect:
                case AssetType.DamageEffect:
                case AssetType.HealEffect:
                case AssetType.CooldownEffect:
                    CreateEffect();
                    break;

                case AssetType.MeleeAttackData:
                    CreateMeleeAttackData();
                    break;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Success", 
                $"Assets created successfully at:\n{_outputPath}", "OK");

            // 重置嚮導
            _currentPage = WizardPage.SelectType;
            _selectedType = AssetType.None;
        }

        private void CreateAbility()
        {
            // 注意：這裡我們創建的是基礎 GameplayEffect 而不是 GameplayAbility
            // 因為 GameplayAbility 是抽象類，需要具體的子類實現
            // 用戶需要自己創建繼承自 GameplayAbility 的類別

            GameplayEffect cooldownEffect = null;
            GameplayEffect costEffect = null;

            // 創建冷卻效果
            if (_createCooldownEffect)
            {
                cooldownEffect = ScriptableObject.CreateInstance<GameplayEffect>();
                cooldownEffect.EffectName = $"{_assetName} Cooldown";
                cooldownEffect.EffectTag = new GameplayTag($"Effect.Cooldown.{_assetName}");
                cooldownEffect.DurationPolicy = DurationPolicy.Duration;
                cooldownEffect.Duration = _cooldownDuration;
                
                string cdPath = $"{_outputPath}/{_assetName}_Cooldown.asset";
                AssetDatabase.CreateAsset(cooldownEffect, cdPath);
            }

            // 創建消耗效果
            if (_createCostEffect && _staminaCost > 0)
            {
                costEffect = ScriptableObject.CreateInstance<GameplayEffect>();
                costEffect.EffectName = $"{_assetName} Cost";
                costEffect.EffectTag = new GameplayTag($"Effect.Cost.{_assetName}");
                costEffect.DurationPolicy = DurationPolicy.Instant;
                costEffect.Modifiers.Add(new GameplayModifier
                {
                    AttributeName = CombatAttributes.Stamina,
                    OperationType = ModifierOperationType.Additive,
                    Magnitude = -_staminaCost,
                    MagnitudeType = ModifierMagnitudeType.ScalableFloat
                });

                string costPath = $"{_outputPath}/{_assetName}_Cost.asset";
                AssetDatabase.CreateAsset(costEffect, costPath);
            }

            // 提示用戶需要創建 Ability 類別
            EditorUtility.DisplayDialog("Note", 
                "Cooldown and Cost effects have been created.\n\n" +
                "To create the actual ability, you need to:\n" +
                "1. Create a class inheriting from GameplayAbility\n" +
                "2. Create a ScriptableObject asset of that type\n" +
                "3. Assign the Cooldown/Cost effects to it", "OK");
        }

        private void CreateEffect()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.EffectName = _assetName;
            effect.EffectTag = new GameplayTag(_tagName);
            effect.Description = _description;
            effect.DurationPolicy = _durationPolicy;
            effect.Duration = _effectDuration;

            // 添加修改器
            var modifier = new GameplayModifier
            {
                AttributeName = _targetAttribute,
                OperationType = _operationType,
                Magnitude = _effectMagnitude,
                MagnitudeType = ModifierMagnitudeType.ScalableFloat
            };
            effect.Modifiers.Add(modifier);

            // 根據類型添加標籤
            if (_selectedType == AssetType.BuffEffect)
            {
                effect.GrantedTags.AddTag(new GameplayTag("State.Buffed"));
            }
            else if (_selectedType == AssetType.DebuffEffect)
            {
                effect.GrantedTags.AddTag(new GameplayTag("State.Debuffed"));
            }

            string assetPath = $"{_outputPath}/{_assetName}.asset";
            AssetDatabase.CreateAsset(effect, assetPath);

            Selection.activeObject = effect;
            EditorGUIUtility.PingObject(effect);
        }

        private void CreateMeleeAttackData()
        {
            var attackData = ScriptableObject.CreateInstance<MeleeAttackData>();
            attackData.AllowInputTime = _allowInputTime;
            attackData.ComboResetTime = _comboResetTime;
            attackData.AllowCancelTime = _allowInputTime + 0.1f;

            // 創建命中視窗
            for (int i = 0; i < _hitWindowCount; i++)
            {
                var hw = new MeleeHitWindow
                {
                    StartTime = 0.1f + (i * 0.15f),
                    EndTime = 0.25f + (i * 0.15f),
                    Shape = HitboxShape.Box,
                    Offset = new Vector3(0, 1, 1),
                    Size = Vector3.one,
                    BaseDamage = _baseDamage,
                    DamageMultiplier = 1f,
                    HitStopDuration = 0.1f,
                    ScreenShakeForce = 1f
                };
                attackData.HitWindows.Add(hw);
            }

            string assetPath = $"{_outputPath}/{_assetName}.asset";
            AssetDatabase.CreateAsset(attackData, assetPath);

            Selection.activeObject = attackData;
            EditorGUIUtility.PingObject(attackData);
        }

        #endregion
    }
}
#endif
