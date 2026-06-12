#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GAS.Editor.Validation
{
    /// <summary>
    /// [C] GAS 設定驗證器 — 純邏輯,負責掃全專案找出 GAS 相關資產的常見錯誤。
    /// UI 在 GASValidatorWindow。
    /// </summary>
    public static class GASValidator
    {
        public enum Severity { Error, Warning }

        public enum Category
        {
            TagReference,        // _tagName 指向 Library 不存在的 Tag
            AttributeName,       // GameplayModifier.AttributeName 拼錯
            SetByCallerMissing,  // HitEffect 的 modifier 缺 SetByCaller(傷害會固定)
            CueAssetMissing,     // HitCueTag 指向不存在的 Cue 資產
            AbilityTagDuplicate, // 不同 GameplayAbility 共用同個 AbilityTag
            HitEffectNull,       // MeleeHitWindow.HitEffect == null
        }

        public sealed class Issue
        {
            public Severity Severity;
            public Category Category;
            public UnityEngine.Object Asset;
            public string AssetPath;
            public string PropertyHint;
            public string Description;
            public string OffendingValue;  // TagReference 時放 unknown Tag 字串,供批次補 Library 用
            public Action AutoFix;

            public bool CanAutoFix => AutoFix != null;
        }

        // ====================================================================

        public static List<Issue> Run()
        {
            List<Issue> issues = new();

            HashSet<string> validTags = CollectValidTags();
            HashSet<string> validAttributes = CollectValidAttributeNames();
            HashSet<string> cueAssetTags = CollectCueAssetTags();

            // 集合所有 .asset / .prefab(過濾 Library 本身與備份)
            List<string> paths = CollectScannablePaths();

            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    if (i % 16 == 0)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Validating GAS assets",
                                $"{i + 1}/{paths.Count}: {path}",
                                (float)(i + 1) / paths.Count))
                        {
                            break;
                        }
                    }
                    ScanAsset(path, validTags, validAttributes, cueAssetTags, issues);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // 跨資產檢查: AbilityTag 重複
            CheckAbilityTagDuplicates(issues);

            return issues;
        }

        // ====================================================================
        // 收集合法值
        // ====================================================================

        private static HashSet<string> CollectValidTags()
        {
            HashSet<string> set = new(StringComparer.Ordinal);
            string libraryPath = "Assets/Resources/GameplayTagLibrary.asset";
            GameplayTagLibrary lib = AssetDatabase.LoadAssetAtPath<GameplayTagLibrary>(libraryPath);
            if (lib?.TagDefinitions != null)
            {
                foreach (GameplayTagLibrary.TagDefinition d in lib.TagDefinitions)
                {
                    if (!string.IsNullOrEmpty(d.TagName))
                    {
                        set.Add(d.TagName);
                    }
                }
            }
            return set;
        }

        private static HashSet<string> CollectValidAttributeNames()
        {
            HashSet<string> set = new(StringComparer.Ordinal);
            foreach (FieldInfo field in typeof(CombatAttributes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(string) && field.IsLiteral)
                {
                    string val = (string)field.GetValue(null);
                    if (!string.IsNullOrEmpty(val))
                    {
                        set.Add(val);
                    }
                }
            }
            return set;
        }

        private static HashSet<string> CollectCueAssetTags()
        {
            HashSet<string> set = new(StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets("t:GameplayCue", new[] { "Assets" });
            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                GameplayCue cue = AssetDatabase.LoadAssetAtPath<GameplayCue>(p);
                if (cue != null && cue.CueTag.IsValid)
                {
                    set.Add(cue.CueTag.TagName);
                }
            }
            return set;
        }

        private static List<string> CollectScannablePaths()
        {
            HashSet<string> paths = new(StringComparer.Ordinal);
            string[] soGuids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" });
            foreach (string g in soGuids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(g));
            }
            List<string> result = new();
            foreach (string p in paths)
            {
                if (p.Contains("/GameplayTagLibrary.asset")) continue;
                if (p.Contains("GameplayTagLibrary_Backups/")) continue;
                if (p.StartsWith("Packages/", StringComparison.Ordinal)) continue;
                result.Add(p);
            }
            return result;
        }

        // ====================================================================
        // 單一 asset 掃描
        // ====================================================================

        private static void ScanAsset(
            string path,
            HashSet<string> validTags,
            HashSet<string> validAttributes,
            HashSet<string> cueAssetTags,
            List<Issue> issues)
        {
            UnityEngine.Object[] objs = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (UnityEngine.Object o in objs)
            {
                if (o == null) continue;

                ScanTagReferences(o, path, validTags, issues);

                if (o is GameplayEffect effect)
                {
                    ScanGameplayEffect(effect, path, validAttributes, issues);
                }
                if (o is MeleeAttackData meleeData)
                {
                    ScanMeleeAttackData(meleeData, path, cueAssetTags, issues);
                }
            }
        }

        // ====================================================================
        // 檢查 1: Tag 引用斷裂 (通用 _tagName 字串)
        // ====================================================================

        private static void ScanTagReferences(
            UnityEngine.Object o, string path,
            HashSet<string> validTags, List<Issue> issues)
        {
            SerializedObject so = new(o);
            SerializedProperty iter = so.GetIterator();
            bool enterChildren = true;
            while (iter.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (iter.propertyType != SerializedPropertyType.String) continue;
                if (iter.name != "_tagName") continue;
                string val = iter.stringValue;
                if (string.IsNullOrEmpty(val)) continue;
                if (validTags.Contains(val)) continue;
                issues.Add(new Issue
                {
                    Severity = Severity.Error,
                    Category = Category.TagReference,
                    Asset = o,
                    AssetPath = path,
                    PropertyHint = iter.propertyPath,
                    Description = $"未知 Tag: '{val}' (不在 Library 中)",
                    OffendingValue = val,
                });
            }
        }

        // ====================================================================
        // 檢查 2 + 3: GameplayEffect 的 Modifier
        // ====================================================================

        private static void ScanGameplayEffect(
            GameplayEffect effect, string path,
            HashSet<string> validAttributes, List<Issue> issues)
        {
            if (effect.Modifiers == null) return;
            for (int i = 0; i < effect.Modifiers.Count; i++)
            {
                GameplayModifier m = effect.Modifiers[i];
                if (m == null) continue;

                // AttributeName 拼錯
                if (!string.IsNullOrEmpty(m.AttributeName) && !validAttributes.Contains(m.AttributeName))
                {
                    issues.Add(new Issue
                    {
                        Severity = Severity.Error,
                        Category = Category.AttributeName,
                        Asset = effect,
                        AssetPath = path,
                        PropertyHint = $"Modifiers[{i}].AttributeName",
                        Description = $"AttributeName '{m.AttributeName}' 不在 CombatAttributes — 可能拼錯。",
                    });
                }

                // 對 IncomingDamage 的 modifier 缺 SetByCaller → 傷害固定為 Magnitude(不可控)
                if (m.AttributeName == CombatAttributes.IncomingDamage
                    && m.MagnitudeType != ModifierMagnitudeType.SetByCaller)
                {
                    GameplayModifier captured = m;
                    GameplayEffect capturedEffect = effect;
                    int capturedIdx = i;
                    issues.Add(new Issue
                    {
                        Severity = Severity.Warning,
                        Category = Category.SetByCallerMissing,
                        Asset = effect,
                        AssetPath = path,
                        PropertyHint = $"Modifiers[{i}].MagnitudeType",
                        Description = $"對 IncomingDamage 的 Modifier 未使用 SetByCaller — 傷害會固定為 Magnitude={m.Magnitude:F1},無法依攻擊參數動態調整。",
                        AutoFix = () =>
                        {
                            captured.MagnitudeType = ModifierMagnitudeType.SetByCaller;
                            captured.SetByCallerDataTag = SetByCallerTags.DAMAGE;
                            EditorUtility.SetDirty(capturedEffect);
                            AssetDatabase.SaveAssetIfDirty(capturedEffect);
                        },
                    });
                }
            }
        }

        // ====================================================================
        // 檢查 4: MeleeAttackData 的 HitWindow / TimelineEvent
        // ====================================================================

        private static void ScanMeleeAttackData(
            MeleeAttackData data, string path,
            HashSet<string> cueAssetTags, List<Issue> issues)
        {
            if (data.HitWindows != null)
            {
                for (int i = 0; i < data.HitWindows.Count; i++)
                {
                    MeleeHitWindow w = data.HitWindows[i];
                    if (w == null) continue;

                    // HitEffect 為 null
                    if (w.HitEffect == null)
                    {
                        issues.Add(new Issue
                        {
                            Severity = Severity.Warning,
                            Category = Category.HitEffectNull,
                            Asset = data,
                            AssetPath = path,
                            PropertyHint = $"HitWindows[{i}].HitEffect",
                            Description = $"HitWindows[{i}].HitEffect 為 null — 此命中不會套用任何 GameplayEffect (傷害不會生效)。",
                        });
                    }
                    // HitCueTag 指向不存在的 Cue 資產
                    if (w.HitCueTag.IsValid && !cueAssetTags.Contains(w.HitCueTag.TagName))
                    {
                        issues.Add(new Issue
                        {
                            Severity = Severity.Warning,
                            Category = Category.CueAssetMissing,
                            Asset = data,
                            AssetPath = path,
                            PropertyHint = $"HitWindows[{i}].HitCueTag",
                            Description = $"HitCueTag '{w.HitCueTag.TagName}' 沒有對應的 GameplayCue 資產 — 命中時 Cue 不會觸發。",
                        });
                    }
                }
            }
            if (data.TimelineEvents != null)
            {
                for (int i = 0; i < data.TimelineEvents.Count; i++)
                {
                    TimelineEvent t = data.TimelineEvents[i];
                    if (t == null) continue;
                    if (t.CueTag.IsValid && !cueAssetTags.Contains(t.CueTag.TagName))
                    {
                        issues.Add(new Issue
                        {
                            Severity = Severity.Warning,
                            Category = Category.CueAssetMissing,
                            Asset = data,
                            AssetPath = path,
                            PropertyHint = $"TimelineEvents[{i}].CueTag",
                            Description = $"TimelineEvents[{i}] 的 CueTag '{t.CueTag.TagName}' 沒有對應的 GameplayCue 資產。",
                        });
                    }
                }
            }
        }

        // ====================================================================
        // 檢查 5: AbilityTag 重複 (跨資產)
        // ====================================================================

        private static void CheckAbilityTagDuplicates(List<Issue> issues)
        {
            Dictionary<string, List<GameplayAbility>> tagToAbilities = new(StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets("t:GameplayAbility", new[] { "Assets" });
            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                GameplayAbility a = AssetDatabase.LoadAssetAtPath<GameplayAbility>(p);
                if (a == null || !a.AbilityTag.IsValid) continue;
                string key = a.AbilityTag.TagName;
                if (!tagToAbilities.TryGetValue(key, out List<GameplayAbility> list))
                {
                    list = new List<GameplayAbility>();
                    tagToAbilities[key] = list;
                }
                list.Add(a);
            }
            // 反查 WeaponData,收集所有被武器引用的 GameplayAbility
            // 此 RPG 採「按武器分流」設計 — 多個 GA 共用 Tag 但屬於不同武器是預期行為,不報重複。
            HashSet<GameplayAbility> abilitiesUsedByWeapons = CollectWeaponReferencedAbilities();
            foreach (KeyValuePair<string, List<GameplayAbility>> kv in tagToAbilities)
            {
                if (kv.Value.Count <= 1) continue;

                // 判定 1: 所有重複 GA 都被某 WeaponData 引用
                bool allWeaponBound = true;
                foreach (GameplayAbility a in kv.Value)
                {
                    if (!abilitiesUsedByWeapons.Contains(a))
                    {
                        allWeaponBound = false;
                        break;
                    }
                }
                // 判定 2: 所有重複 GA 都是同一個 C# 型別
                // (例如 4 個 GA_Bow_Dodge / GA_Katana_Dodge 都是 GA_Dodge 型別,屬於武器分流命名慣例)
                bool allSameType = true;
                Type firstType = kv.Value[0].GetType();
                foreach (GameplayAbility a in kv.Value)
                {
                    if (a.GetType() != firstType)
                    {
                        allSameType = false;
                        break;
                    }
                }

                if (allWeaponBound || allSameType)
                {
                    // 武器分流設計(同型別兄弟 GA 或被武器引用),預期行為,不報。
                    continue;
                }
                string names = string.Join(", ", kv.Value.ConvertAll(a => a.name));
                foreach (GameplayAbility a in kv.Value)
                {
                    issues.Add(new Issue
                    {
                        Severity = Severity.Warning,
                        Category = Category.AbilityTagDuplicate,
                        Asset = a,
                        AssetPath = AssetDatabase.GetAssetPath(a),
                        PropertyHint = "AbilityTag",
                        Description = $"AbilityTag '{kv.Key}' 被 {kv.Value.Count} 個能力共用: {names}。同 Tag 會互相干擾 (TryActivateAbility 只能找到第一個)。",
                    });
                }
            }
        }

        private static HashSet<GameplayAbility> CollectWeaponReferencedAbilities()
        {
            HashSet<GameplayAbility> set = new();
            string[] guids = AssetDatabase.FindAssets("t:WeaponData", new[] { "Assets" });
            foreach (string g in guids)
            {
                WeaponData wd = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(g));
                if (wd == null) continue;
                if (wd.AttackAbility != null) set.Add(wd.AttackAbility);
                if (wd.HeavyAttackAbility != null) set.Add(wd.HeavyAttackAbility);
                if (wd.DodgeAbility != null) set.Add(wd.DodgeAbility);
                if (wd.ParryAssistAbility != null) set.Add(wd.ParryAssistAbility);
                if (wd.DodgeAssistAbility != null) set.Add(wd.DodgeAssistAbility);
            }
            return set;
        }
    }
}
#endif
