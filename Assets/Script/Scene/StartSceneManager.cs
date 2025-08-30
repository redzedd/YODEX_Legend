using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class StartSceneManager : MonoBehaviour
{
    public string sceneToLoad = "GameScene"; // �n���J�������W��

    [Header("UI & Audio")]
    public Text startText;
    public Image fadeImage;                  // �¹� Image�]��l Alpha = 0�^
    public AudioSource bgmSource;            // �I�����ּ���
    public AudioClip bgmClip;
    public AudioClip startSFX;               // ���䭵�ġ]�i��^

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

        // ��l�ƶ¹��z��
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
        // ����i�J���ġ]�i��^
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

            // �e���¹��q�z�� �� ��
            c.a = t;
            fadeImage.color = c;

            // ���֭��q�H�X
            if (bgmSource != null)
                bgmSource.volume = Mathf.Lerp(initialVolume, 0f, t);

            yield return null;
        }

        // �̲׳]�w
        if (bgmSource != null)
            bgmSource.volume = 0f;
    }
}
