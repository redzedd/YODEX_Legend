using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GAS.UI;
using GAS.UI.Inventory;
using Item;

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
        CookingInventoryDisplay display = FindFirstObjectByType<CookingInventoryDisplay>();
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
        }

        ingredientsInPot.Clear();
        UpdateIngredientSlots();

        // 不論退還或料理消耗,食材數量都已變動 — 重建烹飪 UI 顯示資料,
        // 避免 currentDisplayItems 殘留 stale 數量造成下一輪點擊行為怪異。
        CookingInventoryDisplay display = FindFirstObjectByType<CookingInventoryDisplay>();
        if (display != null && display.IsOpen)
        {
            var items = InventoryManager.Instance.GetItemsByCategory(InventoryDisplay.Category.Ingredients);
            items.Sort((a, b) => a.itemData.itemID.CompareTo(b.itemData.itemID));
            int totalPages = Mathf.CeilToInt(items.Count / 12f);
            display.RebuildDisplayData(items, totalPages);
        }
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

        ItemData resultItem = null;
        bool alreadyHad = false;

        if (recipe != null)
        {
            Debug.Log($"🍳 成功料理出：{recipe.resultItem.itemName} × {recipe.resultAmount}");
            resultItem = recipe.resultItem;
            alreadyHad = InventoryManager.Instance.HasObtained(resultItem);
            InventoryManager.Instance.AddItem(resultItem, recipe.resultAmount);
        }
        else
        {
            var failureItem = RecipeManager.Instance.defaultFailureItem;
            int amount = RecipeManager.Instance.failureItemAmount;

            if (failureItem != null)
            {
                Debug.Log($"❌ 沒有符合食譜，煮出了失敗料理：{failureItem.itemName} × {amount}");
                resultItem = failureItem;
                alreadyHad = InventoryManager.Instance.HasObtained(failureItem);
                InventoryManager.Instance.AddItem(failureItem, amount);
            }
            else
            {
                Debug.LogWarning("❌ 沒有設定失敗料理 ItemData，請到 RecipeManager 指定！");
            }
        }

        // 無論成功失敗都要清掉材料(已用於料理,不退還)
        // 烹飪面板保持開啟讓玩家連續料理; 字卡會疊在面板上方,
        // 字卡關閉時會還原進場前的 timeScale/輸入狀態 (見 NewItemDisplayUI 的快照機制)。
        ClearIngredients(refund: false);

        // 重複取得才補展示字卡(首次取得由背包系統處理)
        if (alreadyHad && resultItem != null)
        {
            ShowItemDisplayUI(resultItem);
        }
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
