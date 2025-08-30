using UnityEngine;

public class BossStateMachine
{
    public BossState CurrentState { get; private set; }

    public void ChangeState(BossState newState)
    {
        CurrentState?.Exit();
        CurrentState = newState;
        CurrentState.Enter();
    }

    public void Update()
    {
        CurrentState?.LogicUpdate();
    }
}

