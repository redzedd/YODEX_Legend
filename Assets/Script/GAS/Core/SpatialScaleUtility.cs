using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 空間縮放工具：根據角色模型的 Transform Scale 計算統一縮放係數，
    /// 用於在運行時等比例調整 Hitbox、VFX、移動距離等空間數據。
    /// </summary>
    public static class SpatialScaleUtility
    {
        /// <summary>
        /// 取得角色的統一縮放係數（假設等比例縮放，以 X 軸為準）
        /// </summary>
        /// <param name="modelRoot">角色模型的根 Transform（通常是 AnimancerComponent 所在的 Transform）</param>
        /// <returns>縮放係數，預設為 1</returns>
        public static float GetScaleFactor(Transform modelRoot)
        {
            return modelRoot != null ? modelRoot.lossyScale.x : 1f;
        }
    }
}
