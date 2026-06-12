using UnityEngine;
using UnityEngine.InputSystem;
using Animancer;

/// <summary>
/// 宣傳片演示用:按下按鍵切換怪物 待機 ↔ 跑步 動畫 (Toggle)。
/// 可選擇是否讓怪物真實向前位移 (沿 transform.forward)。
/// 若搭配 TestFaceTarget,怪物會面向目標跑過去。
/// </summary>
public class TestMonsterRunToggle : MonoBehaviour
{
    [Header("Animancer")]
    [SerializeField, Tooltip("怪物的 AnimancerComponent (留空則於 Awake 自動抓)")]
    private AnimancerComponent _animancer;
    [SerializeField, Tooltip("待機動畫 (Loop)")]
    private ClipTransition _idleAnim;
    [SerializeField, Tooltip("跑步動畫 (Loop)")]
    private ClipTransition _runAnim;

    [Header("位移 (選填)")]
    [SerializeField, Tooltip("跑步時每秒沿 transform.forward 位移公尺 (0 = 原地跑步,僅播動畫)"), Min(0f)]
    private float _moveSpeed = 0f;

    [Header("輸入")]
    [SerializeField, Tooltip("切換跑步 / 待機的按鍵 (預設 R)")]
    private InputAction _toggleAction = new InputAction("ToggleRun", InputActionType.Button, "<Keyboard>/r");

    private bool _running;

    public bool IsRunning => _running;

    private void Awake()
    {
        if (_animancer == null)
        {
            _animancer = GetComponent<AnimancerComponent>();
        }
    }

    private void Start()
    {
        if (_animancer != null && _idleAnim != null)
        {
            _animancer.Play(_idleAnim);
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

    private void Update()
    {
        if (!_running || _moveSpeed <= 0f) return;
        transform.position += transform.forward * (_moveSpeed * Time.deltaTime);
    }

    private void OnTogglePerformed(InputAction.CallbackContext ctx) => ToggleRun();

    public void ToggleRun()
    {
        SetRunning(!_running);
    }

    public void SetRunning(bool running)
    {
        if (_running == running) return;
        _running = running;
        if (_animancer == null) return;
        ClipTransition target = _running ? _runAnim : _idleAnim;
        if (target != null)
        {
            _animancer.Play(target);
        }
    }
}
