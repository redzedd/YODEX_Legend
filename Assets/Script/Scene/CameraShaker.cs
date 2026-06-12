using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// 全域相機震動管理器 (支援多鏡頭切換)
/// - 自動偵測當前正在使用的 CinemachineCamera (不管是鎖定、瞄準還是原本的)
/// - 每一幀將震動數值寫入當前鏡頭的 Noise Component
/// </summary>
public class CameraShaker : MonoBehaviour
{
    public static CameraShaker Instance { get; private set; }

    [Tooltip("震動的頻率 (Frequency Gain)，數值越高震動頻率越快")]
    public float defaultFrequency = 12.0f;

    private CinemachineBrain _brain;
    private float shakeTimer;
    private float startIntensity;
    private float timerTotal;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    private void Start()
    {
        // 嘗試獲取主攝影機上的 CinemachineBrain
        if (Camera.main != null)
            _brain = Camera.main.GetComponent<CinemachineBrain>();

        // 雙重保險
        if (_brain == null)
            _brain = FindFirstObjectByType<CinemachineBrain>();

        if (_brain == null)
            Debug.LogError("[CameraShaker] 找不到 CinemachineBrain！請確保 Main Camera 上有掛載 CinemachineBrain。");
    }

    /// <summary>
    /// 觸發震動
    /// </summary>
    public void Shake(float intensity, float time)
    {
        startIntensity = intensity;
        shakeTimer = time;
        timerTotal = time;
    }

    private void Update()
    {
        // 1. 取得當前活躍的虛擬相機 (ActiveVirtualCamera)
        if (_brain == null) return;

        // 將 ICinemachineCamera 轉型為具體的 CinemachineCamera (CM 3.x)
        var activeCam = _brain.ActiveVirtualCamera as CinemachineCamera;

        // 如果現在沒有活躍相機，或正在過渡中抓不到，就跳過
        if (activeCam == null) return;

        // 2. 獲取該相機上的 Noise 組件
        // 注意：為了效能，你可以考慮做個簡單的緩存，但這裡為了確保切換準確，直接GetComponent通常也夠快
        var noise = activeCam.GetComponent<CinemachineBasicMultiChannelPerlin>();

        // 如果這顆鏡頭沒裝 Noise，就沒辦法震
        if (noise == null) return;

        // 3. 計算並應用震動
        if (shakeTimer > 0)
        {
            shakeTimer -= Time.unscaledDeltaTime;

            // 線性衰減
            float currentIntensity = Mathf.Lerp(startIntensity, 0f, 1 - (shakeTimer / timerTotal));

            noise.AmplitudeGain = currentIntensity;

            // 確保有頻率 (防呆)
            if (noise.FrequencyGain <= 0.1f)
            {
                noise.FrequencyGain = defaultFrequency;
            }
        }
        else
        {
            // 時間到，歸零
            // 這是為了防止震動卡在最後一幀的數值
            if (noise.AmplitudeGain > 0)
            {
                noise.AmplitudeGain = 0f;
            }
        }
    }
}