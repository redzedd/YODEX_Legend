using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class StatusEffectManager : MonoBehaviour
{
    public static StatusEffectManager Instance { get; private set; }

    [Header("Attach Target (leave empty to use PlayerHealth.Instance)")]
    [SerializeField] private GameObject regenAttachTarget;

    private Regeneration activeRegen;

    public static StatusEffectManager GetOrCreate()
    {
        if (Instance != null) return Instance;

        var existing = FindObjectOfType<StatusEffectManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("[StatusEffectManager]");
        Instance = go.AddComponent<StatusEffectManager>();
        DontDestroyOnLoad(go);
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private GameObject ResolveAttachTarget()
    {
        if (regenAttachTarget != null) return regenAttachTarget;
        if (PlayerHealth.Instance != null) return PlayerHealth.Instance.gameObject;

        var ph = FindObjectOfType<PlayerHealth>();
        if (ph != null) return ph.gameObject;

        Debug.LogError("[StatusEffectManager] 找不到 PlayerHealth。請指定 regenAttachTarget。");
        return null;
    }

    // ===== 新：百分比版入口 =====
    public void ApplyRegenerationPercent(float percentPerTick, float duration, float tickInterval)
    {
        CancelAllRegeneration();

        var targetGo = ResolveAttachTarget();
        if (targetGo == null) return;

        activeRegen = targetGo.AddComponent<Regeneration>();
        activeRegen.ConfigureByPercent(percentPerTick, duration, Mathf.Max(0.01f, tickInterval));

        activeRegen.OnEffectEnd += () => { activeRegen = null; };
        activeRegen.OnEffectCanceled += () => { activeRegen = null; };

        activeRegen.Begin();
    }

    // ===== 舊：點數版入口（自動換算成百分比，避免舊碼報錯） =====
    public void ApplyRegeneration(int totalAmount, int perTick, float tickInterval)
    {
        var targetGo = ResolveAttachTarget();
        if (targetGo == null) return;

        var ph = targetGo.GetComponent<PlayerHealth>();
        if (ph == null || ph.maxHealth <= 0) return;

        int ticks = Mathf.Max(1, Mathf.CeilToInt((float)totalAmount / Mathf.Max(1, perTick)));
        float duration = tickInterval * ticks;
        float perTickPercent = (perTick / Mathf.Max(1f, (float)ph.maxHealth)) * 100f;

        ApplyRegenerationPercent(perTickPercent, duration, tickInterval);
    }

    public void CancelAllRegeneration()
    {
        var targetGo = ResolveAttachTarget();
        if (targetGo == null) return;

        if (activeRegen != null) { activeRegen.ForceStop(); activeRegen = null; }

        // 保險：把目標上所有 Regeneration 都停掉
        var all = targetGo.GetComponents<Regeneration>();
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null) all[i].ForceStop();
    }
}
