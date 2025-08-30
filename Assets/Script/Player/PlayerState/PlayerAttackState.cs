using UnityEngine;

public class PlayerAttackState : PlayerState
{
    private int comboIndex = 0;
    private int MaxCombo = 6; // 最多幾段攻擊後重新循環
    private float comboTimer = 0f;
    private float comboInputWindow = 0.5f; // 每段攻擊允許輸入下一段的時間（動畫長度的一部分）

    private bool bufferedInput = false;
    private float inputBufferTimer = 0.2f; // 可接受輸入的提前緩衝時間
    private float maxBufferDuration = 0.3f; // 緩衝最多保留秒數
    private float inputWindowStart = 0.1f; // 允許輸入的起始時間（normalizedTime）
    private float inputWindowEnd = 0.95f; // 允許輸入的結束時間
    private float comboTiming = 0.55f; // 招式Combo可馬上切換的時間
    private bool receivedNextAttackInput = false;
    private bool isEnding = false;

    public PlayerAttackState(PlayerController player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.Animator.SetFloat("VSpeed", 0f);
        comboIndex = 1;
        PlayComboAnimation(comboIndex);
        comboTimer = 0f;
        receivedNextAttackInput = false;
        player.Animator.SetBool("IsAttacking", true);
        player.CanRotate = false;
        isEnding = false;
    }

    public override void Update()
    {
        comboTimer += Time.deltaTime;

        // 接收玩家連擊輸入
        if (PlayerInputHandler.Instance.AttackTriggered)
        {
            if (comboTimer > 0.2f && comboTimer < comboInputWindow)
            {
                receivedNextAttackInput = true;
            }
        }

        // 取得動畫狀態進度
        AnimatorStateInfo stateInfo = player.Animator.GetCurrentAnimatorStateInfo(0);
        float normalizedTime = stateInfo.normalizedTime;

        // 允許輸入區間中接受攻擊輸入，並預存
        if (PlayerInputHandler.Instance.AttackTriggered)
        {
            if (normalizedTime >= inputWindowStart && normalizedTime <= inputWindowEnd)
            {
                receivedNextAttackInput = true;
                //Debug.Log($"✅ Combo input accepted IN window");
            }
            else
            {
                bufferedInput = true;
                inputBufferTimer = 0f;
                //Debug.Log($"📥 Input buffered BEFORE window");
            }
        }

        if (bufferedInput)
        {
            inputBufferTimer += Time.deltaTime;

            // 如果進入合法輸入時間，立即觸發連段
            if (normalizedTime >= inputWindowStart && normalizedTime <= inputWindowEnd)
            {
                receivedNextAttackInput = true;
                bufferedInput = false;
                //Debug.Log($"💥 Buffered input executed in window!");
            }
            else if (inputBufferTimer > maxBufferDuration)
            {
                bufferedInput = false; // 超時，取消緩衝
                //Debug.Log($"⌛ Buffered input expired");
            }
        }

        if (!isEnding)
        {
            if (normalizedTime >= comboTiming)
            {
                if (receivedNextAttackInput && comboIndex <= MaxCombo)
                {
                    comboIndex++;
                    if (comboIndex > MaxCombo)
                        comboIndex = 1; // ⭐ 超過最大段數時回到第一段
                    PlayComboAnimation(comboIndex);
                    comboTimer = 0f;
                    receivedNextAttackInput = false;
                    bufferedInput = false;
                    inputBufferTimer = 0f;
                    Debug.Log($"▶️ Combo → Attack_{comboIndex:D2}");
                }
                // 否則沒輸入才結束
                else if (comboIndex >= MaxCombo || (normalizedTime > inputWindowEnd && !receivedNextAttackInput))
                {
                    isEnding = true; // ⭐ 無論是最後段 或沒輸入 → 進入結尾
                    receivedNextAttackInput = false;
                    bufferedInput = false;
                }
            }
        }
        else
        {
            // ⭐ 等待收尾動畫再切狀態
            if (stateInfo.IsName($"Attack_0{comboIndex}_End") && stateInfo.normalizedTime > 0.0f)
            {
                player.Animator.SetBool("IsAttacking", false);
                stateMachine.ChangeState(player.idleState);
            }
        }
    }

    private void PlayComboAnimation(int index)
    {
        string animName = $"Attack_{index.ToString("D2")}";

        if (player.LockOnTarget != null)
        {
            player.CanRotate = true;

            Vector3 direction = (player.LockOnTarget.position - player.transform.position).normalized;
            direction.y = 0f;

            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                player.transform.rotation = targetRotation;
            }
        }
        else
        {
            player.CanRotate = false;
        }

        player.Animator.Play(animName, 0, 0f); // 馬上播放，不等待轉場
        //Debug.Log($"▶️ Combo Attack_{index}");
    }

    public override void Exit()
    {
        comboIndex = 0;
        comboTimer = 0f;
        receivedNextAttackInput = false;
        player.Animator.SetBool("IsAttacking", false);
        player.CanRotate = true;
        isEnding = false;
    }
}

