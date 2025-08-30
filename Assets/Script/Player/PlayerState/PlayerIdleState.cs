using UnityEditor;
using UnityEngine;

public class PlayerIdleState : PlayerState
{
    public PlayerIdleState(PlayerController player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {    
        player.Animator.SetBool("isRun", false);
    }


    public override void Update()
    {
        // ⭐ Dodge 優先於攻擊與移動速度計算（你也可以調整優先權）
        if (PlayerInputHandler.Instance.DodgeTriggered)
        {
            if (PlayerStamina.Instance.HasStamina(15))
            {
                stateMachine.ChangeState(player.dodgeState);
                return;
            }
            else
            {
                // 體力不足 → 不做事
            }
        }
        float currentSpeed = player.Animator.GetFloat("VSpeed");
        float newSpeed = Mathf.Lerp(currentSpeed, 0f, 12.8f * Time.deltaTime); // 5f 是速度，可以調

        // 如果剛剛是煞車完成後回 Idle，就重設速度
        if (player.Animator.GetCurrentAnimatorStateInfo(0).IsName("Run_End"))
        {
            player.Animator.SetFloat("VSpeed", 0f); // 強制清零，防止回彈
        }
        else
        {
            player.Animator.SetFloat("VSpeed", newSpeed);
        }

        if (player.MoveInput.magnitude > 0.1f)
        {
            stateMachine.ChangeState(player.moveState); // 轉入 MoveState，但讓動畫先播
        }

        else if (player.AttackTriggered)
        {
            stateMachine.ChangeState(player.attackState);
        }
    }
}

