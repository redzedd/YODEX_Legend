using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GAS.UI;

namespace GAS
{
    /// <summary>
    /// Buff 效果應用器 — 將 BuffDefinition 資料轉換為 GAS 系統的效果
    /// 取代舊的 BuffManager + Regeneration + StatusEffectManager
    /// </summary>
    public class BuffEffectApplicator : MonoBehaviour
    {
        public static BuffEffectApplicator Instance { get; private set; }

        private class ActiveBuff
        {
            public BuffDefinition def;
            public int level;
            public Coroutine timerCoroutine;
            public Coroutine regenCoroutine;
        }

        private readonly Dictionary<int, ActiveBuff> _active = new();

        /// <summary>
        /// 取得或建立實例（單例模式）
        /// </summary>
        public static BuffEffectApplicator GetOrCreate()
        {
            if (Instance) return Instance;

            BuffEffectApplicator existing = FindFirstObjectByType<BuffEffectApplicator>();
            if (existing)
            {
                Instance = existing;
                return Instance;
            }

            GameObject go = new("[BuffEffectApplicator]");
            Instance = go.AddComponent<BuffEffectApplicator>();
            DontDestroyOnLoad(go);
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 應用 Buff（同 buffId 僅升級不降級）
        /// </summary>
        public void ApplyBuff(AbilitySystemComponent asc, BuffDefinition def, int level)
        {
            if (def == null || asc == null) return;
            level = Mathf.Clamp(level, 1, 3);

            // 同 ID 已存在且等級更高 → 跳過
            if (_active.TryGetValue(def.buffId, out ActiveBuff cur))
            {
                if (level < cur.level) return;
                InternalRemove(def.buffId);
            }

            // 建立並記錄
            ActiveBuff entry = new() { def = def, level = level };
            _active[def.buffId] = entry;

            // 實際效果
            AddEffect(asc, def, level, entry);

            // UI
            if (BuffBarUI.Instance) BuffBarUI.Instance.ShowOrUpdate(def, level);

            // 計時器（自動移除）
            BuffTierData tier = def.GetTier(level);
            if (tier != null && tier.duration > 0f)
            {
                entry.timerCoroutine = StartCoroutine(RemoveAfter(def.buffId, tier.duration));
            }
        }

        /// <summary>
        /// 手動移除指定 buffId
        /// </summary>
        public void RemoveById(int buffId)
        {
            InternalRemove(buffId);
        }

        private void InternalRemove(int buffId)
        {
            if (!_active.TryGetValue(buffId, out ActiveBuff cur)) return;

            if (cur.timerCoroutine != null) StopCoroutine(cur.timerCoroutine);
            if (cur.regenCoroutine != null) StopCoroutine(cur.regenCoroutine);

            _active.Remove(buffId);
            if (BuffBarUI.Instance) BuffBarUI.Instance.RemoveById(buffId);
        }

        private IEnumerator RemoveAfter(int buffId, float seconds)
        {
            yield return new WaitForSeconds(seconds + 0.05f);
            InternalRemove(buffId);
        }

        private void AddEffect(AbilitySystemComponent asc, BuffDefinition def, int level, ActiveBuff entry)
        {
            BuffTierData tier = def.GetTier(level);
            if (tier == null) return;

            switch (def.specialEffect)
            {
                case SpecialEffectType.Regeneration:
                    entry.regenCoroutine = StartCoroutine(RunRegeneration(asc, tier));
                    break;

                // 未來擴充：AttackBoost、FireResistance 等可透過 GameplayEffect 修改屬性
                case SpecialEffectType.AttackBoost:
                case SpecialEffectType.FireResistance:
                    // 保留：可用 asc.ApplyEffectToSelf() 配合對應的 GameplayEffect 資產
                    break;
            }
        }

        /// <summary>
        /// 再生效果協程 — 每隔 tickInterval 回復 percentPerTick% 的最大生命值
        /// </summary>
        private IEnumerator RunRegeneration(AbilitySystemComponent asc, BuffTierData tier)
        {
            if (asc == null) yield break;

            CombatAttributeSet attr = asc.GetAttributeSet<CombatAttributeSet>();
            if (attr == null) yield break;

            float perTickPercent = Mathf.Max(0f, tier.percentPerTick);
            float duration = Mathf.Max(0f, tier.duration);
            float interval = Mathf.Max(0.01f, tier.tickInterval);
            int tickCount = duration > 0f
                ? Mathf.Max(1, Mathf.FloorToInt(duration / interval))
                : 1;

            // 首跳延遲
            yield return new WaitForSeconds(interval);

            for (int i = 0; i < tickCount; i++)
            {
                if (attr == null) yield break;

                float maxHealth = attr.MaxHealth.CurrentValue;
                float healAmount = maxHealth * (perTickPercent * 0.01f);
                healAmount = Mathf.Max(1f, healAmount);

                attr.ApplyHealing(healAmount);

                if (i < tickCount - 1)
                {
                    yield return new WaitForSeconds(interval);
                }
            }
        }
    }
}
