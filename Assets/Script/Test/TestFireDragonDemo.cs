using UnityEngine;
using UnityEngine.InputSystem;
using Animancer;

/// <summary>
/// 宣傳片演示用:按下指定按鍵觸發噴火龍噴火動畫,
/// 並由 AnimationEvent 呼叫 PlayFireVFX / StopFireVFX 控制噴火粒子特效。
/// 停止時使用 ParticleSystem.Stop(StopEmitting),既有粒子走完生命週期自然消散,不會突兀消失。
/// </summary>
public class TestFireDragonDemo : MonoBehaviour
{
    [Header("Animancer")]
    [SerializeField, Tooltip("噴火龍的 AnimancerComponent (留空則於 Awake 從本物件抓取)")]
    private AnimancerComponent _animancer;
    [SerializeField, Tooltip("噴火動畫 (建議設為 One-shot,於此 Clip 中加入 AnimationEvent)")]
    private ClipTransition _fireBreathAnim;
    [SerializeField, Tooltip("噴火結束後回到的待機動畫 (選填)")]
    private ClipTransition _idleAnim;

    [Header("特效")]
    [SerializeField, Tooltip("噴火 ParticleSystem (建議掛在龍嘴位置,Play On Awake 請關閉)")]
    private ParticleSystem _fireVFX;
    [SerializeField, Tooltip("噴火音效 (選填,與 VFX 同步播放/停止)")]
    private AudioSource _fireAudio;

    [Header("輸入")]
    [SerializeField, Tooltip("觸發噴火的按鍵繫結 (預設 F)")]
    private InputAction _fireAction = new InputAction("DragonFire", InputActionType.Button, "<Keyboard>/f");

    private bool _isBreathing;

    private void Awake()
    {
        if (_animancer == null)
        {
            _animancer = GetComponent<AnimancerComponent>();
        }
        if (_fireVFX != null && _fireVFX.isPlaying)
        {
            _fireVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void OnEnable()
    {
        _fireAction.Enable();
        _fireAction.performed += OnFirePerformed;
    }

    private void OnDisable()
    {
        _fireAction.performed -= OnFirePerformed;
        _fireAction.Disable();
    }

    private void OnFirePerformed(InputAction.CallbackContext ctx) => TriggerFireBreath();

    public void TriggerFireBreath()
    {
        if (_isBreathing) return;
        if (_animancer == null || _fireBreathAnim == null)
        {
            Debug.LogWarning("[TestFireDragonDemo] 未設定 Animancer 或噴火動畫", this);
            return;
        }
        _isBreathing = true;
        AnimancerState state = _animancer.Play(_fireBreathAnim);
        state.Events(this).OnEnd = OnFireBreathEnd;
    }

    private void OnFireBreathEnd()
    {
        _isBreathing = false;
        StopFireVFX();
        if (_idleAnim != null)
        {
            _animancer.Play(_idleAnim);
        }
    }

    /// <summary>供 AnimationEvent 呼叫:開始噴出火焰粒子</summary>
    public void PlayFireVFX()
    {
        if (_fireVFX != null)
        {
            _fireVFX.Play(true);
        }
        if (_fireAudio != null && !_fireAudio.isPlaying)
        {
            _fireAudio.Play();
        }
    }

    /// <summary>供 AnimationEvent 呼叫:停止發射新粒子,既有粒子自然消散</summary>
    public void StopFireVFX()
    {
        if (_fireVFX != null && _fireVFX.isPlaying)
        {
            _fireVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        if (_fireAudio != null && _fireAudio.isPlaying)
        {
            _fireAudio.Stop();
        }
    }
}
