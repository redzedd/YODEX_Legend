using System;
using System.Collections;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 能力實例 - 運行時的能力數據
    /// 每個授予的能力對應一個 Spec
    /// </summary>
    public class GameplayAbilitySpec
    {
        /// <summary>
        /// 能力定義
        /// </summary>
        public GameplayAbility AbilityDef { get; private set; }

        /// <summary>
        /// 擁有此能力的 ASC
        /// </summary>
        public AbilitySystemComponent Owner { get; private set; }

        /// <summary>
        /// 能力等級
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// 能力是否正在執行
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// 能力啟動時間
        /// </summary>
        public float ActivationTime { get; private set; }

        /// <summary>
        /// 唯一標識符
        /// </summary>
        public Guid Handle { get; private set; }

        /// <summary>
        /// 輸入是否被按下 (用於持續按住類能力)
        /// </summary>
        public bool InputPressed { get; set; }

        /// <summary>
        /// 自定義數據 (可用於存儲能力執行期間的狀態)
        /// </summary>
        public object CustomData { get; set; }

        // 當前執行的 Coroutine
        private Coroutine _activeCoroutine;

        // 事件
        public event Action<GameplayAbilitySpec> OnActivated;
        public event Action<GameplayAbilitySpec, bool> OnEnded;

        public GameplayAbilitySpec(GameplayAbility abilityDef, AbilitySystemComponent owner, int level = 1)
        {
            AbilityDef = abilityDef;
            Owner = owner;
            Level = level;
            Handle = Guid.NewGuid();
            IsActive = false;
        }

        /// <summary>
        /// 嘗試啟動能力
        /// </summary>
        public bool TryActivate()
        {
            if (AbilityDef == null || Owner == null)
            {
                Debug.LogWarning("[GameplayAbilitySpec] Cannot activate: AbilityDef or Owner is null");
                return false;
            }

            if (!AbilityDef.CanActivateAbility(this))
            {
                if (Owner.DebugMode)
                {
                    Debug.Log($"[GameplayAbilitySpec] Cannot activate {AbilityDef.AbilityName}: conditions not met");
                }
                return false;
            }

            Activate();
            return true;
        }

        /// <summary>
        /// 強制啟動能力 (跳過檢查)
        /// </summary>
        public void ForceActivate()
        {
            Activate();
        }

        /// <summary>
        /// 內部啟動邏輯
        /// </summary>
        private void Activate()
        {
            IsActive = true;
            ActivationTime = Time.time;

            // 添加啟動標籤
            if (!AbilityDef.ActivationOwnedTags.IsEmpty)
            {
                Owner.OwnedTags.AddTags(AbilityDef.ActivationOwnedTags);
            }

            if (Owner.DebugMode)
            {
                Debug.Log($"[GameplayAbilitySpec] Activating: {AbilityDef.AbilityName}");
            }

            OnActivated?.Invoke(this);

            // 調用能力的啟動邏輯
            AbilityDef.ActivateAbility(this);
        }

        /// <summary>
        /// 結束能力
        /// </summary>
        public void EndAbility()
        {
            if (!IsActive) return;
            
            InternalEndAbility(false);
        }

        /// <summary>
        /// 取消能力
        /// </summary>
        public void CancelAbility()
        {
            if (!IsActive) return;
            
            InternalEndAbility(true);
        }

        /// <summary>
        /// 內部結束邏輯
        /// </summary>
        private void InternalEndAbility(bool wasCancelled)
        {
            if (!IsActive) return;
            
            IsActive = false;

            // 停止正在執行的 Coroutine
            if (_activeCoroutine != null && Owner != null)
            {
                Owner.StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }

            // 調用能力的結束邏輯
            AbilityDef?.EndAbility(this, wasCancelled);

            if (Owner != null && Owner.DebugMode)
            {
                Debug.Log($"[GameplayAbilitySpec] Ended: {AbilityDef?.AbilityName} (Cancelled: {wasCancelled})");
            }

            OnEnded?.Invoke(this, wasCancelled);
        }

        /// <summary>
        /// 獲取能力已執行的時間
        /// </summary>
        public float GetElapsedTime()
        {
            if (!IsActive) return 0f;
            return Time.time - ActivationTime;
        }

        /// <summary>
        /// 設置並追蹤 Coroutine
        /// </summary>
        public void SetActiveCoroutine(Coroutine coroutine)
        {
            _activeCoroutine = coroutine;
        }

        /// <summary>
        /// 檢查能力是否可以被指定標籤取消
        /// </summary>
        public bool CanBeCancelledBy(GameplayTag cancellerTag)
        {
            if (!AbilityDef.CancelledByTags.IsEmpty)
            {
                return AbilityDef.CancelledByTags.HasTag(cancellerTag);
            }
            return false;
        }

        public override string ToString()
        {
            string status = IsActive ? "Active" : "Inactive";
            return $"{AbilityDef?.AbilityName ?? "Unknown"} ({status}, Level {Level})";
        }
    }

    /// <summary>
    /// 能力輸入綁定資訊
    /// </summary>
    [Serializable]
    public class AbilityInputBinding
    {
        [Tooltip("綁定的能力標籤")]
        public GameplayTag AbilityTag;

        [Tooltip("輸入動作名稱 (對應 Input System)")]
        public string InputActionName;

        [Tooltip("按下時啟動還是釋放時啟動")]
        public AbilityInputTrigger Trigger = AbilityInputTrigger.OnPressed;
    }

    /// <summary>
    /// 能力輸入觸發類型
    /// </summary>
    public enum AbilityInputTrigger
    {
        /// <summary>
        /// 按下時啟動
        /// </summary>
        OnPressed,

        /// <summary>
        /// 釋放時啟動
        /// </summary>
        OnReleased,

        /// <summary>
        /// 持續按住時持續啟動
        /// </summary>
        WhileHeld
    }
}
