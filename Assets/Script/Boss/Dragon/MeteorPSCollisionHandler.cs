using System.Collections.Generic;
using UnityEngine;

namespace Boss.Dragon
{
    /// <summary>
    /// 隕石 ParticleSystem 碰撞 + 濺射傷害處理器
    /// 流程:粒子撞到地面 → Relay 轉發 OnParticleCollision → Handler 用 GetCollisionEvents 取「實際碰撞點 (intersection)」
    ///       → 在每個碰撞點 OverlapSphere (SplashRadius) 找 IHitReceiver → 套用 HitContext
    /// 同一 splash 內單一 IHitReceiver 只觸發一次 (防多 collider 角色被重複扣);跨 splash 獨立 (多顆隕石可各打一次)
    /// 跟玩家 AoEBehaviour (MeteorRain) 的設計概念一致,但走 BossController 系統 (不沾 GAS Effect)
    /// </summary>
    public class MeteorPSCollisionHandler : MonoBehaviour
    {
        // Debug Gizmo 顯示秒數
        private const float DEBUG_DISPLAY_DURATION = 2.5f;
        // OverlapSphere 緩衝大小 — 16 個 collider 通常夠 (玩家通常 1~4 個 collider)
        private const int OVERLAP_BUFFER_SIZE = 16;

        private MeteorAttackData _data;
        private bool _initialized;
        private readonly List<ParticleCollisionEvent> _collisionBuffer = new List<ParticleCollisionEvent>();
        private readonly Collider[] _overlapBuffer = new Collider[OVERLAP_BUFFER_SIZE];
        private readonly HashSet<IHitReceiver> _splashHitTargets = new HashSet<IHitReceiver>();

        // Debug 紀錄 — Scene View Gizmo 顯示用
        private struct DebugSplashEntry
        {
            public Vector3 Position;
            public float TimeRecorded;
            public int Hits;
        }
        private readonly List<DebugSplashEntry> _debugSplashes = new List<DebugSplashEntry>();

        /// <summary>由 MeteorAttackController 在 Instantiate 此 prefab 後呼叫,注入數值並自動掛 relay 到子 PS</summary>
        public void Initialize(MeteorAttackData data)
        {
            _data = data;
            _initialized = true;

            ParticleSystem[] particles = GetComponentsInChildren<ParticleSystem>(true);
            if (particles.Length == 0)
            {
                Debug.LogWarning($"[{name}] MeteorPSCollisionHandler 找不到任何 ParticleSystem 子物件 — 不會偵測落地。請確認 Prefab 內有 PS + Collision module 開啟", this);
                return;
            }
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem ps = particles[i];
                MeteorPSCollisionRelay relay = ps.gameObject.GetComponent<MeteorPSCollisionRelay>();
                if (relay == null)
                {
                    relay = ps.gameObject.AddComponent<MeteorPSCollisionRelay>();
                }
                relay.Initialize(this);
            }
        }

        /// <summary>由 MeteorPSCollisionRelay 在 OnParticleCollision 時呼叫 — 取得碰撞點後做濺射判定</summary>
        public void HandleParticleHit(ParticleSystem ps, GameObject other)
        {
            if (!_initialized || _data == null || ps == null) return;
            int eventCount = ps.GetCollisionEvents(other, _collisionBuffer);
            for (int i = 0; i < eventCount; i++)
            {
                Vector3 impactPoint = _collisionBuffer[i].intersection;
                ApplySplashAt(impactPoint);
            }
        }

        private void ApplySplashAt(Vector3 worldPoint)
        {
            _splashHitTargets.Clear();
            int hitCount = Physics.OverlapSphereNonAlloc(
                worldPoint,
                _data.SplashRadius,
                _overlapBuffer,
                _data.HitLayerMask,
                QueryTriggerInteraction.Ignore);

            int actualHits = 0;
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null) continue;
                IHitReceiver receiver = col.GetComponentInParent<IHitReceiver>();
                if (receiver == null) continue;
                if (!_splashHitTargets.Add(receiver)) continue;
                actualHits++;
                ApplyHitToReceiver(receiver, worldPoint, col.transform.position);
            }

            if (_data.DebugDrawSplash)
            {
                _debugSplashes.Add(new DebugSplashEntry
                {
                    Position = worldPoint,
                    TimeRecorded = Time.time,
                    Hits = actualHits
                });
                Debug.Log($"[MeteorSplash] 落地 @ {worldPoint:F2}, 半徑 {_data.SplashRadius:F2}, 命中 {actualHits} 個目標", this);
            }
        }

        private void ApplyHitToReceiver(IHitReceiver receiver, Vector3 splashCenter, Vector3 targetPos)
        {
            Vector3 attackDir = targetPos - splashCenter;
            attackDir.y = 0f;
            if (attackDir.sqrMagnitude > 0.0001f) attackDir.Normalize();
            else attackDir = Vector3.forward;

            HitContext ctx = new HitContext
            {
                damage = _data.Damage,
                poiseDamage = _data.DazeBuildup,
                knockbackForce = _data.KnockbackDistance,
                attackTier = _data.AttackTier,
                isHeavyAttack = _data.AttackTier == AttackTier.Heavy,
                hitPoint = targetPos,
                hitNormal = Vector3.up,
                attackDirection = attackDir,
                sourceProfile = null,
                skipHitEffects = false,
                gasDamageApplied = false,
                hitStopDuration = 0f,
                hitStopTimeScale = 1f,
                cameraShakeIntensity = 0f,
            };
            receiver.OnHit(ref ctx);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_data == null || !_data.DebugDrawSplash || _debugSplashes.Count == 0) return;
            float now = Time.time;
            Color prevColor = Gizmos.color;
            for (int i = _debugSplashes.Count - 1; i >= 0; i--)
            {
                DebugSplashEntry s = _debugSplashes[i];
                float age = now - s.TimeRecorded;
                if (age > DEBUG_DISPLAY_DURATION)
                {
                    _debugSplashes.RemoveAt(i);
                    continue;
                }
                float fade = Mathf.Clamp01(1f - age / DEBUG_DISPLAY_DURATION);
                // 命中 = 紅色,沒命中 = 黃色 (給 debug 區分「白打」跟「真的有打到」)
                Color color = s.Hits > 0
                    ? new Color(1f, 0.25f, 0.1f, fade)
                    : new Color(0.95f, 0.9f, 0.2f, fade * 0.6f);
                Gizmos.color = color;
                Gizmos.DrawWireSphere(s.Position, _data.SplashRadius);
            }
            Gizmos.color = prevColor;
        }
#endif
    }
}
