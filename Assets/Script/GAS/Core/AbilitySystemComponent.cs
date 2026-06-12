using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace GAS
{
    /// <summary>
    /// 能力系統組件 - 掛載於角色上，管理所有能力、效果和屬性
    /// 這是 GAS 系統的核心組件
    /// </summary>
    public class AbilitySystemComponent : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("初始授予的能力列表")]
        [SerializeField] private List<GameplayAbility> _startingAbilities = new();

        [Header("Debug")]
        [Tooltip("啟用調試日誌")]
        [FormerlySerializedAs("DebugMode")]
        [SerializeField] private bool _debugMode = false;
        public bool DebugMode => _debugMode;

        // === 核心數據 ===
        
        /// <summary>
        /// 擁有的標籤
        /// </summary>
        public GameplayTagContainer OwnedTags { get; private set; } = new();

        /// <summary>
        /// 活躍的效果容器
        /// </summary>
        public ActiveGameplayEffectsContainer ActiveEffects { get; private set; }

        // 授予的能力
        private readonly List<GameplayAbilitySpec> _grantedAbilities = new();

        // 屬性集
        private AttributeSet _attributeSet;

        // Cue 管理器引用
        private GameplayCueManager _cueManager;

        // === 事件 ===

        /// <summary>
        /// 當能力啟動時觸發
        /// </summary>
        public event Action<GameplayAbilitySpec> OnAbilityActivated;

        /// <summary>
        /// 當能力結束時觸發
        /// </summary>
        public event Action<GameplayAbilitySpec, bool> OnAbilityEnded;

        #region Unity Lifecycle

        private void Awake()
        {
            // 初始化效果容器
            ActiveEffects = new ActiveGameplayEffectsContainer(this);
            ActiveEffects.OnEffectAdded += HandleEffectAdded;
            ActiveEffects.OnEffectRemoved += HandleEffectRemoved;

            // 初始化屬性集
            InitializeAttributeSet();

            // 嘗試獲取 Cue 管理器
            _cueManager = FindFirstObjectByType<GameplayCueManager>();
        }

        private void Start()
        {
            // 授予初始能力
            foreach (var ability in _startingAbilities)
            {
                if (ability != null)
                {
                    GiveAbility(ability);
                }
            }
        }

        private void Update()
        {
            // 更新活躍效果
            ActiveEffects.Update(Time.deltaTime);

            // 處理週期效果
            ProcessPeriodicEffects();
        }

        private void OnDestroy()
        {
            // 清理
            ActiveEffects.Clear();
            _grantedAbilities.Clear();
            _attributeSet?.Cleanup();
        }

        #endregion

        #region Attribute Set

        /// <summary>
        /// 初始化屬性集
        /// </summary>
        private void InitializeAttributeSet()
        {
            _attributeSet = new CombatAttributeSet();
            _attributeSet.Initialize(this);
        }

        /// <summary>
        /// 獲取屬性集
        /// </summary>
        public AttributeSet GetAttributeSet()
        {
            return _attributeSet;
        }

        /// <summary>
        /// 獲取特定類型的屬性集
        /// </summary>
        public T GetAttributeSet<T>() where T : AttributeSet
        {
            return _attributeSet as T;
        }

        #endregion

        #region Ability Management

        /// <summary>
        /// 授予能力
        /// </summary>
        public GameplayAbilitySpec GiveAbility(GameplayAbility ability, int level = 1)
        {
            if (ability == null) return null;

            // 檢查是否已擁有
            var existing = FindAbilitySpec(ability.AbilityTag);
            if (existing != null)
            {
                if (DebugMode)
                {
                    Debug.Log($"[ASC] Ability {ability.AbilityName} already granted");
                }
                return existing;
            }

            var spec = new GameplayAbilitySpec(ability, this, level);
            spec.OnActivated += HandleAbilityActivated;
            spec.OnEnded += HandleAbilityEnded;

            _grantedAbilities.Add(spec);

            if (DebugMode)
            {
                Debug.Log($"[ASC] Granted ability: {ability.AbilityName}");
            }

            return spec;
        }

        /// <summary>
        /// 移除能力
        /// </summary>
        public bool RemoveAbility(GameplayTag abilityTag)
        {
            var spec = FindAbilitySpec(abilityTag);
            if (spec == null) return false;

            // 如果正在執行，先取消
            if (spec.IsActive)
            {
                spec.CancelAbility();
            }

            spec.OnActivated -= HandleAbilityActivated;
            spec.OnEnded -= HandleAbilityEnded;

            _grantedAbilities.Remove(spec);

            if (DebugMode)
            {
                Debug.Log($"[ASC] Removed ability: {spec.AbilityDef.AbilityName}");
            }

            return true;
        }

        /// <summary>
        /// 嘗試啟動能力 (根據標籤)
        /// </summary>
        public bool TryActivateAbility(GameplayTag abilityTag)
        {
            var spec = FindAbilitySpec(abilityTag);
            if (spec == null)
            {
                if (DebugMode)
                {
                    Debug.LogWarning($"[ASC] Ability not found: {abilityTag}");
                }
                return false;
            }

            return spec.TryActivate();
        }

        /// <summary>
        /// 取消能力 (根據標籤)
        /// </summary>
        public void CancelAbility(GameplayTag abilityTag)
        {
            var spec = FindAbilitySpec(abilityTag);
            spec?.CancelAbility();
        }

        /// <summary>
        /// 尋找能力 Spec
        /// </summary>
        public GameplayAbilitySpec FindAbilitySpec(GameplayTag abilityTag)
        {
            foreach (var spec in _grantedAbilities)
            {
                if (spec.AbilityDef.AbilityTag.MatchesTag(abilityTag))
                {
                    return spec;
                }
            }
            return null;
        }

        /// <summary>
        /// 獲取所有授予的能力(含已結束的)。
        /// 回傳具體 List 型別以利 foreach 使用 struct enumerator → 零 GC 分配。
        /// 呼叫端需自行以 <c>spec.IsActive</c> 過濾活躍者。
        /// <b>警告:禁止對回傳 List 執行 Add/Remove/Clear 等修改操作 — 會直接污染 ASC 內部狀態。</b>
        /// </summary>
        public List<GameplayAbilitySpec> GetAllAbilities()
        {
            return _grantedAbilities;
        }

        #endregion

        #region Effect Management

        /// <summary>
        /// 對自己應用效果
        /// </summary>
        public GameplayEffectSpec ApplyEffectToSelf(GameplayEffect effect, float level = 1f)
        {
            return ApplyEffectToTarget(this, effect, level);
        }

        /// <summary>
        /// 對目標應用效果
        /// </summary>
        public GameplayEffectSpec ApplyEffectToTarget(AbilitySystemComponent target, GameplayEffect effect, float level = 1f)
        {
            return ApplyEffectToTargetInternal(target, effect, null, 0f, level);
        }

        /// <summary>
        /// 對目標應用效果，並注入單一 SetByCaller 動態數值（最常用：注入傷害）
        /// </summary>
        public GameplayEffectSpec ApplyEffectToTarget(
            AbilitySystemComponent target,
            GameplayEffect effect,
            string setByCallerTag,
            float setByCallerValue,
            float level = 1f)
        {
            return ApplyEffectToTargetInternal(target, effect, setByCallerTag, setByCallerValue, level);
        }

        private GameplayEffectSpec ApplyEffectToTargetInternal(
            AbilitySystemComponent target,
            GameplayEffect effect,
            string setByCallerTag,
            float setByCallerValue,
            float level)
        {
            if (effect == null || target == null) return null;
            // 檢查效果是否可以應用
            if (!effect.CanApplyTo(target))
            {
                if (DebugMode)
                {
                    Debug.Log($"[ASC] Effect {effect.EffectName} cannot be applied to target");
                }
                return null;
            }
            // 創建效果實例
            var spec = effect.CreateSpec(this, target, level);
            // 注入 SetByCaller 數值
            if (!string.IsNullOrEmpty(setByCallerTag))
            {
                spec.SetSetByCallerMagnitude(setByCallerTag, setByCallerValue);
            }
            // 處理即時效果
            if (effect.IsInstant)
            {
                ExecuteEffect(spec);
                return spec;
            }
            // 添加到目標的活躍效果
            return target.ActiveEffects.AddEffect(spec);
        }

        /// <summary>
        /// 執行效果 (應用修改器)
        /// </summary>
        private void ExecuteEffect(GameplayEffectSpec spec)
        {
            if (spec?.Target == null) return;

            var targetSet = spec.Target.GetAttributeSet();
            if (targetSet == null) return;

            // 應用所有修改器
            foreach (var modifier in spec.EffectDef.Modifiers)
            {
                var attr = targetSet.GetAttribute(modifier.AttributeName);
                if (attr != null)
                {
                    float magnitude = spec.GetCalculatedMagnitude(modifier);
                    if (spec.EffectDef.IsInstant)
                    {
                        // 即時效果：計算新值後交由 PreAttributeChange 驗證
                        float newValue = attr.BaseValue + magnitude;
                        targetSet.PreAttributeChange(attr, ref newValue);
                        attr.BaseValue = newValue;
                    }
                    else
                    {
                        // 持續效果使用修改器
                        targetSet.PreAttributeChange(attr, ref magnitude);
                        modifier.ApplyToAttribute(attr, magnitude);
                    }
                }
            }

            // 調用後處理
            targetSet.PostGameplayEffectExecute(spec);

            // 賦予標籤
            if (!spec.EffectDef.GrantedTags.IsEmpty)
            {
                spec.Target.OwnedTags.AddTags(spec.EffectDef.GrantedTags);
            }

            // 移除其他效果
            if (!spec.EffectDef.RemoveEffectsWithTags.IsEmpty)
            {
                foreach (var tag in spec.EffectDef.RemoveEffectsWithTags)
                {
                    spec.Target.ActiveEffects.RemoveEffectsWithTag(tag);
                }
            }

            // 觸發 Cue
            foreach (var cueTag in spec.EffectDef.CueTags)
            {
                ExecuteGameplayCue(cueTag, spec.Target.transform.position, spec.Target.gameObject);
            }

            if (DebugMode)
            {
                Debug.Log($"[ASC] Executed effect: {spec.EffectDef.EffectName}");
            }
        }

        /// <summary>
        /// 處理週期效果
        /// </summary>
        private void ProcessPeriodicEffects()
        {
            foreach (var spec in ActiveEffects.GetAllEffects())
            {
                if (spec.ShouldExecutePeriodic())
                {
                    ExecuteEffect(spec);
                    spec.MarkPeriodicExecuted();
                }
            }
        }

        /// <summary>
        /// 移除帶有指定標籤的效果
        /// </summary>
        public int RemoveEffectsWithTag(GameplayTag tag)
        {
            return ActiveEffects.RemoveEffectsWithTag(tag);
        }

        #endregion

        #region Gameplay Cue

        /// <summary>
        /// 執行 Gameplay Cue
        /// </summary>
        public void ExecuteGameplayCue(GameplayTag cueTag, Vector3? location = null, GameObject target = null)
        {
            ExecuteGameplayCue(cueTag, location, null, target);
        }

        /// <summary>
        /// 執行 Gameplay Cue（含旋轉資訊，例如表面法線方向）
        /// </summary>
        public void ExecuteGameplayCue(GameplayTag cueTag, Vector3? location, Quaternion? rotation, GameObject target)
        {
            ExecuteGameplayCue(cueTag, location, rotation, target, null);
        }

        /// <summary>
        /// 執行 Gameplay Cue(含旋轉與縮放) — 用於受擊全身特效依目標大小縮放,VFXCue 會以 parameters.Scale 套用
        /// </summary>
        public void ExecuteGameplayCue(GameplayTag cueTag, Vector3? location, Quaternion? rotation, GameObject target, Vector3? scale)
        {
            if (_cueManager == null)
            {
                _cueManager = FindFirstObjectByType<GameplayCueManager>();
            }
            if (_cueManager != null)
            {
                _cueManager.ExecuteCue(cueTag, new GameplayCueParameters
                {
                    Location = location ?? transform.position,
                    Rotation = rotation ?? Quaternion.identity,
                    TargetObject = target ?? gameObject,
                    Instigator = this,
                    Scale = scale ?? Vector3.one
                });
            }
            else if (DebugMode)
            {
                Debug.LogWarning($"[ASC] No GameplayCueManager found for cue: {cueTag}");
            }
        }

        #endregion

        #region Event Handlers

        private void HandleAbilityActivated(GameplayAbilitySpec spec)
        {
            OnAbilityActivated?.Invoke(spec);
        }

        private void HandleAbilityEnded(GameplayAbilitySpec spec, bool wasCancelled)
        {
            OnAbilityEnded?.Invoke(spec, wasCancelled);
        }

        private void HandleEffectAdded(GameplayEffectSpec spec)
        {
            // 首次應用效果
            if (!spec.EffectDef.IsInstant)
            {
                ExecuteEffect(spec);
            }
        }

        private void HandleEffectRemoved(GameplayEffectSpec spec)
        {
            // 移除效果時清理
            var targetSet = spec.Target?.GetAttributeSet();
            if (targetSet != null && !spec.EffectDef.IsInstant)
            {
                // 移除修改器
                foreach (var modifier in spec.EffectDef.Modifiers)
                {
                    var attr = targetSet.GetAttribute(modifier.AttributeName);
                    if (attr != null)
                    {
                        float magnitude = spec.GetCalculatedMagnitude(modifier);
                        modifier.RemoveFromAttribute(attr, magnitude);
                    }
                }
            }

            // 移除效果賦予的標籤
            if (spec.Target != null && !spec.EffectDef.RemoveTagsOnEnd.IsEmpty)
            {
                spec.Target.OwnedTags.RemoveTags(spec.EffectDef.RemoveTagsOnEnd);
            }

            // 移除效果的 GrantedTags
            if (spec.Target != null && !spec.EffectDef.GrantedTags.IsEmpty)
            {
                spec.Target.OwnedTags.RemoveTags(spec.EffectDef.GrantedTags);
            }
        }

        #endregion
    }
}
