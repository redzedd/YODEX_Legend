using System.Collections;
using UnityEngine;
using Animancer;

namespace GAS
{
    /// <summary>
    /// 武器切換能力 - 處理武器/角色切換的邏輯
    /// 支援普通切換和支援切換（招架/迴避支援）
    /// </summary>
    [CreateAssetMenu(fileName = "GA_WeaponSwitch", menuName = "GAS/Abilities/Weapon Switch")]
    public class GA_WeaponSwitch : GameplayAbility
    {
        [Header("Switch Settings")]
        [Tooltip("切換動畫過渡時間")]
        public float TransitionDuration = 0.1f;

        [Tooltip("切換後的無敵時間（支援切換時使用）")]
        public float InvincibilityDuration = 0.3f;

        [Tooltip("是否為支援切換（觸發招架/迴避支援）")]
        public bool IsAssistSwitch = false;

        [Header("Animation Override")]
        [Tooltip("覆寫的切換進場動畫（可選，如果不設置則使用武器資料中的動畫）")]
        public ClipTransition OverrideSwitchInAnimation;

        [Header("Cues")]
        [Tooltip("切換開始時的 Cue")]
        public GameplayTag SwitchStartCue;

        [Tooltip("切換完成時的 Cue")]
        public GameplayTag SwitchCompleteCue;

        public override void ActivateAbility(GameplayAbilitySpec spec)
        {
            WeaponManager weaponManager = spec.Owner.GetComponent<WeaponManager>();
            if (weaponManager == null)
            {
                Debug.LogError("[GA_WeaponSwitch] WeaponManager not found!");
                spec.EndAbility();
                return;
            }

            if (!weaponManager.CanSwitch)
            {
                if (spec.Owner.DebugMode)
                {
                    Debug.Log("[GA_WeaponSwitch] Cannot switch: on cooldown or only one weapon");
                }
                spec.EndAbility();
                return;
            }

            // 啟動切換協程
            Coroutine coroutine = StartCoroutine(spec, SwitchRoutine(spec, weaponManager));
            spec.SetActiveCoroutine(coroutine);
        }

        public override void EndAbility(GameplayAbilitySpec spec, bool wasCancelled)
        {
            // 確保移除切換狀態標籤
            if (spec.Owner != null)
            {
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.Switching);
            }

            base.EndAbility(spec, wasCancelled);
        }

        /// <summary>
        /// 切換主協程
        /// </summary>
        private IEnumerator SwitchRoutine(GameplayAbilitySpec spec, WeaponManager weaponManager)
        {
            AbilitySystemComponent owner = spec.Owner;
            
            // 從 NewGASPlayerController 獲取正確的 Animancer 引用
            NewGASPlayerController playerController = owner.GetComponent<NewGASPlayerController>();
            AnimancerComponent animancer = playerController?.Animancer;
            
            // 如果 PlayerController 沒有 Animancer，嘗試直接獲取
            if (animancer == null)
            {
                animancer = owner.GetComponentInChildren<AnimancerComponent>();
            }

            // 添加切換狀態標籤
            owner.OwnedTags.AddTag(GameplayTags.State.Switching);

            // 觸發切換開始 Cue
            if (SwitchStartCue.IsValid)
            {
                ExecuteGameplayCue(spec, SwitchStartCue, owner.transform.position);
            }

            // 記錄切換前的狀態
            WeaponData oldWeapon = weaponManager.CurrentWeapon;
            bool wasAttacking = weaponManager.IsAttackActive();

            // 執行武器切換
            bool switchSuccess = weaponManager.SwitchToNext();

            if (!switchSuccess)
            {
                spec.EndAbility();
                yield break;
            }

            WeaponData newWeapon = weaponManager.CurrentWeapon;

            // 選擇切換動畫
            ClipTransition switchAnim = OverrideSwitchInAnimation ?? newWeapon?.SwitchInAnimation;

            // 播放切換動畫
            if (switchAnim != null && animancer != null)
            {
                AnimancerState animState = animancer.Play(switchAnim);
                animState.Time = 0;

                // 等待動畫播放
                float animDuration = switchAnim.Clip != null ? switchAnim.Clip.length : TransitionDuration;
                yield return new WaitForSeconds(animDuration);
            }
            else
            {
                // 無動畫時等待過渡時間
                yield return new WaitForSeconds(TransitionDuration);
            }

            // 觸發切換完成 Cue
            if (SwitchCompleteCue.IsValid)
            {
                ExecuteGameplayCue(spec, SwitchCompleteCue, owner.transform.position);
            }

            // 如果是支援切換，觸發對應的支援能力
            if (IsAssistSwitch && newWeapon != null)
            {
                TriggerAssistAbility(spec, newWeapon);
            }

            // 結束能力
            spec.EndAbility();
        }

        /// <summary>
        /// 觸發支援能力
        /// </summary>
        private void TriggerAssistAbility(GameplayAbilitySpec spec, WeaponData weapon)
        {
            GameplayAbility assistAbility = weapon.GetAssistAbility();
            if (assistAbility == null) return;

            // 嘗試啟動支援能力
            spec.Owner.TryActivateAbility(assistAbility.AbilityTag);
        }

        /// <summary>
        /// 檢查是否可以執行支援切換
        /// 用於外部查詢（例如：檢查是否在敵人攻擊的支援窗口內）
        /// </summary>
        public static bool CanPerformAssistSwitch(AbilitySystemComponent owner)
        {
            if (owner == null) return false;

            // 檢查是否在支援窗口內
            if (!owner.OwnedTags.HasTag(GameplayTags.State.AssistWindow))
            {
                return false;
            }

            // 檢查支援點數
            CombatAttributeSet attrSet = owner.GetAttributeSet<CombatAttributeSet>();
            if (attrSet != null)
            {
                // 未來會檢查 AssistPoints
                // if (attrSet.AssistPoints.CurrentValue < 1f) return false;
            }

            return true;
        }
    }
}
