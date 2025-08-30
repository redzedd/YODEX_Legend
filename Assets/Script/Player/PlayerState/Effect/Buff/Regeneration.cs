using UnityEngine;
using System;
using System.Collections;

public class Regeneration : MonoBehaviour
{
    [Header("百分比設定（由外部傳入或以 Configure 設定）")]
    [SerializeField] private float percentPerTick = 1f; // 每跳回復「最大生命」的百分比(%)
    [SerializeField] private float tickInterval = 1f; // 跳動間隔
    [SerializeField] private float totalDuration = 5f; // 總持續時間

    [Header("First Tick Delay")]
    [SerializeField] private float initialDelaySeconds = 1f; // 拿到後先等幾秒才開始第一跳
    [SerializeField] private bool useTickIntervalAsFirstDelay = false;

    private Coroutine regenCoroutine;

    public event Action OnEffectEnd;      // 自然結束
    public event Action OnEffectCanceled; // 被取消

    private HealthRegenController regenCtrl;
    private bool _endedOrCanceled = false;

    // 由外部以「百分比」設定
    public void ConfigureByPercent(float perTickPercent, float duration, float interval)
    {
        percentPerTick = Mathf.Max(0f, perTickPercent);
        totalDuration = Mathf.Max(0f, duration);
        tickInterval = Mathf.Max(0.01f, interval);
    }

    public void Begin()
    {
        if (regenCoroutine != null) StopCoroutine(regenCoroutine);
        regenCtrl = GetComponent<HealthRegenController>();
        regenCoroutine = StartCoroutine(RunRegen());

        // 開始：釘住前景，亮起目標條（從前景值出發）
        regenCtrl?.OnRegenStart();
    }

    private IEnumerator RunRegen()
    {
        // 先決定每跳「點數」與總跳數（以開始當下的 maxHealth 計算）
        var ph = PlayerHealth.Instance;
        if (ph == null || ph.maxHealth <= 0)
        {
            OnEffectEnd?.Invoke();
            Destroy(this);
            yield break;
        }

        int perTickPoints = Mathf.RoundToInt(ph.maxHealth * (percentPerTick * 0.01f));
        perTickPoints = Mathf.Max(1, perTickPoints); // 至少 1 點，避免 0 跳不動
        int tickCount = totalDuration > 0f
            ? Mathf.Max(1, Mathf.FloorToInt(totalDuration / tickInterval))
            : 1;

        // 首跳延遲：把「目標條的目標」一次設到首跳後的位置，等待期間由 HUD 以相同的平滑時間補間
        float firstDelay = useTickIntervalAsFirstDelay ? tickInterval : Mathf.Max(0f, initialDelaySeconds);
        if (firstDelay > 0f)
        {
            regenCtrl?.SetGoalTargetToFirstTick(perTickPoints);
            yield return new WaitForSeconds(firstDelay);
        }

        // 逐跳推進
        for (int i = 0; i < tickCount; i++)
        {
            regenCtrl?.OnRegenTick(perTickPoints);  // 真實血不動，只推「目標條 = current + pending」

            if (i < tickCount - 1)
                yield return new WaitForSeconds(tickInterval);
        }

        if (_endedOrCanceled) yield break;
        _endedOrCanceled = true;
        OnEffectEnd?.Invoke();
        regenCtrl?.OnRegenEndNaturally();
        Destroy(this);
    }

    public void ForceStop()
    {
        if (_endedOrCanceled) { Destroy(this); return; }  // 已經結束/取消過了
        _endedOrCanceled = true;

        if (regenCoroutine != null)
            StopCoroutine(regenCoroutine);

        OnEffectCanceled?.Invoke();
        regenCtrl?.OnRegenCanceled();
        Destroy(this);
    }
}
