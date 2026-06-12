using System;
using UnityEngine;

namespace GAS.Targeting.LockOnV2
{
    /// <summary>
    /// 鎖定目標搜尋與評分參數
    /// 用 class 而非 struct,讓 Inspector 預設值可透過欄位初始化器寫死,
    /// 並讓 Selector 與 Controller 共享同一個物件 (執行期改 Inspector 數值即時生效)
    /// </summary>
    [Serializable]
    public class LockOnSelectorConfig
    {
        [Header("Range")]
        [Tooltip("搜尋半徑 (公尺)")]
        public float SearchRange = 15f;

        [Header("Screen Filter")]
        [Tooltip("螢幕邊界容忍 (0.15 = 超出畫面 15% 仍視為候選)")]
        public float ScreenMargin = 0.15f;

        [Header("Initial Lock Score")]
        [Tooltip("螢幕中央距離評分權重 (越大越偏好畫面中心目標)")]
        public float CenterWeight = 1f;

        [Tooltip("世界距離評分權重 (normalized by SearchRange,越大越偏好近距離目標)")]
        public float DistanceWeight = 0.35f;

        [Header("Occlusion")]
        [Tooltip("視線阻擋 LayerMask;設 0 則不檢測遮擋")]
        public LayerMask OcclusionMask;

        [Tooltip("視線檢測 SphereCast 半徑 (公尺)")]
        public float OcclusionRadius = 0.2f;

        [Header("Directional Switch")]
        [Tooltip("搖桿方向與候選螢幕方向的最小一致性 dot (1=完全同向、0=垂直、0.4≈夾角 66°)")]
        [Range(0f, 1f)]
        public float DirectionDotMin = 0.4f;

        [Tooltip("方向偏離評分倍率 (越大越強制要求 \"正方向\" 而非 \"近距離\")")]
        public float DirectionScoreMul = 2f;
    }
}
