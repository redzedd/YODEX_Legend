namespace GAS
{
    /// <summary>
    /// SetByCaller 資料標籤常量
    /// 用於 GameplayEffectSpec.SetSetByCallerMagnitude / GetSetByCallerMagnitude
    /// </summary>
    public static class SetByCallerTags
    {
        /// <summary>傷害數值（由攻擊邏輯計算後注入）</summary>
        public const string DAMAGE = "Data.Damage";

        /// <summary>治療數值</summary>
        public const string HEAL = "Data.Heal";
    }
}
