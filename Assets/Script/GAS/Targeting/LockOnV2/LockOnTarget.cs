using System;
using UnityEngine;

namespace GAS.Targeting.LockOnV2
{
    /// <summary>
    /// 鎖定目標標記元件 — 掛在可被鎖定的敵人 root 上
    /// 持有面向玩家的 LockOnAnchor 引用，並在 enable 時加入 LockOnRegistry 供搜尋
    /// </summary>
    [DisallowMultipleComponent]
    public class LockOnTarget : MonoBehaviour
    {
        [Header("Anchor")]
        [SerializeField]
        [Tooltip("敵人身上面向玩家的 LockOnAnchor (通常掛在子物件 EnemyAnchor)；留空則自動於子物件搜尋")]
        private LockOnAnchor _anchor;

        [Header("State")]
        [SerializeField]
        [Tooltip("是否可被鎖定；死亡 / 倒地 / 隱身狀態時設為 false 即可暫時排除")]
        private bool _isLockable = true;

        [Header("Metadata")]
        [SerializeField]
        [Tooltip("UI 顯示名稱 (給未來鎖定 HUD / 名稱條使用)")]
        private string _displayName;

        public LockOnAnchor Anchor => _anchor;

        public Transform AnchorTransform => _anchor != null ? _anchor.transform : transform;

        public bool IsLockable
        {
            get => _isLockable;
            set => _isLockable = value;
        }

        public string DisplayName => _displayName;

        public event Action<LockOnTarget> OnLocked;
        public event Action<LockOnTarget> OnUnlocked;

        private void Awake()
        {
            if (_anchor == null)
            {
                _anchor = GetComponentInChildren<LockOnAnchor>(true);
            }
        }

        private void OnEnable()
        {
            LockOnRegistry.Register(this);
        }

        private void OnDisable()
        {
            LockOnRegistry.Unregister(this);
        }

        public void NotifyLocked() => OnLocked?.Invoke(this);

        public void NotifyUnlocked() => OnUnlocked?.Invoke(this);
    }
}
