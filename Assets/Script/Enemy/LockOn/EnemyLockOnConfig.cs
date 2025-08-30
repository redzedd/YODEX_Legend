using UnityEngine;

public class EnemyLockOnConfig : MonoBehaviour
{
    [Header("Profile")]
    public EnemyLockOnProfile profile;

    [Header("Instance Overrides (optional)")]
    public Transform lockAnchorOverride;      // 不填就用 profile.lockAnchorPath 尋找
    public Transform indicatorAnchorOverride; // 不填就用 profile.indicatorAnchorPath 尋找
    public float groupWeightOverride = -1f;   // <0 表示不覆寫
    public float groupRadiusOverride = -1f;   // <0 表示不覆寫

    [Header("Overrides / Rotation Composer")]
    public bool overrideScreenPosition = false;
    public Vector2 screenPositionOverride = Vector2.zero;

    // 有效錨點（鎖定/遮擋/取景用）
    public Transform EffectiveLockAnchor
    {
        get
        {
            if (lockAnchorOverride != null) return lockAnchorOverride;
            if (profile != null && !string.IsNullOrEmpty(profile.lockAnchorPath))
                return transform.Find(profile.lockAnchorPath);
            return transform; // 找不到就退回根物件
        }
    }

    // 指示器錨點（沒有就回傳 null，外部用高度）
    public Transform EffectiveIndicatorAnchor
    {
        get
        {
            if (indicatorAnchorOverride != null) return indicatorAnchorOverride;
            if (profile != null && !string.IsNullOrEmpty(profile.indicatorAnchorPath))
                return transform.Find(profile.indicatorAnchorPath);
            return null;
        }
    }

    public float EffectiveWeight =>
        (groupWeightOverride >= 0f) ? groupWeightOverride :
        (profile != null ? profile.groupWeight : 1f);

    public float EffectiveRadius =>
        (groupRadiusOverride >= 0f) ? groupRadiusOverride :
        (profile != null ? profile.groupRadius : 1f);

    public float IndicatorHeight =>
        (profile != null ? profile.indicatorHeight : 2.0f);

    public Vector2 EffectiveScreenPosition
    {
        get
        {
            if (overrideScreenPosition) return screenPositionOverride;
            if (profile != null) return new Vector2(profile.screenPosX, profile.screenPosY);
            return Vector2.zero;
        }
    }
}
