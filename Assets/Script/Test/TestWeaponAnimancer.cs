using UnityEngine;
using Animancer;

/// <summary>
/// 武器 / 配件用的同步動畫元件。
/// 掛在武器 GameObject 上 (武器需有自己的 Animator + AnimancerComponent),
/// 填入與主角對應的武器版 ClipTransition。
/// TestPlayerDemo 切換狀態時會查詢此元件對應的動畫,播在武器自己的 Animancer 上。
/// 若武器與主角共用同一條 AnimationClip,兩邊填同個 ClipTransition 即可。
/// </summary>
public class TestWeaponAnimancer : MonoBehaviour
{
    [Header("Animancer")]
    [SerializeField, Tooltip("武器上的 AnimancerComponent (留空會自動於 Awake 從本物件抓取)")]
    private AnimancerComponent _animancer;
    [SerializeField, Tooltip("武器上半身 AvatarMask (留空則沿用 TestPlayerDemo 的遮罩)")]
    private AvatarMask _upperBodyMask;

    [Header("移動動畫 (武器版)")]
    [SerializeField, Tooltip("武器對應的 Idle")]
    private ClipTransition _idleAnim;
    [SerializeField, Tooltip("武器對應的 WalkStart")]
    private ClipTransition _walkStartAnim;
    [SerializeField, Tooltip("武器對應的 Walk")]
    private ClipTransition _walkAnim;
    [SerializeField, Tooltip("武器對應的 WalkEnd")]
    private ClipTransition _walkEndAnim;

    [Header("拉弓動畫 (武器版)")]
    [SerializeField, Tooltip("武器對應的 AimStart")]
    private ClipTransition _aimStartAnim;
    [SerializeField, Tooltip("武器對應的拉弓 Loop (循環)")]
    private ClipTransition _aimLoopAnim;
    [SerializeField, Tooltip("武器對應的 AimEnd")]
    private ClipTransition _aimEndAnim;
    [SerializeField, Tooltip("武器對應的 AimIdle Loop (循環)")]
    private ClipTransition _aimIdleLoopAnim;

    public AnimancerComponent Animancer => _animancer;
    public AvatarMask UpperBodyMask => _upperBodyMask;
    public ClipTransition IdleAnim => _idleAnim;
    public ClipTransition WalkStartAnim => _walkStartAnim;
    public ClipTransition WalkAnim => _walkAnim;
    public ClipTransition WalkEndAnim => _walkEndAnim;
    public ClipTransition AimStartAnim => _aimStartAnim;
    public ClipTransition AimLoopAnim => _aimLoopAnim;
    public ClipTransition AimEndAnim => _aimEndAnim;
    public ClipTransition AimIdleLoopAnim => _aimIdleLoopAnim;

    private Animator _animator;
    private Transform _spineBone;
    private Transform _chestBone;
    private Transform _upperChestBone;
    private Transform _headBone;

    private void Awake()
    {
        if (_animancer == null)
        {
            _animancer = GetComponent<AnimancerComponent>();
        }
        _animator = _animancer != null ? _animancer.GetComponent<Animator>() : GetComponent<Animator>();
        if (_animator != null && _animator.isHuman)
        {
            _spineBone = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _chestBone = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperChestBone = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
        }
    }

    /// <summary>由 TestPlayerDemo 在 LateUpdate 呼叫,把同樣的俯仰套用到武器的 Humanoid 脊椎。</summary>
    public void ApplyUpperBodyPitch(float pitchDegrees, Vector3 axis, float spineWeight, float chestWeight, float upperChestWeight, float headWeight)
    {
        if (Mathf.Abs(pitchDegrees) < 0.05f) return;
        if (_spineBone != null && spineWeight > 0f)
        {
            _spineBone.rotation = Quaternion.AngleAxis(pitchDegrees * spineWeight, axis) * _spineBone.rotation;
        }
        if (_chestBone != null && chestWeight > 0f)
        {
            _chestBone.rotation = Quaternion.AngleAxis(pitchDegrees * chestWeight, axis) * _chestBone.rotation;
        }
        if (_upperChestBone != null && upperChestWeight > 0f)
        {
            _upperChestBone.rotation = Quaternion.AngleAxis(pitchDegrees * upperChestWeight, axis) * _upperChestBone.rotation;
        }
        if (_headBone != null && headWeight > 0f)
        {
            _headBone.rotation = Quaternion.AngleAxis(pitchDegrees * headWeight, axis) * _headBone.rotation;
        }
    }
}
