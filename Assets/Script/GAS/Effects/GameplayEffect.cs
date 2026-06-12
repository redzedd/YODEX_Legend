using System;
using System.Collections.Generic;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 遊戲效果定義 - ScriptableObject
    /// 定義效果如何修改屬性、持續時間、條件等
    /// </summary>
    [CreateAssetMenu(fileName = "New GameplayEffect", menuName = "GAS/Gameplay Effect")]
    public class GameplayEffect : ScriptableObject
    {
        [Header("Basic Info")]
        [Tooltip("效果名稱")]
        public string EffectName;

        [Tooltip("效果標籤 (用於識別和查詢)")]
        public GameplayTag EffectTag;

        [TextArea(2, 4)]
        [Tooltip("效果描述")]
        public string Description;

        [Header("Duration")]
        [Tooltip("持續時間策略")]
        public DurationPolicy DurationPolicy = DurationPolicy.Instant;

        [Tooltip("持續時間 (秒)，僅 Duration 策略時有效")]
        public float Duration = 0f;

        [Header("Periodic")]
        [Tooltip("週期執行策略")]
        public PeriodicPolicy PeriodicPolicy = PeriodicPolicy.None;

        [Tooltip("週期間隔 (秒)")]
        public float Period = 1f;

        [Header("Stacking")]
        [Tooltip("堆疊策略")]
        public StackingPolicy StackingPolicy = StackingPolicy.None;

        [Tooltip("最大堆疊層數")]
        public int MaxStacks = 1;

        [Tooltip("每層的數值縮放")]
        public float StackMagnitudeMultiplier = 1f;

        [Header("Modifiers")]
        [Tooltip("屬性修改器列表")]
        public List<GameplayModifier> Modifiers = new();

        [Header("Tags")]
        [Tooltip("效果啟動時賦予目標的標籤")]
        public GameplayTagContainer GrantedTags = new();

        [Tooltip("效果結束時移除的標籤")]
        public GameplayTagContainer RemoveTagsOnEnd = new();

        [Header("Conditions")]
        [Tooltip("應用效果所需的標籤 (目標必須有)")]
        public GameplayTagContainer ApplicationRequiredTags = new();

        [Tooltip("阻止效果應用的標籤 (目標不能有)")]
        public GameplayTagContainer ApplicationBlockedTags = new();

        [Tooltip("效果持續期間所需的標籤 (失去則移除效果)")]
        public GameplayTagContainer OngoingRequiredTags = new();

        [Header("Cues")]
        [Tooltip("效果觸發時執行的 Cue 標籤")]
        public List<GameplayTag> CueTags = new();

        [Header("Removal")]
        [Tooltip("移除帶有這些標籤的其他效果")]
        public GameplayTagContainer RemoveEffectsWithTags = new();

        /// <summary>
        /// 效果是否為即時效果
        /// </summary>
        public bool IsInstant => DurationPolicy == DurationPolicy.Instant;

        /// <summary>
        /// 效果是否有持續時間
        /// </summary>
        public bool HasDuration => DurationPolicy == DurationPolicy.Duration;

        /// <summary>
        /// 效果是否為無限持續
        /// </summary>
        public bool IsInfinite => DurationPolicy == DurationPolicy.Infinite;

        /// <summary>
        /// 效果是否有週期性執行
        /// </summary>
        public bool IsPeriodic => PeriodicPolicy != PeriodicPolicy.None && Period > 0f;

        /// <summary>
        /// 創建效果實例 (Spec)
        /// </summary>
        public GameplayEffectSpec CreateSpec(AbilitySystemComponent instigator, AbilitySystemComponent target, float level = 1f)
        {
            return new GameplayEffectSpec(this, instigator, target, level);
        }

        /// <summary>
        /// 檢查效果是否可以應用到目標
        /// </summary>
        public bool CanApplyTo(AbilitySystemComponent target)
        {
            if (target == null) return false;

            var targetTags = target.OwnedTags;

            // 檢查必需標籤
            if (!ApplicationRequiredTags.IsEmpty && !targetTags.HasAll(ApplicationRequiredTags))
            {
                return false;
            }

            // 檢查阻止標籤
            if (!ApplicationBlockedTags.IsEmpty && targetTags.HasAny(ApplicationBlockedTags))
            {
                return false;
            }

            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 自動設置效果名稱
            if (string.IsNullOrEmpty(EffectName))
            {
                EffectName = name;
            }

            // 確保週期大於 0
            if (Period <= 0f && PeriodicPolicy != PeriodicPolicy.None)
            {
                Period = 0.1f;
            }

            // 確保堆疊層數至少為 1
            if (MaxStacks < 1)
            {
                MaxStacks = 1;
            }
        }
#endif
    }
}
