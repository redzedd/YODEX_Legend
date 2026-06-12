using UnityEngine;
using Unity.Cinemachine;
using Player.Locomotion;
using CameraSystem;

namespace GAS
{
    /// <summary>
    /// 瞄準相機控制器 - 管理肩射瞄準相機的切換和射線計算
    /// 鏡頭 Priority 由 CameraDirector 中控管理（本元件不再自己設 Priority）
    /// 使用 CinemachineCamera + CinemachineThirdPersonFollow
    /// 自動退出規則:
    ///   - 任何時候: 閃避 / 受擊硬直(含 Knockback/Stagger) / 死亡 tag → ExitAim
    ///   - ability 進行中 (擁有 State.Aiming tag): 移動「不」打斷,玩家可邊走邊射
    ///   - post-fire 持久 aim (IsAiming=true 但沒 State.Aiming tag): 移動會觸發 ExitAim
    ///     (用於發射完仍維持 aim 預瞄,但玩家走動表示要離開瞄準狀態)
    /// </summary>
    public class AimCameraController : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField]
        [Tooltip("瞄準用 CinemachineCamera（需要 CinemachineThirdPersonFollow；該鏡頭物件另需掛 CameraEntry，ID=Aim, Layer=Aim）")]
        private CinemachineCamera _aimCamera;

        [Header("Settings")]
        [SerializeField]
        [Tooltip("瞄準時的 FOV")]
        private float _aimFOV = 40f;

        [SerializeField]
        [Tooltip("正常 FOV")]
        private float _normalFOV = 60f;

        [SerializeField]
        [Tooltip("FOV 過渡速度")]
        private float _fovTransitionSpeed = 10f;

        [Header("Aim Ray")]
        [SerializeField]
        [Tooltip("瞄準射線最大距離")]
        private float _aimRayMaxDistance = 100f;

        [SerializeField]
        [Tooltip("瞄準射線碰撞圖層")]
        private LayerMask _aimRayLayers = ~0;

        [Header("Auto-Exit")]
        [SerializeField]
        [Tooltip("受擊硬直(含 Knockback/Stagger)/閃避/死亡時自動退出瞄準")]
        private bool _autoExitOnInterruptStates = true;

        [SerializeField]
        [Tooltip("Post-fire 持久 aim 模式(無 ability 進行中) 偵測移動的門檻 — RawMove 幅度高於此值退出瞄準")]
        private float _persistentMovementExitThreshold = 0.2f;

        [Header("UI Hookup")]
        [SerializeField]
        [Tooltip("瞄準 UI 控制器 — ExitAim 時會自動 HideAll(隱藏準星/收縮環等);留空會在 Awake 自動找")]
        private AimUIController _aimUI;

        /// <summary>是否正在瞄準中</summary>
        public bool IsAiming { get; private set; }

        private Camera _mainCamera;
        private float _currentFOV;
        private AbilitySystemComponent _ownerAsc;
        private LocomotionInputReader _locomotionReader;
        private CameraTicket _aimTicket;

        private void Awake()
        {
            _mainCamera = Camera.main;
            _currentFOV = _normalFOV;

            // 自動退出條件靠 ASC 的 OwnedTags + 移動輸入判斷
            _ownerAsc = GetComponent<AbilitySystemComponent>();
            if (_ownerAsc == null) _ownerAsc = GetComponentInParent<AbilitySystemComponent>();
            _locomotionReader = GetComponent<LocomotionInputReader>();
            if (_locomotionReader == null) _locomotionReader = GetComponentInParent<LocomotionInputReader>();
            // AimUI 用於 ExitAim 時 HideAll(隱藏準星等)
            if (_aimUI == null) _aimUI = GetComponentInChildren<AimUIController>();
            if (_aimUI == null) _aimUI = FindAnyObjectByType<AimUIController>();
        }

        private void OnDisable()
        {
            // 元件停用時釋放 ticket，避免 Director stack 殘留
            ReleaseTicket();
        }

        private void Update()
        {
            if (!IsAiming) return;
            CheckAutoExitConditions();
        }

        /// <summary>
        /// 偵測自動退出條件:
        /// 1. 中斷狀態(HitStunned/Dodging/Dead) — 任何時候達到即退出
        /// 2. Post-fire 持久 aim 模式(沒 State.Aiming tag 但 IsAiming=true) — 偵測到移動就退出
        ///    這樣「ability 進行中可以邊走邊射」與「發射完玩家走動 = 離開瞄準」兩種行為都能滿足
        /// </summary>
        private void CheckAutoExitConditions()
        {
            if (!_autoExitOnInterruptStates) return;
            if (_ownerAsc == null) return;
            // 中斷狀態 → 立即退出
            if (_ownerAsc.OwnedTags.HasTag(GameplayTags.State.HitStunned)
                || _ownerAsc.OwnedTags.HasTag(GameplayTags.State.Dodging)
                || _ownerAsc.OwnedTags.HasTag(GameplayTags.State.Dead))
            {
                ExitAim();
                return;
            }
            // Persistent aim mode 偵測(沒 ability 進行中) → 移動就退出
            bool isInsideAimAbility = _ownerAsc.OwnedTags.HasTag(GameplayTags.State.Aiming);
            if (!isInsideAimAbility
                && _locomotionReader != null
                && (_locomotionReader.RawMove.magnitude > _persistentMovementExitThreshold
                    || _locomotionReader.JumpPressedThisFrame))
            {
                ExitAim();
            }
        }

        /// <summary>
        /// 進入瞄準模式 — 即使 IsAiming 已 true 也重發 Director 請求,
        /// 處理連續按鍵時鏡頭仍在 blend 中的競態 — 確保新一輪按鍵會把鏡頭強制拉回 aim
        /// </summary>
        public void EnterAim()
        {
            ReleaseTicket();
            CameraDirector director = CameraDirector.Instance;
            if (director != null)
            {
                _aimTicket = director.Request(CameraId.Aim);
            }
            IsAiming = true;
        }

        /// <summary>
        /// 退出瞄準模式 — 釋放 Director ticket，並同步隱藏準星等 aim UI
        /// </summary>
        public void ExitAim()
        {
            if (!IsAiming) return;
            IsAiming = false;
            ReleaseTicket();
            // 同步隱藏準星 / 收縮環 / AoE 指示器,避免 auto-exit 後 UI 殘留
            _aimUI?.HideAll();
        }

        private void ReleaseTicket()
        {
            if (_aimTicket == null) return;
            _aimTicket.Release();
            _aimTicket = null;
        }

        /// <summary>
        /// 更新相機 FOV 過渡（在 LateUpdate 中呼叫或由 GA_RangedAttack 驅動）
        /// </summary>
        public void UpdateFOVTransition()
        {
            if (_mainCamera == null) return;

            float targetFOV = IsAiming ? _aimFOV : _normalFOV;
            _currentFOV = Mathf.Lerp(_currentFOV, targetFOV, Time.deltaTime * _fovTransitionSpeed);

            if (_aimCamera != null && _aimCamera.Lens.FieldOfView != _currentFOV)
            {
                var lens = _aimCamera.Lens;
                lens.FieldOfView = _currentFOV;
                _aimCamera.Lens = lens;
            }
        }

        /// <summary>
        /// 取得瞄準方向（從螢幕中心射線）
        /// </summary>
        public Vector3 GetAimDirection(Vector3 fromPosition)
        {
            Vector3 hitPoint = GetAimHitPoint();
            Vector3 direction = (hitPoint - fromPosition).normalized;

            // 防止向下射擊角度過大
            if (direction.y < -0.8f)
            {
                direction.y = -0.8f;
                direction.Normalize();
            }

            return direction;
        }

        /// <summary>
        /// 取得瞄準命中點（螢幕中心射線與場景的交點）
        /// </summary>
        public Vector3 GetAimHitPoint()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            Vector3 screenCenter = new(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            Ray ray = _mainCamera.ScreenPointToRay(screenCenter);

            if (Physics.Raycast(ray, out RaycastHit hit, _aimRayMaxDistance, _aimRayLayers))
            {
                return hit.point;
            }

            // 沒有命中任何物體，返回射線終點
            return ray.GetPoint(_aimRayMaxDistance);
        }

        /// <summary>
        /// 取得地面瞄準點（用於 AoE 地面指示器）
        /// </summary>
        public bool TryGetGroundAimPoint(out Vector3 groundPoint, LayerMask groundMask)
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            Vector3 screenCenter = new(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            Ray ray = _mainCamera.ScreenPointToRay(screenCenter);

            if (Physics.Raycast(ray, out RaycastHit hit, _aimRayMaxDistance, groundMask))
            {
                groundPoint = hit.point;
                return true;
            }

            groundPoint = Vector3.zero;
            return false;
        }

        /// <summary>
        /// 設定瞄準相機的肩部偏移
        /// </summary>
        public void SetShoulderOffset(Vector3 offset)
        {
            if (_aimCamera == null) return;

            var thirdPerson = _aimCamera.GetComponent<CinemachineThirdPersonFollow>();
            if (thirdPerson != null)
            {
                thirdPerson.ShoulderOffset = offset;
            }
        }

        private void LateUpdate()
        {
            UpdateFOVTransition();
        }
    }
}
