using UnityEngine;

[CreateAssetMenu(menuName = "LockOn/Enemy Profile", fileName = "EnemyLockOnProfile")]
public class EnemyLockOnProfile : ScriptableObject
{
    [Header("Info")]
    public string enemyName = "Enemy";

    [Header("Indicator (when locked)")]
    public float indicatorHeight = 2.0f;     // 若沒提供 indicatorAnchor，就用這個高度
    public string indicatorAnchorPath = "";  // 例: "Armature/Hips/Spine/Chest/Head/IndicatorAnchor"

    [Header("Lock Anchor")]
    public string lockAnchorPath = "";       // 例: "Armature/Hips/Spine/Chest/Head/LockAnchor"

    [Header("Cinemachine TargetGroup")]
    public float groupWeight = 1.0f;         // 權重
    public float groupRadius = 1.0f;         // 半徑（越大鏡頭越會拉遠去裝下）

    [Header("Rotation Composer (Screen Position)")]
    [Tooltip("鎖定時套用到 CinemachineRotationComposer 的 ScreenPosition.X")]
    public float screenPosX = 0f;
    [Tooltip("鎖定時套用到 CinemachineRotationComposer 的 ScreenPosition.Y")]
    public float screenPosY = 0f;
}
