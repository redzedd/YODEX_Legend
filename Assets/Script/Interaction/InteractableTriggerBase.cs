using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 互動觸發基底 — 自動處理 Trigger 進出時的 InteractionManager 註冊
    /// 子類僅需實作 Interact() 等互動邏輯，Trigger 進出由此基底統一管理
    /// 場景載入時自動偵測玩家是否已在 Trigger 範圍內
    /// </summary>
    public abstract class InteractableTriggerBase : MonoBehaviour, IInteractable
    {
        [SerializeField]
        [Tooltip("玩家標籤（用於 Trigger 過濾）")]
        private string _playerTag = "Player";

        private bool _isRegistered;
        private Collider _triggerCollider;
        /// <summary>
        /// 是否需由 FixedUpdate 主動偵測退場。
        /// CheckInitialOverlap 繞過 OnTriggerEnter，Unity 不保證對應的 OnTriggerExit 觸發，
        /// 所以在這種情境下改用 OverlapBox 主動輪詢。
        /// </summary>
        private bool _checkExitManually;

        #region IInteractable（子類實作）

        public abstract int Priority { get; }
        public abstract string InteractionTypeName { get; }
        public abstract string PromptText { get; }
        public virtual bool CanInteract => true;
        public abstract void Interact();

        /// <summary>進入聚焦（子類可 override 做額外邏輯）</summary>
        public virtual void OnFocus() { }

        /// <summary>離開聚焦（子類可 override 做額外邏輯）</summary>
        public virtual void OnUnfocus() { }

        #endregion

        #region 生命週期

        protected virtual void Awake()
        {
            _triggerCollider = GetComponent<Collider>();
        }

        protected virtual void Start()
        {
            CheckInitialOverlap();
        }

        /// <summary>
        /// 場景起始重疊的退場偵測。
        /// OnTriggerExit 在 Enter 從未觸發的情境下行為不可靠，
        /// 透過 OverlapBox 主動輪詢確認玩家是否已離開。
        /// 只在 _checkExitManually 為 true（即 CheckInitialOverlap 成功登記）時執行，
        /// 玩家離開後立即停止，效能影響最小。
        /// </summary>
        private void FixedUpdate()
        {
            if (!_isRegistered || !_checkExitManually || _triggerCollider == null) return;
            Bounds bounds = _triggerCollider.bounds;
            Collider[] overlaps = Physics.OverlapBox(bounds.center, bounds.extents, transform.rotation);
            for (int i = 0; i < overlaps.Length; i++)
            {
                if (overlaps[i].CompareTag(_playerTag)) return; // 玩家仍在範圍內
            }
            // 玩家已離開，手動觸發取消登記
            ForceUnregister();
            _checkExitManually = false;
        }

        #endregion

        #region 初始重疊檢測

        /// <summary>
        /// 場景載入時檢查玩家是否已在 Trigger 範圍內
        /// 解決 OnTriggerEnter 不會對已重疊碰撞器觸發的問題
        /// </summary>
        private void CheckInitialOverlap()
        {
            if (_isRegistered) return;
            if (_triggerCollider == null) return;
            if (InteractionManager.Instance == null) return;
            if (!CanInteract) return;
            Bounds bounds = _triggerCollider.bounds;
            Collider[] overlaps = Physics.OverlapBox(
                bounds.center, bounds.extents, transform.rotation);
            for (int i = 0; i < overlaps.Length; i++)
            {
                if (!overlaps[i].CompareTag(_playerTag)) continue;
                InteractionManager.Instance.RegisterInteractable(this);
                _isRegistered = true;
                // OnTriggerExit 不可靠，改由 FixedUpdate 負責偵測退場
                _checkExitManually = true;
                return;
            }
        }

        #endregion

        #region Trigger 註冊

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            if (_isRegistered) return;
            if (!CanInteract) return;
            if (InteractionManager.Instance == null) return;
            InteractionManager.Instance.RegisterInteractable(this);
            _isRegistered = true;
            // 正常 Enter 流程：OnTriggerExit 會正常觸發，不需要 FixedUpdate 輪詢
            _checkExitManually = false;
        }

        /// <summary>
        /// OnTriggerStay 作為 OnTriggerEnter 的後備：
        /// 若場景起始即在範圍內且 Enter 未觸發，由 Stay 補完首次登記，
        /// 同時讓 Unity 物理引擎建立配對追蹤以輔助 Exit 觸發。
        /// </summary>
        protected virtual void OnTriggerStay(Collider other)
        {
            if (_isRegistered) return;
            OnTriggerEnter(other);
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            if (!_isRegistered) return;
            if (InteractionManager.Instance == null) return;
            InteractionManager.Instance.UnregisterInteractable(this);
            _isRegistered = false;
            _checkExitManually = false;
        }

        /// <summary>強制取消註冊（用於互動完成後自行移除）</summary>
        protected void ForceUnregister()
        {
            if (!_isRegistered) return;
            if (InteractionManager.Instance == null) return;
            InteractionManager.Instance.UnregisterInteractable(this);
            _isRegistered = false;
        }

        /// <summary>
        /// 外部呼叫的 Trigger 狀態重新校驗 —
        /// 用 Physics.OverlapBox 確認玩家是否在範圍內，依結果註冊/取消註冊。
        /// 用途：玩家被瞬間傳送、被擊飛等不會觸發 OnTriggerExit / OnTriggerEnter 的情境，
        /// 由外部呼叫此方法強制修正內部狀態。
        /// </summary>
        public void ResyncTriggerState()
        {
            if (_triggerCollider == null) return;
            Bounds bounds = _triggerCollider.bounds;
            Collider[] overlaps = Physics.OverlapBox(bounds.center, bounds.extents, transform.rotation);
            bool playerInside = false;
            for (int i = 0; i < overlaps.Length; i++)
            {
                if (overlaps[i].CompareTag(_playerTag))
                {
                    playerInside = true;
                    break;
                }
            }
            if (!playerInside && _isRegistered)
            {
                ForceUnregister();
                _checkExitManually = false;
            }
            else if (playerInside && !_isRegistered && CanInteract && InteractionManager.Instance != null)
            {
                InteractionManager.Instance.RegisterInteractable(this);
                _isRegistered = true;
                _checkExitManually = true;
            }
        }

        /// <summary>是否已註冊到 InteractionManager</summary>
        protected bool IsRegistered => _isRegistered;

        #endregion
    }
}
