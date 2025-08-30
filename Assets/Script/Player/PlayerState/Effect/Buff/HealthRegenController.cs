using UnityEngine;

/// <summary>
/// �M¾�B�z�u����^�_�v�� HUD ��ı�P�ɦ^�޿�G
/// - �A�Ͷi�椤�G�u��夣�ʡA�u��ܡu�ؼб� = current + pending�v
/// - �ؼб��F�캡��G�u���ߧY�ɨ캡��A���ĪG����ֿn�� overflow
/// - �A�ͦ۵M�����G�@���ʧ� pending �����ɦ^�]overflow ���A�B�z�^
/// - ���ˡG�M�A�͡Bpending ��b�A�ñN overflow �]��b�ର pending �� ���ݤ@�q�ɶ� �� �@���ɦ^
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class HealthRegenController : MonoBehaviour
{
    [Header("Visual / Timing")]
    [Tooltip("���˫�A���ݦh�[�~�� pending �@���ɦ^�]��^")]
    public float commitHoldAfterDamageSeconds = 0.30f;

    [Header("Refs")]
    public PlayerHUD playerHUD;

    private PlayerHealth health;
    private int pendingRegen;          // �|���ɶi�u���B�Ψӱ��ʡu�ؼб��v���q
    private int overflowRegen;         // �ؼб�����ᤴ�b�ֿn���q�]�u�b��������O���^
    private bool regenActive;
    private float commitHoldTimer;
    private bool waitHideGoalUntilCovered = false; // ���e���л\��~���åؼб�
    [SerializeField] private float coverEpsilon = 0.002f; // �л\�P�w���e�\�~�t

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
        // �л\�ˬd�]���ݫe���l�W�A���á^
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

        // �� pending + current ��F���� �� �ߨ��u���ɨ캡��]pending �����α����q�^
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
            // �������G�u��ܥؼб���m = current + pending�]�W���� max�^
            if (playerHUD != null)
            {
                int goalValue = Mathf.Min(health.currentHealth + pendingRegen, health.maxHealth);
                playerHUD.ShowHealthRegenGoal(goalValue, health.maxHealth);
            }

            // ���˫ᵥ�ݡG�u��夣�ʡA�u�v��e��
            if (commitHoldTimer > 0f)
            {
                commitHoldTimer -= Time.deltaTime;
                if (playerHUD != null)
                    playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
                return;
            }

            // �Y�w�S�� active regen �� �۵M�����γQ�����B���ݤ]���� �� �@���ʸɦ^�]�u�� pending�A���B�z overflow�^
            if (!regenActive)
            {
                CommitAllPendingNow();
                return;
            }

            // �A�ʹ����G�T�w�e���]�����W�h�^�A�u���ؼб�����
            if (playerHUD != null)
                playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
        }
        else
        {
            // �S�� pending�A�����b�A�ͤ��G�O����ܡu����ؼб��v�]�p�G�ثe�N�O����^
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

        // ���ؼб���K�u����m�]�Q�e���л\��A���á^
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

    // ===== Regeneration.cs �I�s�o�ǤJ�f =====
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

    // �N�ؼб��u�ؼСv�]�쭺���᪺��m�FHUD �άۦP�����Ʈɶ��ɶ����
    public void SetGoalTargetToFirstTick(int amount)
    {
        if (playerHUD == null || health.maxHealth <= 0) return;

        int room = Mathf.Max(0, health.maxHealth - health.currentHealth);
        int clamped = Mathf.Min(amount, room);
        float target01 = (float)(health.currentHealth + clamped) / health.maxHealth;

        playerHUD.ShowHealthRegenGoal01(target01);
        playerHUD.SetHealthTarget_Direct(health.currentHealth, health.maxHealth);
    }

    /// <summary>�C���[�J�A�͡G�u���� pending�F���F���᷸�X�� overflow�C</summary>
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
        // ���ݴ�������A�� Update() �ɦ^
    }

    // ===== PlayerHealth �ƥ�B�z =====
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

    // ===== �u��G�@���� pending �ɶi�u��� =====
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
