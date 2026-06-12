using UnityEngine;

namespace GAS.Targeting
{
    /// <summary>
    /// 瞄準錨點 — 標記敵人模型中心，給遠程攻擊瞄準/追蹤使用。
    /// 與 LockOnAnchor(鎖定鏡頭/UI 用)分開，允許設計師獨立調整身體中心位置 — 避免子彈射向腳底或鎖定點。
    /// 掛在敵人身上「身體中心」(通常為胸腔)的子物件即可，無需任何欄位設定。
    /// </summary>
    [DisallowMultipleComponent]
    public class AimAnchor : MonoBehaviour
    {
#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField]
        [Tooltip("是否在 Scene 視窗繪製錨點 Gizmo")]
        private bool _drawGizmo = true;

        [SerializeField]
        [Tooltip("Gizmo 顯示半徑(僅視覺，不影響運行)")]
        private float _gizmoRadius = 0.2f;

        private void OnDrawGizmos()
        {
            if (!_drawGizmo) return;
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, _gizmoRadius);
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.25f);
            Gizmos.DrawSphere(transform.position, _gizmoRadius);
        }
#endif
    }
}
