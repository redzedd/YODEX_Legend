using Unity.VisualScripting;
using UnityEngine;

public class BossChaseState : BossState
{
    public BossChaseState(BossController boss, BossStateMachine stateMachine) : base(boss, stateMachine) { }

    private float rotateDelay = 0.5f;
    private float timer = 0f;

    public override void Enter()
    {
        float distance = Vector3.Distance(boss.transform.position, boss.target.position);
        // ✅ 如果一進來就太近，立刻切換為 Idle，避免多餘 RootMotion 滑步
        if (distance < boss.tooCloseDistance)
        {
            stateMachine.ChangeState(boss.idleState);
            return;
        }

        boss.animator.SetBool("IsMoving", true);
        timer = 0f; // ✅ 重設旋轉延遲計時器
    }

    public override void LogicUpdate()
    {
        timer += Time.deltaTime;
        float distance = Vector3.Distance(boss.transform.position, boss.target.position);

        // 如果太遠就放棄追擊
        if (distance > boss.chaseRange)
        {
            boss.attackDecider.Reset(); // 離開時重設等待
            stateMachine.ChangeState(boss.idleState);
            return;
        }
        // ✅ 通用攻擊偵測
        if (boss.attackDecider.TryAttack())
        {
            stateMachine.ChangeState(boss.attackState);
            return;
        }

        // ✅ 如果太近就停止
        if (distance < boss.tooCloseDistance)
        {
            stateMachine.ChangeState(boss.idleState);
            return;
        }

        // 使用 NavMesh 導航方向決定面向（混用 root motion）
        boss.agent.SetDestination(boss.target.position);

        if (timer >= rotateDelay) // ✅ 過0.5秒才允許轉向
        {
            Vector3 dir = boss.agent.steeringTarget - boss.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero)
            {
                Quaternion lookRot = Quaternion.LookRotation(dir);
                boss.transform.rotation = Quaternion.Slerp(boss.transform.rotation, lookRot, Time.deltaTime * 1f);
            }
        }
    }

    public override void Exit()
    {
        boss.animator.SetBool("IsMoving", false);
    }
}


