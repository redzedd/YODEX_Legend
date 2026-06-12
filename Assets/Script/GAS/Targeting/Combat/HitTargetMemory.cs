using UnityEngine;
using GAS.Targeting.LockOnV2;

namespace GAS.Targeting.Combat
{
    /// <summary>
    /// 命中目標記憶 — 攻擊系統在命中時寫入 LastHitTarget,
    /// 供連擊/吸附/Homing/AutoFace 等後續邏輯優先瞄準同一目標。
    /// 攻擊能力結束時呼叫 ScheduleMarkClear() 啟動延遲清除計時(預設 3 秒);
    /// 鎖定中(LockOnController.IsLocked == true)會暫停自動清除。
    /// 掛在玩家根物件上,取代舊 TargetingSystem 的 LastHitTarget 區段。
    /// </summary>
    [DisallowMultipleComponent]
    public class HitTargetMemory : MonoBehaviour
    {
        [Header("設定")]
        [SerializeField, Tooltip("排定清除延遲(秒)— ScheduleMarkClear 後經過此秒數會自動把 LastHitTarget 清空")]
        private float _markClearDelay = 3f;

        [Header("依賴")]
        [SerializeField, Tooltip("鎖定控制器 — 鎖定期間自動清除會暫停(避免鎖著鎖著標記突然消失);未指定時 Awake 自動 GetComponent")]
        private LockOnController _lockOn;

        [Header("除錯")]
        [SerializeField, Tooltip("輸出標記設定/排定/清除的 log")]
        private bool _verboseLog = false;

        private Transform _lastHitTarget;
        private float _clearTimer;
        private bool _isClearScheduled;

        public float MarkClearDelay => _markClearDelay;

        /// <summary>
        /// 最後命中目標。設為非 null 時會重置排定清除計時(新目標 → 取消舊排程,避免立即被清掉)。
        /// 讀取前呼叫者應自行檢查 gameObject.activeInHierarchy,避免在敵人剛死的一幀取到將銷毀的 Transform。
        /// </summary>
        public Transform LastHitTarget
        {
            get => _lastHitTarget;
            set
            {
                _lastHitTarget = value;
                if (value != null)
                {
                    _clearTimer = 0f;
                    _isClearScheduled = false;
                }
            }
        }

        private void Awake()
        {
            if (_lockOn == null)
            {
                _lockOn = GetComponent<LockOnController>();
            }
        }

        private void Update()
        {
            if (!_isClearScheduled) return;
            if (_lastHitTarget == null) return;
            if (_lockOn != null && _lockOn.IsLocked) return;
            _clearTimer += Time.deltaTime;
            if (_clearTimer >= _markClearDelay)
            {
                if (_verboseLog)
                {
                    Debug.Log($"[HitTargetMemory] 延遲清除完成({_markClearDelay:F1} 秒)", this);
                }
                _lastHitTarget = null;
                _isClearScheduled = false;
                _clearTimer = 0f;
            }
        }

        /// <summary>
        /// 啟動延遲清除計時 — 攻擊能力結束時呼叫。
        /// 計時歸零重新計,鎖定期間不會遞增。LastHitTarget 為 null 時呼叫無效。
        /// </summary>
        public void ScheduleMarkClear()
        {
            if (_lastHitTarget == null) return;
            _isClearScheduled = true;
            _clearTimer = 0f;
            if (_verboseLog)
            {
                Debug.Log($"[HitTargetMemory] 排定清除:{_lastHitTarget.name}({_markClearDelay:F1} 秒後)", this);
            }
        }

        /// <summary>
        /// 取消排定清除 — 新一輪連擊 / 鎖定切換到同目標時呼叫,避免標記在連擊中途被清掉。
        /// </summary>
        public void CancelMarkClear()
        {
            if (!_isClearScheduled) return;
            _isClearScheduled = false;
            _clearTimer = 0f;
            if (_verboseLog)
            {
                Debug.Log("[HitTargetMemory] 取消排定清除", this);
            }
        }

        /// <summary>
        /// 立即清除 — 能力取消、受擊打斷、解鎖等「場景劇情」變更時呼叫。
        /// </summary>
        public void ClearMarkImmediate()
        {
            if (_verboseLog && _lastHitTarget != null)
            {
                Debug.Log($"[HitTargetMemory] 立即清除:{_lastHitTarget.name}", this);
            }
            _lastHitTarget = null;
            _isClearScheduled = false;
            _clearTimer = 0f;
        }
    }
}
