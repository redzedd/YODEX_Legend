using Animancer;
using UnityEngine;

namespace Player.Locomotion
{
    /// <summary>
    /// 封裝 AnimancerComponent 的播放與 Mixer 參數平滑。狀態類別只透過此層操作動畫。
    /// 狀態推進改由呼叫端輪詢 AnimancerState.NormalizedTime，因此不在此層註冊 OnEnd 事件。
    /// </summary>
    public sealed class LocomotionAnimatorDriver
    {
        /// <summary>Flinch 上半身疊加使用的 Animancer Layer 索引(主身體在 Layer 0)</summary>
        private const int FlinchLayerIndex = 1;

        private readonly AnimancerComponent _animancer;
        private float _leanParameterVelocity;
        private AnimancerLayer _flinchLayer;
        private AnimancerState _flinchState;
        private bool _flinchFadingOut;

        public LocomotionAnimatorDriver(AnimancerComponent animancer)
        {
            _animancer = animancer;
        }

        /// <summary>
        /// 播放指定 ClipTransition 並回傳其 AnimancerState，呼叫端可在 Tick 中輪詢 NormalizedTime 推進階段。
        /// </summary>
        public AnimancerState Play(ClipTransition transition, float fadeDuration)
        {
            return _animancer.Play(transition, fadeDuration);
        }

        /// <summary>
        /// 強制從頭淡入播放 — 即使目標 transition 正好是當前狀態（Dodge → Dodge 連擊情境),
        /// 也會建立新的淡入過渡,而不是重用現有 state 直接切換。
        /// 使用 Animancer 的 FadeMode.FromStart 處理「同 clip 連擊不出現 fade」的問題。
        /// </summary>
        public AnimancerState PlayFromStart(ClipTransition transition, float fadeDuration)
        {
            return _animancer.Play(transition, fadeDuration, FadeMode.FromStart);
        }

        /// <summary>
        /// 播放 LinearMixerTransition 並回傳對應 state，供呼叫端之後調整 Parameter。
        /// </summary>
        public LinearMixerState PlayMixer(LinearMixerTransition transition, float fadeDuration)
        {
            _animancer.Play(transition, fadeDuration);
            return transition.State;
        }

        /// <summary>
        /// 以 SmoothDamp 推動 Mixer 參數，避免硬切。
        /// </summary>
        public void SmoothMixerParameter(LinearMixerState mixerState, float target, float smoothTime, float deltaTime)
        {
            if (mixerState == null)
            {
                return;
            }
            float current = mixerState.Parameter;
            float next = Mathf.SmoothDamp(current, target, ref _leanParameterVelocity, smoothTime, Mathf.Infinity, deltaTime);
            mixerState.Parameter = next;
        }

        public void ResetLeanSmoothing()
        {
            _leanParameterVelocity = 0f;
        }

        /// <summary>
        /// 設置 Flinch Layer 的 AvatarMask。Controller 於初始化時呼叫一次,
        /// mask 為 null 時 Flinch 會在全身播放(仍可用,但失去「邊走邊晃」效果)。
        /// </summary>
        public void ConfigureFlinchLayer(AvatarMask mask)
        {
            _flinchLayer = _animancer.Layers[FlinchLayerIndex];
            if (_flinchLayer == null)
            {
                return;
            }
            if (mask != null)
            {
                _flinchLayer.Mask = mask;
            }
            _flinchLayer.Weight = 0f;
        }

        /// <summary>
        /// 在 Flinch Layer 上播放指定動畫。Weight 立即設 1(即時反饋),
        /// TickFlinch 會在 clip 播完後自動淡出至 0。
        /// </summary>
        public void PlayFlinch(ClipTransition transition, float fadeDuration)
        {
            if (_flinchLayer == null || transition == null)
            {
                return;
            }
            _flinchLayer.Weight = 1f;
            _flinchState = _flinchLayer.Play(transition, fadeDuration);
            _flinchFadingOut = false;
        }

        /// <summary>
        /// 每幀呼叫以維護 Flinch Layer:clip 播完後開始淡出 Layer Weight → 0,降到 0 時清理 state。
        /// 由 Controller 在 Update 中呼叫。
        /// </summary>
        public void TickFlinch(float deltaTime, float fadeOutDuration)
        {
            if (_flinchLayer == null || _flinchState == null)
            {
                return;
            }
            if (!_flinchFadingOut && _flinchState.NormalizedTime >= 1f)
            {
                _flinchFadingOut = true;
            }
            if (!_flinchFadingOut)
            {
                return;
            }
            if (fadeOutDuration <= 0f)
            {
                _flinchLayer.Weight = 0f;
            }
            else
            {
                float step = deltaTime / fadeOutDuration;
                _flinchLayer.Weight = Mathf.Max(0f, _flinchLayer.Weight - step);
            }
            if (_flinchLayer.Weight <= 0.001f)
            {
                _flinchLayer.Weight = 0f;
                _flinchState = null;
                _flinchFadingOut = false;
            }
        }

        /// <summary>
        /// 強制中止 Flinch(例如玩家被 Stagger,Flinch 應立即讓位給全身受擊動畫)。
        /// </summary>
        public void StopFlinch()
        {
            if (_flinchLayer == null)
            {
                return;
            }
            _flinchLayer.Weight = 0f;
            _flinchState = null;
            _flinchFadingOut = false;
        }

        /// <summary>
        /// 判定目前 state 是否應該開始向下個動畫淡出。
        /// 在剩餘 nextFadeDuration 秒時回傳 true，使 crossfade 的淡入與當前動畫的剩餘時間重疊，
        /// 避免 Start/Turn 動畫先播到最後一幀靜止（hold last frame）才淡出，造成切換瞬間位移歸零的頓挫。
        /// </summary>
        public static bool IsReadyForExitFade(AnimancerState state, float nextFadeDuration, float minNormalizedTime = 0.5f)
        {
            if (state == null)
            {
                return false;
            }
            float length = state.Length;
            if (length <= 0f)
            {
                return state.NormalizedTime >= 1f;
            }
            float threshold = Mathf.Clamp(1f - nextFadeDuration / length, minNormalizedTime, 1f);
            return state.NormalizedTime >= threshold;
        }
    }
}
