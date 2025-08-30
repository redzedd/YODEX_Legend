using UnityEngine;

public class BossAlertState : BossState
{
    private float alertDuration = 1f; // 發現動畫秒數
    private float timer = 0f;

    public BossAlertState(BossController boss, BossStateMachine stateMachine) : base(boss, stateMachine) { }

    public override void Enter()
    {
        //boss.animator.SetTrigger("Roar");
        timer = 0f;
    }

    public override void LogicUpdate()
    {
        timer += Time.deltaTime;
        if (timer >= alertDuration)
        {
            stateMachine.ChangeState(boss.chaseState);
        }
    }
}

