using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class FadeInManager : MonoBehaviour
{
    public Image fadeImage;              // 黑幕圖層（Alpha=1）
    public float fadeDuration = 1.5f;    // 淡入時間

    public AudioSource bgmSource;        // 音樂播放器
    public AudioClip bgmClip;            // BGM 音效片段
    public float maxVolume = 1f;         // 音樂最終音量

    public GameObject Portal;
    void Start()
    {
        if (bgmSource != null && bgmClip != null)
        {
            bgmSource.clip = bgmClip;
            bgmSource.volume = 0f;
            bgmSource.Play();
        }

        Portal.SetActive(false);
        StartCoroutine(FadeInCoroutine());
    }

    IEnumerator FadeInCoroutine()
    {
        float elapsed = 0f;
        Color c = fadeImage.color;

        while (elapsed < fadeDuration)
        {
            PlayerInputHandler.Instance.DisablePlayerInput();
            PlayerInputHandler.Instance.DisableUIMapInput();

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);

            // 畫面淡入
            c.a = 1f - t;
            fadeImage.color = c;

            // 音樂淡入
            if (bgmSource != null)
                bgmSource.volume = Mathf.Lerp(0f, maxVolume, t);

            yield return null;
        }

        // 最終處理（保險）
        //if (fadeImage != null)
        //{
        //    c.a = 0f;
        //    fadeImage.color = c;
        //    fadeImage.gameObject.SetActive(false);
        //}
        PlayerInputHandler.Instance.EnablePlayerInput();
        PlayerInputHandler.Instance.EnableUIMapInput();

        fadeImage.gameObject.SetActive(false);
        Portal.SetActive(true);
        if (bgmSource != null)
            bgmSource.volume = maxVolume;
    }
}