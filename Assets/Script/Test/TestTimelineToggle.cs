using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// 宣傳片演示用:控制 PlayableDirector,場景啟動時不自動播放,
/// 按下設定按鍵後才觸發 Timeline 播放。
/// 支援兩種模式:每次按鍵從頭播 (預設) / 按鍵切換 播放↔暫停。
/// 也可直接呼叫 PlayTimeline() / StopTimeline() 供其他腳本串接。
/// </summary>
public class TestTimelineToggle : MonoBehaviour
{
    [Header("Timeline")]
    [SerializeField, Tooltip("要控制的 PlayableDirector (留空則於 Awake 從本物件抓)")]
    private PlayableDirector _director;

    [Header("輸入")]
    [SerializeField, Tooltip("觸發 Timeline 的按鍵 (預設 T)。欲與跑步切換同鍵,改綁 <Keyboard>/r 即可")]
    private InputAction _toggleAction = new InputAction("ToggleTimeline", InputActionType.Button, "<Keyboard>/t");

    [Header("行為")]
    [SerializeField, Tooltip("每次按鍵都從頭播放 (關閉 = 按鍵切換 播放/暫停)")]
    private bool _restartEachPress = true;

    public PlayableDirector Director => _director;
    public bool IsPlaying => _director != null && _director.state == PlayState.Playing;

    private void Awake()
    {
        if (_director == null)
        {
            _director = GetComponent<PlayableDirector>();
        }
        if (_director != null)
        {
            _director.playOnAwake = false;
            _director.Stop();
            _director.time = 0.0;
            _director.Evaluate();
        }
    }

    private void OnEnable()
    {
        _toggleAction.Enable();
        _toggleAction.performed += OnTogglePerformed;
    }

    private void OnDisable()
    {
        _toggleAction.performed -= OnTogglePerformed;
        _toggleAction.Disable();
    }

    private void OnTogglePerformed(InputAction.CallbackContext ctx)
    {
        if (_director == null)
        {
            Debug.LogWarning("[TestTimelineToggle] 未指定 PlayableDirector", this);
            return;
        }
        if (_restartEachPress)
        {
            PlayTimeline();
            return;
        }
        if (IsPlaying)
        {
            _director.Pause();
        }
        else
        {
            _director.Play();
        }
    }

    /// <summary>從頭播放 Timeline</summary>
    public void PlayTimeline()
    {
        if (_director == null) return;
        _director.time = 0.0;
        _director.Play();
    }

    /// <summary>停止並回到時間 0</summary>
    public void StopTimeline()
    {
        if (_director == null) return;
        _director.Stop();
        _director.time = 0.0;
        _director.Evaluate();
    }
}
