using UnityEngine;
using System.Collections;

public class HitStop : MonoBehaviour
{
    public static HitStop Instance { get; private set; }

    private bool isWaiting = false;

    // �i�H�b Inspector �վ�o�ӭȡA�V�p�V���u�����v�A�V�j�V���u�C�ʧ@�v
    // 0.05 ~ 0.1 �O�������϶��A�J���w��P�S��ݲM�ʧ@
    [Range(0f, 0.5f)] public float stopTimeScale = 0.05f;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public void Trigger(float duration)
    {
        Trigger(duration, stopTimeScale);
    }

    /// <summary>
    /// 觸發頓幀，指定自訂的時間縮放
    /// </summary>
    public void Trigger(float duration, float timeScale)
    {
        if (isWaiting) return;
        if (duration <= 0.001f) return;
        StartCoroutine(DoHitStop(duration, timeScale));
    }

    private IEnumerator DoHitStop(float duration, float timeScale)
    {
        isWaiting = true;
        float originalScale = Time.timeScale;
        if (originalScale <= 0) originalScale = 1f;
        Time.timeScale = timeScale;
        yield return new WaitForSecondsRealtime(duration);
        // 還原前確認 timeScale 仍是頓幀值;若期間被背包/寶箱/烹飪暫停成 0,
        // 不可蓋回,否則 UI 開著遊戲卻繼續跑,玩家在背景被打。
        if (Mathf.Approximately(Time.timeScale, timeScale))
            Time.timeScale = originalScale;
        isWaiting = false;
    }
}