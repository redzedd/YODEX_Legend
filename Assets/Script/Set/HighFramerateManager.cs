using UnityEngine;

public class HighFramerateManager : MonoBehaviour
{
    [Header("設定目標幀率 (例如：60, 120, 144, 240...)")]
    public int targetFPS = 240;

    private float deltaTime = 0.0f;

    void Awake()
    {
        QualitySettings.vSyncCount = 0; // 關閉VSync
        Application.targetFrameRate = targetFPS;
    }

    void Update()
    {
        deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
    }

    void OnGUI()
    {
        int fps = Mathf.CeilToInt(1.0f / deltaTime);
        GUIStyle style = new GUIStyle();

        Rect rect = new Rect(10, 10, 200, 40);
        style.fontSize = 24;
        style.normal.textColor = Color.white;
        GUI.Label(rect, "FPS: " + fps, style);
    }
}
