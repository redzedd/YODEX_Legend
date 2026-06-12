using UnityEngine;
using UnityEngine.InputSystem;
using Animancer;
using Unity.Cinemachine;

/// <summary>
/// 宣傳片演示用玩家控制器,涵蓋移動 (Idle/WalkStart/Walk/WalkEnd) 與
/// 拉弓瞄準 (AimStart/Aim/AimEnd/PostAim) 兩條獨立狀態機。
/// 注意:本腳本必須與 AnimancerComponent 掛在同一個 GameObject,
/// 才能讓 AnimationEvent 透過 SendMessage 呼叫到 OnFireArrowEvent。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class TestPlayerDemo : MonoBehaviour
{
    [Header("移動設定 (使用 Root Motion 動畫,位移由動畫驅動)")]
    [SerializeField, Tooltip("身體旋轉速度 (度/秒,Rotation 仍由程式插值控制)")]
    private float _rotateSpeed = 720f;
    [SerializeField, Tooltip("重力加速度 (一般為負值)")]
    private float _gravity = -15f;
    [SerializeField, Tooltip("Root Motion 整體倍率 (1 = 動畫原速)")]
    private float _rootMotionMultiplier = 1f;

    [Header("動畫元件")]
    [SerializeField, Tooltip("主 Animancer 動畫元件 (建議與本腳本同 GameObject,且掛有 Animator)")]
    private AnimancerComponent _animancer;
    [SerializeField, Tooltip("需要同步動畫的武器 (每項為一個掛有 TestWeaponAnimancer 的物件,使用武器自己的 Clip 集合)")]
    private TestWeaponAnimancer[] _weaponAnimancers;
    [SerializeField, Tooltip("鎖定武器相對父物件的 localPosition / localRotation,防止 Root Motion 或物理造成漂移")]
    private bool _lockWeaponLocalTransform = true;

    [Header("移動動畫")]
    [SerializeField, Tooltip("待機 (循環)")]
    private ClipTransition _idleAnim;
    [SerializeField, Tooltip("起步 (非循環,結束後會自動切到 Walk)")]
    private ClipTransition _walkStartAnim;
    [SerializeField, Tooltip("行走 (循環)")]
    private ClipTransition _walkAnim;
    [SerializeField, Tooltip("停步 (非循環,結束後會自動切回 Idle)")]
    private ClipTransition _walkEndAnim;

    [Header("拉弓動畫 (皆播放在 Layer 1 上半身層)")]
    [SerializeField, Tooltip("上半身 AvatarMask (開啟脊椎/肩/手臂/頭,關閉腿部與 Root)")]
    private AvatarMask _upperBodyMask;
    [SerializeField, Tooltip("上半身層淡入秒數")]
    private float _aimLayerFadeIn = 0.2f;
    [SerializeField, Tooltip("上半身層淡出秒數 (PostAim 歸位用)")]
    private float _aimLayerFadeOut = 0.3f;
    [SerializeField, Tooltip("拉弓開始 (非循環,結束後切到 Aim Loop)")]
    private ClipTransition _aimStartAnim;
    [SerializeField, Tooltip("拉弓持續 Loop (循環,上半身呈拉弓姿勢,俯仰由 Avatar 骨骼旋轉處理)")]
    private ClipTransition _aimLoopAnim;
    [SerializeField, Tooltip("射出 (非循環,請在動畫加 AnimationEvent 呼叫 OnFireArrowEvent)")]
    private ClipTransition _aimEndAnim;
    [SerializeField, Tooltip("射出後保持姿勢的 Loop (循環,PostAim 3 秒內使用)")]
    private ClipTransition _aimIdleLoopAnim;

    [Header("攝影機")]
    [SerializeField, Tooltip("拉弓專用 Cinemachine 攝影機")]
    private CinemachineCamera _aimCamera;
    [SerializeField, Tooltip("拉弓攝影機啟用時的 Priority")]
    private int _aimCameraActivePriority = 20;
    [SerializeField, Tooltip("拉弓攝影機停用時的 Priority")]
    private int _aimCameraInactivePriority = 0;
    [SerializeField, Tooltip("射出後保持瞄準視角與身體姿勢的延遲秒數")]
    private float _aimReturnDelay = 3f;

    [Header("射擊設定")]
    [SerializeField, Tooltip("箭矢 Prefab (建議含 Rigidbody)")]
    private GameObject _arrowPrefab;
    [SerializeField, Tooltip("箭矢生成位置 (通常為弓口或手部 Transform)")]
    private Transform _arrowSpawnPoint;
    [SerializeField, Tooltip("箭矢初速度 (公尺/秒,薩爾達感建議 60~80)")]
    private float _arrowLaunchSpeed = 60f;
    [SerializeField, Range(0f, 1f), Tooltip("箭矢重力倍率 (1 = 正常 9.81, 0.3 ≈ 薩爾達, 0.1 = 幾乎直線)")]
    private float _arrowGravityScale = 0.3f;
    [SerializeField, Tooltip("螢幕中央射線檢測圖層")]
    private LayerMask _aimRaycastMask = ~0;
    [SerializeField, Tooltip("螢幕中央射線最大距離")]
    private float _aimRaycastMaxDistance = 200f;
    [SerializeField, Tooltip("若未設 AnimationEvent,勾此欄位改為 AimEnd 動畫自然結束時射出 (Debug 用)")]
    private bool _fireAtAimEndFallback;

    [Header("瞄準平滑 (避免相機阻尼造成抖動)")]
    [SerializeField, Tooltip("瞄準命中點平滑時間 (秒,越大越穩但延遲越明顯,薩爾達感建議 0.03~0.06)")]
    private float _aimPointSmoothTime = 0.04f;
    [SerializeField, Tooltip("拉弓時身體 Yaw 平滑時間 (秒,身體追目標方向的延遲感,薩爾達感建議 0.08~0.12)")]
    private float _aimYawSmoothTime = 0.1f;
    [SerializeField, Tooltip("上半身俯仰平滑時間 (秒)")]
    private float _aimPitchSmoothTime = 0.08f;
    [SerializeField, Tooltip("俯仰計算基準點高度 (從玩家 Transform 起算的胸口高度,公尺)")]
    private float _aimOriginHeight = 1.6f;

    [Header("上半身瞄準旋轉 (Avatar Humanoid Bones)")]
    [SerializeField, Range(0f, 1f), Tooltip("Spine 骨骼分擔的俯仰比例")]
    private float _spineWeight = 0.2f;
    [SerializeField, Range(0f, 1f), Tooltip("Chest 骨骼分擔的俯仰比例")]
    private float _chestWeight = 0.35f;
    [SerializeField, Range(0f, 1f), Tooltip("UpperChest 骨骼分擔的俯仰比例 (無此骨骼則忽略)")]
    private float _upperChestWeight = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Head 骨骼分擔的俯仰比例")]
    private float _headWeight = 0.15f;
    [SerializeField, Tooltip("最大往上俯仰 (度)")]
    private float _maxPitchUp = 60f;
    [SerializeField, Tooltip("最大往下俯仰 (度)")]
    private float _maxPitchDown = 45f;

    [Header("Debug 可視化 (Game 視窗可見)")]
    [SerializeField, Tooltip("啟用瞄準軌跡與命中點顯示")]
    private bool _showAimDebug = true;
    [SerializeField, Tooltip("箭矢軌跡顏色 (從玩家發射點到目標)")]
    private Color _debugArrowColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField, Tooltip("攝影機瞄準射線顏色 (從鏡頭到命中點)")]
    private Color _debugCameraColor = new Color(0.3f, 1f, 0.9f, 0.8f);
    [SerializeField, Tooltip("軌跡線寬 (公尺)")]
    private float _debugLineWidth = 0.03f;
    [SerializeField, Tooltip("命中點球體直徑 (公尺)")]
    private float _debugMarkerSize = 0.2f;
    [SerializeField, Tooltip("拋物線取樣段數")]
    private int _debugTrajectorySegments = 32;

    [Header("瞄準準星 UI")]
    [SerializeField, Tooltip("TestAimReticle 元件 (點 + 圓環蓄力準星),留空則不顯示準星")]
    private TestAimReticle _aimReticle;

    [Header("鼠標")]
    [SerializeField, Tooltip("遊戲開始時隱藏鼠標並鎖定於螢幕中央")]
    private bool _hideCursorOnStart = true;
    [SerializeField, Tooltip("失去焦點時解鎖鼠標 (方便在編輯器點其他視窗)")]
    private bool _unlockCursorOnFocusLost = true;

    private CharacterController _characterController;
    private Animator _animator;
    private Camera _mainCamera;
    private InputAction _moveAction;
    private InputAction _attackAction;

    private LocomotionState _locomotionState = LocomotionState.Idle;
    private AimPhase _aimPhase = AimPhase.None;
    private float _postAimTimer;
    private Vector3 _currentAimWorldPoint;
    private Vector3 _smoothedAimPoint;
    private Vector3 _aimPointVelocity;
    private float _yawVelocity;
    private float _currentPitch;
    private float _pitchVelocity;
    private float _frozenPitch;
    private float _verticalVelocity;
    private Vector3[] _weaponInitialLocalPos;
    private Quaternion[] _weaponInitialLocalRot;

    private Transform _spineBone;
    private Transform _chestBone;
    private Transform _upperChestBone;
    private Transform _headBone;

    private LineRenderer _debugArrowLine;
    private LineRenderer _debugCameraLine;
    private Transform _debugMarker;

    private const int LOCOMOTION_LAYER = 0;
    private const int AIM_LAYER = 1;

    private enum LocomotionState
    {
        Idle,
        WalkStart,
        Walking,
        WalkEnd
    }

    private enum AimPhase
    {
        None,
        AimStart,
        Aiming,
        AimEnd,
        PostAim
    }

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();
        _mainCamera = Camera.main;
        if (_animancer == null)
        {
            _animancer = GetComponent<AnimancerComponent>();
        }
        if (_animator != null)
        {
            _animator.applyRootMotion = true;
        }
        BuildInputActions();
    }

    private void BuildInputActions()
    {
        _moveAction = new InputAction("TestPlayerMove", InputActionType.Value, expectedControlType: "Vector2");
        _moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        _attackAction = new InputAction("TestPlayerAttack", InputActionType.Button, "<Mouse>/rightButton");
        _attackAction.started += OnAttackStarted;
        _attackAction.canceled += OnAttackCanceled;
    }

    private void OnEnable()
    {
        _moveAction?.Enable();
        _attackAction?.Enable();
    }

    private void OnDisable()
    {
        _moveAction?.Disable();
        _attackAction?.Disable();
    }

    private void OnDestroy()
    {
        if (_attackAction != null)
        {
            _attackAction.started -= OnAttackStarted;
            _attackAction.canceled -= OnAttackCanceled;
            _attackAction.Dispose();
        }
        _moveAction?.Dispose();
        if (_debugArrowLine != null) Destroy(_debugArrowLine.gameObject);
        if (_debugCameraLine != null) Destroy(_debugCameraLine.gameObject);
        if (_debugMarker != null) Destroy(_debugMarker.gameObject);
    }

    private void Start()
    {
        SetupAimLayer(_animancer, _upperBodyMask);
        if (_weaponAnimancers != null)
        {
            _weaponInitialLocalPos = new Vector3[_weaponAnimancers.Length];
            _weaponInitialLocalRot = new Quaternion[_weaponAnimancers.Length];
            for (int i = 0; i < _weaponAnimancers.Length; i++)
            {
                TestWeaponAnimancer w = _weaponAnimancers[i];
                if (w == null) continue;
                _weaponInitialLocalPos[i] = w.transform.localPosition;
                _weaponInitialLocalRot[i] = w.transform.localRotation;
                if (w.Animancer != null)
                {
                    SetupAimLayer(w.Animancer, w.UpperBodyMask != null ? w.UpperBodyMask : _upperBodyMask);
                    Animator weaponAnimator = w.Animancer.GetComponent<Animator>();
                    if (weaponAnimator != null)
                    {
                        weaponAnimator.applyRootMotion = false;
                    }
                }
            }
        }
        ValidateWeaponSetup();
        CacheHumanoidBones();
        _smoothedAimPoint = transform.position + transform.forward * 10f;
        EnterIdle();
        ApplyAimCameraPriority(false);
        if (_showAimDebug)
        {
            SetupDebugVisuals();
        }
        if (_hideCursorOnStart)
        {
            SetCursorLocked(true);
        }
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!_hideCursorOnStart) return;
        if (!hasFocus && _unlockCursorOnFocusLost)
        {
            SetCursorLocked(false);
        }
        else if (hasFocus)
        {
            SetCursorLocked(true);
        }
    }

    private void ValidateWeaponSetup()
    {
        if (_weaponAnimancers == null || _weaponAnimancers.Length == 0)
        {
            Debug.Log("[TestPlayerDemo] 未註冊任何武器 (Weapon Animancers 陣列為空)");
            return;
        }
        for (int i = 0; i < _weaponAnimancers.Length; i++)
        {
            TestWeaponAnimancer w = _weaponAnimancers[i];
            if (w == null)
            {
                Debug.LogWarning($"[TestPlayerDemo] Weapon Animancers[{i}] 為 null,請拖入武器 GameObject (需含 TestWeaponAnimancer 元件)");
                continue;
            }
            if (w.Animancer == null)
            {
                Debug.LogWarning($"[TestPlayerDemo] 武器 '{w.name}' 的 TestWeaponAnimancer 找不到 AnimancerComponent。請在武器上掛 AnimancerComponent,或手動拖到該欄位。");
                continue;
            }
            Animator weaponAnimator = w.Animancer.GetComponent<Animator>();
            if (weaponAnimator == null)
            {
                Debug.LogWarning($"[TestPlayerDemo] 武器 '{w.name}' 缺少 Animator 元件 — AnimancerComponent 需要 Animator 才能運作");
            }
            else if (weaponAnimator.avatar == null && weaponAnimator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[TestPlayerDemo] 武器 '{w.name}' 的 Animator 沒有 Avatar。Animancer 需要 Avatar 才能驅動骨架。FBX Rig 請設成 Generic 並指定 Avatar Definition。");
            }
            int clipsFilled = 0;
            if (w.IdleAnim != null) clipsFilled++;
            if (w.WalkStartAnim != null) clipsFilled++;
            if (w.WalkAnim != null) clipsFilled++;
            if (w.WalkEndAnim != null) clipsFilled++;
            if (w.AimStartAnim != null) clipsFilled++;
            if (w.AimLoopAnim != null) clipsFilled++;
            if (w.AimEndAnim != null) clipsFilled++;
            if (w.AimIdleLoopAnim != null) clipsFilled++;
            if (clipsFilled == 0)
            {
                Debug.LogWarning($"[TestPlayerDemo] 武器 '{w.name}' 的 TestWeaponAnimancer 所有 ClipTransition 都是空的 — 需要至少填對應的武器版動畫");
            }
            else
            {
                Debug.Log($"[TestPlayerDemo] 武器 '{w.name}' 已註冊 ({clipsFilled}/8 個 Clip 已填)");
            }
        }
    }

    private void CacheHumanoidBones()
    {
        if (_animator == null || !_animator.isHuman) return;
        _spineBone = _animator.GetBoneTransform(HumanBodyBones.Spine);
        _chestBone = _animator.GetBoneTransform(HumanBodyBones.Chest);
        _upperChestBone = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
        _headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
    }

    private void SetupAimLayer(AnimancerComponent animancer, AvatarMask mask)
    {
        AnimancerLayer aimLayer = animancer.Layers[AIM_LAYER];
        if (mask != null)
        {
            aimLayer.Mask = mask;
        }
        aimLayer.Weight = 0f;
    }

    // 廣播播放:主 Animancer 播 mainTransition,每把武器播 weaponSelector 挑出的武器版 transition
    private AnimancerState PlayOnAll(ITransition mainTransition, System.Func<TestWeaponAnimancer, ITransition> weaponSelector, int layer)
    {
        AnimancerState mainState = _animancer.Layers[layer].Play(mainTransition);
        if (_weaponAnimancers == null) return mainState;
        for (int i = 0; i < _weaponAnimancers.Length; i++)
        {
            TestWeaponAnimancer w = _weaponAnimancers[i];
            if (w == null || w.Animancer == null) continue;
            ITransition weaponTransition = weaponSelector(w);
            if (weaponTransition != null)
            {
                w.Animancer.Layers[layer].Play(weaponTransition);
            }
        }
        return mainState;
    }

    private void SetAimLayerWeight(float targetWeight, float fadeDuration)
    {
        _animancer.Layers[AIM_LAYER].StartFade(targetWeight, fadeDuration);
        if (_weaponAnimancers == null) return;
        for (int i = 0; i < _weaponAnimancers.Length; i++)
        {
            TestWeaponAnimancer w = _weaponAnimancers[i];
            if (w != null && w.Animancer != null)
            {
                w.Animancer.Layers[AIM_LAYER].StartFade(targetWeight, fadeDuration);
            }
        }
    }

    private float ComputeDesiredPitch()
    {
        if (_aimPhase == AimPhase.None) return 0f;
        if (_aimPhase == AimPhase.PostAim) return _frozenPitch;
        Vector3 origin = transform.position + Vector3.up * _aimOriginHeight;
        Vector3 toTarget = _currentAimWorldPoint - origin;
        float horizDist = new Vector2(toTarget.x, toTarget.z).magnitude;
        if (horizDist < 0.01f) return _currentPitch;
        float pitch = -Mathf.Atan2(toTarget.y, horizDist) * Mathf.Rad2Deg;
        pitch = Mathf.Clamp(pitch, -_maxPitchUp, _maxPitchDown);
        _frozenPitch = pitch;
        return pitch;
    }

    private void ApplyUpperBodyAimRotation()
    {
        float desired = ComputeDesiredPitch();
        _currentPitch = Mathf.SmoothDamp(_currentPitch, desired, ref _pitchVelocity, _aimPitchSmoothTime);
        if (Mathf.Abs(_currentPitch) < 0.05f) return;
        Vector3 axis = transform.right;
        if (_spineBone != null && _spineWeight > 0f)
        {
            _spineBone.rotation = Quaternion.AngleAxis(_currentPitch * _spineWeight, axis) * _spineBone.rotation;
        }
        if (_chestBone != null && _chestWeight > 0f)
        {
            _chestBone.rotation = Quaternion.AngleAxis(_currentPitch * _chestWeight, axis) * _chestBone.rotation;
        }
        if (_upperChestBone != null && _upperChestWeight > 0f)
        {
            _upperChestBone.rotation = Quaternion.AngleAxis(_currentPitch * _upperChestWeight, axis) * _upperChestBone.rotation;
        }
        if (_headBone != null && _headWeight > 0f)
        {
            _headBone.rotation = Quaternion.AngleAxis(_currentPitch * _headWeight, axis) * _headBone.rotation;
        }
        // 獨立 Humanoid 武器:同步套用同一個 pitch 到武器自己的脊椎骨,讓武器跟著上下擺
        if (_weaponAnimancers == null) return;
        for (int i = 0; i < _weaponAnimancers.Length; i++)
        {
            TestWeaponAnimancer w = _weaponAnimancers[i];
            if (w == null) continue;
            w.ApplyUpperBodyPitch(_currentPitch, axis, _spineWeight, _chestWeight, _upperChestWeight, _headWeight);
        }
    }

    private void Update()
    {
        UpdateAimWorldPoint();
        TickLocomotion();
        if (_aimPhase != AimPhase.None)
        {
            TickAim();
        }
        UpdateDebugVisuals();
    }

    // Animator 在 Update 之後寫入 Transform,LateUpdate 再把武器的 localPosition/Rotation 校正回原值,防止漂移
    private void LateUpdate()
    {
        // 1. Animator 已寫入骨骼,先做上半身俯仰,手骨會被連帶旋轉,武器再跟著手骨 (因為 parent 關係)
        ApplyUpperBodyAimRotation();
        // 2. 武器若有自己的 Animator Root Motion,在這裡把 localTransform 鎖回初始值
        if (!_lockWeaponLocalTransform || _weaponAnimancers == null || _weaponInitialLocalPos == null) return;
        for (int i = 0; i < _weaponAnimancers.Length; i++)
        {
            TestWeaponAnimancer w = _weaponAnimancers[i];
            if (w == null) continue;
            w.transform.localPosition = _weaponInitialLocalPos[i];
            w.transform.localRotation = _weaponInitialLocalRot[i];
        }
    }

    private void UpdateAimWorldPoint()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }
        Ray ray = _mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 rawPoint = ray.origin + ray.direction * _aimRaycastMaxDistance;
        if (Physics.Raycast(ray, out RaycastHit hit, _aimRaycastMaxDistance, _aimRaycastMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform))
        {
            rawPoint = hit.point;
        }
        _smoothedAimPoint = Vector3.SmoothDamp(_smoothedAimPoint, rawPoint, ref _aimPointVelocity, _aimPointSmoothTime);
        _currentAimWorldPoint = _smoothedAimPoint;
    }

    private void TickLocomotion()
    {
        // 拉弓期間鎖定輸入,讓 Layer 0 自然過渡回 Idle
        Vector2 input = _aimPhase == AimPhase.None
            ? _moveAction.ReadValue<Vector2>()
            : Vector2.zero;
        bool hasInput = input.sqrMagnitude > 0.01f;
        Vector3 worldMove = Vector3.zero;
        if (hasInput && _mainCamera != null)
        {
            Vector3 camForward = _mainCamera.transform.forward;
            camForward.y = 0f;
            camForward.Normalize();
            Vector3 camRight = _mainCamera.transform.right;
            camRight.y = 0f;
            camRight.Normalize();
            worldMove = camForward * input.y + camRight * input.x;
            if (worldMove.sqrMagnitude > 1f)
            {
                worldMove.Normalize();
            }
        }
        switch (_locomotionState)
        {
            case LocomotionState.Idle:
                if (hasInput) EnterWalkStart();
                break;
            case LocomotionState.Walking:
                if (!hasInput) EnterWalkEnd();
                break;
        }
        if (_aimPhase == AimPhase.None && hasInput && _locomotionState != LocomotionState.WalkEnd)
        {
            Quaternion targetRot = Quaternion.LookRotation(worldMove);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, _rotateSpeed * Time.deltaTime);
        }
    }

    private void EnterIdle()
    {
        _locomotionState = LocomotionState.Idle;
        PlayOnAll(_idleAnim, w => w.IdleAnim, LOCOMOTION_LAYER);
    }

    private void EnterWalkStart()
    {
        _locomotionState = LocomotionState.WalkStart;
        AnimancerState state = PlayOnAll(_walkStartAnim, w => w.WalkStartAnim, LOCOMOTION_LAYER);
        state.Events(this).OnEnd = OnWalkStartFinished;
    }

    private void EnterWalking()
    {
        _locomotionState = LocomotionState.Walking;
        PlayOnAll(_walkAnim, w => w.WalkAnim, LOCOMOTION_LAYER);
    }

    private void EnterWalkEnd()
    {
        _locomotionState = LocomotionState.WalkEnd;
        AnimancerState state = PlayOnAll(_walkEndAnim, w => w.WalkEndAnim, LOCOMOTION_LAYER);
        state.Events(this).OnEnd = OnWalkEndFinished;
    }

    private void OnWalkStartFinished()
    {
        if (_locomotionState != LocomotionState.WalkStart) return;
        Vector2 input = _moveAction.ReadValue<Vector2>();
        if (input.sqrMagnitude > 0.01f)
        {
            EnterWalking();
        }
        else
        {
            EnterWalkEnd();
        }
    }

    private void OnWalkEndFinished()
    {
        if (_locomotionState != LocomotionState.WalkEnd) return;
        Vector2 input = _moveAction.ReadValue<Vector2>();
        if (input.sqrMagnitude > 0.01f)
        {
            EnterWalkStart();
        }
        else
        {
            EnterIdle();
        }
    }

    private void OnAttackStarted(InputAction.CallbackContext ctx)
    {
        if (_aimPhase == AimPhase.None || _aimPhase == AimPhase.PostAim)
        {
            EnterAimStart();
        }
    }

    private void OnAttackCanceled(InputAction.CallbackContext ctx)
    {
        if (_aimPhase == AimPhase.AimStart || _aimPhase == AimPhase.Aiming)
        {
            EnterAimEnd();
        }
    }

    private void EnterAimStart()
    {
        _aimPhase = AimPhase.AimStart;
        AnimancerState state = PlayOnAll(_aimStartAnim, w => w.AimStartAnim, AIM_LAYER);
        state.Events(this).OnEnd = EnterAiming;
        SetAimLayerWeight(1f, _aimLayerFadeIn);
        ApplyAimCameraPriority(true);
        if (_aimReticle != null) _aimReticle.OnAimEnter();
    }

    private void EnterAiming()
    {
        if (_aimPhase != AimPhase.AimStart) return;
        _aimPhase = AimPhase.Aiming;
        PlayOnAll(_aimLoopAnim, w => w.AimLoopAnim, AIM_LAYER);
        if (_aimReticle != null) _aimReticle.StartCharge();
    }

    private void EnterAimEnd()
    {
        _aimPhase = AimPhase.AimEnd;
        AnimancerState state = PlayOnAll(_aimEndAnim, w => w.AimEndAnim, AIM_LAYER);
        state.Events(this).OnEnd = EnterPostAim;
        if (_aimReticle != null) _aimReticle.EndCharge();
    }

    private void EnterPostAim()
    {
        if (_aimPhase != AimPhase.AimEnd) return;
        if (_fireAtAimEndFallback)
        {
            FireArrow();
        }
        _aimPhase = AimPhase.PostAim;
        _postAimTimer = _aimReturnDelay;
        PlayOnAll(_aimIdleLoopAnim, w => w.AimIdleLoopAnim, AIM_LAYER);
    }

    private void ExitAimToIdle()
    {
        _aimPhase = AimPhase.None;
        SetAimLayerWeight(0f, _aimLayerFadeOut);
        ApplyAimCameraPriority(false);
        if (_aimReticle != null) _aimReticle.OnAimExit();
    }

    private void TickAim()
    {
        if (_aimPhase == AimPhase.AimStart || _aimPhase == AimPhase.Aiming || _aimPhase == AimPhase.AimEnd)
        {
            RotateBodyToCamera();
        }
        else if (_aimPhase == AimPhase.PostAim)
        {
            _postAimTimer -= Time.deltaTime;
            if (_postAimTimer <= 0f)
            {
                ExitAimToIdle();
            }
        }
    }

    private void RotateBodyToCamera()
    {
        // 直接面對瞄準目標 (水平面),不再追相機 forward。這樣身體前方 = 目標方向,九宮格不需要扭上半身去補正
        Vector3 toTarget = _currentAimWorldPoint - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return;
        float targetYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        float currentYaw = transform.eulerAngles.y;
        float newYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref _yawVelocity, _aimYawSmoothTime);
        transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
    }

    // Root Motion 由此接收:拉弓時忽略水平位移,僅保留重力讓角色貼地
    private void OnAnimatorMove()
    {
        if (_characterController == null) return;
        Vector3 rootDelta = _aimPhase == AimPhase.None && _animator != null
            ? _animator.deltaPosition * _rootMotionMultiplier
            : Vector3.zero;
        if (_characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }
        _verticalVelocity += _gravity * Time.deltaTime;
        Vector3 motion = rootDelta + new Vector3(0f, _verticalVelocity * Time.deltaTime, 0f);
        _characterController.Move(motion);
    }

    private void ApplyAimCameraPriority(bool active)
    {
        if (_aimCamera == null) return;
        _aimCamera.Priority.Value = active ? _aimCameraActivePriority : _aimCameraInactivePriority;
    }

    // ===== AnimationEvent 入口 =====
    // 在 AimEnd 動畫片段加 AnimationEvent,Function 欄位填 "OnFireArrowEvent",
    // 即可在你想要的時機 (例:箭脫弦的那一格) 觸發實際射出。
    public void OnFireArrowEvent()
    {
        FireArrow();
    }

    private void FireArrow()
    {
        if (_arrowPrefab == null)
        {
            Debug.LogWarning("[TestPlayerDemo] 箭矢 Prefab 未設定,無法射箭");
            return;
        }
        if (_arrowSpawnPoint == null)
        {
            Debug.LogWarning("[TestPlayerDemo] 箭矢生成位置 (Arrow Spawn Point) 未設定");
            return;
        }
        Vector3 spawn = _arrowSpawnPoint.position;
        Vector3 target = _currentAimWorldPoint;
        Vector3 dir = target - spawn;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = transform.forward;
        }
        Vector3 launchVelocity = dir.normalized * _arrowLaunchSpeed;
        GameObject arrow = Instantiate(_arrowPrefab, spawn, Quaternion.LookRotation(launchVelocity));
        if (arrow.TryGetComponent(out TestArrowProjectile proj))
        {
            proj.LaunchSpeed = _arrowLaunchSpeed;
            proj.GravityScale = _arrowGravityScale;
        }
        if (arrow.TryGetComponent(out Rigidbody rb))
        {
            rb.useGravity = false;
            rb.linearVelocity = launchVelocity;
        }
    }

    private void SetupDebugVisuals()
    {
        Shader shader = Shader.Find("Sprites/Default");
        _debugArrowLine = CreateDebugLine("TestPlayerDemo_ArrowTrajectory", shader, _debugArrowColor, _debugLineWidth);
        _debugCameraLine = CreateDebugLine("TestPlayerDemo_CameraAimRay", shader, _debugCameraColor, _debugLineWidth * 0.5f);
        GameObject markerGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerGo.name = "TestPlayerDemo_HitMarker";
        Destroy(markerGo.GetComponent<Collider>());
        markerGo.transform.localScale = Vector3.one * _debugMarkerSize;
        Renderer markerRenderer = markerGo.GetComponent<Renderer>();
        markerRenderer.material = new Material(shader);
        markerRenderer.material.color = _debugArrowColor;
        markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        markerRenderer.receiveShadows = false;
        _debugMarker = markerGo.transform;
        markerGo.SetActive(false);
    }

    private static LineRenderer CreateDebugLine(string name, Shader shader, Color color, float width)
    {
        GameObject go = new GameObject(name);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.material = new Material(shader);
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.positionCount = 0;
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    private void UpdateDebugVisuals()
    {
        if (_debugArrowLine == null) return;
        bool visible = _aimPhase == AimPhase.AimStart || _aimPhase == AimPhase.Aiming;
        if (!visible)
        {
            _debugArrowLine.positionCount = 0;
            _debugCameraLine.positionCount = 0;
            if (_debugMarker != null) _debugMarker.gameObject.SetActive(false);
            return;
        }
        Vector3 spawn = _arrowSpawnPoint != null ? _arrowSpawnPoint.position : transform.position + Vector3.up * 1.6f;
        Vector3 target = _currentAimWorldPoint;
        // 攝影機瞄準射線 (從鏡頭到命中點)
        if (_mainCamera != null)
        {
            _debugCameraLine.positionCount = 2;
            _debugCameraLine.SetPosition(0, _mainCamera.transform.position + _mainCamera.transform.forward * 0.3f);
            _debugCameraLine.SetPosition(1, target);
        }
        // 箭矢實際軌跡 (直線射出 + 自訂重力倍率,模擬薩爾達手感)
        Vector3 aimDir = target - spawn;
        if (aimDir.sqrMagnitude < 0.0001f)
        {
            aimDir = transform.forward;
        }
        Vector3 v0 = aimDir.normalized * _arrowLaunchSpeed;
        Vector3 arrowGravity = Physics.gravity * _arrowGravityScale;
        Vector3 deltaXZ = new Vector3(aimDir.x, 0f, aimDir.z);
        float vHorz = new Vector2(v0.x, v0.z).magnitude;
        float tFlight = deltaXZ.magnitude / Mathf.Max(vHorz, 0.01f);
        int segs = Mathf.Max(_debugTrajectorySegments, 2);
        _debugArrowLine.positionCount = segs;
        for (int i = 0; i < segs; i++)
        {
            float t = (i / (float)(segs - 1)) * tFlight;
            Vector3 p = spawn + v0 * t + 0.5f * arrowGravity * t * t;
            _debugArrowLine.SetPosition(i, p);
        }
        if (_debugMarker != null)
        {
            _debugMarker.gameObject.SetActive(true);
            _debugMarker.position = target;
        }
    }

}
