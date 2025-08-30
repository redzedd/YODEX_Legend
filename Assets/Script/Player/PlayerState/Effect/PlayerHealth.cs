using UnityEngine;
using System;

public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [Header("Health")]
    public int maxHealth = 100;
    public int currentHealth;

    public AudioClip healSound;
    private AudioSource audioSource;

    [Header("Optional")]
    public PlayerHUD playerHUD;

    public event Action<int, int> OnHealthChanged; // (cur, max)
    public event Action<int> OnDamaged;
    public event Action<int> OnHealed;
    public event Action OnDied;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        audioSource = GetComponent<AudioSource>();
    }

    private void Start()
    {
        currentHealth = Mathf.Clamp(currentHealth <= 0 ? maxHealth : currentHealth, 0, maxHealth);

        if (playerHUD != null)
            playerHUD.SetHealthTarget_Direct(currentHealth, maxHealth);

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0) return;

        currentHealth = Mathf.Max(currentHealth - amount, 0);

        if (playerHUD != null)
            playerHUD.SetHealthTarget_Direct(currentHealth, maxHealth);

        OnDamaged?.Invoke(amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0) { OnDied?.Invoke(); Die(); }
    }

    public void HealDirect(int amount)
    {
        if (amount <= 0) return;

        int prev = currentHealth;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);

        if (currentHealth > prev && healSound) audioSource.PlayOneShot(healSound);

        if (playerHUD != null)
            playerHUD.SetHealthTarget_Direct(currentHealth, maxHealth);

        OnHealed?.Invoke(currentHealth - prev);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void Die()
    {
        Debug.Log("Player Die()");
        // TODO: 死亡流程
    }
}
