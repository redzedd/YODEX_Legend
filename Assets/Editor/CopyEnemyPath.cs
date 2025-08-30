#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public static class CopyEnemyPath
{
    // 由選取的 Transform，複製「相對最近的 EnemyLockOnConfig 祖先」的路徑
    [MenuItem("Tools/LockOn/Copy Path (relative to Enemy root)")]
    public static void CopyPathRelativeToEnemyRoot()
    {
        var t = Selection.activeTransform;
        if (t == null)
        {
            EditorUtility.DisplayDialog("Copy Path", "請在 Hierarchy 選取要複製路徑的子物件（例如 Head/IndicatorAnchor）。", "OK");
            return;
        }

        Transform root = FindAncestorWith<EnemyLockOnConfig>(t);
        if (root == null) root = t.root; // 沒有 EnemyLockOnConfig 就退回場景最上層

        string path = GetRelativePath(t, root);
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("Copy Path", "選到的是根物件本身，請選它的子物件。", "OK");
            return;
        }

        EditorGUIUtility.systemCopyBuffer = path;
        Debug.Log($"[LockOn] Copied path: {path}");
        EditorUtility.DisplayDialog("Copy Path", $"已複製路徑：\n{path}", "OK");
    }

    // 也提供 Transform 的右鍵選單（Inspector 的齒輪 → 右鍵選單）
    [MenuItem("CONTEXT/Transform/Copy Path (relative to Enemy root)")]
    private static void CopyPathContext(MenuCommand command)
    {
        var t = command.context as Transform;
        if (t == null) return;

        Transform root = FindAncestorWith<EnemyLockOnConfig>(t);
        if (root == null) root = t.root;

        string path = GetRelativePath(t, root);
        EditorGUIUtility.systemCopyBuffer = path;
        Debug.Log($"[LockOn] Copied path: {path}");
    }

    private static Transform FindAncestorWith<T>(Transform t) where T : Component
    {
        var cur = t;
        while (cur != null)
        {
            if (cur.GetComponent<T>() != null) return cur;
            cur = cur.parent;
        }
        return null;
    }

    private static string GetRelativePath(Transform t, Transform root)
    {
        var names = new Stack<string>();
        var cur = t;
        while (cur != null && cur != root)
        {
            names.Push(cur.name);
            cur = cur.parent;
        }
        // 若 root 不是 t 的祖先，仍回傳從場景根開始的路徑
        return string.Join("/", names.ToArray());
    }
}
#endif
