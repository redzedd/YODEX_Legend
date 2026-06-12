using UnityEngine;
using Animancer;
using Player.Locomotion;

namespace GAS
{
    /// <summary>
    /// 武器類型 - 影響支援能力類型（招架/迴避）
    /// </summary>
    public enum WeaponType
    {
        /// <summary>近戰武器 - 使用招架支援</summary>
        Melee,
        /// <summary>遠程武器 - 使用迴避支援</summary>
        Ranged
    }

    /// <summary>
    /// 武器資料 - 定義武器的所有相關資訊
    /// 包含模型、動畫、關聯能力等
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponData", menuName = "GAS/Weapon/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("Basic Info")]
        [Tooltip("武器名稱")]
        public string WeaponName;

        [Tooltip("武器類型（影響支援類型）")]
        public WeaponType Type = WeaponType.Melee;

        [Tooltip("武器圖示")]
        public Sprite Icon;

        [TextArea(2, 4)]
        [Tooltip("武器描述")]
        public string Description;

        [Header("Model")]
        [Tooltip("角色模型 Prefab（整個角色模型，不只是武器）")]
        public GameObject CharacterModelPrefab;

        [Tooltip("武器模型 Prefab（可選，如果需要單獨顯示武器）")]
        public GameObject WeaponModelPrefab;

        [Header("Player Locomotion Data (新 GAS 架構)")]
        [Tooltip("移動參數配置 — 轉向速率、淡入時間、地面重力、快跑門檻等。\n" +
                 "由 NewGASPlayerController / LocomotionStateMachine 使用。")]
        public LocomotionConfig LocomotionConfig;

        [Tooltip("移動動畫集 — Idle / Walk / Run / FastRun / Jump / Dodge(8 方向) 等全部 ClipTransition。\n" +
                 "切武器時整包替換,取代舊版零散的 IdleAnimation / WalkAnimation... 欄位。")]
        public LocomotionAnimationSet LocomotionAnimations;

        [Tooltip("受擊反應資料 — Stagger / Flinch / Knockback 各方向 clip、硬直時長、Flinch Layer 淡入淡出設定。\n" +
                 "未指派時,NewGASPlayerController.OnHitReceived 會略過受擊播放。")]
        public HitReactionData HitReactionData;

        [Tooltip("死亡資料 — 死亡動畫 clip、淡入 fade、UI 淡入前等待時間。\n" +
                 "未指派時,NewGASPlayerController.Die 沿用切換前的 SO(或 Inspector 預設)。")]
        public PlayerDeathData DeathData;

        [Header("Switch Animations")]
        [Tooltip("切換進場動畫（新角色出現時播放）")]
        public ClipTransition SwitchInAnimation;

        [Tooltip("切換退場動畫（舊角色離開時播放，用於殘影）")]
        public ClipTransition SwitchOutAnimation;

        [Header("Combat Abilities")]
        [Tooltip("此武器使用的攻擊能力（近戰輕攻擊或遠程快速攻擊）")]
        public GameplayAbility AttackAbility;

        [Tooltip("此武器的重攻擊/蓄力攻擊能力（可選）")]
        public GameplayAbility HeavyAttackAbility;

        [Tooltip("此武器使用的閃避能力")]
        public GA_Dodge DodgeAbility;

        [Header("Assist Abilities")]
        [Tooltip("招架支援能力（近戰武器使用）")]
        public GameplayAbility ParryAssistAbility;

        [Tooltip("迴避支援能力（遠程武器使用）")]
        public GameplayAbility DodgeAssistAbility;

        [Header("Defensive Assist (招架支援)")]
        [Tooltip("Start：玩家舉起武器衝向敵人的進場動作。\n動畫播完後停在最後一幀，舉著武器等待接刀（不需 Loop 動畫）")]
        public ClipTransition ParryStartAnimation;

        [Tooltip("End：接到刀或招架結束時播放的收勢動作")]
        public ClipTransition ParryEndAnimation;

        [Header("VFX & SFX")]
        [Tooltip("切換進場特效")]
        public GameObject SwitchInVFXPrefab;

        [Tooltip("切換退場特效")]
        public GameObject SwitchOutVFXPrefab;

        [Tooltip("切換音效")]
        public AudioClip SwitchSFX;

        [Header("Afterimage Settings")]
        [Tooltip("殘影材質（半透明能量體）")]
        public Material AfterImageMaterial;

        [Tooltip("此武器專屬的殘影淡出時間覆寫(秒)。\n" +
                 "• 設為 0(預設):使用 WeaponRuntimeState 元件上的全域 Fade Out Duration\n" +
                 "• > 0:覆寫全域,本武器的殘影改用此值")]
        public float AfterImageFadeDuration = 0f;

        /// <summary>
        /// 根據武器類型獲取對應的支援能力
        /// </summary>
        public GameplayAbility GetAssistAbility()
        {
            return Type switch
            {
                WeaponType.Melee => ParryAssistAbility,
                WeaponType.Ranged => DodgeAssistAbility,
                _ => ParryAssistAbility
            };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(WeaponName))
            {
                WeaponName = name;
            }
        }
#endif
    }
}
