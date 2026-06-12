using System;
using UnityEngine;
using GAS;

namespace Minigame.Archery
{
    /// <summary>
    /// 射箭小遊戲 — 巨大靶心（只接 AOE，不接弓箭）
    /// 偵測方式：實作 IHitReceiver.OnHit；AOE (AoEBehaviour) 命中目標時會主動呼叫 IHitReceiver，
    /// 而弓箭 (ProjectileBehaviour) 只走 GAS Effect 路徑，不會呼叫 OnHit → 天然過濾
    /// 必須與 AbilitySystemComponent 同物件存在，否則 AOE 的 ASC 查詢會 skip 此物件
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(AbilitySystemComponent))]
    public class MinigameAOETarget : MonoBehaviour, IHitReceiver
    {
        [Header("血量")]
        [Tooltip("被 AOE 命中幾次後死亡（1 = 一擊死，>1 適合 Persistent AOE 多 Tick 累計）")]
        [SerializeField] private int _hitsToKill = 1;

        [Tooltip("同次 AOE Tick 群擊的去重秒數（避免單次爆炸算多次）")]
        [SerializeField] private float _hitCooldown = 0.25f;

        [Header("命中後")]
        [Tooltip("被命中時播放的特效 prefab（可留空）")]
        [SerializeField] private GameObject _hitVFX;

        [Tooltip("死亡時播放的特效 prefab（可留空）")]
        [SerializeField] private GameObject _deathVFX;

        [Tooltip("命中音效（可留空）")]
        [SerializeField] private AudioClip _hitSFX;

        [Tooltip("死亡音效（可留空）")]
        [SerializeField] private AudioClip _deathSFX;

        [Tooltip("死亡後延遲秒數才銷毀（讓特效播完）")]
        [SerializeField] private float _destroyDelay = 0.5f;

        /// <summary>靶心被擊破事件 — Controller 訂閱用於勝利判定</summary>
        public event Action<MinigameAOETarget> OnKilled;

        private int _remainingHits;
        private float _lastHitTime = -999f;
        private bool _isDead;

        private void Awake()
        {
            _remainingHits = Mathf.Max(1, _hitsToKill);
        }

        public void OnHit(ref HitContext ctx)
        {
            if (_isDead)
            {
                ctx.wasBlocked = true;
                return;
            }
            // AOE Persistent / MeteorRain 多 Tick 防重複：同一冷卻窗只算一次
            if (Time.time - _lastHitTime < _hitCooldown)
            {
                ctx.wasBlocked = true;
                return;
            }
            _lastHitTime = Time.time;
            _remainingHits--;
            Vector3 hitPos = ctx.hitPoint.sqrMagnitude > 0.001f ? ctx.hitPoint : transform.position;
            if (_hitVFX != null)
                Instantiate(_hitVFX, hitPos, Quaternion.identity);
            if (_hitSFX != null)
                AudioSource.PlayClipAtPoint(_hitSFX, hitPos);
            if (_remainingHits <= 0)
                Die(hitPos);
        }

        private void Die(Vector3 hitPoint)
        {
            _isDead = true;
            if (_deathVFX != null)
                Instantiate(_deathVFX, hitPoint, Quaternion.identity);
            if (_deathSFX != null)
                AudioSource.PlayClipAtPoint(_deathSFX, hitPoint);
            OnKilled?.Invoke(this);
            Destroy(gameObject, _destroyDelay);
        }
    }
}
