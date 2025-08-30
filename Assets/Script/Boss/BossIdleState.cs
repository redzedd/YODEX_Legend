using UnityEngine;

public class BossIdleState : BossState
{
    public BossIdleState(BossController boss, BossStateMachine stateMachine) : base(boss, stateMachine) { }

    private float idleTimer = 0f;
    private float maxIdleTime = 5f;

    public override void LogicUpdate()
    {
        float distance = Vector3.Distance(boss.transform.position, boss.target.position);

        if (distance < boss.detectRange && distance > boss.tooCloseDistance)
        {
            stateMachine.ChangeState(boss.chaseState);
            return;
        }

        if (boss.attackDecider.TryAttack())
        {
            stateMachine.ChangeState(boss.attackState);
            return;
        }

        idleTimer += Time.deltaTime;
        if (idleTimer > maxIdleTime)
        {
            idleTimer = 0f;
            stateMachine.ChangeState(boss.patrolState); // ✨ 進入巡邏狀態
        }
    }
}

