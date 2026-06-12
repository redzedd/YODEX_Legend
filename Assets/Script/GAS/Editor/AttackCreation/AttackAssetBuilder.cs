#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GAS.Editor.AttackCreation
{
    /// <summary>
    /// [B2] 攻擊資產生成器 — 接收 AttackCreationWizard 蒐集到的參數,
    /// 一次建立 AttackData / HitEffect / Cooldown,並自動串接到 WeaponData。
    /// </summary>
    public static class AttackAssetBuilder
    {
        private const string LIBRARY_PATH = "Assets/Resources/GameplayTagLibrary.asset";
        private const string OUTPUT_ROOT = "Assets/GameData/Attacks";

        public sealed class BuildParams
        {
            public WeaponData Weapon;
            public AttackCreationWizard.AttackPlacement Placement;
            public string AttackName;
            public AnimationClip AnimationClip;
            public float HitStart;
            public float HitEnd;
            public float AllowInputTime;
            public float AllowCancelTime;
            public float ComboResetTime;
            public float BaseDamage;
            public float PoiseDamage;
            public float KnockbackForce;
            public bool GenerateCooldown;
            public float CooldownDuration;
            public GameObject HitVFXPrefab;
            public AudioClip HitSFX;
            public float HitStopDuration;
            public float HitStopTimeScale;
            public float CameraShakeIntensity;
            public MeleeAttackData ComboParentAttack;
            public MeleeInputType ComboTriggerInput;
        }

        public sealed class BuildResult
        {
            public bool Success;
            public MeleeAttackData AttackData;
            public GameplayEffect HitEffect;
            public GameplayEffect Cooldown;
            public List<string> CreatedAssetPaths = new();
            public List<string> WiredActions = new();
            public List<string> Warnings = new();
            public string ErrorMessage;
        }

        // ====================================================================

        public static BuildResult Build(BuildParams p)
        {
            BuildResult r = new();
            try
            {
                string weaponSeg = SanitizeIdentifier(p.Weapon.WeaponName ?? p.Weapon.name);
                string attackSeg = SanitizeIdentifier(p.AttackName);
                string baseName = $"{weaponSeg}_{attackSeg}";
                string folder = $"{OUTPUT_ROOT}/{weaponSeg}/{attackSeg}";

                // 1. 建立資料夾
                EnsureFolder(folder);

                // 2. 建 HitEffect
                r.HitEffect = CreateHitEffect(folder, baseName, p);
                r.CreatedAssetPaths.Add(AssetDatabase.GetAssetPath(r.HitEffect));

                // 3. 建 Cooldown (可選)
                if (p.GenerateCooldown)
                {
                    r.Cooldown = CreateCooldownEffect(folder, baseName, weaponSeg, attackSeg, p);
                    r.CreatedAssetPaths.Add(AssetDatabase.GetAssetPath(r.Cooldown));
                }

                // 4. 建 AttackData
                r.AttackData = CreateAttackData(folder, baseName, r.HitEffect, p);
                r.CreatedAssetPaths.Add(AssetDatabase.GetAssetPath(r.AttackData));

                // 5. 串接 WeaponData / Parent Attack
                WireUp(r.AttackData, r.Cooldown, p, r);

                // 6. 若有 Cooldown,加 EffectTag 到 Library → 觸發 A2 自動 regen
                if (r.Cooldown != null)
                {
                    AddTagToLibrary($"Effect.Cooldown.{weaponSeg}.{attackSeg}");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                r.Success = true;
            }
            catch (Exception e)
            {
                r.Success = false;
                r.ErrorMessage = e.Message;
                Debug.LogError($"[AttackAssetBuilder] 建立失敗: {e}");
            }
            return r;
        }

        // ====================================================================
        // Asset 建立
        // ====================================================================

        private static GameplayEffect CreateHitEffect(string folder, string baseName, BuildParams p)
        {
            GameplayEffect e = ScriptableObject.CreateInstance<GameplayEffect>();
            e.EffectName = $"{baseName}_HitEffect";
            e.Description = $"由 AttackCreationWizard 生成 — {baseName} 的命中效果。";
            e.DurationPolicy = DurationPolicy.Instant;
            e.Modifiers.Add(new GameplayModifier
            {
                AttributeName = CombatAttributes.IncomingDamage,
                OperationType = ModifierOperationType.Additive,
                MagnitudeType = ModifierMagnitudeType.SetByCaller,
                SetByCallerDataTag = SetByCallerTags.DAMAGE,
                Magnitude = p.BaseDamage,
            });
            string path = $"{folder}/{baseName}_HitEffect.asset";
            AssetDatabase.CreateAsset(e, path);
            return e;
        }

        private static GameplayEffect CreateCooldownEffect(
            string folder, string baseName, string weaponSeg, string attackSeg, BuildParams p)
        {
            GameplayEffect e = ScriptableObject.CreateInstance<GameplayEffect>();
            e.EffectName = $"{baseName}_Cooldown";
            e.Description = $"由 AttackCreationWizard 生成 — {baseName} 的冷卻效果。";
            e.DurationPolicy = DurationPolicy.Duration;
            e.Duration = p.CooldownDuration;
            // 必要: 給 EffectTag 才能讓 GameplayAbility.CheckCooldown 找到此效果
            e.EffectTag = new GameplayTag($"Effect.Cooldown.{weaponSeg}.{attackSeg}");
            string path = $"{folder}/{baseName}_Cooldown.asset";
            AssetDatabase.CreateAsset(e, path);
            return e;
        }

        private static MeleeAttackData CreateAttackData(
            string folder, string baseName, GameplayEffect hitEffect, BuildParams p)
        {
            MeleeAttackData a = ScriptableObject.CreateInstance<MeleeAttackData>();
            a.name = $"{baseName}_AttackData";

            // 基本動畫 — Clip 用 Animancer ClipTransition 包裝
            if (p.AnimationClip != null)
            {
                a.Clip = new Animancer.ClipTransition { Clip = p.AnimationClip };
            }

            // Timing
            a.AllowInputTime = p.AllowInputTime;
            a.AllowCancelTime = p.AllowCancelTime;
            a.ComboResetTime = p.ComboResetTime;
            a.PoiseDamage = p.PoiseDamage;

            // 加一個 HitWindow
            MeleeHitWindow w = new()
            {
                StartTime = p.HitStart,
                EndTime = p.HitEnd,
                BaseDamage = p.BaseDamage,
                PoiseDamage = p.PoiseDamage,
                KnockbackForce = p.KnockbackForce,
                IsHeavyAttack = p.Placement == AttackCreationWizard.AttackPlacement.FirstHeavy,
                HitEffect = hitEffect,
                HitVFXPrefab = p.HitVFXPrefab,
                HitSFX = p.HitSFX,
                HitStopDuration = p.HitStopDuration,
                HitStopSpeed = p.HitStopTimeScale,
                ScreenShakeForce = p.CameraShakeIntensity,
                // HitCueTag 留空 — 設計師之後若想接 Cue 系統再設
            };
            a.HitWindows.Add(w);

            string path = $"{folder}/{baseName}_AttackData.asset";
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        // ====================================================================
        // 串接
        // ====================================================================

        private static void WireUp(MeleeAttackData attackData, GameplayEffect cooldown, BuildParams p, BuildResult r)
        {
            switch (p.Placement)
            {
                case AttackCreationWizard.AttackPlacement.FirstLight:
                    WireFirstAttack(p.Weapon, attackData, cooldown, isHeavy: false, r);
                    break;
                case AttackCreationWizard.AttackPlacement.FirstHeavy:
                    WireFirstAttack(p.Weapon, attackData, cooldown, isHeavy: true, r);
                    break;
                case AttackCreationWizard.AttackPlacement.ComboFollowUp:
                    WireCombo(p.ComboParentAttack, attackData, p.ComboTriggerInput, r);
                    break;
            }
        }

        private static void WireFirstAttack(WeaponData weapon, MeleeAttackData attackData,
            GameplayEffect cooldown, bool isHeavy, BuildResult r)
        {
            GameplayAbility ability = isHeavy ? weapon.HeavyAttackAbility : weapon.AttackAbility;
            string slotLabel = isHeavy ? "HeavyAttackAbility" : "AttackAbility";
            if (ability == null)
            {
                r.Warnings.Add($"{weapon.name}.{slotLabel} 為 null — 無法設 FirstAttackData,請在 WeaponData 上指派一個 GA_MeleeAttack 後重試。");
                return;
            }
            if (ability is GA_MeleeAttack melee)
            {
                Undo.RecordObject(melee, "Wire FirstAttackData");
                melee.FirstAttackData = attackData;
                EditorUtility.SetDirty(melee);
                r.WiredActions.Add($"設 {melee.name}.FirstAttackData = {attackData.name}");
            }
            else
            {
                r.Warnings.Add($"{weapon.name}.{slotLabel} 是 {ability.GetType().Name},不是 GA_MeleeAttack — 無法設 FirstAttackData。");
            }
            if (cooldown != null)
            {
                Undo.RecordObject(ability, "Wire CooldownEffect");
                ability.CooldownEffect = cooldown;
                EditorUtility.SetDirty(ability);
                r.WiredActions.Add($"設 {ability.name}.CooldownEffect = {cooldown.name}");
            }
        }

        private static void WireCombo(MeleeAttackData parent, MeleeAttackData attackData,
            MeleeInputType trigger, BuildResult r)
        {
            if (parent == null)
            {
                r.Warnings.Add("Combo parent 為 null — 無法串接。");
                return;
            }
            Undo.RecordObject(parent, "Add Combo Link");
            parent.NextCombos.Add(new ComboLink
            {
                InputType = trigger,
                NextAttack = attackData,
            });
            EditorUtility.SetDirty(parent);
            r.WiredActions.Add($"加 ComboLink 到 {parent.name}.NextCombos (Trigger={trigger}, Next={attackData.name})");
        }

        // ====================================================================
        // Tag Library 整合
        // ====================================================================

        private static void AddTagToLibrary(string tagName)
        {
            GameplayTagLibrary lib = AssetDatabase.LoadAssetAtPath<GameplayTagLibrary>(LIBRARY_PATH);
            if (lib == null)
            {
                Debug.LogWarning($"[AttackAssetBuilder] 找不到 Library: {LIBRARY_PATH},未加入 Tag '{tagName}'。");
                return;
            }
            if (TagAlreadyExists(lib, tagName))
            {
                return;
            }
            Undo.RecordObject(lib, "Add Tag");
            SerializedObject so = new(lib);
            SerializedProperty list = so.FindProperty("_tagDefinitions");
            int idx = list.arraySize;
            list.InsertArrayElementAtIndex(idx);
            SerializedProperty newEl = list.GetArrayElementAtIndex(idx);
            newEl.FindPropertyRelative("TagName").stringValue = tagName;
            newEl.FindPropertyRelative("Description").stringValue = "由 AttackCreationWizard 自動加入";
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssetIfDirty(lib);
            // 不需要呼叫 A2 — AssetPostprocessor 會自動觸發 GameplayTags.generated.cs 重新生成
        }

        private static bool TagAlreadyExists(GameplayTagLibrary lib, string tagName)
        {
            if (lib.TagDefinitions == null)
            {
                return false;
            }
            foreach (GameplayTagLibrary.TagDefinition d in lib.TagDefinitions)
            {
                if (d.TagName == tagName)
                {
                    return true;
                }
            }
            return false;
        }

        // ====================================================================
        // 輔助
        // ====================================================================

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            // 遞迴建立每一層
            string[] parts = path.Split('/');
            string accum = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string parent = accum;
                accum += "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(accum))
                {
                    AssetDatabase.CreateFolder(parent, parts[i]);
                }
            }
        }

        private static string SanitizeIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            System.Text.StringBuilder sb = new();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
#endif
