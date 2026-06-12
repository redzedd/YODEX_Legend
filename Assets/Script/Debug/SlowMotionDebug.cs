using UnityEngine;

public class SlowMotionDebug : MonoBehaviour
{
    [Header("設定")]
    [Tooltip("按下此按鍵切換慢動作")]
    public KeyCode toggleKey = KeyCode.T; // 預設按 T 鍵

    [Tooltip("慢動作時的速度 (0.1 = 10% 速度)")]
    [Range(0.0f, 1.0f)]
    public float slowMotionFactor = 0.1f;

    private float defaultFixedDeltaTime;
    private bool isSlowMotion = false;

    void Start()
    {
        // 記錄遊戲原本的物理計算間隔
        defaultFixedDeltaTime = Time.fixedDeltaTime;
    }

    void Update()
    {
        // 偵測是否按下設定的按鍵
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleSlowMotion();
        }
    }

    void ToggleSlowMotion()
    {
        isSlowMotion = !isSlowMotion;

        if (isSlowMotion)
        {
            // 設定為慢動作
            Time.timeScale = slowMotionFactor;

            // 重要：調整物理更新頻率，避免慢動作時物理看起來卡頓
            Time.fixedDeltaTime = defaultFixedDeltaTime * Time.timeScale;

            Debug.Log($"<color=yellow>慢動作模式開啟: {Time.timeScale}x</color>");
        }
        else
        {
            // 恢復正常速度
            Time.timeScale = 1.0f;

            // 恢復物理更新頻率
            Time.fixedDeltaTime = defaultFixedDeltaTime;

            Debug.Log("<color=green>慢動作模式關閉: 正常速度</color>");
        }
    }

    // 確保腳本被銷毀或遊戲結束時，時間恢復正常 (避免影響編輯器或其他場景)
    void OnDestroy()
    {
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = defaultFixedDeltaTime;
    }
}