using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewRecipe", menuName = "Cooking/Recipe")]
public class RecipeData : ScriptableObject
{
    [Serializable]
    public class RecipeSlotRequirement
    {
        public ItemData requiredItemData;
        public IngredientType requiredType = IngredientType.None; // 可為 None 代表不檢查類別
    }

    [Header("配方條件（最多5個）")]
    public List<RecipeSlotRequirement> requirements = new List<RecipeSlotRequirement>(5);

    [Header("產出物")]
    public ItemData resultItem;
    public int resultAmount = 1;

    [TextArea]
    public string recipeNote; // 描述或說明
}