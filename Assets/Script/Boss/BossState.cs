using UnityEngine;

public abstract class BossState
{
    protected BossController boss;
    protected BossStateMachine stateMachine;

    public BossState(BossController boss, BossStateMachine stateMachine)
    {
        this.boss = boss;
        this.stateMachine = stateMachine;
    }

    public virtual void Enter() { }
    public virtual void LogicUpdate() { }
    public virtual void Exit() { }
}

