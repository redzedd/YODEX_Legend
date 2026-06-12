using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Player.Input;

/// <summary>
/// 場景轉場管理器（Singleton / DontDestroyOnLoad）。
/// 自動建立全螢幕純色遮罩，負責：
///   1. 離開場景時：鎖定輸入 → 淡出音訊 → 畫面淡入遮罩
///   2. 非同步載入新場景
///   3. 進入場景後：畫面淡出遮罩 → 淡入音訊 → 解鎖輸入
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }

    [Header("淡入淡出設定")]
    [SerializeField, Tooltip("遮罩顏色")]
    private Color _fadeColor = Color.black;
    [SerializeField, Tooltip("畫面淡入淡出持續秒數")]
    private float _fadeDuration = 1.5f;
    [SerializeField, Tooltip("音訊淡入淡出持續秒數")]
    private float _audioFadeDuration = 1.5f;

    private Image _fadeImage;
    private bool _isTransitioning;

    /// <summary>
    /// 是否正在轉場中。
    /// </summary>
    public bool IsTransitioning => _isTransitioning;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        CreateOverlay();
        // 立即全域暫停音訊，防止場景腳本在 Start() 播放音樂時產生爆音
        AudioListener.pause = true;
    }

    IEnumerator Start()
    {
        // 等待一幀讓場景其他腳本完成初始化（Awake / Start）
        yield return null;
        yield return PerformFadeIn();
    }

    /// <summary>
    /// 載入指定場景，含完整淡出 → 非同步載入 → 淡入流程。
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (_isTransitioning) return;
        StartCoroutine(TransitionCoroutine(sceneName));
    }

    private IEnumerator TransitionCoroutine(string sceneName)
    {
        _isTransitioning = true;
        SetInputEnabled(false);
        // === 淡出當前場景（畫面 → 黑、音訊 → 靜音）===
        List<AudioVolumeData> currentAudio = CollectSceneAudioSources();
        yield return FadeOverlayAndAudio(0f, 1f, currentAudio, fadeToZero: true);
        // === 非同步載入新場景 ===
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(sceneName);
        loadOp.allowSceneActivation = false;
        while (loadOp.progress < 0.9f)
            yield return null;
        // 確保 TimeScale 恢復正常（例如從死亡畫面重新載入時 timeScale = 0）
        Time.timeScale = 1f;
        // 新場景啟動前暫停音訊，防止 Start() 中播放音樂產生爆音
        AudioListener.pause = true;
        loadOp.allowSceneActivation = true;
        while (!loadOp.isDone)
            yield return null;
        // 等待一幀讓新場景的 Awake / Start 完成
        yield return null;
        // === 淡入新場景 ===
        yield return PerformFadeIn();
        _isTransitioning = false;
    }

    /// <summary>
    /// 淡入流程：收集音源 → 靜音 → 遮罩淡出 → 音訊淡入 → 啟用輸入。
    /// </summary>
    private IEnumerator PerformFadeIn()
    {
        List<AudioVolumeData> audioData = CollectSceneAudioSources();
        // 先將所有音源靜音，再解除全域暫停，由漸變動畫恢復至目標音量
        foreach (AudioVolumeData data in audioData)
        {
            if (data.Source != null)
                data.Source.volume = 0f;
        }
        AudioListener.pause = false;
        yield return FadeOverlayAndAudio(1f, 0f, audioData, fadeToZero: false);
        SetInputEnabled(true);
    }

    /// <summary>
    /// 同時驅動遮罩透明度與所有音源音量的漸變動畫。
    /// </summary>
    private IEnumerator FadeOverlayAndAudio(
        float overlayFrom,
        float overlayTo,
        List<AudioVolumeData> audioData,
        bool fadeToZero)
    {
        _fadeImage.raycastTarget = true;
        Color color = _fadeImage.color;
        color.a = overlayFrom;
        _fadeImage.color = color;
        // 組合 DOTween Sequence 同時驅動畫面與音訊
        float duration = Mathf.Max(_fadeDuration, _audioFadeDuration);
        Sequence sequence = DOTween.Sequence();
        sequence.Join(_fadeImage.DOFade(overlayTo, _fadeDuration).SetEase(Ease.InOutSine));
        foreach (AudioVolumeData data in audioData)
        {
            if (data.Source == null) continue;
            float targetVolume = fadeToZero ? 0f : data.TargetVolume;
            sequence.Join(data.Source.DOFade(targetVolume, _audioFadeDuration).SetEase(Ease.InOutSine));
        }
        sequence.SetUpdate(true);
        sequence.SetLink(gameObject);
        yield return sequence.WaitForCompletion();
        // 遮罩完全透明時關閉射線阻擋，允許 UI 互動
        if (overlayTo <= 0f)
            _fadeImage.raycastTarget = false;
    }

    /// <summary>
    /// 以程式碼建立全螢幕遮罩 Canvas（不依賴 Prefab）。
    /// </summary>
    private void CreateOverlay()
    {
        GameObject canvasObj = new GameObject("TransitionCanvas");
        canvasObj.transform.SetParent(transform);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        // 全螢幕遮罩 Image（初始完全不透明）
        GameObject imageObj = new GameObject("FadeImage");
        imageObj.transform.SetParent(canvasObj.transform, false);
        _fadeImage = imageObj.AddComponent<Image>();
        _fadeImage.color = new Color(_fadeColor.r, _fadeColor.g, _fadeColor.b, 1f);
        _fadeImage.raycastTarget = true;
        RectTransform rt = _fadeImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
    }

    /// <summary>
    /// 收集當前場景中所有 AudioSource 及其目前音量（作為淡入目標值）。
    /// 僅搜尋場景內物件，不含 DontDestroyOnLoad 物件。
    /// </summary>
    private List<AudioVolumeData> CollectSceneAudioSources()
    {
        List<AudioVolumeData> result = new List<AudioVolumeData>();
        Scene activeScene = SceneManager.GetActiveScene();
        foreach (GameObject root in activeScene.GetRootGameObjects())
        {
            AudioSource[] sources = root.GetComponentsInChildren<AudioSource>(true);
            foreach (AudioSource source in sources)
                result.Add(new AudioVolumeData(source, source.volume));
        }
        return result;
    }

    private void SetInputEnabled(bool enabled)
    {
        if (SystemInputReader.Instance == null) return;
        if (enabled)
        {
            SystemInputReader.Instance.ResetTriggeredFlags();
            SystemInputReader.Instance.EnablePlayerInput();
            SystemInputReader.Instance.EnableUIMapInput();
        }
        else
        {
            SystemInputReader.Instance.DisablePlayerInput();
            SystemInputReader.Instance.DisableUIMapInput();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// AudioSource 與其目標音量的資料結構。
    /// </summary>
    private readonly struct AudioVolumeData
    {
        public readonly AudioSource Source;
        public readonly float TargetVolume;
        public AudioVolumeData(AudioSource source, float targetVolume)
        {
            Source = source;
            TargetVolume = targetVolume;
        }
    }
}
