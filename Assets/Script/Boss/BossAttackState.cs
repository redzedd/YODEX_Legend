using UnityEngine;

public class BossAttackState : BossState
{
    public BossAttackState(BossController boss, BossStateMachine stateMachine) : base(boss, stateMachine) { }

    public override void Enter()
    {
        // 隨機選一個攻擊資料
        int index = Random.Range(0, boss.attackSet.attacks.Length);
        boss.currentAttackData = boss.attackSet.attacks[index];
        // ✅ 直接播放指定動畫名稱
        boss.animator.Play(boss.currentAttackData.animationName);
        boss.lastAttackTime = Time.time;

        // 可加面向修正
        //boss.FaceTarget();
    }

    public override void LogicUpdate()
    {
        AnimatorStateInfo stateInfo = boss.animator.GetCurrentAnimatorStateInfo(0);
        float progress = stateInfo.normalizedTime % 1f;

        // 是否正在撥放此招式動畫？
        if (stateInfo.IsName(boss.currentAttackData.animationName))
        {
            //float progress = stateInfo.normalizedTime % 1f;

            // ✅ 只在允許區間內才旋轉
            if (progress >= boss.currentAttackData.canTurnStart && progress <= boss.currentAttackData.canTurnEnd)
            {
                boss.FaceTarget();
            }
        }

        // ✅ 攻擊動畫播放結束
        if (progress >= 0.9f)
        {
            float distance = Vector3.Distance(boss.transform.position, boss.target.position);

            // ✅ 玩家離開 → 結束攻擊狀態
            if (distance > boss.chaseRange)
                stateMachine.ChangeState(boss.idleState);
            else
                stateMachine.ChangeState(boss.chaseState);
        }
    }
}

