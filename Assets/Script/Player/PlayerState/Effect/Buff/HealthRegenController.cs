using UnityEngine;

/// <summary>
/// 專職處理「持續回復」的 HUD 視覺與補回邏輯：
/// - 再生進行中：真實血不動，只顯示「目標條 = current + pending」
/// - 目標條達到滿血：真實血立即補到滿血，但效果持續累積到 overflow
/// - 再生自然結束：一次性把 pending 全部補回（overflow 不再處理）
/// - 受傷：清再生、pending 折半，並將 overflow 也折半轉為 pending → 等待一段時間 → 一次補回
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class HealthRegenController : MonoBehaviour
{
    [Header("Visual / Timing")]
    [Tooltip("受傷後，等待多久才把 pending 一次補回（秒）")]
    public float commitHoldAfterDamageSeconds = 0.30f;

    [Header("Refs")]
    public PlayerHUD playerHUD;

    private PlayerHealth health;
    private int pendingRegen;          // 尚未補進真實血、用來推動「目標條」的量
    private int overflowRegen;         // 目標條滿血後仍在累積的量（只在滿血期間記錄）
    private bool regenActive;
    private float commitHoldTimer;
    private bool waitHideGoalUntilCovered = false; // 等前景覆蓋後才隱藏目標條
    [SerializeField] private float coverEpsilon = 0.002f; // 覆蓋判定的容許誤差

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
    }

    private void OnEnable()
    {
        health.OnDamaged += HandleDamaged;
        health.OnHealed += HandleHealed;
        health.OnHealthChanged += HandleHealthChanged;
    }

    private void OnDisable()
    {
        health.OnDamaged -= HandleDamaged;
        health.OnHealed -= HandleHealed;
        health.OnHealthChanged -= HandleHealthChanged;
    }

    private void Update()
    {
        // 覆蓋檢查（等待前景追上再隱藏）
        if (waitHideGoalUntilCovered && playerHUD != null && playerHUD.healthRegenGoalImage != null)
        {
            playerHUD.ShowHealthRegenGoal(health.currentHealth, health.maxHealth);

            float front = playerHUD.healthFillImage
                ? playerHUD.healthFillImage.fillAmount
                : (health.maxHealth > 0 ? (float)health.currentHealth / health.maxHealth : 0f);
            float goal = playerHUD.healthRegenGoalImage.fillAmount;

            const float EPS = 0.002f;
            if (front + EPS >= goal)
            {
                playerHUD.HideHealthRegenGoal();
                waitHideGoalUntilCovered = false;
            }
        }

        // ★ pending + current 抵達滿血 → 立刻把真實血補到滿血（pending 扣掉用掉的量）
        if (regenActive && pendingRegen > 0 && health.currentHealth < health.maxHealth)
        {
            int room = health.maxHealth - health.currentHealth;
            if (health.currentHealth + pendingRegen >= health.maxHealth && room > 0)
            {
                health.HealDirect(room);
                pendingRegen -= room;
                waitHideGoalUntilCovered = true;
            }
        }

        if (pendingRegen > 0)
        {
            // 期間內：只顯示目標條位置 = current + pending（上限為 max）
            if (playerHUD != null)
            {
                int goalValue = Mathf.Min(health.currentHealth + pendingRegen, health.maxHealth);
                playerHUD.ShowHealthRegenGoal(goalValue, health.maxHealth);
            }

            // 受傷後等待：真實血不動，只釘住前景
            if (commitHoldTimer > 0f)
            {
                commitHoldTimer -= Time.deltaTime;
                if (playerHUD != null)
                    playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
                return;
            }

            // 若已沒有 active regen → 自然結束或被取消且等待也結束 → 一次性補回（只補 pending，不處理 overflow）
            if (!regenActive)
            {
                CommitAllPendingNow();
                return;
            }

            // 再生期間：固定前景（不推上去），只讓目標條移動
            if (playerHUD != null)
                playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
        }
        else
        {
            // 沒有 pending，但仍在再生中：保持顯示「滿血目標條」（如果目前就是滿血）
            if (regenActive && playerHUD != null && health.currentHealth >= health.maxHealth)
            {
                playerHUD.ShowHealthRegenGoal(health.maxHealth, health.maxHealth);
                playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
            }
            else
            {
                if (playerHUD != null && !regenActive && !waitHideGoalUntilCovered)
                    playerHUD.HideHealthRegenGoal();
            }
        }
    }

    private void LateUpdate()
    {
        if (!waitHideGoalUntilCovered || playerHUD == null || playerHUD.healthRegenGoalImage == null)
            return;

        // 讓目標條緊貼真實血位置（被前景覆蓋後再隱藏）
        playerHUD.ShowHealthRegenGoal(health.currentHealth, health.maxHealth);

        float curRatio = (health.maxHealth > 0) ? (float)health.currentHealth / health.maxHealth : 0f;

        float front = playerHUD.healthFillImage
            ? playerHUD.healthFillImage.fillAmount
            : curRatio;

        float goal = playerHUD.healthRegenGoalImage.fillAmount;

        bool coveredGoal = (front + coverEpsilon >= goal);
        bool frontAtTarget = (Mathf.Abs(front - curRatio) < coverEpsilon);

        if (coveredGoal && frontAtTarget)
        {
            playerHUD.HideHealthRegenGoal();
            waitHideGoalUntilCovered = false;
        }
    }

    // ===== Regeneration.cs 呼叫這些入口 =====
    public void OnRegenStart()
    {
        regenActive = true;

        if (playerHUD != null)
        {
            playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
            float cur01 = health.maxHealth > 0 ? (float)health.currentHealth / health.maxHealth : 0f;
            playerHUD.ShowHealthRegenGoal01(cur01);
        }
    }

    // 將目標條「目標」設到首跳後的位置；HUD 用相同的平滑時間補間到位
    public void SetGoalTargetToFirstTick(int amount)
    {
        if (playerHUD == null || health.maxHealth <= 0) return;

        int room = Mathf.Max(0, health.maxHealth - health.currentHealth);
        int clamped = Mathf.Min(amount, room);
        float target01 = (float)(health.currentHealth + clamped) / health.maxHealth;

        playerHUD.ShowHealthRegenGoal01(target01);
        playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
    }

    /// <summary>每跳加入再生：優先填滿 pending；滿了之後溢出到 overflow。</summary>
    public void OnRegenTick(int addAmount)
    {
        if (addAmount <= 0) return;

        int roomToMax = Mathf.Max(0, health.maxHealth - (health.currentHealth + pendingRegen));
        int addToPending = Mathf.Min(addAmount, roomToMax);
        pendingRegen += addToPending;

        int leftover = addAmount - addToPending;
        if (leftover > 0)
            overflowRegen += leftover;

        if (playerHUD != null)
        {
            int goalValue = Mathf.Min(health.currentHealth + pendingRegen, health.maxHealth);
            playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
            playerHUD.ShowHealthRegenGoal(goalValue, health.maxHealth);
        }
    }

    public void OnRegenEndNaturally()
    {
        regenActive = false;
        CommitAllPendingNow();
        overflowRegen = 0;
    }

    public void OnRegenCanceled()
    {
        regenActive = false;
        // 等待期結束後，由 Update() 補回
    }

    // ===== PlayerHealth 事件處理 =====
    private void HandleDamaged(int dmg)
    {
        if (StatusEffectManager.Instance != null)
            StatusEffectManager.Instance.CancelAllRegeneration();

        if (pendingRegen > 0)
            pendingRegen = Mathf.CeilToInt(pendingRegen * 0.5f);

        if (overflowRegen > 0)
        {
            int halfOverflow = Mathf.CeilToInt(overflowRegen * 0.5f);
            int room = Mathf.Max(0, health.maxHealth - (health.currentHealth + pendingRegen));
            int push = Mathf.Min(room, halfOverflow);
            pendingRegen += push;
            overflowRegen = 0;
        }

        if (pendingRegen > 0 && playerHUD != null)
        {
            playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
            playerHUD.ShowHealthRegenGoal(Mathf.Min(health.currentHealth + pendingRegen, health.maxHealth), health.maxHealth);
        }

        commitHoldTimer = commitHoldAfterDamageSeconds;
    }

    private void HandleHealed(int healed)
    {
        if (pendingRegen > 0 && playerHUD != null)
            playerHUD.ShowHealthRegenGoal(Mathf.Min(health.currentHealth + pendingRegen, health.maxHealth), health.maxHealth);
        else if (regenActive && playerHUD != null && health.currentHealth >= health.maxHealth)
            playerHUD.ShowHealthRegenGoal(health.maxHealth, health.maxHealth);
    }

    private void HandleHealthChanged(int cur, int max)
    {
        if (playerHUD != null)
            playerHUD.SetHealthTarget_Direct(cur, max);
    }

    // ===== 工具：一次把 pending 補進真實血 =====
    private void CommitAllPendingNow()
    {
        if (pendingRegen <= 0) return;

        int gain = Mathf.Min(pendingRegen, health.maxHealth - health.currentHealth);
        if (gain > 0)
        {
            health.HealDirect(gain);
            pendingRegen -= gain;
        }

        waitHideGoalUntilCovered = true;
        pendingRegen = 0;
    }
}
