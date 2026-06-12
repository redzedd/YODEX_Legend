using UnityEngine;
using UnityEngine.EventSystems;

public class CookingSlotUI : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    public int slotIndex;
    public CookingInventoryDisplay cookingDisplay;

    public void OnPointerEnter(PointerEventData eventData)
    {
        //Debug.Log($"🖱️ 滑鼠移入格子 {slotIndex}");
        cookingDisplay.OnPointerHover(slotIndex);
        EventSystem.current.SetSelectedGameObject(this.gameObject);
    }

    public void OnSelect(BaseEventData eventData)
    {
        cookingDisplay.OnPointerHover(slotIndex);
    }
}