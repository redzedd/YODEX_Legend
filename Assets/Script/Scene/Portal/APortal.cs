using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class APortal : MonoBehaviour
{
    [Tooltip("要傳送到的場景名稱")]
    public string targetSceneName;
    public float enterDuration = 2.5f;
    public float fadeOutSpeedMultiplier = 1.5f;
    public Image fadeImage; // ⬅️ 黑幕 UI
    public AudioSource bgmAudioSource; // 背景音樂音源

    private float initialVolume;       // 初始音量記錄
    private bool isPlayerInTrigger = false;
    private float timer = 0f;
    private Coroutine fadeCoroutine;
    private Coroutine revertCoroutine;
    private bool hasTeleported = false;

    private void Start()
    {
        if (bgmAudioSource != null)
        {
            initialVolume = bgmAudioSource.volume;
        }
    }

    private void Update()
    {
        if (hasTeleported) return;

        if (isPlayerInTrigger)
        {
            fadeImage.gameObject.SetActive(true);
            timer += Time.deltaTime;

            if (timer >= enterDuration)
            {
                timer = enterDuration;
                if (fadeCoroutine == null)
                    fadeCoroutine = StartCoroutine(FadeAndTeleport());
            }
        }
        else
        {
            if (timer > 0f)
            {
                timer -= Time.deltaTime * fadeOutSpeedMultiplier;
                timer = Mathf.Max(timer, 0f);

                float progress = timer / enterDuration;

                if (fadeImage != null && !hasTeleported)
                {
                    Color baseColor = fadeImage.color;
                    baseColor.a = progress;
                    fadeImage.color = baseColor;
                }

                if (bgmAudioSource != null && !hasTeleported)
                {
                    bgmAudioSource.volume = Mathf.Lerp(initialVolume, 0f, progress);
                }
            }
            else
            {
                fadeImage.gameObject.SetActive(false);
            }
        }

        // 淡入黑幕依比例
        if (fadeImage != null && !hasTeleported)
        {
            Color baseColor = fadeImage.color;
            baseColor.a = timer / enterDuration;
            fadeImage.color = baseColor;
        }

        if (bgmAudioSource != null && !hasTeleported)
        {
            bgmAudioSource.volume = Mathf.Lerp(initialVolume, 0f, timer / enterDuration);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInTrigger = true;

            // 停止還原協程，恢復正常累積
            if (revertCoroutine != null)
            {
                StopCoroutine(revertCoroutine);
                revertCoroutine = null;
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInTrigger = false;

            // 中止傳送協程
            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
                fadeCoroutine = null;
            }

            // 開始還原協程（畫面與音量）
            if (revertCoroutine != null)
                StopCoroutine(revertCoroutine);
            revertCoroutine = StartCoroutine(RevertFadeOut());
        }
    }

    IEnumerator FadeAndTeleport()
    {
        // 補：停止還原協程避免干擾
        if (revertCoroutine != null)
        {
            StopCoroutine(revertCoroutine);
            revertCoroutine = null;
        }

        // 確保畫面與音量補滿
        if (fadeImage != null)
        {
            Color baseColor = fadeImage.color;
            float t = baseColor.a;
            while (t < 1f)
            {
                t += Time.deltaTime;
                baseColor.a = Mathf.Clamp01(t);
                fadeImage.color = baseColor;
                yield return null;
            }
        }

        if (bgmAudioSource != null)
            bgmAudioSource.volume = 0f;

        yield return new WaitForSeconds(0.3f); // 稍等再傳送

        hasTeleported = true; // ✅ 移到最後階段才設定
        SceneManager.LoadScene(targetSceneName);
    }

    IEnumerator RevertFadeOut()
    {
        while (timer > 0f && !isPlayerInTrigger)
        {
            timer -= Time.deltaTime * fadeOutSpeedMultiplier;
            timer = Mathf.Max(timer, 0f);

            float progress = timer / enterDuration;

            if (fadeImage != null)
            {
                Color baseColor = fadeImage.color;
                baseColor.a = progress;
                fadeImage.color = baseColor;
            }

            if (bgmAudioSource != null)
            {
                bgmAudioSource.volume = Mathf.Lerp(initialVolume, 0f, progress);
            }

            yield return null;
        }

        revertCoroutine = null;
    }

}