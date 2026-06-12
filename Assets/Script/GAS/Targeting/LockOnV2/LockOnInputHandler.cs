using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Player.Input;

namespace GAS.Targeting.LockOnV2
{
    /// <summary>
    /// 鎖定輸入處理器 — 將 Input System 事件轉為 LockOnController API 呼叫
    /// 按鍵:ToggleBestLock (鎖定/解除) 事件訂閱;搖桿:每幀讀值,用 hysteresis + 冷卻避免連續觸發
    /// 注意:若場景仍掛著舊的 AbilityInputHandler 且其 LockOnAction 也綁同一個 InputActionReference,
    ///      按一次會同時觸發兩邊。測試 V2 時請把 AbilityInputHandler 的 LockOnAction 欄位清空或停用該元件
    /// </summary>
    [DisallowMultipleComponent]
    public class LockOnInputHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        [Tooltip("LockOn 控制器;留空則自父物件搜尋")]
        private LockOnController _controller;

        [Header("Input Actions")]
        [SerializeField]
        [Tooltip("鎖定 / 解除切換 (預期綁 R 鍵 / 右搖桿按下)")]
        private InputActionReference _toggleLockAction;

        [SerializeField]
        [Tooltip("方向切換搖桿 Value Vector2 (預期綁右搖桿/LookStick);鎖定中撥動觸發 TryLockDirectional")]
        private InputActionReference _switchStickAction;

        [Header("Directional Switch")]
        [SerializeField, Range(0.1f, 0.95f)]
        [Tooltip("搖桿觸發閾值 — 向量長度超過此值才算「撥動」")]
        private float _stickTriggerThreshold = 0.7f;

        [SerializeField, Range(0.05f, 0.9f)]
        [Tooltip("搖桿回中閾值 — 向量長度低於此值才視為「回中」;須小於觸發閾值")]
        private float _stickReleaseThreshold = 0.3f;

        [SerializeField]
        [Tooltip("連續兩次方向切換的最小間隔 (秒);避免搖桿停在對角抖動造成高速切換")]
        private float _switchCooldown = 0.2f;

        [Header("Gate")]
        [SerializeField]
        [Tooltip("是否受 PlayerInputHandler.IsPlayerInputEnabled 限制 (UI 開啟時不響應)")]
        private bool _respectUIGate = true;

        [SerializeField]
        [Tooltip("輸出觸發除錯訊息")]
        private bool _verboseLog = false;

        private bool _stickEngaged;
        private float _lastSwitchTime;
        private Action<InputAction.CallbackContext> _toggleHandler;

        private void Awake()
        {
            if (_controller == null) _controller = GetComponentInParent<LockOnController>();
        }

        private void OnEnable()
        {
            if (_toggleLockAction != null && _toggleLockAction.action != null)
            {
                _toggleHandler = OnToggleLock;
                _toggleLockAction.action.started += _toggleHandler;
                _toggleLockAction.action.Enable();
            }
            if (_switchStickAction != null && _switchStickAction.action != null)
            {
                _switchStickAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (_toggleLockAction != null && _toggleLockAction.action != null && _toggleHandler != null)
            {
                _toggleLockAction.action.started -= _toggleHandler;
            }
            _toggleHandler = null;
            _stickEngaged = false;
        }

        private void Update()
        {
            TickDirectionalSwitch();
        }

        private void OnToggleLock(InputAction.CallbackContext ctx)
        {
            if (_controller == null) return;
            if (!IsInputAllowed()) return;
            bool nowLocked = _controller.ToggleBestLock();
            if (_verboseLog) Debug.Log($"[LockOnV2 Input] ToggleBestLock => locked={nowLocked}");
        }

        private void TickDirectionalSwitch()
        {
            if (_controller == null || !_controller.IsLocked) return;
            if (_switchStickAction == null || _switchStickAction.action == null) return;
            if (!IsInputAllowed())
            {
                _stickEngaged = false;
                return;
            }
            Vector2 stick = _switchStickAction.action.ReadValue<Vector2>();
            float mag = stick.magnitude;
            if (_stickEngaged)
            {
                if (mag < _stickReleaseThreshold) _stickEngaged = false;
                return;
            }
            if (mag < _stickTriggerThreshold) return;
            if (Time.unscaledTime - _lastSwitchTime < _switchCooldown) return;
            Vector2 dir = stick / Mathf.Max(mag, 0.0001f);
            bool ok = _controller.TryLockDirectional(dir);
            _stickEngaged = true;
            _lastSwitchTime = Time.unscaledTime;
            if (_verboseLog) Debug.Log($"[LockOnV2 Input] TryLockDirectional({dir:F2}) => {ok}");
        }

        private bool IsInputAllowed()
        {
            if (!_respectUIGate) return true;
            if (SystemInputReader.Instance == null) return true;
            return SystemInputReader.Instance.IsPlayerInputEnabled;
        }
    }
}
