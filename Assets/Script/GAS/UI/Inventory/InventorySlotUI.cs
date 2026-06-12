using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GAS.UI.Inventory
{
    /// <summary>
    /// 背包格子 UI 事件處理 — 處理懸浮描述、焦點追蹤、跨分類翻頁
    /// 懸浮動畫委派給 InventoryAnimator
    /// </summary>
    public class InventorySlotUI : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler,
        IMoveHandler
    {
        [HideInInspector] public int slotIndex;
        [HideInInspector] public InventoryDisplay inventoryDisplay;

        [SerializeField] private InventoryAnimator _animator;

        private RectTransform _rectTransform;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (inventoryDisplay == null) return;
            inventoryDisplay.ShowItemDescription(slotIndex);
            EventSystem.current.SetSelectedGameObject(gameObject);
            PlayHoverEnter();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            PlayHoverExit();
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (inventoryDisplay == null) return;
            inventoryDisplay.ShowItemDescription(slotIndex);
            Button button = GetComponent<Button>();
            if (button != null)
                inventoryDisplay.SetLastClickedSlotButton(button);
            PlayHoverEnter();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            PlayHoverExit();
        }

        public void OnMove(AxisEventData eventData)
        {
            if (inventoryDisplay == null) return;
            if (eventData.moveDir == MoveDirection.Right || eventData.moveDir == MoveDirection.Left)
            {
                bool handled = inventoryDisplay.OnSlotMove(slotIndex, eventData.moveDir);
                if (handled) eventData.Use();
            }
        }

        private void PlayHoverEnter()
        {
            if (_animator != null && _rectTransform != null)
                _animator.PlaySlotHoverEnter(_rectTransform);
        }

        private void PlayHoverExit()
        {
            if (_animator != null && _rectTransform != null)
                _animator.PlaySlotHoverExit(_rectTransform);
        }
    }
}
