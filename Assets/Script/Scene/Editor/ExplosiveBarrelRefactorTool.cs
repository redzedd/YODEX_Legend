#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 爆炸桶重構工具 — 將場景中所有掛 ExplosiveBarrel 的物件改造成「視覺/互動分離」結構:
///   原本: Barrel (LODGroup + MeshRenderer + Collider + Script + Static 標記)
///   重構後: Barrel (Collider + Script, 無 Static 標記)
///           └── Visual (LODGroup + MeshRenderer + 原本的 LOD 子物件)
/// 目的:LODGroup / Static Batching 會干擾 Collider 在運行時的物理偵測,把視覺隔離後便不會打架。
/// </summary>
public static class ExplosiveBarrelRefactorTool
{
    [MenuItem("Tools/爆炸桶/重構場景中所有桶子(分離 Visual)")]
    public static void RefactorAllInScene()
    {
        ExplosiveBarrel[] barrels = Object.FindObjectsByType<ExplosiveBarrel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (barrels.Length == 0)
        {
            Debug.LogWarning("[爆炸桶重構] 場景中找不到任何 ExplosiveBarrel");
            return;
        }
        if (!EditorUtility.DisplayDialog(
                "重構爆炸桶",
                $"將對場景中 {barrels.Length} 個 ExplosiveBarrel 執行重構:\n" +
                "  • 視覺元件(LODGroup / MeshRenderer / MeshFilter / LOD 子物件)移到 Visual 子物件\n" +
                "  • 清除根物件的 Static 標記\n" +
                "  • Prefab 連結會自動解除(Unpack)\n\n" +
                "操作可用 Ctrl+Z 復原。要繼續嗎?",
                "確定", "取消"))
        {
            return;
        }

        int processed = 0;
        int skipped = 0;
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Refactor All Explosive Barrels");
        foreach (ExplosiveBarrel barrel in barrels)
        {
            if (barrel == null) continue;
            if (RefactorOne(barrel.gameObject)) processed++;
            else skipped++;
        }
        Undo.CollapseUndoOperations(undoGroup);

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));
        }
        Debug.Log($"<color=cyan>[爆炸桶重構]</color> 完成 — 處理 {processed} 個,略過 {skipped} 個(已重構或無視覺元件)");
    }

    [MenuItem("Tools/爆炸桶/重構選中的桶子")]
    public static void RefactorSelected()
    {
        GameObject[] selection = Selection.gameObjects;
        if (selection.Length == 0)
        {
            Debug.LogWarning("[爆炸桶重構] 未選取任何物件");
            return;
        }
        int processed = 0;
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Refactor Selected Explosive Barrels");
        foreach (GameObject go in selection)
        {
            if (go == null) continue;
            if (go.GetComponent<ExplosiveBarrel>() == null) continue;
            if (RefactorOne(go)) processed++;
        }
        Undo.CollapseUndoOperations(undoGroup);
        if (processed > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
        Debug.Log($"<color=cyan>[爆炸桶重構]</color> 完成 — 處理 {processed} 個");
    }

    private static bool RefactorOne(GameObject root)
    {
        if (root == null) return false;
        if (root.transform.Find("Visual") != null) return false;

        LODGroup lod = root.GetComponent<LODGroup>();
        MeshFilter mf = root.GetComponent<MeshFilter>();
        MeshRenderer mr = root.GetComponent<MeshRenderer>();
        bool hasVisualComponents = lod != null || mf != null || mr != null;
        bool hasStaticFlags = GameObjectUtility.GetStaticEditorFlags(root) != 0;
        if (!hasVisualComponents && !hasStaticFlags) return false;

        if (PrefabUtility.IsPartOfPrefabInstance(root))
        {
            GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(root);
            if (prefabRoot != null)
            {
                PrefabUtility.UnpackPrefabInstance(prefabRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
        }

        Undo.RegisterFullObjectHierarchyUndo(root, "Refactor Explosive Barrel");

        if (hasVisualComponents)
        {
            GameObject visual = new GameObject("Visual");
            Undo.RegisterCreatedObjectUndo(visual, "Create Visual child");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            MeshFilter newMf = null;
            MeshRenderer newMr = null;
            LODGroup newLod = null;
            if (mf != null)
            {
                ComponentUtility.CopyComponent(mf);
                ComponentUtility.PasteComponentAsNew(visual);
                newMf = visual.GetComponent<MeshFilter>();
            }
            if (mr != null)
            {
                ComponentUtility.CopyComponent(mr);
                ComponentUtility.PasteComponentAsNew(visual);
                newMr = visual.GetComponent<MeshRenderer>();
            }
            if (lod != null)
            {
                ComponentUtility.CopyComponent(lod);
                ComponentUtility.PasteComponentAsNew(visual);
                newLod = visual.GetComponent<LODGroup>();
            }

            List<Transform> childrenToMove = new List<Transform>();
            for (int i = 0; i < root.transform.childCount; i++)
            {
                Transform c = root.transform.GetChild(i);
                if (c == visual.transform) continue;
                childrenToMove.Add(c);
            }
            foreach (Transform c in childrenToMove)
            {
                Undo.SetTransformParent(c, visual.transform, "Reparent under Visual");
            }

            if (newLod != null && mr != null && newMr != null)
            {
                LOD[] lods = newLod.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    Renderer[] renderers = lods[i].renderers;
                    if (renderers == null) continue;
                    for (int j = 0; j < renderers.Length; j++)
                    {
                        if (renderers[j] == mr) renderers[j] = newMr;
                    }
                    lods[i].renderers = renderers;
                }
                newLod.SetLODs(lods);
                newLod.RecalculateBounds();
            }

            if (lod != null) Undo.DestroyObjectImmediate(lod);
            if (mr != null) Undo.DestroyObjectImmediate(mr);
            if (mf != null) Undo.DestroyObjectImmediate(mf);
        }

        if (hasStaticFlags)
        {
            GameObjectUtility.SetStaticEditorFlags(root, 0);
        }
        SetStaticRecursive(root, false);

        EditorUtility.SetDirty(root);
        return true;
    }

    private static void SetStaticRecursive(GameObject go, bool isStatic)
    {
        go.isStatic = isStatic;
        for (int i = 0; i < go.transform.childCount; i++)
        {
            SetStaticRecursive(go.transform.GetChild(i).gameObject, isStatic);
        }
    }
}
#endif
