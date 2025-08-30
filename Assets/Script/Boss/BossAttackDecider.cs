using UnityEngine;

public class BossAttackDecider
{
    private BossController boss;
    private bool isWaiting;
    private float delayTimer;
    private float currentDelay;

    public BossAttackDecider(BossController boss)
    {
        this.boss = boss;
        Reset();
    }

    public void Reset()
    {
        isWaiting = false;
        delayTimer = 0f;
        currentDelay = 0f;
    }

    public bool TryAttack()
    {
        float distance = Vector3.Distance(boss.transform.position, boss.target.position);
        bool isInRange = distance < boss.attackRange;
        bool isCooledDown = Time.time > boss.lastAttackTime + boss.attackCooldown;

        if (isInRange && isCooledDown)
        {
            if (!isWaiting)
            {
                isWaiting = true;
                float maxDelay = (distance < boss.tooCloseDistance) ? 0.5f : boss.maxAttackDelay;

                currentDelay = Random.Range(0f, maxDelay);
                delayTimer = 0f;
            }


            delayTimer += Time.deltaTime;
            if (delayTimer >= currentDelay)
            {
                Reset();
                return true; // 🔥 通知狀態可以攻擊
            }
        }
        else
        {
            Reset(); // 玩家離開範圍或冷卻未完成 → 取消等待
        }

        return false;
    }
}
