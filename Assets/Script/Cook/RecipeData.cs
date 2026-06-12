using System;
using System.Collections.Generic;
using UnityEngine;
using Item;

[CreateAssetMenu(fileName = "NewRecipe", menuName = "Cooking/Recipe")]
public class RecipeData : ScriptableObject
{
    [Serializable]
    public class RecipeSlotRequirement
    {
        [Tooltip("指定食材：放入鍋中的素材必須是這個 ItemData。留空代表不限定特定食材，只用下方類型判斷。")]
        public ItemData requiredItemData;

        [Tooltip("食材類型：選 None 代表不檢查類型。與「指定食材」可擇一或併用（兩者都填代表必須同時符合）。")]
        public IngredientType requiredType = IngredientType.None;
    }

    [Header("配方需求（最多 5 個材料格）")]
    [Tooltip("每一格代表一個需放入鍋中的材料；放入鍋中的素材數量必須與此清單數量完全相同才會配對成功。")]
    public List<RecipeSlotRequirement> requirements = new List<RecipeSlotRequirement>(5);

    [Header("產出物")]
    [Tooltip("成功料理後產出的物品。")]
    public ItemData resultItem;

    [Tooltip("成功料理後產出的數量。建議 1~3。")]
    public int resultAmount = 1;

    [Tooltip("給設計師看的備註說明，不影響遊戲邏輯。")]
    [TextArea]
    public string recipeNote;
}
