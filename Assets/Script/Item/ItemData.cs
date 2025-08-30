using UnityEngine;

public enum IngredientType
{
    None, Fruit, Meat, Vegetable, Seafood, Grain, Spice, Dairy
}

public enum RareLevel { Normal, Rare, Legend }

[CreateAssetMenu(fileName = "NewItemData", menuName = "Inventory/ItemData")]
public class ItemData : ScriptableObject
{
    public int itemID;
    public string itemName;
    public Sprite icon;
    public InventoryDisplay.Category category;
    public IngredientType ingredientType;
    public RareLevel rareLevel;
    public string description;
    public string effectDescription;
    public Sprite fullSizeImage;

    // 立即回復（可選）
    public int healAmount;

    // ★ 僅靠 BuffDefinition + 等級
    [Header("Buff (Driven by BuffDefinition)")]
    public BuffDefinition buffDefinition;
    [Range(1, 3)] public int effectTier = 1;
}