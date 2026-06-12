using System;
using System.Collections.Generic;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 效果實例 - 運行時的效果數據
    /// 包含實際計算的數值和運行狀態
    /// </summary>
    public class GameplayEffectSpec
    {
        /// <summary>
        /// 效果定義
        /// </summary>
        public GameplayEffect EffectDef { get; private set; }

        /// <summary>
        /// 效果施放者
        /// </summary>
        public AbilitySystemComponent Instigator { get; private set; }

        /// <summary>
        /// 效果目標
        /// </summary>
        public AbilitySystemComponent Target { get; private set; }

        /// <summary>
        /// 效果等級 (用於縮放計算)
        /// </summary>
        public float Level { get; private set; }

        /// <summary>
        /// 當前堆疊層數
        /// </summary>
        public int StackCount { get; private set; }

        /// <summary>
        /// 效果開始時間
        /// </summary>
        public float StartTime { get; private set; }

        /// <summary>
        /// 剩餘持續時間
        /// </summary>
        public float RemainingDuration { get; private set; }

        /// <summary>
        /// 下次週期執行的時間
        /// </summary>
        public float NextPeriodicTime { get; private set; }

        /// <summary>
        /// 效果是否已過期
        /// </summary>
        public bool IsExpired { get; private set; }

        /// <summary>
        /// 唯一標識符
        /// </summary>
        public Guid Handle { get; private set; }

        // 快取計算後的修改器數值
        private readonly Dictionary<GameplayModifier, float> _calculatedMagnitudes = new();

        // 由呼叫方動態設定的數值（SetByCaller 機制）
        private Dictionary<string, float> _setByCallerMagnitudes;

        // 當效果過期時觸發
        public event Action<GameplayEffectSpec> OnExpired;

        // 當效果被移除時觸發
        public event Action<GameplayEffectSpec> OnRemoved;

        // 當堆疊數變化時觸發
        public event Action<GameplayEffectSpec, int, int> OnStackChanged;

        public GameplayEffectSpec(GameplayEffect effectDef, AbilitySystemComponent instigator, 
            AbilitySystemComponent target, float level = 1f)
        {
            EffectDef = effectDef;
            Instigator = instigator;
            Target = target;
            Level = level;
            StackCount = 1;
            Handle = Guid.NewGuid();
            IsExpired = false;

            StartTime = Time.time;
            RemainingDuration = effectDef.HasDuration ? effectDef.Duration : float.MaxValue;

            // 預計算所有修改器的數值
            CalculateAllMagnitudes();

            // 設置首次週期執行時間
            if (effectDef.IsPeriodic)
            {
                bool executeOnStart = effectDef.PeriodicPolicy == PeriodicPolicy.ExecuteOnStart ||
                                     effectDef.PeriodicPolicy == PeriodicPolicy.ExecuteOnStartAndInterval;
                
                NextPeriodicTime = executeOnStart ? StartTime : StartTime + effectDef.Period;
            }
        }

        /// <summary>
        /// 計算所有修改器的數值
        /// </summary>
        private void CalculateAllMagnitudes()
        {
            _calculatedMagnitudes.Clear();

            foreach (var modifier in EffectDef.Modifiers)
            {
                float magnitude = modifier.CalculateMagnitude(this);
                _calculatedMagnitudes[modifier] = magnitude;
            }
        }

        /// <summary>
        /// 獲取指定修改器的計算後數值
        /// SetByCaller 類型的 Modifier 會即時從動態數值中取值（因為建構時尚未設定）
        /// </summary>
        public float GetCalculatedMagnitude(GameplayModifier modifier)
        {
            float value;
            if (modifier.MagnitudeType == ModifierMagnitudeType.SetByCaller)
            {
                // SetByCaller 必須即時計算，因為值在建構後才被設定
                value = modifier.CalculateMagnitude(this);
            }
            else if (!_calculatedMagnitudes.TryGetValue(modifier, out value))
            {
                return 0f;
            }
            // 應用堆疊倍率
            return value * (1f + (StackCount - 1) * EffectDef.StackMagnitudeMultiplier);
        }

        /// <summary>
        /// 設定由呼叫方傳入的動態數值（例如攻擊計算後的傷害）
        /// </summary>
        public void SetSetByCallerMagnitude(string dataTag, float magnitude)
        {
            _setByCallerMagnitudes ??= new Dictionary<string, float>();
            _setByCallerMagnitudes[dataTag] = magnitude;
        }

        /// <summary>
        /// 取得由呼叫方設定的動態數值
        /// </summary>
        public float GetSetByCallerMagnitude(string dataTag, float defaultValue = 0f)
        {
            if (_setByCallerMagnitudes != null && _setByCallerMagnitudes.TryGetValue(dataTag, out float value))
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// 更新效果狀態
        /// </summary>
        public void Update(float deltaTime)
        {
            if (IsExpired) return;

            // 更新持續時間
            if (EffectDef.HasDuration)
            {
                RemainingDuration -= deltaTime;
                if (RemainingDuration <= 0f)
                {
                    Expire();
                    return;
                }
            }

            // 檢查持續條件
            if (!CheckOngoingRequirements())
            {
                Expire();
                return;
            }
        }

        /// <summary>
        /// 檢查是否應該執行週期效果
        /// </summary>
        public bool ShouldExecutePeriodic()
        {
            if (!EffectDef.IsPeriodic || IsExpired) return false;
            return Time.time >= NextPeriodicTime;
        }

        /// <summary>
        /// 標記週期效果已執行
        /// </summary>
        public void MarkPeriodicExecuted()
        {
            NextPeriodicTime = Time.time + EffectDef.Period;
        }

        /// <summary>
        /// 檢查持續條件
        /// </summary>
        private bool CheckOngoingRequirements()
        {
            if (Target == null) return false;
            
            if (!EffectDef.OngoingRequiredTags.IsEmpty)
            {
                return Target.OwnedTags.HasAll(EffectDef.OngoingRequiredTags);
            }
            
            return true;
        }

        /// <summary>
        /// 增加堆疊層數
        /// </summary>
        public bool AddStack()
        {
            if (StackCount >= EffectDef.MaxStacks) return false;

            int oldCount = StackCount;
            StackCount++;
            OnStackChanged?.Invoke(this, oldCount, StackCount);
            return true;
        }

        /// <summary>
        /// 移除堆疊層數
        /// </summary>
        public bool RemoveStack()
        {
            if (StackCount <= 1) return false;

            int oldCount = StackCount;
            StackCount--;
            OnStackChanged?.Invoke(this, oldCount, StackCount);
            return true;
        }

        /// <summary>
        /// 設置堆疊層數
        /// </summary>
        public void SetStackCount(int count)
        {
            int oldCount = StackCount;
            StackCount = Mathf.Clamp(count, 1, EffectDef.MaxStacks);
            
            if (oldCount != StackCount)
            {
                OnStackChanged?.Invoke(this, oldCount, StackCount);
            }
        }

        /// <summary>
        /// 刷新持續時間
        /// </summary>
        public void RefreshDuration()
        {
            if (EffectDef.HasDuration)
            {
                RemainingDuration = EffectDef.Duration;
            }
        }

        /// <summary>
        /// 效果過期
        /// </summary>
        public void Expire()
        {
            if (IsExpired) return;
            
            IsExpired = true;
            OnExpired?.Invoke(this);
        }

        /// <summary>
        /// 手動移除效果
        /// </summary>
        public void Remove()
        {
            IsExpired = true;
            OnRemoved?.Invoke(this);
        }

        /// <summary>
        /// 獲取效果已持續的時間
        /// </summary>
        public float GetElapsedTime()
        {
            return Time.time - StartTime;
        }

        /// <summary>
        /// 獲取持續時間進度 (0~1)
        /// </summary>
        public float GetDurationProgress()
        {
            if (!EffectDef.HasDuration || EffectDef.Duration <= 0f)
                return 0f;
            
            return 1f - (RemainingDuration / EffectDef.Duration);
        }

        public override string ToString()
        {
            string stackInfo = StackCount > 1 ? $" x{StackCount}" : "";
            string durationInfo = EffectDef.HasDuration ? $" ({RemainingDuration:F1}s)" : "";
            return $"{EffectDef.EffectName}{stackInfo}{durationInfo}";
        }
    }

    /// <summary>
    /// 效果容器 - 管理一組活躍的效果
    /// </summary>
    public class ActiveGameplayEffectsContainer
    {
        private readonly List<GameplayEffectSpec> _activeEffects = new();
        private readonly AbilitySystemComponent _owner;

        /// <summary>
        /// 當前活躍的效果數量
        /// </summary>
        public int Count => _activeEffects.Count;

        /// <summary>
        /// 當效果被添加時觸發
        /// </summary>
        public event Action<GameplayEffectSpec> OnEffectAdded;

        /// <summary>
        /// 當效果被移除時觸發
        /// </summary>
        public event Action<GameplayEffectSpec> OnEffectRemoved;

        public ActiveGameplayEffectsContainer(AbilitySystemComponent owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// 添加效果
        /// </summary>
        public GameplayEffectSpec AddEffect(GameplayEffectSpec spec)
        {
            // 檢查堆疊
            var existing = FindExistingEffect(spec.EffectDef);
            if (existing != null)
            {
                return HandleStacking(existing, spec);
            }

            _activeEffects.Add(spec);
            spec.OnExpired += HandleEffectExpired;
            spec.OnRemoved += HandleEffectRemoved;
            
            OnEffectAdded?.Invoke(spec);
            return spec;
        }

        /// <summary>
        /// 處理效果堆疊
        /// </summary>
        private GameplayEffectSpec HandleStacking(GameplayEffectSpec existing, GameplayEffectSpec newSpec)
        {
            switch (existing.EffectDef.StackingPolicy)
            {
                case StackingPolicy.None:
                    // 替換舊效果
                    RemoveEffect(existing);
                    return AddEffect(newSpec);

                case StackingPolicy.StackCount:
                    existing.AddStack();
                    return existing;

                case StackingPolicy.RefreshDuration:
                    existing.RefreshDuration();
                    return existing;

                case StackingPolicy.StackAndRefresh:
                    existing.AddStack();
                    existing.RefreshDuration();
                    return existing;

                default:
                    return existing;
            }
        }

        /// <summary>
        /// 尋找相同效果定義的現有效果
        /// </summary>
        private GameplayEffectSpec FindExistingEffect(GameplayEffect effectDef)
        {
            foreach (var spec in _activeEffects)
            {
                if (spec.EffectDef == effectDef && !spec.IsExpired)
                {
                    return spec;
                }
            }
            return null;
        }

        /// <summary>
        /// 移除效果
        /// </summary>
        public bool RemoveEffect(GameplayEffectSpec spec)
        {
            if (_activeEffects.Remove(spec))
            {
                spec.Remove();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 根據標籤移除效果
        /// </summary>
        public int RemoveEffectsWithTag(GameplayTag tag)
        {
            int removed = 0;
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var spec = _activeEffects[i];
                if (spec.EffectDef.EffectTag.MatchesTagHierarchy(tag))
                {
                    RemoveEffect(spec);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// 更新所有效果
        /// </summary>
        public void Update(float deltaTime)
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var spec = _activeEffects[i];
                spec.Update(deltaTime);
            }

            // 清理過期效果
            _activeEffects.RemoveAll(s => s.IsExpired);
        }

        /// <summary>
        /// 獲取所有活躍效果
        /// </summary>
        public IReadOnlyList<GameplayEffectSpec> GetAllEffects()
        {
            return _activeEffects;
        }

        /// <summary>
        /// 檢查是否有指定標籤的效果
        /// </summary>
        public bool HasEffectWithTag(GameplayTag tag)
        {
            foreach (var spec in _activeEffects)
            {
                if (!spec.IsExpired && spec.EffectDef.EffectTag.MatchesTagHierarchy(tag))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 清除所有效果
        /// </summary>
        public void Clear()
        {
            foreach (var spec in _activeEffects)
            {
                spec.Remove();
            }
            _activeEffects.Clear();
        }

        private void HandleEffectExpired(GameplayEffectSpec spec)
        {
            OnEffectRemoved?.Invoke(spec);
        }

        private void HandleEffectRemoved(GameplayEffectSpec spec)
        {
            OnEffectRemoved?.Invoke(spec);
        }
    }
}
