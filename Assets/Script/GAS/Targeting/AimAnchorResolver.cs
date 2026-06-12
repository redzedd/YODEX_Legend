using UnityEngine;

namespace GAS.Targeting
{
    /// <summary>
    /// 瞄準點解析器 — 取得敵人「模型中心」的世界座標。
    /// 解析順序:
    ///   1. 子物件上的 AimAnchor 元件(設計師明確配置 → 最佳精度)
    ///   2. 子物件 Collider 的 bounds.center(自動 fallback)
    ///   3. 敵人 root 位置(最終 fallback,通常在腳底)
    /// 解析 Transform 版本(ResolveAimAnchorOrRoot)專供需要「持續追蹤」的場景(例 Homing 子彈),
    /// 因 AimAnchor 為子物件會隨敵人移動/動畫自然更新位置。
    /// </summary>
    public static class AimAnchorResolver
    {
        /// <summary>
        /// 取得瞄準點世界座標 — 一次性查詢,適用於發射瞬間決定方向。
        /// </summary>
        public static Vector3 ResolveAimPosition(Transform root)
        {
            if (root == null) return Vector3.zero;
            AimAnchor anchor = root.GetComponentInChildren<AimAnchor>(true);
            if (anchor != null) return anchor.transform.position;
            Collider col = root.GetComponentInChildren<Collider>();
            if (col != null) return col.bounds.center;
            return root.position;
        }

        /// <summary>
        /// 取得瞄準點 Transform(供持續追蹤使用 — 例 Homing 子彈每幀讀位置)。
        /// AimAnchor 存在 → 回傳該子物件,位置會隨敵人移動;
        /// 否則 → 回傳 root,並用 localOffset 表示「root→bounds 中心」的本地偏移(若無 collider 則為零)。
        /// 呼叫端用 root.TransformPoint(localOffset) 取得每幀世界座標。
        /// </summary>
        public static void ResolveHomingAnchor(Transform root, out Transform anchorTransform, out Vector3 localOffset)
        {
            anchorTransform = null;
            localOffset = Vector3.zero;
            if (root == null) return;

            AimAnchor anchor = root.GetComponentInChildren<AimAnchor>(true);
            if (anchor != null)
            {
                anchorTransform = anchor.transform;
                return;
            }

            anchorTransform = root;
            Collider col = root.GetComponentInChildren<Collider>();
            if (col != null)
            {
                localOffset = root.InverseTransformPoint(col.bounds.center);
            }
        }
    }
}
