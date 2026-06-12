using System;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// Gameplay Cue 參數 - 傳遞給 Cue 的執行上下文
    /// </summary>
    public struct GameplayCueParameters
    {
        /// <summary>
        /// Cue 執行的位置
        /// </summary>
        public Vector3 Location;

        /// <summary>
        /// Cue 執行的旋轉
        /// </summary>
        public Quaternion Rotation;

        /// <summary>
        /// 目標物件
        /// </summary>
        public GameObject TargetObject;

        /// <summary>
        /// 施放者 ASC
        /// </summary>
        public AbilitySystemComponent Instigator;

        /// <summary>
        /// 關聯的效果 Spec (可選)
        /// </summary>
        public GameplayEffectSpec EffectSpec;

        /// <summary>
        /// 縮放
        /// </summary>
        public Vector3 Scale;

        /// <summary>
        /// 強度 (用於縮放效果)
        /// </summary>
        public float Magnitude;

        /// <summary>
        /// 自定義數據
        /// </summary>
        public object CustomData;

        /// <summary>
        /// 創建默認參數
        /// </summary>
        public static GameplayCueParameters Default => new()
        {
            Location = Vector3.zero,
            Rotation = Quaternion.identity,
            Scale = Vector3.one,
            Magnitude = 1f
        };
    }

    /// <summary>
    /// Gameplay Cue 基類 - ScriptableObject
    /// 用於定義視覺/音效反饋
    /// </summary>
    public abstract class GameplayCue : ScriptableObject
    {
        [Header("Basic Info")]
        [Tooltip("Cue 標籤 (用於查找)")]
        public GameplayTag CueTag;

        [Tooltip("Cue 描述")]
        [TextArea(1, 3)]
        public string Description;

        [Header("Settings")]
        [Tooltip("是否跟隨目標物件")]
        public bool AttachToTarget = false;

        [Tooltip("自動銷毀時間 (0 = 不自動銷毀)")]
        public float AutoDestroyTime = 2f;

        /// <summary>
        /// 當 Cue 被執行時調用 (一次性效果)
        /// </summary>
        public abstract void OnExecute(GameplayCueParameters parameters);

        /// <summary>
        /// 當 Cue 被啟動時調用 (持續效果開始)
        /// </summary>
        public virtual void OnActivate(GameplayCueParameters parameters) { }

        /// <summary>
        /// 當 Cue 被停用時調用 (持續效果結束)
        /// </summary>
        public virtual void OnDeactivate(GameplayCueParameters parameters) { }

        /// <summary>
        /// 當 Cue 需要更新時調用 (持續效果更新)
        /// </summary>
        public virtual void OnTick(GameplayCueParameters parameters, float deltaTime) { }

        /// <summary>
        /// 輔助方法：獲取生成位置
        /// </summary>
        protected Vector3 GetSpawnPosition(GameplayCueParameters parameters)
        {
            if (AttachToTarget && parameters.TargetObject != null)
            {
                return parameters.TargetObject.transform.position;
            }
            return parameters.Location;
        }

        /// <summary>
        /// 輔助方法：獲取生成旋轉
        /// </summary>
        protected Quaternion GetSpawnRotation(GameplayCueParameters parameters)
        {
            if (AttachToTarget && parameters.TargetObject != null)
            {
                return parameters.TargetObject.transform.rotation;
            }
            return parameters.Rotation != default ? parameters.Rotation : Quaternion.identity;
        }

        /// <summary>
        /// 輔助方法：獲取父物件
        /// </summary>
        protected Transform GetParentTransform(GameplayCueParameters parameters)
        {
            if (AttachToTarget && parameters.TargetObject != null)
            {
                return parameters.TargetObject.transform;
            }
            return null;
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (!CueTag.IsValid)
            {
                CueTag = new GameplayTag($"Cue.{name}");
            }
        }
#endif
    }

    /// <summary>
    /// Cue 執行器 - 運行時實例
    /// 用於管理持續 Cue 的生命週期
    /// </summary>
    public class GameplayCueHandler
    {
        public GameplayCue CueDef { get; private set; }
        public GameplayCueParameters Parameters { get; private set; }
        public bool IsActive { get; private set; }
        public GameObject SpawnedObject { get; set; }

        private float _activeTime;

        public GameplayCueHandler(GameplayCue cueDef, GameplayCueParameters parameters)
        {
            CueDef = cueDef;
            Parameters = parameters;
            IsActive = false;
            _activeTime = 0f;
        }

        public void Activate()
        {
            IsActive = true;
            _activeTime = 0f;
            CueDef.OnActivate(Parameters);
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive) return;

            _activeTime += deltaTime;
            CueDef.OnTick(Parameters, deltaTime);

            // 檢查自動銷毀
            if (CueDef.AutoDestroyTime > 0f && _activeTime >= CueDef.AutoDestroyTime)
            {
                Deactivate();
            }
        }

        public void Deactivate()
        {
            if (!IsActive) return;

            IsActive = false;
            CueDef.OnDeactivate(Parameters);

            // 清理生成的物件
            if (SpawnedObject != null)
            {
                UnityEngine.Object.Destroy(SpawnedObject);
                SpawnedObject = null;
            }
        }
    }
}
