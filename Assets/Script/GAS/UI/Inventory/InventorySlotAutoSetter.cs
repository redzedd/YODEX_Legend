#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace GAS.UI.Inventory
{
    /// <summary>
    /// InventoryDisplay 的 Editor 擴展 — 提供一鍵自動設定格子陣列的按鈕
    /// 前提：InventoryDisplay 下有名為 "ItemSlots" 的子物件，內含 12 個格子子物件
    /// 每個格子子物件命名規則：
    ///   格子根  [Button + InventorySlotUI]
    ///     ├── ItemImage   [Image]
    ///     ├── ItemName    [Text]
    ///     └── Quantity    [Text]
    /// </summary>
    [CustomEditor(typeof(InventoryDisplay))]
    public class InventorySlotAutoSetter : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("工具", EditorStyles.boldLabel);

            if (GUILayout.Button("自動設定格子陣列（依子物件順序）"))
            {
                AutoSetSlots();
            }

            if (GUILayout.Button("自動設定 InventorySlotUI 的 Animator 引用"))
            {
                AutoSetSlotAnimators();
            }
        }

        private void AutoSetSlots()
        {
            InventoryDisplay display = (InventoryDisplay)target;
            Transform parent = display.transform.Find("ItemSlots");

            if (parent == null)
            {
                Debug.LogError("[AutoSetter] 找不到名為 'ItemSlots' 的子物件，請確認 UI 結構。");
                return;
            }

            SerializedProperty slotsProp = serializedObject.FindProperty("_slots");
            if (slotsProp == null)
            {
                Debug.LogError("[AutoSetter] 找不到 _slots 屬性，請確認 InventoryDisplay 欄位名稱。");
                return;
            }

            int count = Mathf.Min(parent.childCount, 12);
            slotsProp.arraySize = 12;

            for (int i = 0; i < 12; i++)
            {
                SerializedProperty slotElement = slotsProp.GetArrayElementAtIndex(i);

                if (i >= count)
                {
                    // 超出範圍的格子清空
                    slotElement.FindPropertyRelative("itemButton").objectReferenceValue = null;
                    slotElement.FindPropertyRelative("itemImage").objectReferenceValue = null;
                    slotElement.FindPropertyRelative("itemText").objectReferenceValue = null;
                    slotElement.FindPropertyRelative("quantityText").objectReferenceValue = null;
                    continue;
                }

                Transform slotObj = parent.GetChild(i);

                // itemButton — 子物件 "ItemSelect" 上的 Button
                Transform selectTf = slotObj.Find("ItemSelect");
                UnityEngine.UI.Button btn = selectTf != null
                    ? selectTf.GetComponent<UnityEngine.UI.Button>()
                    : null;
                slotElement.FindPropertyRelative("itemButton").objectReferenceValue = btn;

                // itemImage — 子物件 "ItemImage"
                Transform imgTf = slotObj.Find("ItemImage");
                slotElement.FindPropertyRelative("itemImage").objectReferenceValue =
                    imgTf != null ? imgTf.GetComponent<UnityEngine.UI.Image>() : null;

                // itemText — 子物件 "ItemName"
                Transform nameTf = slotObj.Find("ItemName");
                slotElement.FindPropertyRelative("itemText").objectReferenceValue =
                    nameTf != null ? nameTf.GetComponent<UnityEngine.UI.Text>() : null;

                // quantityText — 子物件 "Quantity"
                Transform qtyTf = slotObj.Find("Quantity");
                slotElement.FindPropertyRelative("quantityText").objectReferenceValue =
                    qtyTf != null ? qtyTf.GetComponent<UnityEngine.UI.Text>() : null;
            }

            serializedObject.ApplyModifiedProperties();
            Debug.Log("[AutoSetter] 格子陣列自動設定完成！");
        }

        private void AutoSetSlotAnimators()
        {
            InventoryDisplay display = (InventoryDisplay)target;
            Transform parent = display.transform.Find("ItemSlots");

            if (parent == null)
            {
                Debug.LogError("[AutoSetter] 找不到名為 'ItemSlots' 的子物件。");
                return;
            }

            // 尋找場景中的 InventoryAnimator
            InventoryAnimator animator = display.GetComponentInParent<InventoryAnimator>(true);
            if (animator == null)
                animator = Object.FindFirstObjectByType<InventoryAnimator>();

            if (animator == null)
            {
                Debug.LogWarning("[AutoSetter] 找不到 InventoryAnimator，請先設定場景中的 InventoryAnimator 再執行此操作。");
                return;
            }

            int setCount = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                // InventorySlotUI 掛在子物件 "ItemSelect" 上
                Transform selectTf = parent.GetChild(i).Find("ItemSelect");
                InventorySlotUI slotUI = selectTf != null
                    ? selectTf.GetComponent<InventorySlotUI>()
                    : null;
                if (slotUI == null) continue;
                SerializedObject slotSO = new SerializedObject(slotUI);
                SerializedProperty animProp = slotSO.FindProperty("_animator");
                if (animProp != null)
                {
                    animProp.objectReferenceValue = animator;
                    slotSO.ApplyModifiedProperties();
                    setCount++;
                }
            }

            Debug.Log($"[AutoSetter] 已對 {setCount} 個 InventorySlotUI 設定 InventoryAnimator 引用。");
        }
    }
}

/// <summary>
/// CookingInventoryDisplay 的 Editor 擴展 — 提供一鍵自動設定烹飪格子陣列的按鈕
/// 前提：CookingInventoryDisplay 下有名為 "ItemSlots" 的子物件，內含 12 個格子子物件
/// 每個格子子物件命名規則：
///   格子根
///     ├── ItemSelect  [Button]
///     ├── ItemImage   [Image]
///     ├── ItemName    [Text]
///     └── Quantity    [Text]
/// </summary>
[CustomEditor(typeof(CookingInventoryDisplay))]
public class CookingSlotAutoSetter : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("工具", EditorStyles.boldLabel);

        if (GUILayout.Button("自動設定烹飪格子陣列（依子物件順序）"))
        {
            AutoSetCookingSlots();
        }
    }

    private void AutoSetCookingSlots()
    {
        CookingInventoryDisplay display = (CookingInventoryDisplay)target;
        Transform parent = display.transform.Find("ItemSlots");

        if (parent == null)
        {
            Debug.LogError("[CookingAutoSetter] 找不到名為 'ItemSlots' 的子物件，請確認 UI 結構。");
            return;
        }

        SerializedProperty slotsProp = serializedObject.FindProperty("slots");
        if (slotsProp == null)
        {
            Debug.LogError("[CookingAutoSetter] 找不到 slots 屬性，請確認 CookingInventoryDisplay 欄位名稱。");
            return;
        }

        int count = Mathf.Min(parent.childCount, 12);
        slotsProp.arraySize = 12;

        for (int i = 0; i < 12; i++)
        {
            SerializedProperty slotElement = slotsProp.GetArrayElementAtIndex(i);

            if (i >= count)
            {
                slotElement.FindPropertyRelative("itemButton").objectReferenceValue = null;
                slotElement.FindPropertyRelative("itemImage").objectReferenceValue = null;
                slotElement.FindPropertyRelative("itemText").objectReferenceValue = null;
                slotElement.FindPropertyRelative("quantityText").objectReferenceValue = null;
                continue;
            }

            Transform slotObj = parent.GetChild(i);

            // itemButton — 子物件 "ItemSelect" 上的 Button
            Transform selectTf = slotObj.Find("ItemSelect");
            UnityEngine.UI.Button btn = selectTf != null
                ? selectTf.GetComponent<UnityEngine.UI.Button>()
                : null;
            slotElement.FindPropertyRelative("itemButton").objectReferenceValue = btn;

            // itemImage — 子物件 "ItemImage"
            Transform imgTf = slotObj.Find("ItemImage");
            slotElement.FindPropertyRelative("itemImage").objectReferenceValue =
                imgTf != null ? imgTf.GetComponent<UnityEngine.UI.Image>() : null;

            // itemText — 子物件 "ItemName"
            Transform nameTf = slotObj.Find("ItemName");
            slotElement.FindPropertyRelative("itemText").objectReferenceValue =
                nameTf != null ? nameTf.GetComponent<UnityEngine.UI.Text>() : null;

            // quantityText — 子物件 "Quantity"
            Transform qtyTf = slotObj.Find("Quantity");
            slotElement.FindPropertyRelative("quantityText").objectReferenceValue =
                qtyTf != null ? qtyTf.GetComponent<UnityEngine.UI.Text>() : null;
        }

        serializedObject.ApplyModifiedProperties();
        Debug.Log("[CookingAutoSetter] 烹飪格子陣列自動設定完成！");
    }
}
#endif
