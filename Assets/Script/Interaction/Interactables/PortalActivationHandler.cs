using UnityEngine;
using CameraSystem;
using GAS.UI.Inventory;
using Item;

namespace Interaction
{
    /// <summary>
    /// 傳送門激活處理器 — 需要特定鑰匙道具才能激活
    /// 無鑰匙：透過 InteractionHintUI 顯示提示文字
    /// 有鑰匙：透過 CinematicCameraSequence 演出（含 UI 淡入淡出、輸入鎖定、相機聚焦）→ 啟動傳送門特效
    /// 由 GenericInteractable 委派呼叫
    /// </summary>
    public class PortalActivationHandler : InteractionHandler
    {
        [Header("鑰匙需求")]
        [Tooltip("需要的鑰匙物品")]
        [SerializeField] private ItemData _requiredKeyItem;
        [Tooltip("激活後是否消耗鑰匙")]
        [SerializeField] private bool _consumeKey = true;

        [Header("傳送門")]
        [Tooltip("傳送門特效物件（激活後顯示）")]
        [SerializeField] private GameObject _portalVFX;
        [Tooltip("傳送門碰撞觸發器（激活後啟用）")]
        [SerializeField] private Collider _portalTrigger;

        [Header("失敗提示")]
        [Tooltip("缺少鑰匙時的提示文字內容")]
        [SerializeField] private string _failureMessage = "需要鑰匙才能開啟傳送門";
        [Tooltip("缺少鑰匙時的失敗音效")]
        [SerializeField] private AudioClip _failureSFX;

        [Header("成功音效")]
        [Tooltip("傳送門激活音效")]
        [SerializeField] private AudioClip _activationSFX;
        [SerializeField] private AudioSource _audioSource;

        [Header("演出序列")]
        [Tooltip("Camera 切換 / UI 淡入淡出 / 輸入鎖定 / 計時等都在這裡設定")]
        [SerializeField] private CinematicCameraSequence _sequence = new();

        private bool _isActivated;
        private GenericInteractable _interactable;

        private void Awake()
        {
            _interactable = GetComponentInParent<GenericInteractable>();
        }

        private void OnDestroy()
        {
            _sequence?.Cleanup();
        }

        /// <summary>已激活或演出中時不可再互動</summary>
        public override bool CanExecute() => !_isActivated && !_sequence.IsPlaying;

        public override void Execute()
        {
            if (_isActivated || _sequence.IsPlaying) return;
            if (!HasRequiredKey())
            {
                ShowFailureHint();
                return;
            }
            // 立即從互動系統取消註冊，隱藏互動提示 UI
            if (_interactable != null && InteractionManager.Instance != null)
                InteractionManager.Instance.UnregisterInteractable(_interactable);
            StartCoroutine(_sequence.Play(ActivatePortal));
        }

        private bool HasRequiredKey()
        {
            if (_requiredKeyItem == null) return true;
            return InventoryManager.Instance != null
                && InventoryManager.Instance.HasItemByName(_requiredKeyItem.itemName);
        }

        private void ShowFailureHint()
        {
            if (InteractionHintUI.Instance != null)
                InteractionHintUI.Instance.Show(_failureMessage, _failureSFX);
        }

        // 演出動作回呼：消耗鑰匙 + 啟動傳送門 + 播音效
        private void ActivatePortal()
        {
            if (_consumeKey && _requiredKeyItem != null && InventoryManager.Instance != null)
                InventoryManager.Instance.RemoveItemByName(_requiredKeyItem.itemName);
            if (_portalVFX != null)
                _portalVFX.SetActive(true);
            if (_portalTrigger != null)
                _portalTrigger.enabled = true;
            if (_audioSource != null && _activationSFX != null)
                _audioSource.PlayOneShot(_activationSFX);
            _isActivated = true;
        }
    }
}
