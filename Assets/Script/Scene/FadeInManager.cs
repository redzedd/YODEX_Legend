using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Player.Input;

public class FadeInManager : MonoBehaviour
{
    public Image fadeImage;              // �¹��ϼh�]Alpha=1�^
    public float fadeDuration = 1.5f;    // �H�J�ɶ�

    public AudioSource bgmSource;        // ���ּ���
    public AudioClip bgmClip;            // BGM ���Ĥ��q
    public float maxVolume = 1f;         // ���ֳ̲׭��q

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
            SystemInputReader.Instance.DisablePlayerInput();
            SystemInputReader.Instance.DisableUIMapInput();

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);

            // �e���H�J
            c.a = 1f - t;
            fadeImage.color = c;

            // ���ֲH�J
            if (bgmSource != null)
                bgmSource.volume = Mathf.Lerp(0f, maxVolume, t);

            yield return null;
        }

        // �̲׳B�z�]�O�I�^
        //if (fadeImage != null)
        //{
        //    c.a = 0f;
        //    fadeImage.color = c;
        //    fadeImage.gameObject.SetActive(false);
        //}
        SystemInputReader.Instance.EnablePlayerInput();
        SystemInputReader.Instance.EnableUIMapInput();

        fadeImage.gameObject.SetActive(false);
        Portal.SetActive(true);
        if (bgmSource != null)
            bgmSource.volume = maxVolume;
    }
}