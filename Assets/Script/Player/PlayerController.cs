using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float rotationSpeed = 10f;
    public Camera mainCamera;

    [Header("Lock-On Settings")]
    public float lockOnRange = 10f;
    public float nearPriorityRange = 4f;

    public LockOnManager lockOnManager; // Inspector 連結
    public StateMachine stateMachine;

    [HideInInspector] public PlayerIdleState idleState;
    [HideInInspector] public PlayerMoveState moveState;
    [HideInInspector] public PlayerAttackState attackState;
    public PlayerDodgeState dodgeState { get; private set; }

    public Animator animator; // 連到模型 Animator
    public Animator Animator => animator;

    public Vector2 MoveInput => PlayerInputHandler.Instance.MoveInput;
    public bool RunTriggered => PlayerInputHandler.Instance.RunTriggered;
    public bool AttackTriggered => PlayerInputHandler.Instance.AttackTriggered;
    public Transform LockOnTarget => (lockOnManager != null && lockOnManager.IsLocked)? lockOnManager.CurrentTarget: null;

    public bool CanRotate { get; set; } = true;


    // 右類比左右切換的最基礎參數（非鏡頭滑順）
    private float lookStickCooldown = 0.25f;  // 每次切換後冷卻（設 0 代表立即可再次切）
    private float lookStickTimer = 0f;
    private float lookThreshold = 0.5f;       // Stick 左右判定門檻

    private void Awake()
    {
        mainCamera = Camera.main;

        stateMachine = new StateMachine();
        idleState = new PlayerIdleState(this, stateMachine);
        moveState = new PlayerMoveState(this, stateMachine);
        attackState = new PlayerAttackState(this, stateMachine);
        dodgeState = new PlayerDodgeState(this, stateMachine);
    }

    private void Start()
    {
        stateMachine.Initialize(idleState);
    }

    private void Update()
    {
        // 互動
        if (PlayerInputHandler.Instance.InteractTriggered)
            InteractionManager.Instance.TryInteract();

        // 鎖定/解鎖切換
        if (PlayerInputHandler.Instance.LockOnTriggered)
            ToggleLockOn();

        // 右類比左右切換（在鎖定狀態下）
        lookStickTimer += Time.deltaTime;
        if (lockOnManager != null && lockOnManager.IsLocked && lookStickTimer >= lookStickCooldown)
        {
            float stickX = PlayerInputHandler.Instance.LookStick.x;
            if (stickX > lookThreshold)
            {
                var next = lockOnManager.SelectSiblingTarget(
                    toRight: true,
                    range: lockOnRange,
                    player: transform,
                    current: lockOnManager.CurrentTarget,
                    nearPriorityRangeLocal: nearPriorityRange
                );
                if (next != null) lockOnManager.SetTarget(next);
                lookStickTimer = 0f;
            }
            else if (stickX < -lookThreshold)
            {
                var next = lockOnManager.SelectSiblingTarget(
                    toRight: false,
                    range: lockOnRange,
                    player: transform,
                    current: lockOnManager.CurrentTarget,
                    nearPriorityRangeLocal: nearPriorityRange
                );
                if (next != null) lockOnManager.SetTarget(next);
                lookStickTimer = 0f;
            }
        }

        // 超距離保險 & 每幀維護（遮擋/指示器/回退）
        // 超距離保險 & 每幀維護（遮擋/指示器/回退）
        if (lockOnManager != null && lockOnManager.IsLocked)
        {
            var target = lockOnManager.CurrentTarget;   // 可能為 null（目標被 Destroy）
            if (target == null)
            {
                // 目標不在了：立刻清掉鎖定，避免後續再引用到 .position
                lockOnManager.ClearLockOn();
            }
            else
            {
                float distance = Vector3.Distance(transform.position, target.position);
                if (distance > lockOnRange * 1.5f)
                {
                    lockOnManager.ClearLockOn();
                }
                else
                {
                    lockOnManager.TickMaintain(lockOnRange, transform);
                }
            }
        }
        stateMachine.Update();
    }

    // 鎖定/解鎖（最簡切換，不做任何鏡頭預對齊等動作）
    public void ToggleLockOn()
    {
        if (lockOnManager == null) return;

        if (lockOnManager.IsLocked)
            lockOnManager.ClearLockOn();
        else
            lockOnManager.TryLockOn(lockOnRange, nearPriorityRange, transform);
    }

    private void OnDrawGizmos()
    {
        // 鎖定範圍
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, lockOnRange);
        // 近距離優先圈
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, nearPriorityRange);
    }
}
