using System;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 戰鬥屬性集 - 包含戰鬥相關的所有屬性
    /// </summary>
    [Serializable]
    public class CombatAttributeSet : AttributeSet
    {
        // === 生命值相關 ===
        
        [Header("Health")]
        public GameplayAttribute Health = new("Health", 100f);
        public GameplayAttribute MaxHealth = new("MaxHealth", 100f);

        // === 攻擊相關 ===
        
        [Header("Offense")]
        public GameplayAttribute AttackPower = new("AttackPower", 10f);
        public GameplayAttribute CriticalChance = new("CriticalChance", 0.05f);  // 5% 暴擊率
        public GameplayAttribute CriticalDamage = new("CriticalDamage", 1.5f);   // 150% 暴擊傷害

        // === 防禦相關 ===
        
        [Header("Defense")]
        public GameplayAttribute Defense = new("Defense", 5f);
        public GameplayAttribute DamageReduction = new("DamageReduction", 0f);   // 百分比傷害減免

        // === 移動相關 ===
        
        [Header("Movement")]
        public GameplayAttribute MoveSpeed = new("MoveSpeed", 5f);
        public GameplayAttribute DodgeCooldown = new("DodgeCooldown", 1f);

        // === 能量/資源 ===
        
        [Header("Resources - Stamina")]
        public GameplayAttribute Stamina = new("Stamina", 100f);
        public GameplayAttribute MaxStamina = new("MaxStamina", 100f);
        public GameplayAttribute StaminaRegen = new("StaminaRegen", 10f);  // 每秒恢復

        [Header("Resources - Mana")]
        public GameplayAttribute Mana = new("Mana", 100f);
        public GameplayAttribute MaxMana = new("MaxMana", 100f);
        public GameplayAttribute ManaRegen = new("ManaRegen", 5f);  // 每秒恢復

        [Header("Resources - Poise（韌性）")]
        [Tooltip("當前韌性；受擊時扣減,歸零時才觸發硬直(Stagger)")]
        public GameplayAttribute Poise = new("Poise", 100f);
        [Tooltip("韌性上限")]
        public GameplayAttribute MaxPoise = new("MaxPoise", 100f);
        [Tooltip("韌性每秒回復量")]
        public GameplayAttribute PoiseRegen = new("PoiseRegen", 20f);

        // === 支援點數 ===
        
        [Header("Assist Points")]
        [Tooltip("支援點數（用於招架/迴避支援）")]
        public GameplayAttribute AssistPoints = new("AssistPoints", 3f);
        
        [Tooltip("支援點數上限")]
        public GameplayAttribute MaxAssistPoints = new("MaxAssistPoints", 3f);

        // === 臨時屬性 (用於傷害計算的中間值) ===
        
        [Header("Meta Attributes")]
        [Tooltip("臨時傷害值，用於傳遞傷害計算結果")]
        public GameplayAttribute IncomingDamage = new("IncomingDamage", 0f);

        /// <summary>
        /// 當生命值變化時觸發
        /// </summary>
        public event Action<float, float> OnHealthChanged;

        /// <summary>
        /// 當生命值歸零時觸發
        /// </summary>
        public event Action OnDeath;

        /// <summary>
        /// 當受到傷害時觸發
        /// 參數: (傷害來源 ASC, 傷害值)
        /// </summary>
        public event Action<AbilitySystemComponent, float> OnDamageTaken;

        /// <summary>
        /// 當支援點數變化時觸發
        /// 參數: (舊值, 新值)
        /// </summary>
        public event Action<float, float> OnAssistPointsChanged;

        protected override void OnInitialize()
        {
            // 確保生命值不超過最大值
            Health.OnCurrentValueChanged += (attr, oldVal, newVal) =>
            {
                // 限制生命值在 0 ~ MaxHealth 之間
                float clampedValue = Mathf.Clamp(newVal, 0f, MaxHealth.CurrentValue);
                if (Math.Abs(newVal - clampedValue) > float.Epsilon)
                {
                    attr.BaseValue = clampedValue - (newVal - attr.BaseValue);
                }
                
                OnHealthChanged?.Invoke(oldVal, newVal);
            };

            // 同樣處理體力值
            Stamina.OnCurrentValueChanged += (attr, oldVal, newVal) =>
            {
                float clampedValue = Mathf.Clamp(newVal, 0f, MaxStamina.CurrentValue);
                if (Math.Abs(newVal - clampedValue) > float.Epsilon)
                {
                    attr.BaseValue = clampedValue - (newVal - attr.BaseValue);
                }
                OnStaminaChanged?.Invoke(oldVal, newVal);
            };

            // 處理魔力值
            Mana.OnCurrentValueChanged += (attr, oldVal, newVal) =>
            {
                float clampedValue = Mathf.Clamp(newVal, 0f, MaxMana.CurrentValue);
                if (Math.Abs(newVal - clampedValue) > float.Epsilon)
                {
                    attr.BaseValue = clampedValue - (newVal - attr.BaseValue);
                }
                OnManaChanged?.Invoke(oldVal, newVal);
            };

            // 處理韌性值
            Poise.OnCurrentValueChanged += (attr, oldVal, newVal) =>
            {
                float clampedValue = Mathf.Clamp(newVal, 0f, MaxPoise.CurrentValue);
                if (Math.Abs(newVal - clampedValue) > float.Epsilon)
                {
                    attr.BaseValue = clampedValue - (newVal - attr.BaseValue);
                }
                OnPoiseChanged?.Invoke(oldVal, clampedValue);
                if (oldVal > 0f && clampedValue <= 0f)
                {
                    OnPoiseBroken?.Invoke();
                }
            };

            // 處理支援點數
            AssistPoints.OnCurrentValueChanged += (attr, oldVal, newVal) =>
            {
                float clampedValue = Mathf.Clamp(newVal, 0f, MaxAssistPoints.CurrentValue);
                if (Math.Abs(newVal - clampedValue) > float.Epsilon)
                {
                    attr.BaseValue = clampedValue - (newVal - attr.BaseValue);
                }
                
                OnAssistPointsChanged?.Invoke(oldVal, clampedValue);
            };
        }

        protected override void OnAttributeChanged(GameplayAttribute attr, float oldValue, float newValue)
        {
            // 處理生命值歸零
            if (attr.AttributeName == "Health" && newValue <= 0f && oldValue > 0f)
            {
                OnDeath?.Invoke();
            }
        }

        public override void PreAttributeChange(GameplayAttribute attr, ref float newValue)
        {
            // 防止資源值變成負數
            if (attr.AttributeName == "Health" || attr.AttributeName == "Stamina"
                || attr.AttributeName == "Mana" || attr.AttributeName == "AssistPoints"
                || attr.AttributeName == "Poise")
            {
                newValue = Mathf.Max(0f, newValue);
            }

            // 防止超過最大值
            if (attr.AttributeName == "Health")
            {
                newValue = Mathf.Min(newValue, MaxHealth.CurrentValue);
            }
            else if (attr.AttributeName == "Stamina")
            {
                newValue = Mathf.Min(newValue, MaxStamina.CurrentValue);
            }
            else if (attr.AttributeName == "Mana")
            {
                newValue = Mathf.Min(newValue, MaxMana.CurrentValue);
            }
            else if (attr.AttributeName == "AssistPoints")
            {
                newValue = Mathf.Min(newValue, MaxAssistPoints.CurrentValue);
            }
            else if (attr.AttributeName == "Poise")
            {
                newValue = Mathf.Min(newValue, MaxPoise.CurrentValue);
            }
        }

        public override void PostGameplayEffectExecute(GameplayEffectSpec spec)
        {
            // 處理 IncomingDamage 元屬性
            if (IncomingDamage.CurrentValue > 0f)
            {
                float damage = IncomingDamage.CurrentValue;
                
                // 應用防禦和傷害減免
                float effectiveDamage = CalculateDamageReduction(damage);
                
                // 扣除生命值
                float newHealth = Health.CurrentValue - effectiveDamage;
                Health.BaseValue = Mathf.Max(0f, newHealth);
                
                // 觸發受傷事件
                OnDamageTaken?.Invoke(spec?.Instigator, effectiveDamage);
                
                // 重置臨時傷害值
                IncomingDamage.BaseValue = 0f;

                if (OwningASC != null && OwningASC.DebugMode)
                {
                    Debug.Log($"[CombatAttributeSet] Damage taken: {effectiveDamage:F1} (Raw: {damage:F1}). " +
                             $"Health: {Health.CurrentValue:F1}/{MaxHealth.CurrentValue:F1}");
                }
            }
        }

        /// <summary>
        /// 計算傷害減免
        /// 公式: 實際傷害 = 原始傷害 * (1 - DamageReduction) * (100 / (100 + Defense))
        /// </summary>
        private float CalculateDamageReduction(float rawDamage)
        {
            // 百分比減免
            float afterReduction = rawDamage * (1f - Mathf.Clamp01(DamageReduction.CurrentValue));
            
            // 防禦值減免 (使用類似 LoL 的公式)
            float defenseMultiplier = 100f / (100f + Defense.CurrentValue);
            float finalDamage = afterReduction * defenseMultiplier;
            
            return Mathf.Max(1f, finalDamage); // 最少造成 1 點傷害
        }

        /// <summary>
        /// 直接造成傷害 (繞過 GameplayEffect)
        /// </summary>
        public void ApplyDamage(float damage, AbilitySystemComponent source = null)
        {
            float effectiveDamage = CalculateDamageReduction(damage);
            float newHealth = Health.CurrentValue - effectiveDamage;
            Health.BaseValue = Mathf.Max(0f, newHealth);
            
            OnDamageTaken?.Invoke(source, effectiveDamage);
        }

        /// <summary>
        /// 直接恢復生命值
        /// </summary>
        public void ApplyHealing(float amount)
        {
            float newHealth = Mathf.Min(Health.CurrentValue + amount, MaxHealth.CurrentValue);
            Health.BaseValue = newHealth;
        }

        /// <summary>
        /// 消耗體力
        /// </summary>
        public bool TryConsumeStamina(float amount)
        {
            if (Stamina.CurrentValue >= amount)
            {
                Stamina.BaseValue -= amount;
                _staminaRegenDelay = StaminaRegenDelayTime;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 恢復體力
        /// </summary>
        public void RestoreStamina(float amount)
        {
            float newStamina = Mathf.Min(Stamina.CurrentValue + amount, MaxStamina.CurrentValue);
            Stamina.BaseValue = newStamina;
        }

        /// <summary>
        /// 生命值百分比 (0~1)
        /// </summary>
        public float HealthPercent => MaxHealth.CurrentValue > 0f 
            ? Health.CurrentValue / MaxHealth.CurrentValue 
            : 0f;

        /// <summary>
        /// 體力百分比 (0~1)
        /// </summary>
        public float StaminaPercent => MaxStamina.CurrentValue > 0f 
            ? Stamina.CurrentValue / MaxStamina.CurrentValue 
            : 0f;

        /// <summary>
        /// 支援點數百分比 (0~1)
        /// </summary>
        public float AssistPointsPercent => MaxAssistPoints.CurrentValue > 0f 
            ? AssistPoints.CurrentValue / MaxAssistPoints.CurrentValue 
            : 0f;

        /// <summary>
        /// 消耗支援點數
        /// </summary>
        /// <param name="amount">消耗數量</param>
        /// <returns>是否成功消耗</returns>
        public bool TryConsumeAssistPoints(float amount = 1f)
        {
            if (AssistPoints.CurrentValue >= amount)
            {
                AssistPoints.BaseValue -= amount;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 恢復支援點數
        /// </summary>
        /// <param name="amount">恢復數量</param>
        public void RestoreAssistPoints(float amount = 1f)
        {
            float newValue = Mathf.Min(AssistPoints.CurrentValue + amount, MaxAssistPoints.CurrentValue);
            AssistPoints.BaseValue = newValue;
        }

        /// <summary>
        /// 重置支援點數到最大值
        /// </summary>
        public void ResetAssistPoints()
        {
            AssistPoints.BaseValue = MaxAssistPoints.CurrentValue;
        }

        /// <summary>
        /// 檢查是否有足夠的支援點數
        /// </summary>
        public bool HasAssistPoints(float amount = 1f)
        {
            return AssistPoints.CurrentValue >= amount;
        }

        // === 回復延遲 ===
        
        /// <summary>體力回復延遲計時器 (消耗後延遲回復)</summary>
        private float _staminaRegenDelay = 0f;
        
        /// <summary>魔力回復延遲計時器</summary>
        private float _manaRegenDelay = 0f;

        /// <summary>體力回復延遲時間 (秒)</summary>
        public float StaminaRegenDelayTime { get; set; } = 1.0f;

        /// <summary>魔力回復延遲時間 (秒)</summary>
        public float ManaRegenDelayTime { get; set; } = 1.5f;

        /// <summary>體力回復是否被阻斷</summary>
        private int _staminaRegenBlockCount = 0;

        /// <summary>魔力回復是否被阻斷</summary>
        private int _manaRegenBlockCount = 0;

        /// <summary>當體力變化時觸發</summary>
        public event Action<float, float> OnStaminaChanged;

        /// <summary>當魔力變化時觸發</summary>
        public event Action<float, float> OnManaChanged;

        /// <summary>當韌性變化時觸發</summary>
        public event Action<float, float> OnPoiseChanged;

        /// <summary>當韌性擊破時觸發(Poise 降至 0 的瞬間)</summary>
        public event Action OnPoiseBroken;

        /// <summary>韌性回復延遲計時器 — 韌性受損後延遲回復</summary>
        private float _poiseRegenDelay = 0f;
        /// <summary>韌性回復延遲時間(秒)— 受擊後多久才開始回復</summary>
        public float PoiseRegenDelayTime { get; set; } = 1.5f;
        /// <summary>韌性回復阻斷計數器</summary>
        private int _poiseRegenBlockCount = 0;

        /// <summary>
        /// 每幀更新回復邏輯 (需由外部呼叫，如 NewGASPlayerController 或自訂 MonoBehaviour)
        /// </summary>
        public void TickRegeneration(float deltaTime)
        {
            // 耐力回復
            if (_staminaRegenDelay > 0f)
            {
                _staminaRegenDelay -= deltaTime;
            }
            else if (_staminaRegenBlockCount <= 0 && Stamina.CurrentValue < MaxStamina.CurrentValue)
            {
                float regen = StaminaRegen.CurrentValue * deltaTime;
                RestoreStamina(regen);
            }

            // 魔力回復
            if (_manaRegenDelay > 0f)
            {
                _manaRegenDelay -= deltaTime;
            }
            else if (_manaRegenBlockCount <= 0 && Mana.CurrentValue < MaxMana.CurrentValue)
            {
                float regen = ManaRegen.CurrentValue * deltaTime;
                RestoreMana(regen);
            }

            // 韌性回復
            if (_poiseRegenDelay > 0f)
            {
                _poiseRegenDelay -= deltaTime;
            }
            else if (_poiseRegenBlockCount <= 0 && Poise.CurrentValue < MaxPoise.CurrentValue)
            {
                float regen = PoiseRegen.CurrentValue * deltaTime;
                RestorePoise(regen);
            }
        }

        /// <summary>
        /// 消耗魔力
        /// </summary>
        public bool TryConsumeMana(float amount)
        {
            if (Mana.CurrentValue >= amount)
            {
                Mana.BaseValue -= amount;
                _manaRegenDelay = ManaRegenDelayTime;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 恢復魔力
        /// </summary>
        public void RestoreMana(float amount)
        {
            float newMana = Mathf.Min(Mana.CurrentValue + amount, MaxMana.CurrentValue);
            Mana.BaseValue = newMana;
        }

        /// <summary>
        /// 魔力百分比 (0~1)
        /// </summary>
        public float ManaPercent => MaxMana.CurrentValue > 0f
            ? Mana.CurrentValue / MaxMana.CurrentValue
            : 0f;

        /// <summary>
        /// 阻斷耐力回復
        /// </summary>
        public void PushStaminaRegenBlock() => _staminaRegenBlockCount++;

        /// <summary>
        /// 解除耐力回復阻斷
        /// </summary>
        public void PopStaminaRegenBlock() => _staminaRegenBlockCount = Mathf.Max(0, _staminaRegenBlockCount - 1);

        /// <summary>
        /// 阻斷魔力回復
        /// </summary>
        public void PushManaRegenBlock() => _manaRegenBlockCount++;

        /// <summary>
        /// 解除魔力回復阻斷
        /// </summary>
        public void PopManaRegenBlock() => _manaRegenBlockCount = Mathf.Max(0, _manaRegenBlockCount - 1);

        /// <summary>
        /// 對韌性扣值。回傳本次是否「擊破」韌性(扣後 <= 0)。
        /// 擊破者由呼叫端決定後續處理(觸發 Stagger / 重置韌性等)。
        /// </summary>
        public bool ApplyPoiseDamage(float amount)
        {
            if (amount <= 0f)
            {
                return false;
            }
            float newPoise = Poise.CurrentValue - amount;
            Poise.BaseValue = Mathf.Max(0f, newPoise);
            _poiseRegenDelay = PoiseRegenDelayTime;
            return Poise.CurrentValue <= 0f;
        }

        /// <summary>回復韌性(一般性回復,clamp 到 MaxPoise)</summary>
        public void RestorePoise(float amount)
        {
            float newPoise = Mathf.Min(Poise.CurrentValue + amount, MaxPoise.CurrentValue);
            Poise.BaseValue = newPoise;
        }

        /// <summary>將韌性直接重置為滿 — Stagger 開始/結束時使用,避免連續擊破鎖死</summary>
        public void ResetPoise()
        {
            Poise.BaseValue = MaxPoise.CurrentValue;
        }

        /// <summary>韌性百分比(0~1)</summary>
        public float PoisePercent => MaxPoise.CurrentValue > 0f
            ? Poise.CurrentValue / MaxPoise.CurrentValue
            : 0f;

        /// <summary>阻斷韌性回復</summary>
        public void PushPoiseRegenBlock() => _poiseRegenBlockCount++;

        /// <summary>解除韌性回復阻斷</summary>
        public void PopPoiseRegenBlock() => _poiseRegenBlockCount = Mathf.Max(0, _poiseRegenBlockCount - 1);
    }

    /// <summary>
    /// 戰鬥屬性名稱常量
    /// </summary>
    public static class CombatAttributes
    {
        public const string Health = "Health";
        public const string MaxHealth = "MaxHealth";
        public const string AttackPower = "AttackPower";
        public const string CriticalChance = "CriticalChance";
        public const string CriticalDamage = "CriticalDamage";
        public const string Defense = "Defense";
        public const string DamageReduction = "DamageReduction";
        public const string MoveSpeed = "MoveSpeed";
        public const string DodgeCooldown = "DodgeCooldown";
        public const string Stamina = "Stamina";
        public const string MaxStamina = "MaxStamina";
        public const string StaminaRegen = "StaminaRegen";
        public const string Mana = "Mana";
        public const string MaxMana = "MaxMana";
        public const string ManaRegen = "ManaRegen";
        public const string IncomingDamage = "IncomingDamage";
        public const string AssistPoints = "AssistPoints";
        public const string MaxAssistPoints = "MaxAssistPoints";
        public const string Poise = "Poise";
        public const string MaxPoise = "MaxPoise";
        public const string PoiseRegen = "PoiseRegen";
    }
}

