using System.Collections.Generic;
using UnityEngine;
using GAS.UI.Inventory;
using Item;

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
        // ✅ 先比材料數量(多者優先),數量相同時比精確度——讓最精確的食譜一定贏,
        //    不受 Inspector 拖曳順序影響,避免煮出比較通用/簡單的食物。
        // 先濾掉清單裡的空項目 / 沒設定需求的食譜,避免 Inspector 留空格時崩潰。
        var sorted = new List<RecipeData>();
        foreach (var recipe in allRecipes)
        {
            if (recipe != null && recipe.requirements != null)
                sorted.Add(recipe);
        }

        sorted.Sort((a, b) =>
        {
            int countCompare = b.requirements.Count.CompareTo(a.requirements.Count);
            if (countCompare != 0) return countCompare;
            return GetSpecificity(b).CompareTo(GetSpecificity(a));
        });

        foreach (var recipe in sorted)
        {
            if (IsMatch(recipe, inputItems))
                return recipe;
        }

        return null;
    }

    // 食譜整體精確度:所有需求格精確度加總,分數越高越精確。
    private int GetSpecificity(RecipeData recipe)
    {
        int score = 0;
        foreach (var req in recipe.requirements)
        {
            score += GetRequirementSpecificity(req);
        }

        return score;
    }

    // 單一需求格精確度:指定特定食材 +2、限定類型 +1、任意(留空)+0。
    private int GetRequirementSpecificity(RecipeData.RecipeSlotRequirement req)
    {
        if (req == null) return 0;
        if (req.requiredItemData != null) return 2;
        if (req.requiredType != IngredientType.None) return 1;
        return 0;
    }

    private bool IsMatch(RecipeData recipe, List<InventoryItem> inputItems)
    {
        // ✅ 只要鍋裡的食材「包含」食譜所有需求即可，多放的食材會被忽略（容錯）
        if (inputItems.Count < recipe.requirements.Count)
            return false;

        // ✅ 讓最精確的需求格先挑食材,避免通用格先把專屬食材吃掉造成誤判失敗
        var requirements = new List<RecipeData.RecipeSlotRequirement>(recipe.requirements);
        requirements.Sort((a, b) => GetRequirementSpecificity(b).CompareTo(GetRequirementSpecificity(a)));

        var available = new List<ItemData>();
        foreach (var inv in inputItems)
        {
            if (inv != null && inv.itemData != null)
                available.Add(inv.itemData);
        }

        foreach (var req in requirements)
        {
            if (req == null)
                return false;

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