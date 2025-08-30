using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IMoveHandler
{
    public int slotIndex; // 0~11（3x4）
    public InventoryDisplay inventoryDisplay;
    public Button Button;

    public void OnPointerEnter(PointerEventData eventData)
    {
        inventoryDisplay.ShowItemDescription(slotIndex);
        EventSystem.current.SetSelectedGameObject(this.gameObject);
    }

    public void OnSelect(BaseEventData eventData)
    {
        inventoryDisplay.ShowItemDescription(slotIndex);
        var button = GetComponent<Button>();
        if (button != null) inventoryDisplay.SetLastClickedSlotButton(button);
    }

    // ★ 攔截橫向移動：在最左/最右欄時跨分類翻頁
    public void OnMove(AxisEventData eventData)
    {
        if (inventoryDisplay == null) return;
        if (eventData.moveDir == MoveDirection.Right || eventData.moveDir == MoveDirection.Left)
        {
            bool handled = inventoryDisplay.OnSlotMove(slotIndex, eventData.moveDir);
            if (handled) eventData.Use(); // ← 必須吃掉
        }
    }
}
