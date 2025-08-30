using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 全域 Buff 管理：
/// - 任何 Buff 以 buffId 唯一，高階(等級大)覆蓋低階
/// - 再生(Regeneration) 改為『按照最大生命百分比』回血（數值來自 BuffDefinition 的 tier.percentPerTick / duration / tickInterval）
/// - 再生開始：直接在玩家身上 AddComponent<Regeneration>() → ConfigureByPercent(...) → Begin()
/// - 再生結束/被取消：自動移除管理表與 UI（訂閱 Regeneration 的事件 + 備援計時器）
/// - UI：若場上有 BuffBarUI，會自動新增/更新/移除圖示，並依 buffId 排序
/// - 你可以把本物件放場景，也可不放；若沒放，呼叫 GetOrCreate() 會自動建立並 DontDestroyOnLoad
/// </summary>
public class BuffManager : MonoBehaviour
{
    public static BuffManager Instance { get; private set; }

    // ---- 方便在任何地方保證可用（若你已把它放進場景，也不影響）----
    public static BuffManager GetOrCreate()
    {
        if (Instance) return Instance;

        var existing = FindObjectOfType<BuffManager>();
        if (existing)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("[BuffManager]");
        Instance = go.AddComponent<BuffManager>();
        DontDestroyOnLoad(go);
        return Instance;
    }

    private class ActiveBuff
    {
        public BuffDefinition def;
        public int level;
        public Coroutine fallbackTimer;  // 備援自動清除
    }

    // 以 buffId 當 key（每種 Buff 同時僅一個等級生效）
    private readonly Dictionary<int, ActiveBuff> _active = new();

    // ====== 生命週期 ======
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ====== 對外 API ======

    /// <summary>套用一個 Buff（同 buffId：高階覆蓋低階；低階無法覆蓋高階）</summary>
    public void Apply(BuffDefinition def, int level)
    {
        if (!def) return;
        level = Mathf.Clamp(level, 1, 3);

        // 高階覆蓋低階；如已存在且新等級較低 → 不動
        if (_active.TryGetValue(def.buffId, out var cur))
        {
            if (level < cur.level) return; // 低階不覆蓋
            InternalRemove(def.buffId, visualOnly: false); // 先乾淨移除舊的（效果+UI+計時器）
        }

        // 實際加上效果
        AddEffect(def, level);

        // 記錄為啟用中
        var entry = new ActiveBuff { def = def, level = level };
        _active[def.buffId] = entry;

        // UI：顯示/更新（有 BuffBarUI 才動作）
        if (BuffBarUI.Instance) BuffBarUI.Instance.ShowOrUpdate(def, level);

        // 備援清理（以定義的總持續時間做自動移除；事件會更快觸發清理）
        var tier = def.GetTier(level);
        if (tier != null && tier.duration > 0f)
            entry.fallbackTimer = StartCoroutine(RemoveAfter(def.buffId, tier.duration));
    }

    /// <summary>手動移除某個 buffId 的 Buff</summary>
    public void RemoveById(int buffId)
    {
        InternalRemove(buffId, visualOnly: false);
    }

    // ====== 內部：加/移/綁定 等 ======

    private void InternalRemove(int buffId, bool visualOnly)
    {
        if (_active.TryGetValue(buffId, out var cur))
        {
            if (cur.fallbackTimer != null) StopCoroutine(cur.fallbackTimer);

            if (!visualOnly)
                RemoveEffect(cur.def); // 只有外部主動移除或覆蓋時才真的「取消效果」

            _active.Remove(buffId);
            if (BuffBarUI.Instance) BuffBarUI.Instance.RemoveById(buffId);
        }
    }

    private IEnumerator RemoveAfter(int buffId, float seconds)
    {
        // 稍加 0.05s buffer，避免與最後一跳同幀競態
        yield return new WaitForSeconds(seconds + 0.05f);
        InternalRemove(buffId, visualOnly: false);
    }

    private void AddEffect(BuffDefinition def, int level)
    {
        var tier = def.GetTier(level);
        switch (def.specialEffect)
        {
            case SpecialEffectType.Regeneration:
                {
                    // —— 轉成「百分比再生」——
                    // 取玩家目標
                    var player = PlayerHealth.Instance ? PlayerHealth.Instance.gameObject : null;
                    if (player == null) return;

                    // 安全起見：取消任何現存再生（避免重複）
                    // 若你仍有 StatusEffectManager，保留這行能更保險；
                    // 沒有也沒關係，下面還會檢查掛在玩家身上的 Regeneration 並停止。
                    if (StatusEffectManager.Instance != null)
                        StatusEffectManager.Instance.CancelAllRegeneration();

                    // 先清掉場上現有的 Regeneration（若還有遺留）
                    var existed = player.GetComponent<Regeneration>();
                    if (existed != null) existed.ForceStop();

                    float perTickPercent = Mathf.Max(0f, tier.percentPerTick);
                    float duration = Mathf.Max(0f, tier.duration);
                    float interval = Mathf.Max(0.01f, tier.tickInterval);

                    // 掛上百分比版 Regeneration
                    var regen = player.AddComponent<Regeneration>();
                    regen.ConfigureByPercent(perTickPercent, duration, interval);
                    regen.Begin();

                    // ★ 綁定事件（雙保險之一）：結束/取消 → 自動清理管理表與 UI
                    regen.OnEffectEnd += () => OnRegenFinishedOrCanceled(def.buffId);
                    regen.OnEffectCanceled += () => OnRegenFinishedOrCanceled(def.buffId);

                    break;
                }

                // TODO: 其他特殊效果可在此擴充（AttackBoost、FireResistance...）
        }
    }

    private void RemoveEffect(BuffDefinition def)
    {
        switch (def.specialEffect)
        {
            case SpecialEffectType.Regeneration:
                {
                    // 優先呼叫你的 StatusEffectManager（若存在）
                    if (StatusEffectManager.Instance != null)
                        StatusEffectManager.Instance.CancelAllRegeneration();

                    // 再保險：把玩家身上殘留的 Regeneration 強制關掉
                    var player = PlayerHealth.Instance ? PlayerHealth.Instance.gameObject : null;
                    if (player != null)
                    {
                        var regen = player.GetComponent<Regeneration>();
                        if (regen != null) regen.ForceStop();
                    }
                    break;
                }

                // TODO: 其他特殊效果的清除
        }
    }

    private void OnRegenFinishedOrCanceled(int buffId)
    {
        // 事件已保證效果結束/被取消了，這裡「只做記帳與 UI 收尾」，不要再 Cancel 一次
        InternalRemove(buffId, visualOnly: true);
    }
}
