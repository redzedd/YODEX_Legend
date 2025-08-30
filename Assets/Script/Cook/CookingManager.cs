using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CookingManager : MonoBehaviour
{
    public int maxIngredients = 5;
    public GameObject newItemDisplayPrefab;
    public Transform newItemDisplaySpawnRoot; // 建議掛在 UI Canvas 上

    public Transform slotContainer; // 用來放材料格的 UI 容器（可手動指定 5 格空物件）
    public CookingIngredientSlotUI[] ingredientSlots = new CookingIngredientSlotUI[5];

    private readonly List<InventoryItem> ingredientsInPot = new();

    public void TryAddIngredient(InventoryItem item)
    {
        if (ingredientsInPot.Count >= maxIngredients)
        {
            Debug.Log("🍲 鍋子已滿，無法再加入更多材料！");
            return;
        }

        if (item.quantity <= 0)
        {
            Debug.Log("❌ 食材數量不足，無法投入！");
            return;
        }

        // ✅ 從背包中扣除 1 個
        InventoryManager.Instance.RemoveItem(item.itemData, 1);

        ingredientsInPot.Add(item);
        Debug.Log($"✅ 加入材料：{item.itemName}");

        UpdateIngredientSlots();

        // ✅ 同步烹飪清單與 UI 顯示（InventoryDisplay 更新）
        CookingInventoryDisplay display = FindObjectOfType<CookingInventoryDisplay>();
        if (display != null) display.RefreshDisplay();
    }

    public void ClearIngredients(bool refund = true)
    {
        if (refund)
        {
            foreach (var item in ingredientsInPot)
            {
                InventoryManager.Instance.AddItem(item.itemData, 1);
                Debug.Log($"🔁 已退還：{item.itemName}");
            }

            // ✅ 主動刷新烹飪用背包顯示
            CookingInventoryDisplay display = FindObjectOfType<CookingInventoryDisplay>();
            if (display != null)
            {
                // 重新建構顯示清單
                var items = InventoryManager.Instance.GetItemsByCategory(InventoryDisplay.Category.Ingredients);
                items.Sort((a, b) => a.itemData.itemID.CompareTo(b.itemData.itemID));
                int totalPages = Mathf.CeilToInt(items.Count / 12f);

                display.RebuildDisplayData(items, totalPages);
            }
        }

        ingredientsInPot.Clear();
        UpdateIngredientSlots();
    }

    private void UpdateIngredientSlots()
    {
        for (int i = 0; i < ingredientSlots.Length; i++)
        {
            if (i < ingredientsInPot.Count)
            {
                InventoryItem item = ingredientsInPot[i];
                ingredientSlots[i].SetItem(item);
            }
            else
            {
                ingredientSlots[i].ClearSlot();
            }
        }
    }

    public bool HasIngredients()
    {
        return ingredientsInPot.Count > 0;
    }

    public void TryCook()
    {
        var ingredients = GetCurrentIngredients();

        // ✅ 如果什麼都沒放，直接返回
        if (ingredients.Count == 0)
        {
            Debug.Log("⚠️ 請至少放入一個材料再開始料理！");
            return;
        }

        var recipe = RecipeManager.Instance.FindMatchingRecipe(ingredients);
        // ⭐ 先關閉介面
        //FindObjectOfType<CookingInventoryDisplay>().CloseCookingUI();

        if (recipe != null)
        {
            Debug.Log($"🍳 成功料理出：{recipe.resultItem.itemName} × {recipe.resultAmount}");

            bool alreadyHad = InventoryManager.Instance.HasObtained(recipe.resultItem);
            InventoryManager.Instance.AddItem(recipe.resultItem, recipe.resultAmount);

            if (alreadyHad)
            {
                ShowItemDisplayUI(recipe.resultItem); // ❗ 如果不是第一次才額外補提示
            }
        }
        else
        {
            var failureItem = RecipeManager.Instance.defaultFailureItem;
            int amount = RecipeManager.Instance.failureItemAmount;

            if (failureItem != null)
            {
                Debug.Log($"❌ 沒有符合食譜，煮出了失敗料理：{failureItem.itemName} × {amount}");

                bool alreadyHad = InventoryManager.Instance.HasObtained(failureItem);
                InventoryManager.Instance.AddItem(failureItem, amount);

                if (alreadyHad)
                {
                    ShowItemDisplayUI(failureItem); // 同理補提示
                }
            }
            else
            {
                Debug.LogWarning("❌ 沒有設定失敗料理 ItemData，請到 RecipeManager 指定！");
            }
        }
        // 無論成功失敗都要清掉材料
        ClearIngredients(refund: false);
    }

    public List<InventoryItem> GetCurrentIngredients()
    {
        return new List<InventoryItem>(ingredientsInPot);
    }

    private void ShowItemDisplayUI(ItemData data)
    {
        if (newItemDisplayPrefab == null || data == null) return;

        GameObject ui = Instantiate(newItemDisplayPrefab, newItemDisplaySpawnRoot);
        NewItemDisplayUI display = ui.GetComponent<NewItemDisplayUI>();
        if (display != null)
        {
            display.Setup(data);
        }
    }
}
