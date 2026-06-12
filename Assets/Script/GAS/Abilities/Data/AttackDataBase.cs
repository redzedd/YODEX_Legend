using System;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

namespace GAS
{
    /// <summary>
    /// 攻擊數據基類 - MeleeAttackData 和 RangedAttackData 的共用基類
    /// 用於統一連招系統和編輯器支援
    /// </summary>
    public abstract class AttackDataBase : ScriptableObject
    {
        [Header("Timing")]
        [Tooltip("最早允許輸入連招的時間")]
        public float AllowInputTime = 0.2f;

        [Tooltip("最晚允許連招的時間（超過此時間輸入攻擊將重置為第一擊）")]
        public float ComboResetTime = 0.8f;

        [Tooltip("允許取消動作（如閃避）的時間")]
        public float AllowCancelTime = 0.2f;

        [Tooltip("收刀取消時間 - 超過此時間若有移動輸入（走路/跑步/跳躍等）可直接取消攻擊進入移動，攻擊輸入優先於移動取消")]
        public float SheatheCancelTime = -1f;

        [Header("Damage")]
        [Tooltip("命中造成的韌性傷害(Poise Damage)。擊破目標 Poise 才會觸發 Stagger,否則視為輕攻擊被吸收(Phase 2 MVP 無視覺反應,Phase 3 會接上 Flinch)")]
        public float PoiseDamage = 50f;

        [Header("Combo")]
        [Tooltip("連招連結（支援跨近戰/遠程類型）")]
        public List<ComboLink> NextCombos = new();

        [Header("Timeline Events")]
        [Tooltip("時間軸事件（VFX/SFX）")]
        public List<TimelineEvent> TimelineEvents = new();

        /// <summary>
        /// 取得主要動畫片段（用於編輯器時間軸顯示）
        /// </summary>
        public abstract AnimationClip GetPrimaryAnimationClip();
    }

    /// <summary>
    /// 統一連招連結 - 支援跨近戰/遠程類型
    /// </summary>
    [Serializable]
    public class ComboLink
    {
        [Tooltip("觸發的輸入類型")]
        public MeleeInputType InputType = MeleeInputType.LightAttack;

        [Tooltip("下一個攻擊數據（可以是近戰或遠程）")]
        public AttackDataBase NextAttack;
    }
}
