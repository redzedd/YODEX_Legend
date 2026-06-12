using UnityEngine;

namespace CameraSystem
{
    /// <summary>
    /// 鏡頭優先層數值設定 — 設計師在 .asset 上手調每層 Priority 基準值。
    /// Director 切換鏡頭時依這份設定計算實際 Priority。
    /// </summary>
    [CreateAssetMenu(fileName = "CameraPriorityProfile", menuName = "CameraSystem/Priority Profile")]
    public class CameraPriorityProfile : ScriptableObject
    {
        [Header("各層 Priority 基準")]

        [SerializeField]
        [Tooltip("Background — 常駐底層（主視角第三人稱）。建議 10")]
        private int _backgroundPriority = 10;

        [SerializeField]
        [Tooltip("LockOn — 鎖定。被 Aim/Action/Cinematic 等高層覆蓋時 LockOnBridge 會自動解除鎖定，避免玩家被 anchor 持續拉向敵人。建議 40")]
        private int _lockOnPriority = 40;

        [SerializeField]
        [Tooltip("Aim — 瞄準（肩射）。壓過 LockOn 取得清晰準心方向與自由轉向。建議 50")]
        private int _aimPriority = 50;

        [SerializeField]
        [Tooltip("Action — 動作特寫（格擋等戰鬥演出）。建議 100")]
        private int _actionPriority = 100;

        [SerializeField]
        [Tooltip("Cinematic — 劇情演出（永遠壓一切）。建議 200")]
        private int _cinematicPriority = 200;

        [Header("關閉值")]

        [SerializeField]
        [Tooltip("未被請求的鏡頭實際套用的 Priority — 用負值確保低於任何啟用鏡頭。建議 -1")]
        private int _inactivePriority = -1;

        /// <summary>查詢某層的 Priority 基準值</summary>
        public int GetPriority(CameraLayer layer)
        {
            return layer switch
            {
                CameraLayer.Background => _backgroundPriority,
                CameraLayer.Aim => _aimPriority,
                CameraLayer.LockOn => _lockOnPriority,
                CameraLayer.Action => _actionPriority,
                CameraLayer.Cinematic => _cinematicPriority,
                _ => 0
            };
        }

        public int InactivePriority => _inactivePriority;
    }
}
