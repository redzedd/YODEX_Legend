using System.Text;
using UnityEditor;
using UnityEngine;

namespace YODEX.EditorTools
{
    public static class BonePathCopier
    {
        private const string MENU_COPY_FROM_ANIMATOR = "GameObject/複製骨骼路徑/從上層 Animator 物件 (動畫用)";
        private const string MENU_COPY_FROM_ROOT = "GameObject/複製骨骼路徑/從最頂層根物件";
        private const string MENU_COPY_FULL_SCENE = "GameObject/複製骨骼路徑/完整場景路徑";

        [MenuItem(MENU_COPY_FROM_ANIMATOR, false, 0)]
        private static void CopyFromAnimatorRoot()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return;
            }

            Transform animatorRoot = FindAnimatorRoot(selected.transform);
            if (animatorRoot == null)
            {
                Debug.LogWarning("[骨骼路徑] 找不到上層 Animator 或 Animation 元件，已改用最頂層根物件作為起點。");
                animatorRoot = GetTopmostRoot(selected.transform);
            }

            string path = BuildRelativePath(animatorRoot, selected.transform);
            CopyToClipboard(path, animatorRoot.name);
        }

        [MenuItem(MENU_COPY_FROM_ROOT, false, 1)]
        private static void CopyFromTopmostRoot()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return;
            }

            Transform root = GetTopmostRoot(selected.transform);
            string path = BuildRelativePath(root, selected.transform);
            CopyToClipboard(path, root.name);
        }

        [MenuItem(MENU_COPY_FULL_SCENE, false, 2)]
        private static void CopyFullScenePath()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return;
            }

            string path = BuildFullPath(selected.transform);
            CopyToClipboard(path, "場景根");
        }

        [MenuItem(MENU_COPY_FROM_ANIMATOR, true)]
        [MenuItem(MENU_COPY_FROM_ROOT, true)]
        [MenuItem(MENU_COPY_FULL_SCENE, true)]
        private static bool Validate()
        {
            return Selection.activeGameObject != null;
        }

        private static Transform FindAnimatorRoot(Transform from)
        {
            Transform current = from;
            while (current != null)
            {
                if (current.GetComponent<Animator>() != null || current.GetComponent<Animation>() != null)
                {
                    return current;
                }
                current = current.parent;
            }
            return null;
        }

        private static Transform GetTopmostRoot(Transform from)
        {
            Transform current = from;
            while (current.parent != null)
            {
                current = current.parent;
            }
            return current;
        }

        private static string BuildRelativePath(Transform root, Transform target)
        {
            if (root == target)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            Transform current = target;
            while (current != null && current != root)
            {
                if (sb.Length > 0)
                {
                    sb.Insert(0, '/');
                }
                sb.Insert(0, current.name);
                current = current.parent;
            }
            return sb.ToString();
        }

        private static string BuildFullPath(Transform target)
        {
            StringBuilder sb = new StringBuilder();
            Transform current = target;
            while (current != null)
            {
                if (sb.Length > 0)
                {
                    sb.Insert(0, '/');
                }
                sb.Insert(0, current.name);
                current = current.parent;
            }
            return sb.ToString();
        }

        private static void CopyToClipboard(string path, string rootName)
        {
            EditorGUIUtility.systemCopyBuffer = path;
            Debug.Log($"[骨骼路徑] 已複製到剪貼簿（起點：{rootName}）：\n{path}");
        }
    }
}
