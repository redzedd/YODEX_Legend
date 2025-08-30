using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class StartSceneManager : MonoBehaviour
{
    public string sceneToLoad = "GameScene"; // 要載入的場景名稱

    [Header("UI & Audio")]
    public Text startText;
    public Image fadeImage;                  // 黑幕 Image（初始 Alpha = 0）
    public AudioSource bgmSource;            // 背景音樂播放器
    public AudioClip bgmClip;
    public AudioClip startSFX;               // 按鍵音效（可選）

    [Header("Timing Settings")]
    public float fadeDuration = 1.5f;
    public float textLerpSpeed = 2f;
    public float bgmMaxVolume = 1f;

    private bool hasPressed = false;

    void Start()
    {
        if (bgmSource != null && bgmClip != null)
        {
            bgmSource.clip = bgmClip;
            bgmSource.volume = bgmMaxVolume;
            bgmSource.loop = true;
            bgmSource.Play();
        }

        // 初始化黑幕透明
        if (fadeImage != null)
        {
            Color c = fadeImage.color;
            c.a = 0f;
            fadeImage.color = c;
        }
    }

    void Update()
    {
        FlashText();

        if (!hasPressed && Input.anyKeyDown)
        {
            hasPressed = true;
            StartCoroutine(TransitionCoroutine());
        }
    }

    void FlashText()
    {
        if (startText == null) return;

        float alpha = Mathf.Lerp(0.3f, 1f, Mathf.PingPong(Time.time * textLerpSpeed, 1));
        Color c = startText.color;
        c.a = alpha;
        startText.color = c;
    }

    IEnumerator TransitionCoroutine()
    {
        // 播放進入音效（可選）
        if (startSFX != null && bgmSource != null)
            bgmSource.PlayOneShot(startSFX);

        yield return StartCoroutine(FadeOut());

        SceneManager.LoadScene(sceneToLoad);
    }

    IEnumerator FadeOut()
    {
        float elapsed = 0f;
        Color c = fadeImage.color;

        float initialVolume = (bgmSource != null) ? bgmSource.volume : 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);

            // 畫面黑幕從透明 → 黑
            c.a = t;
            fadeImage.color = c;

            // 音樂音量淡出
            if (bgmSource != null)
                bgmSource.volume = Mathf.Lerp(initialVolume, 0f, t);

            yield return null;
        }

        // 最終設定
        if (bgmSource != null)
            bgmSource.volume = 0f;
    }
}
