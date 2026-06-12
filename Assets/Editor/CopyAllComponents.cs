using UnityEngine;
using UnityEditor;

public class CopyAllComponents : EditorWindow
{
    static Component[] copiedComponents;

    // 在 GameObject 選單或 Hierarchy 右鍵選單中加入 "Copy All Components"
    [MenuItem("GameObject/Copy All Components", false, 0)]
    static void Copy()
    {
        if (Selection.activeGameObject == null) return;

        // 取得所有 Component
        copiedComponents = Selection.activeGameObject.GetComponents<Component>();
        Debug.Log($"已複製 {Selection.activeGameObject.name} 上的 {copiedComponents.Length} 個 Component。");
    }

    // 在 GameObject 選單或 Hierarchy 右鍵選單中加入 "Paste All Components"
    [MenuItem("GameObject/Paste All Components", false, 0)]
    static void Paste()
    {
        if (Selection.activeGameObject == null || copiedComponents == null)
        {
            Debug.LogWarning("沒有複製任何 Component 或未選擇目標物件。");
            return;
        }

        Undo.IncrementCurrentGroup(); // 支援 Undo 功能

        foreach (var comp in copiedComponents)
        {
            // 跳過 Transform，因為每個物件原本就有，且不能重複
            if (comp is Transform) continue;

            // 使用 Unity 內部的複製貼上功能
            UnityEditorInternal.ComponentUtility.CopyComponent(comp);

            // 貼上為新的 Component
            UnityEditorInternal.ComponentUtility.PasteComponentAsNew(Selection.activeGameObject);
        }

        Undo.SetCurrentGroupName("Paste All Components");
        Debug.Log("已貼上所有 Component！");
    }
}