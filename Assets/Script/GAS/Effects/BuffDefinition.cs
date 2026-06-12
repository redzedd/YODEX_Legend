using UnityEngine;

namespace GAS
{
    public enum SpecialEffectType
    {
        None,
        Regeneration,   // 再生：需要 tickInterval、percentPerTick
        FireResistance, // 範例：只用 duration / magnitude
        AttackBoost     // 範例：只用 duration / magnitude
    }

    public enum BuffAffectType
    {
        Health,   // 血量
        Mana,     // 魔力
        Stamina,  // 體力
        General   // 一般增益
    }

    [System.Serializable]
    public class BuffTierData
    {
        [Range(1, 3)] public int level = 1;

        [Header("顯示")]
        public Sprite icon;

        [Header("時序（大多數 Buff 會用到）")]
        public float duration = 0f;

        [Header("再生（Regeneration）專用")]
        public float tickInterval = 0f;
        [Range(0f, 100f)] public float percentPerTick = 0f;

        [Header("一般增量（Attack/Resistance...）")]
        public float magnitude = 0f;
    }

    [CreateAssetMenu(fileName = "BuffDefinition", menuName = "Game/Buffs/Buff Definition")]
    public class BuffDefinition : ScriptableObject
    {
        [Header("識別/排序")]
        [Tooltip("用來排序顯示；數字越小越靠前/上")]
        public int buffId = 1;

        [Header("屬性 / 作用類型")]
        public BuffAffectType affectType = BuffAffectType.General;

        [Header("效果類型")]
        public SpecialEffectType specialEffect = SpecialEffectType.None;

        [Header("三個等級（固定 1~3）")]
        public BuffTierData tier1;
        public BuffTierData tier2;
        public BuffTierData tier3;

        public BuffTierData GetTier(int level)
        {
            if (level <= 1) return tier1;
            if (level == 2) return tier2;
            return tier3;
        }
    }
}
