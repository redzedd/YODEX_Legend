using System;
using UnityEngine;

namespace GAS.Targeting.LockOnV2
{
    /// <summary>
    /// 鎖定控制器 — 掛在玩家 root 上
    /// 維護鎖定狀態，將 PlayerAnchor 與目標 EnemyAnchor 雙向綁定使其互相面對
    /// 攝影機切換不在此處處理；外部 (Cinemachine 整合層) 訂閱 OnTargetChanged 事件即可
    /// </summary>
    [DisallowMultipleComponent]
    public class LockOnController : MonoBehaviour
    {
        [Header("Player Anchor")]
        [SerializeField]
        [Tooltip("玩家身上面向敵人的 LockOnAnchor (通常掛在子物件 PlayerAnchor)；留空則自動於子物件搜尋")]
        private LockOnAnchor _playerAnchor;

        [Header("Behavior")]
        [SerializeField]
        [Tooltip("鎖定瞬間是否讓 PlayerAnchor 立即對齊目標 (避免鏡頭從預設方向滑入)")]
        private bool _snapPlayerAnchorOnLock = true;

        [SerializeField]
        [Tooltip("自動斷開偵測的檢查間隔 (秒);0 = 每幀檢查")]
        private float _autoUnlockCheckInterval = 0.1f;

        [SerializeField]
        [Tooltip("鎖定後若目標超過此距離 → 自動觸發斷開流程")]
        private float _autoDisconnectRange = 22f;

        [SerializeField]
        [Tooltip("自動斷開時「視野近距離範圍」— 僅在此範圍內找下一個目標;找不到才真的解鎖")]
        private float _autoSwitchRange = 10f;

        [Header("Target Selection")]
        [SerializeField]
        [Tooltip("螢幕評分用攝影機;留空時 Awake 取 Camera.main")]
        private Camera _camera;

        [SerializeField]
        [Tooltip("目標搜尋與評分參數 (距離、權重、視線 mask 等)")]
        private LockOnSelectorConfig _selectorConfig = new();

        [Header("Debug")]
        [SerializeField]
        [Tooltip("輸出鎖定 / 解鎖 / 切換的除錯訊息")]
        private bool _verboseLog = false;

        [SerializeField]
        [Tooltip("輸出定期斷開檢查的距離與判定 (每 _autoUnlockCheckInterval 一次,訊息較密集)")]
        private bool _logDisconnectChecks = false;

        [SerializeField]
        [Tooltip("Scene 視窗顯示搜尋範圍 / 自動斷開範圍 / 鎖定連線 (選中玩家時才顯示)")]
        private bool _drawGizmos = true;

        public LockOnAnchor PlayerAnchor => _playerAnchor;

        public LockOnTarget CurrentTarget => _currentTarget;

        public bool IsLocked => _currentTarget != null;

        public event Action<LockOnTarget> OnTargetChanged;

        public LockOnSelector Selector => _selector;

        private LockOnTarget _currentTarget;
        private float _autoUnlockTimer;
        private LockOnSelector _selector;

        private void Awake()
        {
            if (_playerAnchor == null)
            {
                _playerAnchor = GetComponentInChildren<LockOnAnchor>(true);
            }
            if (_camera == null) _camera = Camera.main;
            _selector = new LockOnSelector(_camera, _selectorConfig);
        }

        private void OnDisable()
        {
            if (_currentTarget != null) Unlock();
        }

        private void Update()
        {
            if (_currentTarget == null) return;
            _autoUnlockTimer += Time.deltaTime;
            if (_autoUnlockTimer < _autoUnlockCheckInterval) return;
            _autoUnlockTimer = 0f;
            if (ShouldAutoDisconnect())
            {
                AutoSwitchOrUnlock();
            }
        }

        /// <summary>
        /// 目標失效 (死亡 / 隱藏 / IsLockable=false) 或超出自動斷開距離 → 返回 true
        /// </summary>
        private bool ShouldAutoDisconnect()
        {
            if (_currentTarget == null) return LogCheckResult(true, "currentTarget=null");
            if (!_currentTarget.isActiveAndEnabled) return LogCheckResult(true, "target inactive");
            if (!_currentTarget.IsLockable) return LogCheckResult(true, "IsLockable=false");
            Transform anchor = _currentTarget.AnchorTransform;
            if (anchor == null) return LogCheckResult(true, "AnchorTransform=null");
            Vector3 origin = GetSelectorOrigin();
            float sqDist = (anchor.position - origin).sqrMagnitude;
            float rangeSq = _autoDisconnectRange * _autoDisconnectRange;
            bool over = sqDist > rangeSq;
            if (_logDisconnectChecks)
            {
                float dist = Mathf.Sqrt(sqDist);
                Debug.Log($"[LockOnV2 check] dist={dist:F2}m vs range={_autoDisconnectRange:F2}m → over={over} | originY={origin.y:F2} anchorY={anchor.position.y:F2}");
            }
            return over;
        }

        private bool LogCheckResult(bool result, string reason)
        {
            if (_logDisconnectChecks) Debug.Log($"[LockOnV2 check] disconnect={result} reason={reason}");
            return result;
        }

        /// <summary>
        /// 自動斷開流程:優先在 SearchRange 內找下一個目標,找不到才真的解鎖
        /// 注意:僅由自動偵測路徑呼叫,玩家主動 Unlock / ToggleBestLock 不會走這裡
        /// </summary>
        private void AutoSwitchOrUnlock()
        {
            LockOnTarget previous = _currentTarget;
            LockOnTarget next = _selector?.FindInitialTarget(GetSelectorOrigin(), _autoSwitchRange);
            if (next != null && next != previous)
            {
                if (_verboseLog) Debug.Log($"[LockOnV2] 自動切換目標: {SafeName(previous)} → {next.name}");
                Lock(next);
                return;
            }
            if (_verboseLog) Debug.Log($"[LockOnV2] 自動解鎖: AutoSwitchRange 內無後續目標");
            Unlock();
        }

        private static string SafeName(LockOnTarget t) => t == null ? "null" : t.name;

        public void Lock(LockOnTarget target)
        {
            if (target == null)
            {
                Unlock();
                return;
            }
            if (!target.IsLockable) return;
            if (target == _currentTarget) return;
            UnbindCurrent();
            _currentTarget = target;
            _autoUnlockTimer = 0f;
            BindCurrent();
            if (_snapPlayerAnchorOnLock && _playerAnchor != null)
            {
                _playerAnchor.SnapToTarget();
            }
            if (_verboseLog) Debug.Log($"[LockOnV2] 鎖定目標：{target.name}");
            OnTargetChanged?.Invoke(target);
        }

        public bool TryLock(GameObject targetRoot)
        {
            if (targetRoot == null) return false;
            LockOnTarget target = targetRoot.GetComponent<LockOnTarget>();
            if (target == null) return false;
            Lock(target);
            return _currentTarget == target;
        }

        public void Unlock()
        {
            if (_currentTarget == null) return;
            if (_verboseLog) Debug.Log($"[LockOnV2] 解除鎖定：{_currentTarget.name}");
            UnbindCurrent();
            _currentTarget = null;
            _autoUnlockTimer = 0f;
            OnTargetChanged?.Invoke(null);
        }

        public void ToggleLock(LockOnTarget target)
        {
            if (_currentTarget != null && (_currentTarget == target || target == null))
            {
                Unlock();
                return;
            }
            Lock(target);
        }

        /// <summary>
        /// 自動挑選最佳目標並鎖定 (玩家按下鎖定鍵的入口)
        /// </summary>
        public bool TryLockBest()
        {
            if (_selector == null) return false;
            LockOnTarget t = _selector.FindInitialTarget(GetSelectorOrigin());
            if (t == null) return false;
            Lock(t);
            return true;
        }

        /// <summary>
        /// 8 方向切換目標 (R-stick / 滑鼠手勢)
        /// stickDir 為螢幕空間方向 (右=+X、上=+Y);未鎖定時呼叫無效
        /// </summary>
        public bool TryLockDirectional(Vector2 stickDir)
        {
            if (_selector == null || _currentTarget == null) return false;
            LockOnTarget t = _selector.FindDirectionalTarget(_currentTarget, stickDir, GetSelectorOrigin());
            if (t == null) return false;
            Lock(t);
            return true;
        }

        /// <summary>
        /// 鎖定 / 解除鎖定切換 (玩家按下鎖定鍵的常見綁法)
        /// 已鎖定 → 解除;未鎖定 → 自動挑最佳目標
        /// </summary>
        public bool ToggleBestLock()
        {
            if (IsLocked)
            {
                Unlock();
                return false;
            }
            return TryLockBest();
        }

        private Vector3 GetSelectorOrigin() =>
            _playerAnchor != null ? _playerAnchor.transform.position : transform.position;

        private void BindCurrent()
        {
            if (_currentTarget == null) return;
            if (_playerAnchor != null) _playerAnchor.SetTarget(_currentTarget.AnchorTransform);
            LockOnAnchor enemyAnchor = _currentTarget.Anchor;
            if (enemyAnchor != null) enemyAnchor.SetTarget(_playerAnchor != null ? _playerAnchor.transform : transform);
            _currentTarget.NotifyLocked();
        }

        private void UnbindCurrent()
        {
            if (_currentTarget == null) return;
            if (_playerAnchor != null) _playerAnchor.ClearTarget();
            LockOnAnchor enemyAnchor = _currentTarget.Anchor;
            if (enemyAnchor != null) enemyAnchor.ClearTarget();
            _currentTarget.NotifyUnlocked();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmos) return;
            Vector3 origin = GetSelectorOrigin();
            if (_selectorConfig != null)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.8f);
                Gizmos.DrawWireSphere(origin, _selectorConfig.SearchRange);
            }
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(origin, _autoSwitchRange);
            Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(origin, _autoDisconnectRange);
            if (!Application.isPlaying || _currentTarget == null) return;
            Transform anchor = _currentTarget.AnchorTransform;
            if (anchor == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, anchor.position);
            Gizmos.DrawWireSphere(anchor.position, 0.3f);
        }

        // ContextMenu 在 Editor 非標準幀觸發,會讓 CinemachineBrain 累積異常 deltaTime → blend 一幀跑完看似閃現
        // 僅供「驗證鎖定邏輯本身」測試;檢查 blend 過渡請改用鍵盤或實際輸入觸發 Lock/Unlock
        [ContextMenu("除錯：鎖定 Registry 第一個目標")]
        private void DebugLockFirstRegistered()
        {
            foreach (LockOnTarget t in LockOnRegistry.All)
            {
                if (t == this) continue;
                Lock(t);
                return;
            }
            Debug.LogWarning("[LockOnV2] Registry 內無可鎖定目標");
        }

        [ContextMenu("除錯：解除鎖定")]
        private void DebugUnlock() => Unlock();
#endif
    }
}
