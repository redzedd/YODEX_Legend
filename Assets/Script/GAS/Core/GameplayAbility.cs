using System;
using System.Collections;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 遊戲能力基類 - ScriptableObject
    /// 定義一個可執行的能力（如攻擊、閃避、技能等）
    /// </summary>
    public abstract class GameplayAbility : ScriptableObject
    {
        [Header("Basic Info")]
        [Tooltip("能力名稱")]
        public string AbilityName;

        [Tooltip("能力標籤 (唯一識別)")]
        public GameplayTag AbilityTag;

        [TextArea(2, 4)]
        [Tooltip("能力描述")]
        public string Description;

        [Header("Activation")]
        [Tooltip("能力等級")]
        public int AbilityLevel = 1;

        [Tooltip("是否可以在被其他能力打斷後重新啟動")]
        public bool CanReactivateWhileActive = false;

        [Tooltip("能力結束後的冷卻效果")]
        public GameplayEffect CooldownEffect;

        [Tooltip("使用能力的消耗效果")]
        public GameplayEffect CostEffect;

        [Header("Activation Tags")]
        [Tooltip("啟動能力所需的標籤 (擁有者必須有這些標籤)")]
        public GameplayTagContainer ActivationRequiredTags = new();

        [Tooltip("阻止能力啟動的標籤 (擁有者不能有這些標籤)")]
        public GameplayTagContainer ActivationBlockedTags = new();

        [Tooltip("能力啟動時賦予擁有者的標籤")]
        public GameplayTagContainer ActivationOwnedTags = new();

        [Header("Blocking & Cancellation")]
        [Tooltip("此能力會阻止帶有這些標籤的能力啟動")]
        public GameplayTagContainer BlockAbilitiesWithTags = new();

        [Tooltip("此能力會取消帶有這些標籤的正在執行的能力")]
        public GameplayTagContainer CancelAbilitiesWithTags = new();

        [Tooltip("帶有這些標籤的能力可以取消此能力")]
        public GameplayTagContainer CancelledByTags = new();

        #region Abstract Methods

        /// <summary>
        /// 能力執行的主要邏輯 - 子類必須實現
        /// </summary>
        public abstract void ActivateAbility(GameplayAbilitySpec spec);

        /// <summary>
        /// 能力結束時調用 - 子類可覆寫
        /// </summary>
        public virtual void EndAbility(GameplayAbilitySpec spec, bool wasCancelled)
        {
            // 移除啟動時賦予的標籤
            if (spec.Owner != null && !ActivationOwnedTags.IsEmpty)
            {
                spec.Owner.OwnedTags.RemoveTags(ActivationOwnedTags);
            }

            // 應用冷卻
            if (!wasCancelled && CooldownEffect != null && spec.Owner != null)
            {
                spec.Owner.ApplyEffectToSelf(CooldownEffect);
            }
        }

        #endregion

        #region Activation Checks

        /// <summary>
        /// 檢查能力是否可以啟動
        /// </summary>
        public virtual bool CanActivateAbility(GameplayAbilitySpec spec)
        {
            if (spec?.Owner == null) return false;

            var owner = spec.Owner;

            // 檢查是否已在執行中
            if (spec.IsActive && !CanReactivateWhileActive)
            {
                return false;
            }

            // 檢查冷卻
            if (IsOnCooldown(owner))
            {
                return false;
            }

            // 檢查消耗
            if (!CanPayCost(spec))
            {
                return false;
            }

            // 檢查標籤條件
            if (!CheckTagRequirements(owner))
            {
                return false;
            }

            // 檢查是否被其他能力阻止
            if (IsBlockedByOtherAbilities(owner))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 檢查標籤需求
        /// </summary>
        protected virtual bool CheckTagRequirements(AbilitySystemComponent owner)
        {
            var ownedTags = owner.OwnedTags;

            // 檢查必需標籤
            if (!ActivationRequiredTags.IsEmpty && !ownedTags.HasAll(ActivationRequiredTags))
            {
                return false;
            }

            // 檢查阻止標籤
            if (!ActivationBlockedTags.IsEmpty && ownedTags.HasAny(ActivationBlockedTags))
            {
                return false;
            }

            // 受擊硬直期間阻止所有能力啟動
            if (ownedTags.HasTag(GameplayTags.State.HitStunned))
            {
                return false;
            }

            // 招架期間阻止所有能力啟動 — DefensiveAssistResponder / GA_ParryAssist 維護該 Tag
            // 防止玩家在 ParryStart / ParryHold / ParryEnd 期間插攻擊造成 spec.IsActive 卡死
            if (ownedTags.HasTag(GameplayTags.State.Parrying))
            {
                return false;
            }

            // Dodge 鎖定期間阻止攻擊類能力啟動 — 由 NewGASPlayerController 同步 IsDodgeLocked 維護該 Tag
            if (ownedTags.HasTag(GameplayTags.State.DodgeNonCancellable)
                && AbilityTag.MatchesTagHierarchy(GameplayTags.Ability.Attack.Root))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 檢查是否在冷卻中
        /// </summary>
        protected virtual bool IsOnCooldown(AbilitySystemComponent owner)
        {
            if (CooldownEffect == null) return false;

            // 檢查是否有冷卻效果的標籤
            return owner.ActiveEffects.HasEffectWithTag(CooldownEffect.EffectTag);
        }

        /// <summary>
        /// 檢查是否可以支付消耗
        /// </summary>
        protected virtual bool CanPayCost(GameplayAbilitySpec spec)
        {
            if (CostEffect == null) return true;

            // 檢查每個消耗修改器
            foreach (var modifier in CostEffect.Modifiers)
            {
                var attr = spec.Owner.GetAttributeSet()?.GetAttribute(modifier.AttributeName);
                if (attr != null)
                {
                    float cost = modifier.CalculateMagnitude(null);
                    if (attr.CurrentValue < Mathf.Abs(cost))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 支付能力消耗
        /// </summary>
        protected virtual void PayCost(GameplayAbilitySpec spec)
        {
            if (CostEffect != null && spec.Owner != null)
            {
                spec.Owner.ApplyEffectToSelf(CostEffect);
            }
        }

        /// <summary>
        /// 檢查是否被其他能力阻止
        /// </summary>
        protected virtual bool IsBlockedByOtherAbilities(AbilitySystemComponent owner)
        {
            foreach (var otherSpec in owner.GetAllAbilities())
            {
                if (!otherSpec.IsActive) continue;
                if (otherSpec.AbilityDef == this) continue;

                if (!otherSpec.AbilityDef.BlockAbilitiesWithTags.IsEmpty &&
                    otherSpec.AbilityDef.BlockAbilitiesWithTags.HasTag(AbilityTag))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 嘗試取消其他能力
        /// </summary>
        protected void TryCancelOtherAbilities(GameplayAbilitySpec spec)
        {
            if (CancelAbilitiesWithTags.IsEmpty) return;

            foreach (var otherSpec in spec.Owner.GetAllAbilities())
            {
                if (!otherSpec.IsActive) continue;
                if (otherSpec == spec) continue;

                if (CancelAbilitiesWithTags.HasTag(otherSpec.AbilityDef.AbilityTag))
                {
                    otherSpec.CancelAbility();
                }
            }
        }

        /// <summary>
        /// 應用效果到自身
        /// </summary>
        protected GameplayEffectSpec ApplyEffectToSelf(GameplayAbilitySpec abilitySpec, GameplayEffect effect)
        {
            if (effect == null || abilitySpec.Owner == null) return null;
            return abilitySpec.Owner.ApplyEffectToSelf(effect);
        }

        /// <summary>
        /// 應用效果到目標
        /// </summary>
        protected GameplayEffectSpec ApplyEffectToTarget(GameplayAbilitySpec abilitySpec, 
            AbilitySystemComponent target, GameplayEffect effect)
        {
            if (effect == null || target == null) return null;
            return abilitySpec.Owner.ApplyEffectToTarget(target, effect);
        }

        /// <summary>
        /// 啟動 Coroutine
        /// </summary>
        protected Coroutine StartCoroutine(GameplayAbilitySpec spec, IEnumerator routine)
        {
            if (spec?.Owner == null) return null;
            return spec.Owner.StartCoroutine(routine);
        }

        /// <summary>
        /// 停止 Coroutine
        /// </summary>
        protected void StopCoroutine(GameplayAbilitySpec spec, Coroutine routine)
        {
            if (spec?.Owner == null || routine == null) return;
            spec.Owner.StopCoroutine(routine);
        }

        /// <summary>
        /// 觸發 Gameplay Cue
        /// </summary>
        protected void ExecuteGameplayCue(GameplayAbilitySpec spec, GameplayTag cueTag, 
            Vector3? location = null, GameObject target = null)
        {
            if (spec?.Owner == null) return;
            spec.Owner.ExecuteGameplayCue(cueTag, location, target);
        }

        #endregion

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (string.IsNullOrEmpty(AbilityName))
            {
                AbilityName = name;
            }
        }
#endif
    }
}
