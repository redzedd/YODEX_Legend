using Animancer;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 掛在武器 Prefab 上：完全複製父物件（角色）當前播放的動畫，支援單一 Clip 與 Mixer Transition。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class WeaponAnimationSync : MonoBehaviour
    {
        [SerializeField, Tooltip("父物件（角色）的 AnimancerComponent。留空會自動向上層搜尋")]
        private AnimancerComponent _parentAnimancer;

        [SerializeField, Tooltip("武器自己的 AnimancerComponent。留空會自動抓本物件上的元件")]
        private AnimancerComponent _weaponAnimancer;

        [SerializeField, Tooltip("要監聽父物件的哪一個 Animancer Layer。一般填 0")]
        private int _parentLayerIndex = 0;

        [SerializeField, Tooltip("時間同步容差（NormalizedTime 0~1）。武器與父物件動畫時間差超過此值才重新對齊。建議 0.03~0.1")]
        private float _timeResyncThreshold = 0.05f;

        private object _lastParentKey;
        private AnimancerState _currentWeaponState;

        private void Reset()
        {
            _parentAnimancer = GetComponentInParent<AnimancerComponent>();
            _weaponAnimancer = GetComponent<AnimancerComponent>();
        }

        private void LateUpdate()
        {
            if (_parentAnimancer == null || _weaponAnimancer == null)
            {
                return;
            }
            if (_parentLayerIndex < 0 || _parentLayerIndex >= _parentAnimancer.Layers.Count)
            {
                return;
            }
            SyncUpdateMode();
            AnimancerState parentState = _parentAnimancer.Layers[_parentLayerIndex].CurrentState;
            object parentKey = parentState != null ? parentState.Key : null;
            if (!ReferenceEquals(parentKey, _lastParentKey))
            {
                _lastParentKey = parentKey;
                _currentWeaponState = PlayMatchingState(parentState);
                if (_currentWeaponState != null && parentState != null)
                {
                    _currentWeaponState.NormalizedTime = parentState.NormalizedTime;
                }
            }
            if (_currentWeaponState == null || parentState == null)
            {
                return;
            }
            _currentWeaponState.Speed = parentState.Speed;
            float timeDiff = parentState.NormalizedTime - _currentWeaponState.NormalizedTime;
            if (Mathf.Abs(timeDiff) > _timeResyncThreshold)
            {
                _currentWeaponState.NormalizedTime = parentState.NormalizedTime;
            }
            SyncMixerParameter(parentState, _currentWeaponState);
        }

        private void SyncUpdateMode()
        {
            if (_parentAnimancer.Animator == null || _weaponAnimancer.Animator == null)
            {
                return;
            }
            // 死亡時角色切 UnscaledTime(timeScale=0 下才播得動),武器須跟著切。
            // 必須用 Animancer 的 UpdateMode 屬性(非 Animator.updateMode)— 它的 setter 會同時把
            // 動畫圖的 DirectorUpdateMode 設成 UnscaledGameTime,否則 graph 仍用縮放時間,timeScale=0 時凍結不動。
            AnimatorUpdateMode parentMode = _parentAnimancer.UpdateMode;
            if (_weaponAnimancer.UpdateMode != parentMode)
            {
                _weaponAnimancer.UpdateMode = parentMode;
            }
        }

        private AnimancerState PlayMatchingState(AnimancerState parentState)
        {
            if (parentState == null)
            {
                _weaponAnimancer.Stop();
                return null;
            }
            if (parentState.Key is ITransition transition)
            {
                return _weaponAnimancer.Play(transition);
            }
            if (parentState.Clip != null)
            {
                return _weaponAnimancer.Play(parentState.Clip);
            }
            _weaponAnimancer.Stop();
            return null;
        }

        private void SyncMixerParameter(AnimancerState source, AnimancerState target)
        {
            if (source is MixerState<float> sourceFloat && target is MixerState<float> targetFloat)
            {
                targetFloat.Parameter = sourceFloat.Parameter;
                return;
            }
            if (source is MixerState<Vector2> sourceVec && target is MixerState<Vector2> targetVec)
            {
                targetVec.Parameter = sourceVec.Parameter;
            }
        }
    }
}
