#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public static class CopyEnemyPath
{
    // �ѿ���� Transform�A�ƻs�u�۹�̪� EnemyLockOnConfig �����v�����|
    [MenuItem("Tools/LockOn/Copy Path (relative to Enemy root)")]
    public static void CopyPathRelativeToEnemyRoot()
    {
        var t = Selection.activeTransform;
        if (t == null)
        {
            EditorUtility.DisplayDialog("Copy Path", "�Цb Hierarchy ����n�ƻs���|���l����]�Ҧp Head/IndicatorAnchor�^�C", "OK");
            return;
        }

        Transform root = FindAncestorWith<EnemyLockOnConfig>(t);
        if (root == null) root = t.root; // �S�� EnemyLockOnConfig �N�h�^�����̤W�h

        string path = GetRelativePath(t, root);
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("Copy Path", "��쪺�O�ڪ��󥻨��A�п復���l����C", "OK");
            return;
        }

        EditorGUIUtility.systemCopyBuffer = path;
        Debug.Log($"[LockOn] Copied path: {path}");
        EditorUtility.DisplayDialog("Copy Path", $"�w�ƻs���|�G\n{path}", "OK");
    }

    // �]���� Transform ���k����]Inspector ������ �� �k����^
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
        // �Y root ���O t �������A���^�Ǳq�����ڶ}�l�����|
        return string.Join("/", names.ToArray());
    }
}
#endif
