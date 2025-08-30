using UnityEngine;
using System.Collections;

public class TestHealthDebug : MonoBehaviour
{
    public int totalAmount = 10;   // 總回復
    public int perTick = 2;        // 每跳
    public float tickInterval = 1; // 每跳間隔(秒)
    private Coroutine regenRoutine;

    void Update()
    {
        // 按 K → 扣血 10
        if (Input.GetKeyDown(KeyCode.K))
        {
            if (PlayerHealth.Instance != null)
                PlayerHealth.Instance.TakeDamage(10);
        }

        // 按 H → 直接回血 10
        if (Input.GetKeyDown(KeyCode.H))
        {
            if (PlayerHealth.Instance != null)
                PlayerHealth.Instance.HealDirect(10);
        }

        // 按 J → 開始一個持續回血 buff（每秒+2，持續5秒）
        if (Input.GetKeyDown(KeyCode.J))
        {
            StatusEffectManager.GetOrCreate().ApplyRegeneration(totalAmount, perTick, tickInterval);
            Debug.Log($"[TestRegenLauncher] 啟動再生：total={totalAmount}, perTick={perTick}, interval={tickInterval}s");
        }
    }
}
