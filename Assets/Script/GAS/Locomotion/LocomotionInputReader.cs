using UnityEngine;
using UnityEngine.InputSystem;

namespace Player.Locomotion
{
    /// <summary>
    /// 移動系統輸入層：透過 InputActionAsset 綁定 Move / Run Action。
    /// 不依賴任何舊的 PlayerInputHandler 邏輯，保持 asmdef 純粹只依賴 Unity.InputSystem。
    /// </summary>
    public sealed class LocomotionInputReader : MonoBehaviour
    {
        [SerializeField, Tooltip("拖入 Assets/Input/PlayerControls.inputactions")]
        private InputActionAsset _inputActions;
        [SerializeField, Tooltip("Action Map 名稱")] private string _actionMapName = "Player";
        [SerializeField] private string _moveActionName = "Move";
        [SerializeField] private string _runActionName = "Run";
        [SerializeField] private string _jumpActionName = "Jump";
        [SerializeField] private string _dodgeActionName = "Dodge";

        private InputAction _moveAction;
        private InputAction _runAction;
        private InputAction _jumpAction;
        private InputAction _dodgeAction;

        public Vector2 RawMove { get; private set; }
        public bool RunHeld { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool JumpPressedThisFrame { get; private set; }
        public bool DodgePressedThisFrame { get; private set; }

        private void Awake()
        {
            if (_inputActions == null)
            {
                Debug.LogError("[LocomotionInputReader] InputActionAsset 未指定。", this);
                return;
            }
            InputActionMap map = _inputActions.FindActionMap(_actionMapName, throwIfNotFound: true);
            _moveAction = map.FindAction(_moveActionName, throwIfNotFound: true);
            _runAction = map.FindAction(_runActionName, throwIfNotFound: true);
            _jumpAction = map.FindAction(_jumpActionName, throwIfNotFound: false);
            if (_jumpAction == null)
            {
                Debug.LogWarning($"[LocomotionInputReader] 未於 Action Map '{_actionMapName}' 找到 '{_jumpActionName}' action，跳躍輸入將失效。", this);
            }
            _dodgeAction = map.FindAction(_dodgeActionName, throwIfNotFound: false);
            if (_dodgeAction == null)
            {
                Debug.LogWarning($"[LocomotionInputReader] 未於 Action Map '{_actionMapName}' 找到 '{_dodgeActionName}' action，閃避輸入將失效。", this);
            }
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _runAction?.Enable();
            _jumpAction?.Enable();
            _dodgeAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _runAction?.Disable();
            _jumpAction?.Disable();
            _dodgeAction?.Disable();
        }

        private void Update()
        {
            if (_moveAction == null || _runAction == null)
            {
                return;
            }
            RawMove = _moveAction.ReadValue<Vector2>();
            RunHeld = _runAction.IsPressed();
            if (_jumpAction != null)
            {
                JumpHeld = _jumpAction.IsPressed();
                JumpPressedThisFrame = _jumpAction.WasPressedThisFrame();
            }
            else
            {
                JumpHeld = false;
                JumpPressedThisFrame = false;
            }
            DodgePressedThisFrame = _dodgeAction != null && _dodgeAction.WasPressedThisFrame();
        }
    }
}
