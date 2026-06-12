using System;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// GAS 受傷接收器 - 實現 IHitReceiver 介面
    /// 整合無敵幀判定，並透過 CombatAttributeSet 處理傷害
    /// 回饋效果（頓幀、相機抖動等）由攻擊方透過 HitContext 指定
    /// </summary>
    [RequireComponent(typeof(AbilitySystemComponent))]
    public class GASDamageReceiver : MonoBehaviour, IHitReceiver
    {
        [Header("Components")]
        [Tooltip("玩家控制器引用")]
        public NewGASPlayerController PlayerController;

        private AbilitySystemComponent _asc;

        /// <summary>
        /// 完美閃避觸發事件 — 由本體無敵判定或殘留影分身命中時發出
        /// </summary>
        public event Action<HitContext> OnPerfectDodge;

        private void Awake()
        {
            _asc = GetComponent<AbilitySystemComponent>();
            if (PlayerController == null) PlayerController = GetComponent<NewGASPlayerController>();
        }

        public void OnHit(ref HitContext ctx)
        {
            if (_asc == null) return;

            // 1. 無敵幀檢查
            if (_asc.OwnedTags.HasTag(GameplayTags.State.Invincible))
            {
                ctx.wasBlocked = true;
                // 完美閃避判定：無敵期間且處於完美閃避偵測窗口
                if (_asc.OwnedTags.HasTag(GameplayTags.State.PerfectDodgeWindow))
                {
                    ctx.wasPerfectDodged = true;
                    OnPerfectDodge?.Invoke(ctx);
                    Debug.Log($"<color=yellow>[完美閃避!]</color> 本體無敵幀完美閃避了 {ctx.damage} 點傷害");
                }
                else
                {
                    Debug.Log($"<color=cyan>[閃避成功]</color> 無敵幀擋住了 {ctx.damage} 點傷害");
                }
                return;
            }

            // 2. 死亡檢查
            if (_asc.OwnedTags.HasTag(GameplayTags.State.Dead))
            {
                ctx.wasBlocked = true;
                return;
            }

            // --- 正常受傷流程 ---
            Debug.Log($"<color=red>[被命中]</color> 受到 {ctx.damage} 點傷害");

            // 3. 應用傷害到 CombatAttributeSet
            var attrSet = _asc.GetAttributeSet<CombatAttributeSet>();
            if (attrSet != null)
            {
                attrSet.ApplyDamage(ctx.damage, null);
            }

            // 此擊致死(OnDeath → Die 已凍結世界並觸發死亡 UI)— 不再跑 HitReaction / CameraShake / HitStop,
            // 尤其 HitStop 會在 Die 的 timescale=0 之後再動 timescale,破壞死亡節奏感。
            if (_asc.OwnedTags.HasTag(GameplayTags.State.Dead))
            {
                return;
            }

            // 4. 觸發受擊反應（硬直 + 動畫 + 擊退由 OnHitReceived 內部統一處理）
            if (PlayerController != null)
            {
                PlayerController.OnHitReceived(ctx);
            }

            // 擊退由 NewGASPlayerController.OnHitReceived 內部以 HitContext 資料流統一處理,
            // 此處不再直接呼叫 PlayerController.AddForce(避免與新版受擊流程雙重觸發)。

            // 5. 鏡頭震動 — 依照攻擊方指定的強度，0 = 不觸發
            if (!ctx.skipHitEffects && ctx.cameraShakeIntensity > 0f && CameraShaker.Instance != null)
            {
                CameraShaker.Instance.Shake(ctx.cameraShakeIntensity, 0.25f);
            }

            // 6. 頓幀 — 依照攻擊方指定的持續時間，0 = 不觸發
            if (!ctx.skipHitEffects && ctx.hitStopDuration > 0f && HitStop.Instance != null)
            {
                HitStop.Instance.Trigger(ctx.hitStopDuration, ctx.hitStopTimeScale);
            }

            if (_asc.DebugMode)
            {
                Debug.Log($"[GASDamageReceiver] Hit received: damage={ctx.damage}, " +
                         $"knockback={ctx.knockbackForce}, direction={ctx.attackDirection}");
            }
        }

        /// <summary>
        /// 供外部（如殘留影分身）呼叫，發送完美閃避事件
        /// </summary>
        public void InvokePerfectDodge(HitContext ctx)
        {
            OnPerfectDodge?.Invoke(ctx);
        }
    }
}
