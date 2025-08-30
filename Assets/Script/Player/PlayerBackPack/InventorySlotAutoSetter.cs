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

        if (GUILayout.Button("�۰ʧ���D��ѡ]�l���󶶧ǡ^"))
        {
            Transform parent = display.transform.Find("ItemSlots"); // �w�] UI ������
            if (parent == null)
            {
                Debug.LogError("�䤣��W�� 'ItemSlots' ���l����A�нT�{UI���c�C");
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
            Debug.Log("�D��Ѧ۰ʳ]�w�����I");
        }
    }
}
#endif
