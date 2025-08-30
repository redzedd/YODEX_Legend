using UnityEngine;

public abstract class PlayerState
{
    protected PlayerController player;
    protected StateMachine stateMachine;

    public PlayerState(PlayerController player, StateMachine stateMachine)
    {
        this.player = player;
        this.stateMachine = stateMachine;
    }

    public virtual void Enter() { }
    public virtual void Update() { }
    public virtual void Exit() { }
}

