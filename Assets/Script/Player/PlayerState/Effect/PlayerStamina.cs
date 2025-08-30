using UnityEngine;
using System;

public class PlayerStamina : MonoBehaviour
{
    public static PlayerStamina Instance { get; private set; }

    [Header("Stamina Settings")]
    [SerializeField] private int maxStamina = 100;
    [SerializeField] private float regenRate = 10f;           // 每秒回復量
    [SerializeField] private float regenDelaySeconds = 1.5f;  // 使用後延遲回復
    [SerializeField] private float runStaminaCostPerSecond = 5f;

    public PlayerHUD playerHUD;

    public int CurrentStamina { get; private set; }
    public event Action<int, int> OnStaminaChanged; // (cur, max)

    // 回復與連續消耗的累積器（避免 FPS 影響數值）
    private float regenDelayTimer = 0f;
    private float regenAccumulator = 0f;
    private float drainAccumulator = 0f;

    public float RunStaminaCostPerSecond => runStaminaCostPerSecond;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        CurrentStamina = maxStamina;
    }

    private void Start()
    {
        // 初始對齊（Direct）
        if (playerHUD != null)
            playerHUD.SetStaminaTarget_Direct(CurrentStamina, maxStamina);

        // 體力變動時 → 直接更新 HUD（Direct）
        OnStaminaChanged += (cur, max) =>
        {
            if (playerHUD != null)
                playerHUD.SetStaminaTarget_Direct(cur, max);
        };
    }

    private void Update()
    {
        if (regenDelayTimer > 0f)
            regenDelayTimer -= Time.deltaTime;

        if (regenDelayTimer <= 0f)
            Regenerate();
    }

    private void Regenerate()
    {
        if (CurrentStamina >= maxStamina) return;

        regenAccumulator += regenRate * Time.deltaTime;

        if (regenAccumulator >= 1f)
        {
            int gain = Mathf.FloorToInt(regenAccumulator);
            int newValue = Mathf.Min(CurrentStamina + gain, maxStamina);

            if (newValue != CurrentStamina)
            {
                CurrentStamina = newValue;
                OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
            }
            regenAccumulator -= gain;
        }
    }

    public bool HasStamina(int amount)
    {
        return CurrentStamina >= amount;
    }

    public bool TryUseStamina(int amount)
    {
        if (CurrentStamina < amount)
            return false;

        CurrentStamina -= amount;
        OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);

        regenDelayTimer = regenDelaySeconds;
        regenAccumulator = 0f; // 避免延遲結束瞬間跳回過多
        return true;
    }

    /// <summary>連續消耗（例如跑步）。依每秒量 * deltaTime 累積，滿1才扣整數；會刷新回復延遲。</summary>
    public bool ConsumeOverTime(float amountPerSecond)
    {
        if (CurrentStamina <= 0) return false;

        drainAccumulator += amountPerSecond * Time.deltaTime;

        if (drainAccumulator >= 1f)
        {
            int loss = Mathf.FloorToInt(drainAccumulator);
            int newValue = Mathf.Max(CurrentStamina - loss, 0);

            if (newValue != CurrentStamina)
            {
                CurrentStamina = newValue;
                OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
            }
            drainAccumulator -= loss;
        }

        regenDelayTimer = regenDelaySeconds;
        return CurrentStamina > 0;
    }

    /// <summary>停止連續消耗時呼叫，避免殘量造成下一次瞬間多扣。</summary>
    public void CancelContinuousConsume()
    {
        drainAccumulator = 0f;
    }
}
