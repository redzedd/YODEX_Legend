using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 移動配置 - 定義 Walk/Run 的 Start 與 Stop 位移參數以及旋轉行為。
    /// 由 WeaponData 指派，在 WeaponManager 切換武器時同步給 NewGASPlayerController 使用。
    /// </summary>
    [CreateAssetMenu(fileName = "LocomotionMovementConfig", menuName = "GAS/Locomotion/Movement Config")]
    public class LocomotionMovementConfig : ScriptableObject
    {
        [Header("Start 位移")]
        [Tooltip("走路起步階段的位移距離（公尺）")]
        public float WalkStartDistance = 0.5f;

        [Tooltip("跑步起步階段的位移距離（公尺）")]
        public float RunStartDistance = 1.0f;

        [Header("Walk Stop 位移")]
        [Tooltip("走路停止階段的位移距離（公尺）")]
        public float WalkStopDistance = 0.3f;

        [Tooltip("走路停止階段的位移持續時間（秒）")]
        public float WalkStopDuration = 0.25f;

        [Tooltip("走路停止位移的速率曲線")]
        public AnimationCurve WalkStopCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Brake (Run Stop) 位移")]
        [Tooltip("跑步煞車階段的位移距離（公尺）")]
        public float BrakeDistance = 1.2f;

        [Tooltip("跑步煞車階段的位移持續時間（秒）")]
        public float BrakeDuration = 0.4f;

        [Tooltip("跑步煞車位移的速率曲線")]
        public AnimationCurve BrakeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Start 期間旋轉")]
        [Tooltip("Start 動畫期間的旋轉速率（度/秒）")]
        public float StartRotationSpeed = 720f;

        [Tooltip("Start 動畫期間是否允許程式控制旋轉跟隨搖桿輸入")]
        public bool RotateDuringStart = true;
    }
}
