using UnityEngine;

public class EnemyLockOnConfig : MonoBehaviour
{
    [Header("Profile")]
    public EnemyLockOnProfile profile;

    [Header("Instance Overrides (optional)")]
    public Transform lockAnchorOverride;      // ����N�� profile.lockAnchorPath �M��
    public Transform indicatorAnchorOverride; // ����N�� profile.indicatorAnchorPath �M��
    public float groupWeightOverride = -1f;   // <0 ��ܤ��мg
    public float groupRadiusOverride = -1f;   // <0 ��ܤ��мg

    [Header("Overrides / Rotation Composer")]
    public bool overrideScreenPosition = false;
    public Vector2 screenPositionOverride = Vector2.zero;

    // �������I�]��w/�B��/�����Ρ^
    public Transform EffectiveLockAnchor
    {
        get
        {
            if (lockAnchorOverride != null) return lockAnchorOverride;
            if (profile != null && !string.IsNullOrEmpty(profile.lockAnchorPath))
                return transform.Find(profile.lockAnchorPath);
            return transform; // �䤣��N�h�^�ڪ���
        }
    }

    // ���ܾ����I�]�S���N�^�� null�A�~���ΰ��ס^
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
