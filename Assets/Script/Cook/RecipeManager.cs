using System.Collections.Generic;
using UnityEngine;

public class RecipeManager : MonoBehaviour
{
    public static RecipeManager Instance { get; private set; }

    [Header("所有食譜資料")]
    public List<RecipeData> allRecipes = new();

    [Header("❌ 失敗時預設產物")]
    public ItemData defaultFailureItem;
    public int failureItemAmount = 1;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public RecipeData FindMatchingRecipe(List<InventoryItem> inputItems)
    {
        // ✅ 按照需求數量多到少排列
        var sorted = new List<RecipeData>(allRecipes);
        sorted.Sort((a, b) => b.requirements.Count.CompareTo(a.requirements.Count));

        foreach (var recipe in sorted)
        {
            if (IsMatch(recipe, inputItems))
                return recipe;
        }

        return null;
    }

    private bool IsMatch(RecipeData recipe, List<InventoryItem> inputItems)
    {
        // ✅ 要求材料數量完全一致
        if (inputItems.Count != recipe.requirements.Count)
            return false;

        var requirements = new List<RecipeData.RecipeSlotRequirement>(recipe.requirements);
        var available = new List<ItemData>();
        foreach (var inv in inputItems)
        {
            available.Add(inv.itemData);
        }

        foreach (var req in requirements)
        {
            bool found = false;

            for (int i = 0; i < available.Count; i++)
            {
                var item = available[i];
                bool nameMatch = req.requiredItemData == null || item == req.requiredItemData;
                bool typeMatch = req.requiredType == IngredientType.None || item.ingredientType == req.requiredType;

                if (nameMatch && typeMatch)
                {
                    available.RemoveAt(i); // ✅ 用掉這個素材，避免重複配對
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false; // 這格需求沒被滿足
            }
        }

        // 所有需求都有對到素材
        return true;
    }
}