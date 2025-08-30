using UnityEngine;
using UnityEngine.UI;

public class CookingIngredientSlotUI : MonoBehaviour
{
    public Image iconImage;
    public Sprite emptySprite;

    public void SetItem(InventoryItem item)
    {
        if (item != null && item.icon != null)
        {
            iconImage.sprite = item.icon;
            iconImage.color = Color.white;
        }
        else
        {
            ClearSlot();
        }
    }

    public void ClearSlot()
    {
        iconImage.sprite = emptySprite;
        iconImage.color = new Color(1, 1, 1, 0.3f); // 半透明表示空
    }
}
