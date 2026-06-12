using UnityEngine;

namespace GAS
{
    /// <summary>
    /// VFX Cue - 視覺特效 Cue 實現
    /// </summary>
    [CreateAssetMenu(fileName = "New VFX Cue", menuName = "GAS/Cues/VFX Cue")]
    public class VFXCue : GameplayCue
    {
        [Header("VFX Settings")]
        [Tooltip("特效預製體")]
        public GameObject VFXPrefab;

        [Tooltip("額外位置偏移（在 TimeLineEvent 偏移之上）")]
        public Vector3 AdditionalPositionOffset = Vector3.zero;

        [Tooltip("額外旋轉偏移（在 TimeLineEvent 旋轉之上）")]
        public Vector3 AdditionalRotationOffset = Vector3.zero;

        [Tooltip("額外縮放倍率（乘以 TimeLineEvent 的縮放）")]
        public Vector3 AdditionalScale = Vector3.one;

        [Tooltip("優先使用 Parameters 中的 Transform 設定（由 TimeLineEvent 提供）")]
        public bool UseParameterTransform = true;

        [Header("Particle Settings")]
        [Tooltip("是否為粒子系統")]
        public bool IsParticleSystem = true;

        [Tooltip("粒子播放完成後銷毀")]
        public bool DestroyOnParticleComplete = true;
        
        /// <summary>
        /// 最後創建的實例（用於追蹤）
        /// </summary>
        [System.NonSerialized]
        public GameObject LastSpawnedInstance;

        public override void OnExecute(GameplayCueParameters parameters)
        {
            LastSpawnedInstance = SpawnVFX(parameters, true);
        }

        public override void OnActivate(GameplayCueParameters parameters)
        {
            // 持續 VFX 的啟動邏輯 - 不自動銷毀
            LastSpawnedInstance = SpawnVFX(parameters, false);
        }

        public override void OnDeactivate(GameplayCueParameters parameters)
        {
            // 持續 VFX 的停用邏輯 - 由外部處理
        }
        
        /// <summary>
        /// 生成 VFX 實例
        /// </summary>
        /// <param name="parameters">參數</param>
        /// <param name="autoDestroy">是否自動銷毀</param>
        /// <returns>創建的 GameObject</returns>
        protected virtual GameObject SpawnVFX(GameplayCueParameters parameters, bool autoDestroy)
        {
            if (VFXPrefab == null) return null;

            // 優先使用 parameters 中的位置和旋轉（從 TimeLineEvent 傳入）
            Vector3 position;
            Quaternion rotation;
            Transform parent = GetParentTransform(parameters);

            if (UseParameterTransform && parameters.Location != Vector3.zero)
            {
                // 使用 TimeLineEvent 設定的位置和旋轉
                position = parameters.Location + (parameters.Rotation * AdditionalPositionOffset);
                rotation = parameters.Rotation * Quaternion.Euler(AdditionalRotationOffset);
            }
            else
            {
                // 回退到舊的行為（使用 AttachToTarget）
                position = GetSpawnPosition(parameters) + AdditionalPositionOffset;
                rotation = GetSpawnRotation(parameters) * Quaternion.Euler(AdditionalRotationOffset);
            }

            GameObject instance = Instantiate(VFXPrefab, position, rotation, parent);

            // 應用縮放：優先使用 parameters.Scale（從 TimeLineEvent），再乘以額外縮放
            Vector3 finalScale = (UseParameterTransform && parameters.Scale != Vector3.zero) 
                ? Vector3.Scale(parameters.Scale, AdditionalScale)
                : AdditionalScale;
            instance.transform.localScale = finalScale;

            // 處理粒子系統
            if (IsParticleSystem)
            {
                var particles = instance.GetComponentsInChildren<ParticleSystem>();
                foreach (var ps in particles)
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }

                if (autoDestroy)
                {
                    if (DestroyOnParticleComplete)
                    {
                        // 計算最長粒子持續時間
                        float maxDuration = 0f;
                        foreach (var ps in particles)
                        {
                            float duration = ps.main.duration + ps.main.startLifetime.constantMax;
                            if (duration > maxDuration)
                            {
                                maxDuration = duration;
                            }
                        }

                        Destroy(instance, maxDuration + 0.5f);
                    }
                    else if (AutoDestroyTime > 0f)
                    {
                        Destroy(instance, AutoDestroyTime);
                    }
                }
            }
            else if (autoDestroy && AutoDestroyTime > 0f)
            {
                Destroy(instance, AutoDestroyTime);
            }
            
            return instance;
        }
    }

}
