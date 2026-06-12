using UnityEngine;
using Player.Input;

namespace GAS
{
    /// <summary>
    /// ASC 事件橋接 — 訂閱 AbilitySystemComponent 的能力生命週期事件,
    /// 轉換為 NewGASPlayerController 的 TopState 切換。
    /// 職責單一:只做「事件 → API 呼叫」的轉接,不包含戰鬥邏輯。
    /// </summary>
    [RequireComponent(typeof(NewGASPlayerController))]
    [RequireComponent(typeof(AbilitySystemComponent))]
    public sealed class PlayerAbilityBridge : MonoBehaviour
    {
        private NewGASPlayerController _controller;
        private AbilitySystemComponent _asc;

        [ContextMenu("Debug/Force Attack")]
private void Debug_ForceAttack() => _asc.TryActivateAbility(GameplayTags.Ability.Attack.Light);

        private void Awake()
        {
            _controller = GetComponent<NewGASPlayerController>();
            _asc = GetComponent<AbilitySystemComponent>();
            _asc.OnAbilityActivated += HandleAbilityActivated;
            _asc.OnAbilityEnded += HandleAbilityEnded;
        }

        private void OnDestroy()
        {
            if (_asc != null)
            {
                _asc.OnAbilityActivated -= HandleAbilityActivated;
                _asc.OnAbilityEnded -= HandleAbilityEnded;
            }
        }

        private void HandleAbilityActivated(GameplayAbilitySpec spec)
        {
            if (_asc.DebugMode)
            {
                Debug.Log($"[PlayerAbilityBridge] 能力啟動: {spec.AbilityDef?.AbilityName} → EnterAbilityState");
            }
            _controller.EnterAbilityState();
        }

        private void HandleAbilityEnded(GameplayAbilitySpec spec, bool wasCancelled)
        {
            // 只有在沒有其他活躍能力時才還給 Locomotion
            foreach (GameplayAbilitySpec other in _asc.GetAllAbilities())
            {
                if (other != spec && other.IsActive)
                {
                    if (_asc.DebugMode)
                    {
                        Debug.Log($"[PlayerAbilityBridge] 能力結束: {spec.AbilityDef?.AbilityName} (仍有其他活躍能力,維持 Ability 狀態)");
                    }
                    return;
                }
            }
            if (_asc.DebugMode)
            {
                Debug.Log($"[PlayerAbilityBridge] 能力結束: {spec.AbilityDef?.AbilityName}, cancelled={wasCancelled} → ExitAbilityState");
            }
            _controller.ExitAbilityState();
        }

#if UNITY_EDITOR
        [ContextMenu("Debug/Check SystemInputReader State")]
        private void Debug_CheckInputState()
        {
            var pih = SystemInputReader.Instance;
            if (pih == null)
            {
                Debug.LogWarning("[Debug] SystemInputReader.Instance 為 NULL — AbilityInputHandler 所有輸入被擋");
            }
            else
            {
                Debug.Log($"[Debug] SystemInputReader.Instance 存在, IsPlayerInputEnabled = {pih.IsPlayerInputEnabled}");
            }
        }
#endif
    }
}
