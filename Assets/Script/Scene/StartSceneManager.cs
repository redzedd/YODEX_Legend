using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 開場場景管理器：播放 BGM、閃爍提示文字，按任意鍵後透過 SceneTransitionManager 載入遊戲場景。
/// </summary>
public class StartSceneManager : MonoBehaviour
{
    [Header("場景設定")]
    [SerializeField, Tooltip("要載入的目標場景名稱")]
    private string _sceneToLoad = "GameScene";

    [Header("UI")]
    [SerializeField, Tooltip("「按任意鍵開始」提示文字")]
    private Text _startText;

    [Header("音效")]
    [SerializeField, Tooltip("BGM 音源")]
    private AudioSource _bgmSource;
    [SerializeField, Tooltip("BGM 音樂片段")]
    private AudioClip _bgmClip;
    [SerializeField, Tooltip("按下任意鍵時的音效（可留空）")]
    private AudioClip _startSFX;

    [Header("設定")]
    [SerializeField, Tooltip("文字閃爍速度")]
    private float _textFlashSpeed = 2f;
    [SerializeField, Tooltip("BGM 最大音量"), Range(0f, 1f)]
    private float _bgmMaxVolume = 1f;

    private bool _hasPressed;

    void Start()
    {
        InitializeBGM();
    }

    void Update()
    {
        FlashText();
        if (_hasPressed) return;
        if (!Input.anyKeyDown) return;
        _hasPressed = true;
        OnStartPressed();
    }

    private void InitializeBGM()
    {
        if (_bgmSource == null || _bgmClip == null) return;
        _bgmSource.clip = _bgmClip;
        _bgmSource.volume = _bgmMaxVolume;
        _bgmSource.loop = true;
        _bgmSource.Play();
    }

    private void FlashText()
    {
        if (_startText == null) return;
        float alpha = Mathf.Lerp(0.3f, 1f, Mathf.PingPong(Time.time * _textFlashSpeed, 1f));
        Color color = _startText.color;
        color.a = alpha;
        _startText.color = color;
    }

    private void OnStartPressed()
    {
        // 播放開始音效
        if (_startSFX != null && _bgmSource != null)
            _bgmSource.PlayOneShot(_startSFX);
        // 透過轉場管理器載入目標場景
        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.LoadScene(_sceneToLoad);
        else
            SceneManager.LoadScene(_sceneToLoad);
    }
}
