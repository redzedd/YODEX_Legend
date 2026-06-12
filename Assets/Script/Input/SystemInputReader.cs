using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player.Input
{
    /// <summary>
    /// 系統輸入讀取器 — 處理移動、戰鬥之外的所有玩家輸入。
    /// 涵蓋:背包開關、UI 導航(Cancel / Page)、物品使用、ActionMap 切換等。
    /// 自動單例:由 RuntimeInitializeOnLoadMethod 建立,場景與 Prefab 不需要任何掛載。
    /// </summary>
    public sealed class SystemInputReader : MonoBehaviour
    {
        public static SystemInputReader Instance { get; private set; }

        public enum InventoryToggleIntent { None, Open, Close }

        // === Triggered 旗標(LateUpdate 清空) ===
        public bool OpenInventoryTriggered { get; private set; }
        public bool CloseInventoryTriggered { get; private set; }
        public bool UseItemTriggered { get; private set; }
        public bool CancelTriggered { get; private set; }
        public bool NextPageTriggered { get; private set; }
        public bool PrevPageTriggered { get; private set; }

        [Header("Inventory Toggle")]
        [Tooltip("背包開關防抖間隔(秒)")]
        public float InventoryDebounce = 0.12f;

        private PlayerControls _controls;
        private float _nextAllowedToggleAtUnscaled;
        private float _blockOpenInventoryUntilUnscaled;
        private float _blockCancelUntilUnscaled;
        private Coroutine _deferredEnableRoutine;

        /// <summary>
        /// 玩家 Action Map 是否啟用 — 用來判斷玩家是否能下達戰鬥/移動/UI 指令。
        /// UI 開啟、過場、死亡等場景應呼叫 <see cref="DisablePlayerInput"/> 暫時停用。
        /// </summary>
        public bool IsPlayerInputEnabled => _controls != null && _controls.Player.enabled;

        // 在所有場景物件 Awake 之前就緒,確保場景物件 Start() 能安全取得 Instance
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void AutoCreate()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("[SystemInputReader]");
            DontDestroyOnLoad(go);
            go.AddComponent<SystemInputReader>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _controls = new PlayerControls();
            _controls.Enable();
            // 與 PlayerInputHandler 對齊的初始狀態:UIMap 預設關閉,Player 預設啟用
            _controls.UIMap.Disable();

            _controls.Player.OpenInventory.performed += OnOpenInventory;
            _controls.Player.UseItem.performed += OnUseItem;

            _controls.UIMap.CloseInventory.performed += OnCloseInventory;
            _controls.UIMap.NextPage.performed += OnNextPage;
            _controls.UIMap.PrevPage.performed += OnPrevPage;
            _controls.UIMap.Cancel.performed += OnCancel;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_controls == null) return;
            _controls.Player.OpenInventory.performed -= OnOpenInventory;
            _controls.Player.UseItem.performed -= OnUseItem;
            _controls.UIMap.CloseInventory.performed -= OnCloseInventory;
            _controls.UIMap.NextPage.performed -= OnNextPage;
            _controls.UIMap.PrevPage.performed -= OnPrevPage;
            _controls.UIMap.Cancel.performed -= OnCancel;
            _controls.Disable();
            _controls.Dispose();
        }

        private void OnEnable() => _controls?.Enable();
        private void OnDisable() => _controls?.Disable();

        private void LateUpdate()
        {
            OpenInventoryTriggered = false;
            CloseInventoryTriggered = false;
            UseItemTriggered = false;
            CancelTriggered = false;
            NextPageTriggered = false;
            PrevPageTriggered = false;
        }

        // === Input Callbacks ===

        private void OnOpenInventory(InputAction.CallbackContext _)
        {
            if (Time.unscaledTime < _blockOpenInventoryUntilUnscaled) return;
            OpenInventoryTriggered = true;
        }
        private void OnCloseInventory(InputAction.CallbackContext _) => CloseInventoryTriggered = true;
        private void OnUseItem(InputAction.CallbackContext _) => UseItemTriggered = true;
        private void OnNextPage(InputAction.CallbackContext _) => NextPageTriggered = true;
        private void OnPrevPage(InputAction.CallbackContext _) => PrevPageTriggered = true;
        private void OnCancel(InputAction.CallbackContext _)
        {
            // 封鎖期間略過 — 開 UI 瞬間若同一個物理鍵被按住,
            // UIMap 啟用會立刻 fire Cancel performed,造成「剛開就被關」。
            if (Time.unscaledTime < _blockCancelUntilUnscaled) return;
            CancelTriggered = true;
        }

        // === 背包去抖 / 開關邏輯 ===

        /// <summary>
        /// 暫時封鎖開背包鍵指定秒數 — NewItemDisplayUI 拾取展示動畫期間呼叫,
        /// 避免玩家剛拾取就立刻按 I 開背包導致 UI 競爭。
        /// </summary>
        public void BlockOpenInventoryFor(float seconds)
        {
            _blockOpenInventoryUntilUnscaled = Mathf.Max(_blockOpenInventoryUntilUnscaled, Time.unscaledTime + seconds);
        }

        /// <summary>
        /// 暫時封鎖 Cancel 鍵指定秒數 — 開啟 UI 時呼叫,
        /// 避免互動鍵與 Cancel 鍵共用同一個物理鍵時,UIMap 啟用瞬間被當成「立即關閉」。
        /// </summary>
        public void BlockCancelFor(float seconds)
        {
            _blockCancelUntilUnscaled = Mathf.Max(_blockCancelUntilUnscaled, Time.unscaledTime + seconds);
        }

        public bool TryToggleInventory(bool isInventoryOpen, out InventoryToggleIntent intent)
        {
            intent = InventoryToggleIntent.None;

            bool wantOpen = OpenInventoryTriggered;
            bool wantClose = CloseInventoryTriggered;

            if (Time.unscaledTime < _blockOpenInventoryUntilUnscaled && wantOpen) return false;
            if (Time.unscaledTime < _nextAllowedToggleAtUnscaled && (wantOpen || wantClose)) return false;

            if (!isInventoryOpen && wantOpen) intent = InventoryToggleIntent.Open;
            else if (isInventoryOpen && (wantClose || CancelTriggered)) intent = InventoryToggleIntent.Close;
            else return false;

            _nextAllowedToggleAtUnscaled = Time.unscaledTime + InventoryDebounce;
            return true;
        }

        // === ActionMap 切換 ===
        // 完全自管,不再依賴 PlayerInputHandler。場景無需掛載 PlayerInputHandler 也能正常運作。

        public void EnablePlayerInput() => _controls?.Player.Enable();
        public void DisablePlayerInput() => _controls?.Player.Disable();
        public void EnableUIMapInput() => _controls?.UIMap.Enable();
        public void DisableUIMapInput() => _controls?.UIMap.Disable();

        /// <summary>
        /// 延後啟用 Player ActionMap — 等到玩家放開所有按鍵後才實際啟用。
        /// 解決 Unity Input System 的 carry-over:若 ActionMap 啟用瞬間有控制項仍被按住,
        /// 該動作會立即 fire performed。例如關 UI 的按鍵也是 Jump/Interact 鍵時,
        /// 立刻 EnablePlayerInput 會讓角色跳起來或重新觸發互動。
        /// maxWaitUnscaled: 即使按鍵沒放開也最多等這麼久 (避免卡死)。
        /// </summary>
        public void EnablePlayerInputDeferred(float maxWaitUnscaled = 0.5f)
        {
            if (_deferredEnableRoutine != null) StopCoroutine(_deferredEnableRoutine);
            _deferredEnableRoutine = StartCoroutine(EnablePlayerInputDeferredRoutine(maxWaitUnscaled));
        }

        private IEnumerator EnablePlayerInputDeferredRoutine(float maxWait)
        {
            float t = 0f;
            while (t < maxWait && IsAnyButtonHeld())
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            // 多等一幀讓 Input System 處理 release 事件
            yield return null;
            _controls?.Player.Enable();
            _deferredEnableRoutine = null;
        }

        private static bool IsAnyButtonHeld()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.isPressed) return true;
            if (Gamepad.current != null)
            {
                Gamepad gp = Gamepad.current;
                if (gp.buttonSouth.isPressed || gp.buttonNorth.isPressed
                    || gp.buttonEast.isPressed || gp.buttonWest.isPressed
                    || gp.startButton.isPressed || gp.selectButton.isPressed
                    || gp.leftShoulder.isPressed || gp.rightShoulder.isPressed) return true;
            }
            return false;
        }

        public void ResetTriggeredFlags()
        {
            OpenInventoryTriggered = false;
            CloseInventoryTriggered = false;
            UseItemTriggered = false;
            CancelTriggered = false;
            NextPageTriggered = false;
            PrevPageTriggered = false;
        }
    }
}
