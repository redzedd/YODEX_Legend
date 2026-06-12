using System.Collections;
using UnityEngine;
using Animancer;

namespace GAS
{
    /// <summary>
    /// 招架支援能力 - 近戰武器使用
    /// 切換武器時觸發，在支援窗口內格擋敵人攻擊並造成失衡值
    /// </summary>
    [CreateAssetMenu(fileName = "GA_ParryAssist", menuName = "GAS/Abilities/Parry Assist")]
    public class GA_ParryAssist : GameplayAbility
    {
        [Header("Animation")]
        [Tooltip("招架動畫")]
        public ClipTransition ParryAnimation;

        [Header("Timing")]
        [Tooltip("招架動作持續時間")]
        public float ParryDuration = 0.5f;

        [Tooltip("自動格檔窗口開始時間（相對於動畫開始）")]
        public float AutoBlockStartTime = 0f;

        [Tooltip("自動格檔持續時間")]
        public float AutoBlockDuration = 0.3f;

        [Header("Combat")]
        [Tooltip("招架成功造成的失衡值")]
        public float StaggerDamage = 30f;

        [Tooltip("招架成功造成的傷害加成")]
        public float DamageMultiplier = 1.5f;

        [Tooltip("招架成功後的追擊時間窗口")]
        public float CounterAttackWindow = 1.0f;

        [Header("Cost")]
        [Tooltip("支援點數消耗")]
        public float AssistPointsCost = 1f;

        [Header("Effects")]
        [Tooltip("招架時應用的無敵效果")]
        public GameplayEffect InvincibilityEffect;

        [Tooltip("招架成功後應用給敵人的失衡效果")]
        public GameplayEffect StaggerEffect;

        [Header("Cues")]
        [Tooltip("招架開始 Cue")]
        public GameplayTag ParryStartCue;

        [Tooltip("招架成功 Cue")]
        public GameplayTag ParrySuccessCue;

        [Tooltip("招架結束 Cue")]
        public GameplayTag ParryEndCue;

        protected override bool CanPayCost(GameplayAbilitySpec spec)
        {
            // 檢查支援點數
            CombatAttributeSet attrSet = spec.Owner.GetAttributeSet<CombatAttributeSet>();
            if (attrSet != null)
            {
                return attrSet.HasAssistPoints(AssistPointsCost);
            }
            return base.CanPayCost(spec);
        }

        protected override void PayCost(GameplayAbilitySpec spec)
        {
            // 消耗支援點數
            CombatAttributeSet attrSet = spec.Owner.GetAttributeSet<CombatAttributeSet>();
            if (attrSet != null)
            {
                attrSet.TryConsumeAssistPoints(AssistPointsCost);
            }
            base.PayCost(spec);
        }

        public override void ActivateAbility(GameplayAbilitySpec spec)
        {
            // 支付消耗
            PayCost(spec);

            // 啟動招架協程
            Coroutine coroutine = StartCoroutine(spec, ParryRoutine(spec));
            spec.SetActiveCoroutine(coroutine);
        }

        public override void EndAbility(GameplayAbilitySpec spec, bool wasCancelled)
        {
            // 清理招架狀態
            if (spec.CustomData is ParryRuntimeData runtimeData)
            {
                runtimeData.Cleanup();
            }

            // 移除招架狀態標籤
            if (spec.Owner != null)
            {
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.Parrying);
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.Invincible);
            }

            // 觸發結束 Cue
            if (ParryEndCue.IsValid && spec.Owner != null)
            {
                ExecuteGameplayCue(spec, ParryEndCue, spec.Owner.transform.position);
            }

            base.EndAbility(spec, wasCancelled);
        }

        /// <summary>
        /// 招架主協程
        /// </summary>
        private IEnumerator ParryRoutine(GameplayAbilitySpec spec)
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

            if (animancer == null || ParryAnimation == null)
            {
                Debug.LogError("[GA_ParryAssist] Missing AnimancerComponent or ParryAnimation!");
                spec.EndAbility();
                yield break;
            }

            // 創建運行時數據
            ParryRuntimeData runtimeData = new ParryRuntimeData(owner);
            spec.CustomData = runtimeData;

            // 添加招架狀態標籤
            owner.OwnedTags.AddTag(GameplayTags.State.Parrying);

            // 播放招架動畫
            AnimancerState animState = animancer.Play(ParryAnimation);
            animState.Time = 0;

            // 觸發開始 Cue
            if (ParryStartCue.IsValid)
            {
                ExecuteGameplayCue(spec, ParryStartCue, owner.transform.position);
            }

            // 處理自動格檔窗口
            owner.StartCoroutine(HandleAutoBlock(spec, AutoBlockStartTime, AutoBlockDuration));

            // 訂閱受擊事件以檢測招架成功
            CombatAttributeSet attrSet = owner.GetAttributeSet<CombatAttributeSet>();
            if (attrSet != null)
            {
                runtimeData.OnDamageTakenHandler = (attacker, damage) => OnParrySuccess(spec, runtimeData, attacker);
                attrSet.OnDamageTaken += runtimeData.OnDamageTakenHandler;
            }

            // 等待招架動作完成
            float animDuration = ParryAnimation.Clip != null ? ParryAnimation.Clip.length : ParryDuration;
            yield return new WaitForSeconds(Mathf.Max(animDuration, ParryDuration));

            // 結束能力
            spec.EndAbility();
        }

        /// <summary>
        /// 處理自動格檔窗口
        /// </summary>
        private IEnumerator HandleAutoBlock(GameplayAbilitySpec spec, float startDelay, float duration)
        {
            // 等待開始時間
            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            if (!spec.IsActive) yield break;

            // 添加無敵標籤
            spec.Owner.OwnedTags.AddTag(GameplayTags.State.Invincible);

            // 如果有無敵效果，應用它
            if (InvincibilityEffect != null)
            {
                ApplyEffectToSelf(spec, InvincibilityEffect);
            }

            // 等待格檔持續時間
            yield return new WaitForSeconds(duration);

            // 移除無敵標籤
            if (spec.Owner != null && spec.IsActive)
            {
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.Invincible);
            }
        }

        /// <summary>
        /// 招架成功時的處理
        /// </summary>
        private void OnParrySuccess(GameplayAbilitySpec spec, ParryRuntimeData runtimeData, AbilitySystemComponent attacker)
        {
            if (runtimeData.HasParried) return; // 防止多次觸發
            runtimeData.HasParried = true;

            // 觸發成功 Cue
            if (ParrySuccessCue.IsValid)
            {
                ExecuteGameplayCue(spec, ParrySuccessCue, spec.Owner.transform.position);
            }

            // 對攻擊者施加失衡效果
            if (attacker != null && StaggerEffect != null)
            {
                spec.Owner.ApplyEffectToTarget(attacker, StaggerEffect);
            }

            // 記錄攻擊者以便追擊
            runtimeData.LastAttacker = attacker;

            if (spec.Owner.DebugMode)
            {
                Debug.Log($"[GA_ParryAssist] Parry successful! Attacker: {attacker?.name}");
            }
        }
    }

    /// <summary>
    /// 招架運行時數據
    /// </summary>
    public class ParryRuntimeData
    {
        public AbilitySystemComponent Owner { get; private set; }
        public bool HasParried { get; set; }
        public AbilitySystemComponent LastAttacker { get; set; }
        public System.Action<AbilitySystemComponent, float> OnDamageTakenHandler { get; set; }

        public ParryRuntimeData(AbilitySystemComponent owner)
        {
            Owner = owner;
            HasParried = false;
            LastAttacker = null;
        }

        public void Cleanup()
        {
            // 取消訂閱受擊事件
            if (OnDamageTakenHandler != null)
            {
                CombatAttributeSet attrSet = Owner?.GetAttributeSet<CombatAttributeSet>();
                if (attrSet != null)
                {
                    attrSet.OnDamageTaken -= OnDamageTakenHandler;
                }
                OnDamageTakenHandler = null;
            }
        }
    }
}
