using UnityEngine;

public class PlayerInputHandler : MonoBehaviour
{
    public static PlayerInputHandler Instance { get; private set; }

    private PlayerControls controls;

    // 連續值
    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    public Vector2 LookStick { get; private set; }
    public bool RunTriggered => controls != null && controls.Player.Run.IsPressed();

    // 單發觸發（LateUpdate 清）
    public bool JumpTriggered { get; private set; }
    public bool AttackTriggered { get; private set; }
    public bool LockOnTriggered { get; private set; }
    public bool DodgeTriggered { get; private set; }
    public bool InteractTriggered { get; private set; }
    public bool UseItemTriggered { get; private set; }
    public bool OpenInventoryTriggered { get; private set; }
    public bool CloseInventoryTriggered { get; private set; }
    public bool CancelTriggered { get; private set; }
    public bool NextPageTriggered { get; private set; }
    public bool PrevPageTriggered { get; private set; }

    // —— 開背包防抖 / 封鎖（用未縮放時間）——
    [Header("Inventory Toggle")]
    [Tooltip("背包開關的防抖秒數")]
    public float inventoryDebounce = 0.12f;
    private float nextAllowedToggleAtUnscaled = 0f;

    private float blockOpenInventoryUntilUnscaled = 0f; // 由彈窗關閉時短暫封鎖
    public void BlockOpenInventoryFor(float seconds)
    {
        blockOpenInventoryUntilUnscaled = Mathf.Max(blockOpenInventoryUntilUnscaled, Time.unscaledTime + seconds);
        Debug.Log($"[Input] BlockOpenInventoryFor {seconds:0.###}s → until {blockOpenInventoryUntilUnscaled:0.000}");
    }

    public enum InventoryToggleIntent { None, Open, Close }

    /// <summary>
    /// 用來集中判斷是否該「開/關背包」。會處理防抖與封鎖。
    /// 呼叫處：InventoryDisplay.Update()
    /// </summary>
    public bool TryToggleInventory(bool isInventoryOpen, out InventoryToggleIntent intent)
    {
        intent = InventoryToggleIntent.None;

        // 先讀當幀觸發
        bool wantOpen = OpenInventoryTriggered;
        bool wantClose = CloseInventoryTriggered;

        // 若剛關彈窗 → 於封鎖期間忽略「開背包」
        if (Time.unscaledTime < blockOpenInventoryUntilUnscaled && wantOpen)
        {
            Debug.Log("[Input] TryToggleInventory: blocked by BlockOpenInventoryUntil");
            return false;
        }

        // 防抖：距離上次允許時間太近就不處理
        if (Time.unscaledTime < nextAllowedToggleAtUnscaled && (wantOpen || wantClose))
        {
            Debug.Log($"[Input] TryToggleInventory: debounced ({nextAllowedToggleAtUnscaled - Time.unscaledTime:0.000}s)");
            return false;
        }

        if (!isInventoryOpen && wantOpen)
        {
            intent = InventoryToggleIntent.Open;
        }
        else if (isInventoryOpen && (wantClose || CancelTriggered))
        {
            intent = InventoryToggleIntent.Close;
        }
        else
        {
            return false; // 沒有符合開/關條件
        }

        // 成功決定一次 → 設定下次允許時間
        nextAllowedToggleAtUnscaled = Time.unscaledTime + inventoryDebounce;
        Debug.Log($"[Input] TryToggleInventory: intent={intent}, nextAllowed={nextAllowedToggleAtUnscaled:0.000}");
        return true;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        controls = new PlayerControls();
        controls.Enable();
        controls.Player.Enable();
        controls.UIMap.Disable();

        // Player map
        controls.Player.Move.performed += _ => { };
        controls.Player.Move.canceled += _ => { };
        controls.Player.Look.performed += _ => { };
        controls.Player.Look.canceled += _ => { };

        controls.Player.LookStick.performed += ctx => LookStick = ctx.ReadValue<Vector2>();
        controls.Player.LookStick.canceled += _ => LookStick = Vector2.zero;

        controls.Player.Jump.performed += _ => JumpTriggered = true;
        controls.Player.Attack.performed += _ => AttackTriggered = true;
        controls.Player.LockOn.performed += _ => LockOnTriggered = true;
        controls.Player.Dodge.performed += _ => DodgeTriggered = true;
        controls.Player.Interact.performed += _ => InteractTriggered = true;
        controls.Player.UseItem.performed += _ => UseItemTriggered = true;

        // 開背包（受封鎖影響）
        controls.Player.OpenInventory.performed += _ =>
        {
            if (Time.unscaledTime < blockOpenInventoryUntilUnscaled)
            {
                Debug.Log("[Input] OpenInventory performed but blocked");
                return;
            }
            OpenInventoryTriggered = true;
            Debug.Log("[Input] OpenInventory performed");
        };

        // UI map
        controls.UIMap.CloseInventory.performed += _ => { CloseInventoryTriggered = true; Debug.Log("[Input] CloseInventory performed"); };
        controls.UIMap.NextPage.performed += _ => NextPageTriggered = true;
        controls.UIMap.PrevPage.performed += _ => PrevPageTriggered = true;
        controls.UIMap.Cancel.performed += _ => CancelTriggered = true;
    }

    private void OnEnable() { if (controls != null) controls.Enable(); }
    private void OnDisable() { if (controls != null) controls.Disable(); }

    private void Update()
    {
        if (controls != null)
        {
            MoveInput = controls.Player.Move.ReadValue<Vector2>();
            LookInput = controls.Player.Look.ReadValue<Vector2>();
        }
    }

    private void LateUpdate()
    {
        // 單發重置
        JumpTriggered = AttackTriggered = LockOnTriggered = DodgeTriggered = InteractTriggered = UseItemTriggered = false;
        OpenInventoryTriggered = CloseInventoryTriggered = CancelTriggered = false;
        NextPageTriggered = PrevPageTriggered = false;
    }

    // Map 切換（背包開關時會用到）
    public void DisablePlayerInput() { if (controls == null) return; controls.Player.Disable(); }
    public void EnablePlayerInput() { if (controls == null) return; controls.Player.Enable(); }
    public void EnableUIMapInput() { if (controls == null) return; controls.UIMap.Enable(); }
    public void DisableUIMapInput() { if (controls == null) return; controls.UIMap.Disable(); }
}
