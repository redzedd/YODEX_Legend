using UnityEngine;

namespace GAS
{
    /// <summary>
    /// TimelineEvent 的觸發與清理共用 helper。
    /// 優先順序:VFXPrefab / SFX 任一有設 → 直接 Instantiate / PlayClipAtPoint;否則 fallback 走 CueTag → GameplayCueManager。
    /// 直接 Prefab 流程會在 VFX 上掛 <see cref="TimelineVFXFollower"/>,實現 6 軸獨立跟隨,
    /// 並把子物件 ParticleSystem 的 scalingMode 設為 Hierarchy,讓整個特效連子粒子一起隨角色等比縮放。
    /// 玩家近戰 / 殘影 / 玩家遠程 / 殘影遠程 四個觸發端共用本類,確保行為在各處一致。
    /// </summary>
    public static class TimelineEventSpawner
    {
        /// <summary>
        /// 觸發一個 TimelineEvent,回傳追蹤物件供 Cleanup 使用。
        /// </summary>
        public static TimelineEventInstance Trigger(TimelineEvent evt, Transform socket, float scaleFactor, AbilitySystemComponent instigator)
        {
            TimelineEventInstance inst = new TimelineEventInstance { Event = evt };
            if (evt == null || socket == null) return inst;

            // 注意：不要對 evt.PositionOffset 預先乘 scaleFactor。
            // socket.TransformPoint(...) 內部就會套 socket.lossyScale，再預乘會雙重縮放
            // 導致角色放大 N 倍時 VFX 跑到 N² 倍距離（Editor 預覽用同樣的 TransformPoint(evt.PositionOffset) 才會看起來對）
            Vector3 spawnPos = socket.TransformPoint(evt.PositionOffset);
            Quaternion spawnRot = socket.rotation * Quaternion.Euler(evt.RotationOffset);

            // 主要流程 — 直接拉 Prefab / SFX
            if (evt.VFXPrefab != null || evt.SFX != null)
            {
                if (evt.VFXPrefab != null)
                {
                    GameObject vfx = Object.Instantiate(evt.VFXPrefab, spawnPos, spawnRot);
                    ApplyHierarchyScalingMode(vfx);

                    TimelineVFXFollower follower = vfx.AddComponent<TimelineVFXFollower>();
                    follower.Setup(socket, evt.Axes, evt.PositionOffset, evt.RotationOffset, evt.Scale);

                    inst.SpawnedVFX = vfx;
                    inst.Follower = follower;
                    AutoDestroyByLifetime(vfx);
                }
                if (evt.SFX != null)
                {
                    AudioSource.PlayClipAtPoint(evt.SFX, spawnPos);
                }
                return inst;
            }

            // Fallback — CueTag → GameplayCueManager
            if (!evt.CueTag.IsValid) return inst;
            Vector3 finalScale = evt.IsAttached ? evt.Scale : evt.Scale * scaleFactor;
            GameplayCueParameters parameters = new GameplayCueParameters
            {
                Location = spawnPos,
                Rotation = spawnRot,
                Scale = finalScale,
                TargetObject = evt.IsAttached ? socket.gameObject : null,
                Instigator = instigator
            };
            GameplayCueManager cueManager = GameplayCueManager.Instance;
            if (evt.StopOnInterrupt && cueManager != null)
            {
                inst.CueHandler = cueManager.ActivateCue(evt.CueTag, parameters);
            }
            else
            {
                cueManager?.ExecuteCue(evt.CueTag, parameters);
            }
            return inst;
        }

        /// <summary>
        /// 攻擊結束 / 被打斷 時的特效收尾。
        /// wasInterrupted=true 對應「攻擊在 AllowCancelTime 提前被連段 / 取消」;false 對應動畫自然播完。
        /// </summary>
        public static void Cleanup(TimelineEventInstance inst, bool wasInterrupted)
        {
            if (inst == null || inst.Event == null) return;
            TimelineEvent evt = inst.Event;
            bool shouldStop = wasInterrupted && evt.StopOnInterrupt;

            // 直接 Prefab 路徑
            if (inst.SpawnedVFX != null)
            {
                if (shouldStop && evt.InterruptBehavior == VFXInterruptBehavior.StopAndDestroy)
                {
                    Object.Destroy(inst.SpawnedVFX);
                }
                else
                {
                    // DetachAndContinue / 自然結束 / 攻擊正常結束但有跟隨軸 → 一律停止跟隨,
                    // VFX 凍結在當下位置繼續播完粒子,避免 socket 被銷毀後 follower 跟著飄
                    if (inst.Follower != null) inst.Follower.StopFollowing();
                }
                inst.SpawnedVFX = null;
                inst.Follower = null;
                return;
            }

            // CueTag fallback 路徑
            if (inst.CueHandler == null || !inst.CueHandler.IsActive) return;
            GameplayCueManager cueManager = GameplayCueManager.Instance;
            if (cueManager == null) return;
            bool shouldDetach = evt.IsAttached;

            if (shouldStop)
            {
                if (evt.InterruptBehavior == VFXInterruptBehavior.StopAndDestroy)
                {
                    cueManager.DeactivateCue(inst.CueHandler);
                }
                else
                {
                    DetachCueHandler(inst.CueHandler, cueManager);
                }
            }
            else if (shouldDetach && inst.CueHandler.SpawnedObject != null)
            {
                DetachCueHandler(inst.CueHandler, cueManager);
            }
            else
            {
                inst.CueHandler.SpawnedObject = null;
                cueManager.DeactivateCue(inst.CueHandler);
            }
            inst.CueHandler = null;
        }

        /// <summary>把整個 VFX(含子物件)的 ParticleSystem 縮放模式設為 Hierarchy,確保 transform.localScale 改變時粒子等比放大。</summary>
        private static void ApplyHierarchyScalingMode(GameObject vfx)
        {
            if (vfx == null) return;
            ParticleSystem[] particles = vfx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem.MainModule main = particles[i].main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        private static void DetachCueHandler(GameplayCueHandler handler, GameplayCueManager cueManager)
        {
            GameObject vfx = handler.SpawnedObject;
            if (vfx != null)
            {
                Transform tf = vfx.transform;
                Vector3 worldPos = tf.position;
                Quaternion worldRot = tf.rotation;
                Vector3 worldScale = tf.lossyScale;
                tf.SetParent(null);
                tf.SetPositionAndRotation(worldPos, worldRot);
                tf.localScale = worldScale;
                float destroyTime = handler.CueDef != null ? handler.CueDef.AutoDestroyTime : 0f;
                if (destroyTime <= 0f) destroyTime = 3f;
                Object.Destroy(vfx, destroyTime);
                handler.SpawnedObject = null;
            }
            cueManager.DeactivateCue(handler);
        }

        private static void AutoDestroyByLifetime(GameObject vfx)
        {
            if (vfx == null) return;
            ParticleSystem[] particles = vfx.GetComponentsInChildren<ParticleSystem>(true);
            float maxDuration = 0f;
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem ps = particles[i];
                float duration = ps.main.duration + ps.main.startLifetime.constantMax;
                if (duration > maxDuration) maxDuration = duration;
            }
            float destroyTime = maxDuration > 0f ? maxDuration + 0.5f : 3f;
            Object.Destroy(vfx, destroyTime);
        }
    }
}
