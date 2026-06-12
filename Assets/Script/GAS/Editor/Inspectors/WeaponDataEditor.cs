#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Animancer;

namespace GAS.Editor
{
    /// <summary>
    /// WeaponData 自訂 Inspector
    /// 提供武器預覽卡片、動畫分組、能力關聯和驗證提示
    /// </summary>
    [CustomEditor(typeof(WeaponData))]
    public class WeaponDataEditor : UnityEditor.Editor
    {
        // 摺疊狀態
        private bool _showBasicInfo = true;
        private bool _showModel = true;
        private bool _showPlayerLocomotionData = true;
        private bool _showSwitchAnims = true;
        private bool _showCombatAbilities = true;
        private bool _showAssistAbilities = false;
        private bool _showDefensiveAssist = true;
        private bool _showVFX = false;
        private bool _showAfterimage = false;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var weapon = (WeaponData)target;

            DrawPreviewCard(weapon);
            DrawValidationWarnings(weapon);

            EditorGUILayout.Space(4);

            _showBasicInfo = DrawSection("Basic Info", _showBasicInfo, DrawBasicInfo);
            _showModel = DrawSection("Model", _showModel, DrawModel);
            _showPlayerLocomotionData = DrawSection("Player Locomotion Data", _showPlayerLocomotionData, DrawPlayerLocomotionData);
            _showSwitchAnims = DrawSection("Switch Animations", _showSwitchAnims, DrawSwitchAnims);
            _showCombatAbilities = DrawSection("Combat Abilities", _showCombatAbilities, DrawCombatAbilities);
            _showAssistAbilities = DrawSection("Assist Abilities", _showAssistAbilities, DrawAssistAbilities);
            _showDefensiveAssist = DrawSection("Defensive Assist (招架支援)", _showDefensiveAssist, DrawDefensiveAssist);
            _showVFX = DrawSection("VFX & SFX", _showVFX, DrawVFX);
            _showAfterimage = DrawSection("Afterimage", _showAfterimage, DrawAfterimage);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPreviewCard(WeaponData weapon)
        {
            // 武器預覽卡片
            var cardRect = EditorGUILayout.BeginVertical();
            var bgColor = weapon.Type == WeaponType.Melee
                ? new Color(0.2f, 0.35f, 0.55f, 0.3f)
                : new Color(0.55f, 0.3f, 0.2f, 0.3f);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 60), bgColor);

            EditorGUILayout.BeginHorizontal();

            // 圖示
            if (weapon.Icon != null)
            {
                Texture2D tex = AssetPreview.GetAssetPreview(weapon.Icon);
                if (tex != null)
                {
                    GUILayout.Label(tex, GUILayout.Width(56), GUILayout.Height(56));
                }
                else
                {
                    GUILayout.Label("", GUILayout.Width(56), GUILayout.Height(56));
                }
            }
            else
            {
                GUILayout.Label("[No Icon]", EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(56), GUILayout.Height(56));
            }

            EditorGUILayout.BeginVertical();
            GUILayout.Space(8);

            // 名稱
            var nameStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            string displayName = string.IsNullOrEmpty(weapon.WeaponName) ? weapon.name : weapon.WeaponName;
            EditorGUILayout.LabelField(displayName, nameStyle);

            // 類型標籤
            var tagStyle = new GUIStyle(EditorStyles.miniLabel);
            string typeLabel = weapon.Type == WeaponType.Melee ? "[Melee]" : "[Ranged]";
            EditorGUILayout.LabelField(typeLabel, tagStyle);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        private void DrawValidationWarnings(WeaponData weapon)
        {
            if (weapon.CharacterModelPrefab == null)
                EditorGUILayout.HelpBox("Character Model Prefab is missing!", MessageType.Warning);

            if (weapon.LocomotionConfig == null)
                EditorGUILayout.HelpBox("Locomotion Config 未指派 — 套用此武器後,移動轉向、淡入時間、快跑門檻等參數將使用預設值。", MessageType.Warning);

            if (weapon.LocomotionAnimations == null)
                EditorGUILayout.HelpBox("Locomotion Animations 未指派 — 套用此武器後,Idle / Walk / Run / Dodge 等動畫將無法播放。", MessageType.Warning);

            if (weapon.HitReactionData == null)
                EditorGUILayout.HelpBox("Hit Reaction Data 未指派 — 此武器無自定義受擊反應,玩家受擊時會直接略過播放(Invincible/SuperArmor 除外)。", MessageType.Info);

            if (weapon.DeathData == null)
                EditorGUILayout.HelpBox("Death Data 未指派 — 切到此武器後仍沿用切換前的死亡動畫 / UI 延遲。若此武器要自訂死亡表現,請指派一份 PlayerDeathData。", MessageType.Info);

            if (weapon.AttackAbility == null)
                EditorGUILayout.HelpBox("Attack Ability is not assigned.", MessageType.Warning);
        }

        private void DrawBasicInfo()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("WeaponName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Type"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Icon"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Description"));
        }

        private void DrawModel()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CharacterModelPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("WeaponModelPrefab"));
        }

        private void DrawPlayerLocomotionData()
        {
            EditorGUILayout.HelpBox(
                "四個 ScriptableObject 承載此武器的所有玩家移動 / 受擊 / 死亡資料。\n" +
                "切武器時由 WeaponManager 整包交給 NewGASPlayerController,取代舊版零散的 clip 欄位指派。",
                MessageType.Info);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("LocomotionConfig"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("LocomotionAnimations"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("HitReactionData"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DeathData"));
        }

        private void DrawSwitchAnims()
        {
            DrawAnimField("SwitchInAnimation");
            DrawAnimField("SwitchOutAnimation");
        }

        private void DrawCombatAbilities()
        {
            var weapon = (WeaponData)target;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("AttackAbility"));
            if (weapon.AttackAbility != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Light", weapon.AttackAbility.AbilityName, EditorStyles.miniLabel);
                DrawAbilityTagInfo(weapon.AttackAbility, expectedTag: "Ability.Attack.Light");
                if (GUILayout.Button("Select Light Attack Ability", EditorStyles.miniButton))
                    Selection.activeObject = weapon.AttackAbility;
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("HeavyAttackAbility"));
            if (weapon.HeavyAttackAbility != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Heavy", weapon.HeavyAttackAbility.AbilityName, EditorStyles.miniLabel);
                DrawAbilityTagInfo(weapon.HeavyAttackAbility, expectedTag: "Ability.Attack.Heavy");
                if (GUILayout.Button("Select Heavy Attack Ability", EditorStyles.miniButton))
                    Selection.activeObject = weapon.HeavyAttackAbility;
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("(Optional) 重攻擊未指派 — 玩家按重攻擊鍵時無能力觸發", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DodgeAbility"));
            if (weapon.DodgeAbility != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Dodge", weapon.DodgeAbility.AbilityName, EditorStyles.miniLabel);
                if (GUILayout.Button("Select Dodge Ability", EditorStyles.miniButton))
                    Selection.activeObject = weapon.DodgeAbility;
                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        /// 顯示能力 Tag 並在不符預期 Tag 時警告（避免把 Light Ability 塞進 Heavy 槽位）
        /// </summary>
        private void DrawAbilityTagInfo(GameplayAbility ability, string expectedTag)
        {
            if (ability == null) return;
            string actualTag = ability.AbilityTag.ToString();
            if (string.IsNullOrEmpty(actualTag) || actualTag == "None")
            {
                EditorGUILayout.HelpBox($"此 Ability 沒有設定 AbilityTag — 輸入系統無法觸發。預期：{expectedTag}", MessageType.Error);
                return;
            }
            EditorGUILayout.LabelField("Tag", actualTag, EditorStyles.miniLabel);
            if (!actualTag.StartsWith(expectedTag))
            {
                EditorGUILayout.HelpBox(
                    $"此 Ability 的 Tag 為「{actualTag}」,但此槽位預期「{expectedTag}」起始 — 輸入鍵可能觸發不到。",
                    MessageType.Warning);
            }
        }

        private void DrawAssistAbilities()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ParryAssistAbility"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DodgeAssistAbility"));
        }

        private void DrawDefensiveAssist()
        {
            EditorGUILayout.HelpBox(
                "招架兩段動畫 — 此武器作為「招架者」換上場後依序播放：\n" +
                "Start（舉武器衝向前 → 停在最後一幀舉刀等接刀）\n" +
                "End（接到刀或 timeout 時播放收勢動作）",
                MessageType.Info);

            DrawAnimField("ParryStartAnimation");
            DrawAnimField("ParryEndAnimation");
        }

        private void DrawVFX()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SwitchInVFXPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SwitchOutVFXPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SwitchSFX"));
        }

        private void DrawAfterimage()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AfterImageMaterial"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AfterImageFadeDuration"));
        }

        // === Helpers ===

        private void DrawAnimField(string propertyName)
        {
            var prop = serializedObject.FindProperty(propertyName);
            if (prop == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(prop, true);

            // 顯示動畫長度
            var weapon = (WeaponData)target;
            var field = typeof(WeaponData).GetField(propertyName);
            if (field != null)
            {
                var clip = field.GetValue(weapon) as ClipTransition;
                if (clip != null && clip.Clip != null)
                {
                    var labelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                    EditorGUILayout.LabelField($"{clip.Clip.length:F2}s", labelStyle, GUILayout.Width(45));
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private bool DrawSection(string title, bool isExpanded, System.Action drawContent)
        {
            // 帶顏色的摺疊區塊
            var headerRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.foldoutHeader);
            var headerColor = new Color(0.22f, 0.22f, 0.22f, 0.6f);
            EditorGUI.DrawRect(headerRect, headerColor);

            isExpanded = EditorGUI.Foldout(headerRect, isExpanded, " " + title, true, EditorStyles.foldoutHeader);

            if (isExpanded)
            {
                EditorGUI.indentLevel++;
                drawContent();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }

            return isExpanded;
        }
    }
}
#endif
