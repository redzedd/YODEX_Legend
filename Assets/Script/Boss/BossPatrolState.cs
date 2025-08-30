using UnityEngine;

public class BossPatrolState : BossState
{
    private int currentPatrolIndex = 0;

    public BossPatrolState(BossController boss, BossStateMachine stateMachine) : base(boss, stateMachine) { }

    public override void Enter()
    {
        boss.animator.SetBool("IsMoving", true);
        MoveToNextPoint();
    }

    public override void LogicUpdate()
    {
        float distanceToTarget = Vector3.Distance(boss.transform.position, boss.target.position);

        // 若玩家進入範圍，轉追擊
        if (distanceToTarget < boss.detectRange && distanceToTarget > boss.tooCloseDistance)
        {
            stateMachine.ChangeState(boss.chaseState);
            return;
        }

        if (!boss.agent.pathPending && boss.agent.remainingDistance < 0.5f)
        {
            currentPatrolIndex = (currentPatrolIndex + 1) % boss.patrolPoints.Length;
            MoveToNextPoint();
        }

        boss.FaceMovementDirection();
    }

    public override void Exit()
    {
        boss.animator.SetBool("IsMoving", false);
    }

    private void MoveToNextPoint()
    {
        if (boss.patrolPoints.Length == 0) return;
        boss.agent.SetDestination(boss.patrolPoints[currentPatrolIndex].position);
    }
}
