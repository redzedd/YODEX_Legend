using System.Collections.Generic;
using UnityEngine;
using Item;

namespace Interaction
{
    /// <summary>
    /// 物品拾取處理器 — 偵測範圍內的 PickUpItem 並執行拾取
    /// 不繼承 InteractableTriggerBase（使用不同的 Trigger 模式：偵測 PickUpItem 而非 Player tag）
    /// </summary>
    public class ItemPickupHandler : MonoBehaviour, IInteractable
    {
        [Header("互動設定")]
        [Tooltip("互動類型名稱（需與 InteractionPromptUI 的圖示對應名稱一致）")]
        [SerializeField] private string _interactionTypeName = InteractionType.Pickup;
        [Tooltip("互動提示文字")]
        [SerializeField] private string _promptText = "拾取";
        [Tooltip("互動優先級（數值越低越優先）。預設 -1000,確保拾取永遠贏過任何 GenericInteractable(GenericInteractable 預設值 1,平手時會卡在當前焦點不切換)。除非有特殊互動要蓋過拾取,否則不要動")]
        [SerializeField] private int _priority = -1000;

        [Header("音效")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _pickUpSFX;

        private readonly List<PickUpItem> _itemsInRange = new List<PickUpItem>();
        private bool _isRegisteredToManager;
        private Collider _triggerCollider;
        /// <summary>
        /// 是否存在由 Physics.OverlapBox 初始加入的物品。
        /// OverlapBox 繞過 OnTriggerEnter，Unity 不追蹤碰撞對，
        /// 故 OnTriggerExit 不會對這些物品觸發，需由 FixedUpdate 主動偵測退場。
        /// </summary>
        private bool _hasInitialItems;

        #region IInteractable

        public int Priority => _priority;
        public string InteractionTypeName => _interactionTypeName;
        public string PromptText => _promptText;
        public bool CanInteract => _itemsInRange.Count > 0;

        public void Interact()
        {
            if (_itemsInRange.Count == 0) return;
            PickUpItem closest = GetClosestItem();
            if (closest == null) return;
            closest.Pickup(_audioSource, _pickUpSFX);
            _itemsInRange.Remove(closest);
        }

        public void OnFocus() { }
        public void OnUnfocus() { }

        #endregion

        #region 註冊管理

        private void LateUpdate()
        {
            if (InteractionManager.Instance == null) return;
            // 清除已銷毀的 PickUpItem
            for (int i = _itemsInRange.Count - 1; i >= 0; i--)
            {
                if (_itemsInRange[i] == null)
                    _itemsInRange.RemoveAt(i);
            }
            bool hasItem = _itemsInRange.Count > 0;
            if (hasItem && !_isRegisteredToManager)
            {
                InteractionManager.Instance.RegisterInteractable(this);
                _isRegisteredToManager = true;
            }
            else if (!hasItem && _isRegisteredToManager)
            {
                InteractionManager.Instance.UnregisterInteractable(this);
                _isRegisteredToManager = false;
            }
        }

        #endregion

        #region 生命週期

        private void Awake()
        {
            _triggerCollider = GetComponent<Collider>();
        }

        #endregion

        #region 初始重疊檢測

        /// <summary>
        /// 場景載入時檢查是否已有 PickUpItem 在 Trigger 範圍內
        /// 解決 OnTriggerEnter 不會對已重疊碰撞器觸發的問題
        /// </summary>
        private void Start()
        {
            if (_triggerCollider == null) return;
            Bounds bounds = _triggerCollider.bounds;
            Collider[] overlaps = Physics.OverlapBox(
                bounds.center, bounds.extents, transform.rotation);
            for (int i = 0; i < overlaps.Length; i++)
            {
                PickUpItem item = overlaps[i].GetComponent<PickUpItem>();
                if (item != null && !_itemsInRange.Contains(item))
                {
                    _itemsInRange.Add(item);
                    _hasInitialItems = true;
                }
            }
        }

        /// <summary>
        /// 主動輪詢初始重疊物品是否仍在範圍內。
        /// OnTriggerExit 對 OverlapBox 加入的物品不觸發，
        /// 故在此透過 OverlapBox 比對確認退場，玩家離開後立即停止。
        /// </summary>
        private void FixedUpdate()
        {
            if (!_hasInitialItems || _triggerCollider == null) return;
            Bounds bounds = _triggerCollider.bounds;
            Collider[] currentOverlaps = Physics.OverlapBox(
                bounds.center, bounds.extents, transform.rotation);
            for (int i = _itemsInRange.Count - 1; i >= 0; i--)
            {
                if (_itemsInRange[i] == null) continue;
                if (IsItemInOverlaps(_itemsInRange[i], currentOverlaps)) continue;
                _itemsInRange.RemoveAt(i);
            }
            if (_itemsInRange.Count == 0)
                _hasInitialItems = false;
        }

        private bool IsItemInOverlaps(PickUpItem item, Collider[] overlaps)
        {
            for (int i = 0; i < overlaps.Length; i++)
            {
                if (overlaps[i].GetComponent<PickUpItem>() == item)
                    return true;
            }
            return false;
        }

        #endregion

        #region Trigger 偵測

        private void OnTriggerEnter(Collider other)
        {
            PickUpItem item = other.GetComponent<PickUpItem>();
            if (item != null && !_itemsInRange.Contains(item))
                _itemsInRange.Add(item);
        }

        private void OnTriggerExit(Collider other)
        {
            PickUpItem item = other.GetComponent<PickUpItem>();
            if (item != null)
                _itemsInRange.Remove(item);
        }

        #endregion

        /// <summary>取得距離最近的可拾取物品</summary>
        private PickUpItem GetClosestItem()
        {
            float minDist = float.MaxValue;
            PickUpItem closest = null;
            for (int i = 0; i < _itemsInRange.Count; i++)
            {
                if (_itemsInRange[i] == null) continue;
                float dist = Vector3.SqrMagnitude(
                    transform.position - _itemsInRange[i].transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = _itemsInRange[i];
                }
            }
            return closest;
        }
    }
}
