using UnityEngine;

public class PlayerDodgeState : PlayerState
{
    private Vector3 dodgeDir = Vector3.zero;

    // 動畫參數
    private readonly string animName = "Dodge";   // Dodge clip / state 名稱
    private float exitTime = 0.11f;               // 允許被中斷/接下一動作的門檻
    private float endTime = 0.11f;               // 視為播完

    // 轉向 & 轉場細節
    private float rotateSpeed = 18f;
    private float crossFadeTime = 0f;          // 進 Dodge/攻擊時的固定轉場時間
    private float startOffsetNormalized = 0.08f;  // Dodge 從 8% 開始播，避開站立首幀


    public PlayerDodgeState(PlayerController player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        // Dodge消耗能量
        PlayerStamina.Instance.TryUseStamina(15);
        // 方向：有輸入用相機空間，否則沿角色面向
        Vector2 moveInput = player.MoveInput;
        Vector3 move = new Vector3(moveInput.x, 0f, moveInput.y);
        if (move.sqrMagnitude > 0.0001f)
        {
            move = Quaternion.Euler(0, player.mainCamera.transform.eulerAngles.y, 0) * move;
            dodgeDir = move.normalized;
        }
        else
        {
            dodgeDir = player.transform.forward;
        }


        // 鎖旋轉、清參數
        player.CanRotate = false;
        player.Animator.SetBool("isRun", false);
        player.Animator.SetFloat("VSpeed", 0f);

        // 以 CrossFade + 起始偏移進 Dodge，避免第一幀站立閃爍
        player.Animator.CrossFadeInFixedTime(animName, crossFadeTime, 0, startOffsetNormalized);
    }

    public override void Update()
    {
        // 朝向更新
        if (dodgeDir != Vector3.zero)
        {
            Quaternion t = Quaternion.LookRotation(dodgeDir);
            player.transform.rotation = Quaternion.Lerp(player.transform.rotation, t, rotateSpeed * Time.deltaTime);
        }

        // 讀取 Animator 狀態（同時考慮 Transition 中）
        var anim = player.Animator;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        var nxt = anim.GetNextAnimatorStateInfo(0);
        bool isTrans = anim.IsInTransition(0);

        bool curIsDodge = IsDodgeState(st);
        bool nextIsDodge = isTrans && IsDodgeState(nxt);
        bool inDodgeOrTransition = curIsDodge || nextIsDodge;

        // 取得 Dodge 的 normalizedTime（若在 transition，取對應那一側）
        float tNorm = curIsDodge ? st.normalizedTime : nxt.normalizedTime;

        // —— 只在 exitTime 之後接受動作；不做任何指令排隊／儲存 ——
        if (tNorm >= exitTime)
        {
            // 1) 立刻攻擊（不儲存）：避免被移動吃掉，先 CrossFade 再切狀態
            if (PlayerInputHandler.Instance.AttackTriggered)
            {
                anim.CrossFadeInFixedTime("Attack_1", crossFadeTime, 0, 0f);
                stateMachine.ChangeState(player.attackState);
                return;
            }

            // 2) 立刻再次 Dodge（不儲存）：就算在 Dodge→Move 的 transition 也會被搶回
            if (PlayerInputHandler.Instance.DodgeTriggered)
            {
                anim.CrossFadeInFixedTime(animName, crossFadeTime, 0, startOffsetNormalized);
                stateMachine.ChangeState(player.dodgeState);
                return;
            }
        }

        // 沒有按其他動作 → 讓動畫播放完才回到預設，避免插入一幀走路
        if (tNorm >= endTime)
        {
            if (player.MoveInput.magnitude > 0.1f)
                stateMachine.ChangeState(player.moveState);
            else
                stateMachine.ChangeState(player.idleState);
        }
    }

    private bool IsDodgeState(AnimatorStateInfo info)
    {
        // 建議在 Animator 的 Dodge 狀態加 Tag "Dodge"；或名字即 "Dodge"
        return info.IsName(animName) || info.tagHash == Animator.StringToHash("Dodge");
    }

    public override void Exit()
    {
        player.CanRotate = true;
    }
}
