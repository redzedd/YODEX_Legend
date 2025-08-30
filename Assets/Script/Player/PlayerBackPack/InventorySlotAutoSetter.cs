#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(InventoryDisplay))]
public class InventorySlotAutoSetter : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        InventoryDisplay display = (InventoryDisplay)target;

        if (GUILayout.Button("自動抓取道具槽（子物件順序）"))
        {
            Transform parent = display.transform.Find("ItemSlots"); // 預設 UI 父物件
            if (parent == null)
            {
                Debug.LogError("找不到名為 'ItemSlots' 的子物件，請確認UI結構。");
                return;
            }

            InventoryDisplay.InventorySlot[] slots = new InventoryDisplay.InventorySlot[12];
            int count = Mathf.Min(parent.childCount, 12);

            for (int i = 0; i < count; i++)
            {
                Transform slotObj = parent.GetChild(i);
                InventoryDisplay.InventorySlot slot = new InventoryDisplay.InventorySlot();
                slot.itemImage = slotObj.Find("ItemImage").GetComponent<UnityEngine.UI.Image>();
                slot.itemText = slotObj.Find("ItemName").GetComponent<UnityEngine.UI.Text>();
                slot.quantityText = slotObj.Find("Quantity").GetComponent<UnityEngine.UI.Text>();
                slots[i] = slot;
            }

            SerializedProperty slotsProp = serializedObject.FindProperty("slots");
            slotsProp.arraySize = 12;
            for (int i = 0; i < 12; i++)
            {
                slotsProp.GetArrayElementAtIndex(i).FindPropertyRelative("itemImage").objectReferenceValue = slots[i].itemImage;
                slotsProp.GetArrayElementAtIndex(i).FindPropertyRelative("itemText").objectReferenceValue = slots[i].itemText;
                slotsProp.GetArrayElementAtIndex(i).FindPropertyRelative("quantityText").objectReferenceValue = slots[i].quantityText;
            }

            serializedObject.ApplyModifiedProperties();
            Debug.Log("道具槽自動設定完成！");
        }
    }
}
#endif
