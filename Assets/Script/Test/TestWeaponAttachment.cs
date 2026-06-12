using UnityEngine;

/// <summary>
/// 把武器 GameObject 自動附著到玩家角色 Avatar 的指定骨骼。
/// 用法:武器只保留視覺 Mesh (與選擇性的內部動畫骨骼,例如弓弦),
/// 掛上本元件 → 指定玩家 Animator → 選擇手骨 → Awake 會自動 SetParent 到該骨並套用偏移。
/// </summary>
public class TestWeaponAttachment : MonoBehaviour
{
    [Header("目標骨骼")]
    [SerializeField, Tooltip("玩家角色的 Animator (Humanoid)")]
    private Animator _characterAnimator;
    [SerializeField, Tooltip("是否使用 Humanoid 骨骼查詢 (推薦)。關閉則使用 Custom Bone")]
    private bool _useHumanoidBone = true;
    [SerializeField, Tooltip("Humanoid 骨骼 (常用 LeftHand 持弓 / RightHand 持劍)")]
    private HumanBodyBones _humanoidBone = HumanBodyBones.LeftHand;
    [SerializeField, Tooltip("自訂骨骼 Transform (Generic Rig 或要附著到非 Humanoid 骨骼時使用)")]
    private Transform _customBone;

    [Header("附著偏移")]
    [SerializeField, Tooltip("相對骨骼的 Local Position (公尺)")]
    private Vector3 _localPosition;
    [SerializeField, Tooltip("相對骨骼的 Local Rotation (歐拉角)")]
    private Vector3 _localEuler;
    [SerializeField, Tooltip("武器本地縮放 (1,1,1 = 原大小)")]
    private Vector3 _localScale = Vector3.one;

    [Header("時機")]
    [SerializeField, Tooltip("是否於 Awake 自動附著 (預設開啟,確保 TestPlayerDemo.Start 拿到正確的 localTransform)")]
    private bool _attachOnAwake = true;

    private void Awake()
    {
        if (_attachOnAwake)
        {
            Attach();
        }
    }

    /// <summary>立即執行附著。可從 Inspector 右鍵選單在 Edit 模式預覽位置。</summary>
    [ContextMenu("立即附著到骨骼")]
    public void Attach()
    {
        Transform bone = ResolveBone();
        if (bone == null) return;
        transform.SetParent(bone, worldPositionStays: false);
        transform.localPosition = _localPosition;
        transform.localEulerAngles = _localEuler;
        transform.localScale = _localScale;
    }

    private Transform ResolveBone()
    {
        if (!_useHumanoidBone)
        {
            if (_customBone == null)
            {
                Debug.LogWarning($"[TestWeaponAttachment] '{name}' 未指定 Custom Bone");
            }
            return _customBone;
        }
        if (_characterAnimator == null)
        {
            Debug.LogWarning($"[TestWeaponAttachment] '{name}' 未指定 Character Animator");
            return null;
        }
        if (!_characterAnimator.isHuman)
        {
            Debug.LogWarning($"[TestWeaponAttachment] '{name}' 的 Animator 不是 Humanoid,請改勾 Custom Bone 並手動指定骨骼");
            return null;
        }
        Transform bone = _characterAnimator.GetBoneTransform(_humanoidBone);
        if (bone == null)
        {
            Debug.LogWarning($"[TestWeaponAttachment] '{name}' 在 Animator 找不到 Humanoid 骨骼 {_humanoidBone}");
        }
        return bone;
    }
}
