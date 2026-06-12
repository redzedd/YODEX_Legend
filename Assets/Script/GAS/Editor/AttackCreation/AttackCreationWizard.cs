#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GAS.Editor.AttackCreation
{
    /// <summary>
    /// [B1] 攻擊建立精靈 — 一頁式表單,設計師填好後一鍵生成完整的攻擊資產組
    /// (AttackData + HitEffect + Cooldown + HitCue + 自動加 Tag 到 Library + 串接到 WeaponData)。
    /// B1 階段: 純 UI + 驗證 + 預覽。B2 階段才接實際生成邏輯。
    /// </summary>
    public class AttackCreationWizard : EditorWindow
    {
        // ==== 表單欄位 ====
        private WeaponData _weapon;
        private AttackPlacement _placement = AttackPlacement.FirstLight;
        private string _attackName = "NewSlash";
        private AnimationClip _animationClip;
        private float _hitStart = 0.15f;
        private float _hitEnd = 0.25f;
        private float _allowInputTime = 0.30f;
        private float _allowCancelTime = 0.35f;
        private float _comboResetTime = 0.80f;
        private float _baseDamage = 25f;
        private float _poiseDamage = 30f;
        private float _knockbackForce = 1.5f;
        private float _cooldownDuration = 0.4f;
        private bool _generateCooldown;
        private GameObject _hitVFXPrefab;
        private AudioClip _hitSFX;
        private float _hitStopDuration = 0.08f;
        [Range(0f, 1f)] private float _hitStopTimeScale = 0f;
        private float _cameraShakeIntensity = 1f;
        private MeleeAttackData _parentAttackForCombo;
        private MeleeInputType _comboTriggerInput = MeleeInputType.LightAttack;

        // ==== 摺疊 ====
        private bool _foldBasic = true;
        private bool _foldAnim = true;
        private bool _foldTiming = true;
        private bool _foldCombat = true;
        private bool _foldFeedback = true;
        private bool _foldPlacement = true;
        private bool _foldPreview = true;

        private Vector2 _scroll;

        public enum AttackPlacement
        {
            FirstLight,
            FirstHeavy,
            ComboFollowUp,
        }

        [MenuItem("GAS/Attack/Create New Attack", priority = 0)]
        public static void Open()
        {
            AttackCreationWizard w = GetWindow<AttackCreationWizard>();
            w.titleContent = new GUIContent("Create Attack");
            w.minSize = new Vector2(520, 760);
            w.Show();
        }

        /// <summary>
        /// 以一個現有 AttackData 為基礎開啟 wizard,預填所有數值,placement 自動設為「連擊延伸」。
        /// 由 MeleeAttackDataEditor 的「建立連擊下一段」按鈕呼叫。
        /// </summary>
        public static void OpenAsFollowUp(MeleeAttackData parent)
        {
            AttackCreationWizard w = GetWindow<AttackCreationWizard>();
            w.titleContent = new GUIContent("Create Combo Follow-Up");
            w.minSize = new Vector2(520, 760);

            w._placement = AttackPlacement.ComboFollowUp;
            w._parentAttackForCombo = parent;
            w._comboTriggerInput = MeleeInputType.LightAttack;
            w._attackName = parent.name.Replace("_AttackData", "") + "_Next";

            // 從 parent 抄 timing / 戰鬥數值
            w._allowInputTime = parent.AllowInputTime;
            w._allowCancelTime = parent.AllowCancelTime;
            w._comboResetTime = parent.ComboResetTime;
            w._poiseDamage = parent.PoiseDamage;
            if (parent.HitWindows != null && parent.HitWindows.Count > 0)
            {
                MeleeHitWindow ph = parent.HitWindows[0];
                w._hitStart = ph.StartTime;
                w._hitEnd = ph.EndTime;
                w._baseDamage = ph.BaseDamage;
                w._poiseDamage = ph.PoiseDamage;
                w._knockbackForce = ph.KnockbackForce;
                w._hitVFXPrefab = ph.HitVFXPrefab;
                w._hitSFX = ph.HitSFX;
                w._hitStopDuration = ph.HitStopDuration;
                w._hitStopTimeScale = ph.HitStopSpeed;
                w._cameraShakeIntensity = ph.ScreenShakeForce;
            }
            // 嘗試從專案內現有 WeaponData 反查 — 找 AttackAbility/HeavyAttackAbility 的 FirstAttackData
            // 等於 parent 的 WeaponData,或在其連擊鏈中能間接走到 parent 的 WeaponData
            w._weapon = FindWeaponContainingAttack(parent);

            w.Show();
        }

        private static WeaponData FindWeaponContainingAttack(MeleeAttackData target)
        {
            string[] guids = AssetDatabase.FindAssets("t:WeaponData");
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                WeaponData wd = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
                if (wd == null) continue;
                if (WeaponContainsAttack(wd.AttackAbility, target)) return wd;
                if (WeaponContainsAttack(wd.HeavyAttackAbility, target)) return wd;
            }
            return null;
        }

        private static bool WeaponContainsAttack(GameplayAbility ability, MeleeAttackData target)
        {
            if (ability is not GA_MeleeAttack melee || target == null) return false;
            return SubtreeContains(melee.FirstAttackData, target, new HashSet<MeleeAttackData>())
                || SubtreeContains(melee.FallbackFirstAttack, target, new HashSet<MeleeAttackData>());
        }

        private static bool SubtreeContains(MeleeAttackData node, MeleeAttackData target, HashSet<MeleeAttackData> visited)
        {
            if (node == null || !visited.Add(node)) return false;
            if (node == target) return true;
            if (node.NextCombos == null) return false;
            foreach (ComboLink link in node.NextCombos)
            {
                if (link.NextAttack is MeleeAttackData m && SubtreeContains(m, target, visited)) return true;
            }
            return false;
        }

        // ====================================================================

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            DrawBasicSection();
            DrawAnimationSection();
            DrawPlacementSection();
            DrawTimingSection();
            DrawCombatSection();
            DrawFeedbackSection();
            DrawPreviewSection();

            EditorGUILayout.EndScrollView();
            DrawFooter();
        }

        // ----------------------------------------------------------------
        // Sections
        // ----------------------------------------------------------------

        private void DrawHeader()
        {
            EditorGUILayout.Space(4);
            GUIStyle titleStyle = new(EditorStyles.boldLabel) { fontSize = 14 };
            EditorGUILayout.LabelField("攻擊建立精靈", titleStyle);
            EditorGUILayout.HelpBox(
                "填好下方欄位 → 按底部「建立攻擊」自動產出 AttackData / HitEffect / Cooldown / HitCue 並串接到 WeaponData。\n" +
                "目前為 B1 階段 — 預覽功能完成,實際生成邏輯將在 B2 接上。",
                MessageType.Info);
            EditorGUILayout.Space(4);
        }

        private void DrawBasicSection()
        {
            _foldBasic = DrawFoldoutBox("① 基本", _foldBasic, () =>
            {
                _weapon = (WeaponData)EditorGUILayout.ObjectField(
                    new GUIContent("武器", "此攻擊屬於哪把武器? 將自動加進該 WeaponData 的能力列表"),
                    _weapon, typeof(WeaponData), false);

                _attackName = EditorGUILayout.TextField(
                    new GUIContent("攻擊名稱", "用於資產檔名與 Tag 路徑。建議 PascalCase,例: SlashHorizontal"),
                    _attackName);

                if (!string.IsNullOrEmpty(_attackName) && !IsValidIdentifier(_attackName))
                {
                    EditorGUILayout.HelpBox("名稱含非法字元 — 僅允許字母、數字、底線,且不可數字開頭。", MessageType.Warning);
                }
            });
        }

        private void DrawAnimationSection()
        {
            _foldAnim = DrawFoldoutBox("② 動畫", _foldAnim, () =>
            {
                _animationClip = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("攻擊動畫 Clip", "拖入該攻擊使用的 AnimationClip,長度會自動套到時間軸"),
                    _animationClip, typeof(AnimationClip), false);

                if (_animationClip != null)
                {
                    float len = _animationClip.length;
                    EditorGUILayout.LabelField(
                        "動畫長度",
                        $"{len:F3}s ({len * 30f:F0} 幀 @ 30fps)",
                        EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("請拖入動畫 Clip — 時間軸會以此 Clip 長度為上限。", MessageType.None);
                }
            });
        }

        private void DrawPlacementSection()
        {
            _foldPlacement = DrawFoldoutBox("③ 連擊定位", _foldPlacement, () =>
            {
                EditorGUILayout.LabelField("此攻擊在連擊鏈中的位置:", EditorStyles.miniLabel);
                _placement = (AttackPlacement)GUILayout.SelectionGrid(
                    (int)_placement,
                    new[] { "武器的第一輕擊", "武器的第一重擊", "連擊延伸" },
                    3);

                EditorGUILayout.Space(4);

                switch (_placement)
                {
                    case AttackPlacement.FirstLight:
                        EditorGUILayout.HelpBox(
                            "將設為 武器 AttackAbility 的 FirstAttackData(玩家按輕攻擊鍵時的第一擊)。\n" +
                            "若該欄位已有資料,將被取代。",
                            MessageType.None);
                        break;
                    case AttackPlacement.FirstHeavy:
                        EditorGUILayout.HelpBox(
                            "將設為 武器 HeavyAttackAbility 的 FirstAttackData(玩家按重攻擊鍵時的第一擊)。\n" +
                            "若該欄位已有資料,將被取代。",
                            MessageType.None);
                        break;
                    case AttackPlacement.ComboFollowUp:
                        _parentAttackForCombo = (MeleeAttackData)EditorGUILayout.ObjectField(
                            new GUIContent("接續自:", "選擇前一段攻擊的 AttackData。新攻擊會加進其 NextCombos 列表"),
                            _parentAttackForCombo, typeof(MeleeAttackData), false);
                        _comboTriggerInput = (MeleeInputType)EditorGUILayout.EnumPopup(
                            new GUIContent("觸發按鍵", "玩家按下哪個鍵時連到此攻擊?"),
                            _comboTriggerInput);
                        if (_parentAttackForCombo == null)
                        {
                            EditorGUILayout.HelpBox("請選擇要從哪一段 AttackData 接續。", MessageType.Warning);
                        }
                        break;
                }
            });
        }

        private void DrawTimingSection()
        {
            _foldTiming = DrawFoldoutBox("④ 時間軸", _foldTiming, () =>
            {
                float upper = _animationClip != null ? _animationClip.length : 2f;

                EditorGUILayout.LabelField("命中視窗 (Hit Window)", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{_hitStart:F2}s", GUILayout.Width(48));
                EditorGUILayout.MinMaxSlider(ref _hitStart, ref _hitEnd, 0f, upper);
                EditorGUILayout.LabelField($"{_hitEnd:F2}s", GUILayout.Width(48));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(
                    $"命中判定持續 {(_hitEnd - _hitStart):F2}s — 動畫的此段時間內會偵測碰撞。",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(6);

                _allowInputTime = EditorGUILayout.Slider(
                    new GUIContent("允許輸入時間", "從此時間起,玩家按下攻擊可觸發連擊(讀取輸入緩衝)"),
                    _allowInputTime, 0f, upper);
                _allowCancelTime = EditorGUILayout.Slider(
                    new GUIContent("允許取消時間", "從此時間起,可被閃避/招架等能力取消"),
                    _allowCancelTime, 0f, upper);
                _comboResetTime = EditorGUILayout.Slider(
                    new GUIContent("連招重置時間", "超過此時間後,連擊重置回第一擊"),
                    _comboResetTime, 0f, Mathf.Max(upper, 2f));

                if (_animationClip == null)
                {
                    EditorGUILayout.HelpBox("尚未指定動畫 — 滑塊以 2 秒為暫定上限。", MessageType.None);
                }
            });
        }

        private void DrawCombatSection()
        {
            _foldCombat = DrawFoldoutBox("⑤ 戰鬥數值", _foldCombat, () =>
            {
                _baseDamage = EditorGUILayout.FloatField(
                    new GUIContent("基礎傷害", "命中時造成的傷害(未計入暴擊/防禦)"),
                    _baseDamage);
                _poiseDamage = EditorGUILayout.FloatField(
                    new GUIContent("韌性傷害", "扣敵人韌性 — 累積到 0 觸發 stagger"),
                    _poiseDamage);
                _knockbackForce = EditorGUILayout.FloatField(
                    new GUIContent("擊退距離 (m)", "Poise 擊破後敵人被推開的距離"),
                    _knockbackForce);

                EditorGUILayout.Space(4);

                _generateCooldown = EditorGUILayout.ToggleLeft(
                    new GUIContent("產生獨立 Cooldown Effect", "勾選 → 額外建立一個冷卻效果。一般攻擊不需要,留空即可"),
                    _generateCooldown);
                if (_generateCooldown)
                {
                    _cooldownDuration = EditorGUILayout.FloatField(
                        new GUIContent("Cooldown 秒數", "攻擊完成後多久才能再觸發"),
                        _cooldownDuration);
                }
            });
        }

        private void DrawFeedbackSection()
        {
            _foldFeedback = DrawFoldoutBox("⑥ 命中回饋", _foldFeedback, () =>
            {
                _hitVFXPrefab = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("命中 VFX Prefab", "命中時生成的特效預製體 — 將打包進 HitCue"),
                    _hitVFXPrefab, typeof(GameObject), false);
                _hitSFX = (AudioClip)EditorGUILayout.ObjectField(
                    new GUIContent("命中 SFX", "命中時播放的音效 — 將打包進 HitCue"),
                    _hitSFX, typeof(AudioClip), false);
                _hitStopDuration = EditorGUILayout.FloatField(
                    new GUIContent("頓幀時間 (s)", "命中時遊戲短暫凍結的秒數 (0 = 無)"),
                    _hitStopDuration);
                _hitStopTimeScale = EditorGUILayout.Slider(
                    new GUIContent("頓幀時的 TimeScale", "0 = 完全凍結,1 = 不凍結"),
                    _hitStopTimeScale, 0f, 1f);
                _cameraShakeIntensity = EditorGUILayout.FloatField(
                    new GUIContent("鏡頭震動強度", "0 = 不震動"),
                    _cameraShakeIntensity);
            });
        }

        private void DrawPreviewSection()
        {
            _foldPreview = DrawFoldoutBox("📋 將生成什麼 (預覽)", _foldPreview, () =>
            {
                List<string> errors = new();
                List<string> warnings = new();
                List<string> assets = ComputeAssetPlan(errors, warnings);

                if (errors.Count > 0)
                {
                    foreach (string e in errors)
                    {
                        EditorGUILayout.HelpBox(e, MessageType.Error);
                    }
                }
                if (warnings.Count > 0)
                {
                    foreach (string w in warnings)
                    {
                        EditorGUILayout.HelpBox(w, MessageType.Warning);
                    }
                }
                if (errors.Count == 0)
                {
                    EditorGUILayout.LabelField($"輸出資料夾: Assets/GameData/Attacks/{SafeWeaponName()}/{SafeAttackName()}/", EditorStyles.miniLabel);
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("將建立資產:", EditorStyles.miniBoldLabel);
                    foreach (string a in assets)
                    {
                        EditorGUILayout.LabelField("  • " + a, EditorStyles.miniLabel);
                    }

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("將加入 Library 的 Tag:", EditorStyles.miniBoldLabel);
                    foreach (string t in ComputeNewTags())
                    {
                        EditorGUILayout.LabelField("  • " + t, EditorStyles.miniLabel);
                    }

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("將執行的串接:", EditorStyles.miniBoldLabel);
                    foreach (string action in ComputeWireUpActions())
                    {
                        EditorGUILayout.LabelField("  • " + action, EditorStyles.miniLabel);
                    }
                }
            });
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("取消", GUILayout.Height(28)))
            {
                Close();
            }
            List<string> errors = new();
            List<string> _ = new();
            ComputeAssetPlan(errors, _);
            using (new EditorGUI.DisabledScope(errors.Count > 0))
            {
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = errors.Count == 0 ? new Color(0.4f, 0.85f, 0.4f) : Color.white;
                if (GUILayout.Button("建立攻擊!", GUILayout.Height(28)))
                {
                    BuildNow();
                }
                GUI.backgroundColor = prev;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void BuildNow()
        {
            // 確認對話框 — 列出將生成內容
            List<string> errors = new();
            List<string> warnings = new();
            List<string> assets = ComputeAssetPlan(errors, warnings);
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("無法建立", string.Join("\n", errors), "知道了");
                return;
            }
            StringBuilder msg = new();
            msg.AppendLine($"將建立攻擊「{SafePrefix()}」並串接到 {SafeWeaponName()}。");
            msg.AppendLine();
            msg.AppendLine("輸出資料夾:");
            msg.AppendLine($"  Assets/GameData/Attacks/{SafeWeaponName()}/{SafeAttackName()}/");
            msg.AppendLine();
            msg.AppendLine("資產:");
            foreach (string a in assets)
            {
                msg.Append("  • ").AppendLine(a);
            }
            if (warnings.Count > 0)
            {
                msg.AppendLine();
                msg.AppendLine("注意:");
                foreach (string w in warnings)
                {
                    msg.Append("  • ").AppendLine(w);
                }
            }
            msg.AppendLine();
            msg.Append("確認建立?");
            if (!EditorUtility.DisplayDialog("建立攻擊確認", msg.ToString(), "確認建立", "取消"))
            {
                return;
            }

            // 包成 BuildParams 呼叫 AttackAssetBuilder
            AttackAssetBuilder.BuildParams p = new()
            {
                Weapon = _weapon,
                Placement = _placement,
                AttackName = _attackName,
                AnimationClip = _animationClip,
                HitStart = _hitStart,
                HitEnd = _hitEnd,
                AllowInputTime = _allowInputTime,
                AllowCancelTime = _allowCancelTime,
                ComboResetTime = _comboResetTime,
                BaseDamage = _baseDamage,
                PoiseDamage = _poiseDamage,
                KnockbackForce = _knockbackForce,
                GenerateCooldown = _generateCooldown,
                CooldownDuration = _cooldownDuration,
                HitVFXPrefab = _hitVFXPrefab,
                HitSFX = _hitSFX,
                HitStopDuration = _hitStopDuration,
                HitStopTimeScale = _hitStopTimeScale,
                CameraShakeIntensity = _cameraShakeIntensity,
                ComboParentAttack = _parentAttackForCombo,
                ComboTriggerInput = _comboTriggerInput,
            };
            AttackAssetBuilder.BuildResult r = AttackAssetBuilder.Build(p);

            if (!r.Success)
            {
                EditorUtility.DisplayDialog("建立失敗", r.ErrorMessage ?? "未知錯誤", "知道了");
                return;
            }

            // 結果視窗
            StringBuilder result = new();
            result.AppendLine($"已建立 {r.CreatedAssetPaths.Count} 個資產:");
            foreach (string path in r.CreatedAssetPaths)
            {
                result.Append("  • ").AppendLine(path);
            }
            result.AppendLine();
            result.AppendLine("串接動作:");
            foreach (string w in r.WiredActions)
            {
                result.Append("  ✓ ").AppendLine(w);
            }
            if (r.Warnings.Count > 0)
            {
                result.AppendLine();
                result.AppendLine("警告:");
                foreach (string w in r.Warnings)
                {
                    result.Append("  ⚠ ").AppendLine(w);
                }
            }
            EditorUtility.DisplayDialog("建立完成", result.ToString(), "好");

            // Ping AttackData 讓設計師立即跳到該資產
            if (r.AttackData != null)
            {
                Selection.activeObject = r.AttackData;
                EditorGUIUtility.PingObject(r.AttackData);
            }
        }

        // ----------------------------------------------------------------
        // 預覽運算
        // ----------------------------------------------------------------

        private List<string> ComputeAssetPlan(List<string> errors, List<string> warnings)
        {
            if (_weapon == null)
            {
                errors.Add("尚未指定武器。");
            }
            if (string.IsNullOrWhiteSpace(_attackName))
            {
                errors.Add("尚未指定攻擊名稱。");
            }
            else if (!IsValidIdentifier(_attackName))
            {
                errors.Add("攻擊名稱含非法字元。");
            }
            if (_animationClip == null)
            {
                warnings.Add("尚未指定動畫 Clip。可先建立資產之後再補。");
            }
            if (_placement == AttackPlacement.ComboFollowUp && _parentAttackForCombo == null)
            {
                errors.Add("連擊延伸模式需指定 Parent AttackData。");
            }
            if (_hitEnd <= _hitStart)
            {
                errors.Add("Hit Window 結束時間必須大於開始時間。");
            }
            if (_animationClip != null && _hitEnd > _animationClip.length + 0.01f)
            {
                warnings.Add($"Hit Window 結束 ({_hitEnd:F2}s) 超出動畫長度 ({_animationClip.length:F2}s),會被截斷。");
            }

            List<string> assets = new();
            if (errors.Count > 0)
            {
                return assets;
            }
            string baseName = SafePrefix();
            assets.Add($"{baseName}_AttackData.asset (MeleeAttackData)");
            assets.Add($"{baseName}_HitEffect.asset (GameplayEffect, 含 SetByCaller=Data.Damage)");
            if (_generateCooldown)
            {
                assets.Add($"{baseName}_Cooldown.asset (GameplayEffect, Duration={_cooldownDuration:F2}s)");
            }
            if (_hitVFXPrefab != null || _hitSFX != null || _hitStopDuration > 0f || _cameraShakeIntensity > 0f)
            {
                assets.Add($"{baseName}_HitCue.asset (CombinedCue: VFX + SFX + HitStop + Shake)");
            }
            return assets;
        }

        private List<string> ComputeNewTags()
        {
            List<string> tags = new();
            string weaponSeg = SafeWeaponName();
            string attackSeg = SafeAttackName();
            string typeSeg = _placement == AttackPlacement.FirstHeavy ? "Heavy" : "Light";
            tags.Add($"Ability.Attack.{weaponSeg}.{typeSeg}.{attackSeg}");
            if (_hitVFXPrefab != null || _hitSFX != null)
            {
                tags.Add($"Cue.Hit.{weaponSeg}.{attackSeg}");
            }
            return tags;
        }

        private List<string> ComputeWireUpActions()
        {
            List<string> actions = new();
            string baseName = SafePrefix();
            switch (_placement)
            {
                case AttackPlacement.FirstLight:
                    actions.Add($"設為 {SafeWeaponName()}.AttackAbility 的 FirstAttackData");
                    break;
                case AttackPlacement.FirstHeavy:
                    actions.Add($"設為 {SafeWeaponName()}.HeavyAttackAbility 的 FirstAttackData");
                    break;
                case AttackPlacement.ComboFollowUp:
                    string parentName = _parentAttackForCombo != null ? _parentAttackForCombo.name : "<未選>";
                    actions.Add($"加進 {parentName}.NextCombos (觸發鍵 = {_comboTriggerInput})");
                    break;
            }
            actions.Add($"{baseName}_AttackData.HitWindows[0].HitEffect = {baseName}_HitEffect");
            if (_hitVFXPrefab != null || _hitSFX != null)
            {
                actions.Add($"{baseName}_AttackData.HitWindows[0].HitCueTag = Cue.Hit.{SafeWeaponName()}.{SafeAttackName()}");
                actions.Add($"註冊 {baseName}_HitCue 到場景的 GameplayCueManager");
            }
            if (_generateCooldown)
            {
                actions.Add($"設定攻擊能力的 CooldownEffect = {baseName}_Cooldown");
            }
            return actions;
        }

        // ----------------------------------------------------------------
        // 輔助
        // ----------------------------------------------------------------

        private string SafeWeaponName()
        {
            return _weapon != null ? SanitizeIdentifier(_weapon.WeaponName ?? _weapon.name) : "<未指定>";
        }

        private string SafeAttackName()
        {
            return string.IsNullOrWhiteSpace(_attackName) ? "<未指定>" : SanitizeIdentifier(_attackName);
        }

        private string SafePrefix()
        {
            return $"{SafeWeaponName()}_{SafeAttackName()}";
        }

        private static string SanitizeIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            StringBuilder sb = new();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static bool IsValidIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            if (!(char.IsLetter(s[0]) || s[0] == '_'))
            {
                return false;
            }
            for (int i = 1; i < s.Length; i++)
            {
                if (!(char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool DrawFoldoutBox(string title, bool foldout, System.Action drawBody)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool newFold = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            if (newFold)
            {
                EditorGUI.indentLevel++;
                drawBody?.Invoke();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
            return newFold;
        }
    }
}
#endif
