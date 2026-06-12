using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 完美閃避殘留影分身 — 閃避瞬間在玩家原始位置留下的虛擬受擊點
    /// 獨立 GameObject，不跟隨玩家。被敵人攻擊命中時通知玩家觸發完美閃避。
    /// </summary>
    public class PerfectDodgeGhost : MonoBehaviour, IHitReceiver
    {
        private AbilitySystemComponent _ownerASC;
        private GASDamageReceiver _ownerDamageReceiver;
        private float _lifetime;

        /// <summary>
        /// 在指定位置生成殘留影分身
        /// </summary>
        public static PerfectDodgeGhost Spawn(
            Vector3 position,
            float radius,
            float duration,
            AbilitySystemComponent owner,
            GASDamageReceiver damageReceiver)
        {
            var go = new GameObject("PerfectDodgeGhost");
            go.transform.position = position;
            go.layer = owner.gameObject.layer;
            var collider = go.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = radius;
            var ghost = go.AddComponent<PerfectDodgeGhost>();
            ghost._ownerASC = owner;
            ghost._ownerDamageReceiver = damageReceiver;
            ghost._lifetime = duration;
            return ghost;
        }

        private void Update()
        {
            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f)
            {
                Destroy(gameObject);
            }
        }

        public void OnHit(ref HitContext ctx)
        {
            // 影分身永遠擋住攻擊（不受傷、不產生任何效果）
            ctx.wasBlocked = true;
            // 檢查玩家是否仍在完美閃避窗口中
            if (_ownerASC != null &&
                _ownerASC.OwnedTags.HasTag(GameplayTags.State.PerfectDodgeWindow))
            {
                ctx.wasPerfectDodged = true;
                // 透過玩家的 GASDamageReceiver 發送完美閃避事件
                _ownerDamageReceiver?.InvokePerfectDodge(ctx);
                Debug.Log("<color=yellow>[完美閃避!]</color> 殘留影分身被命中 — 觸發完美閃避");
            }
            // 命中後立即銷毀（一次性）
            Destroy(gameObject);
        }
    }
}
