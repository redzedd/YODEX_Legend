using UnityEngine;
using UnityEngine.InputSystem;

namespace GAS.UI
{
    /// <summary>
    /// HP / MP 鍵盤 Debug 控制 — 僅在 Editor / Development Build 啟用
    /// 用於測試 PlayerHPMPBarUI 的 Fill 動畫與事件
    /// </summary>
    public class PlayerHPMPDebugInput : MonoBehaviour
    {
        [Header("玩家參考")]
        [Tooltip("玩家 ASC（留空則自動尋找場景中第一個）")]
        [SerializeField] private AbilitySystemComponent _asc;

        [Header("Debug 數值")]
        [Tooltip("每次按鍵變動的 HP 量")]
        [SerializeField] private float _healthDelta = 10f;

        [Tooltip("每次按鍵變動的 MP 量")]
        [SerializeField] private float _manaDelta = 10f;

        [Header("按鍵設定（Input System Key）")]
        [Tooltip("HP - ：扣血")]
        [SerializeField] private Key _healthDownKey = Key.F1;

        [Tooltip("HP + ：補血")]
        [SerializeField] private Key _healthUpKey = Key.F2;

        [Tooltip("MP - ：消耗魔力")]
        [SerializeField] private Key _manaDownKey = Key.F3;

        [Tooltip("MP + ：恢復魔力")]
        [SerializeField] private Key _manaUpKey = Key.F4;

        private CombatAttributeSet _cachedAttrSet;

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_asc == null)
            {
                _asc = FindFirstObjectByType<AbilitySystemComponent>();
            }
            if (_asc == null)
            {
                Debug.LogWarning("[PlayerHPMPDebugInput] 找不到 AbilitySystemComponent，Debug 鍵不會作用。");
                return;
            }
            _cachedAttrSet = _asc.GetAttributeSet<CombatAttributeSet>();
            if (_cachedAttrSet == null)
            {
                Debug.LogWarning("[PlayerHPMPDebugInput] ASC 上沒有 CombatAttributeSet，Debug 鍵不會作用。");
            }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (_cachedAttrSet == null) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb[_healthDownKey].wasPressedThisFrame)
            {
                _cachedAttrSet.ApplyDamage(_healthDelta, _asc);
                Debug.Log($"[Debug] HP -{_healthDelta} → {_cachedAttrSet.Health.CurrentValue:F0}/{_cachedAttrSet.MaxHealth.CurrentValue:F0}");
            }
            if (kb[_healthUpKey].wasPressedThisFrame)
            {
                _cachedAttrSet.ApplyHealing(_healthDelta);
                Debug.Log($"[Debug] HP +{_healthDelta} → {_cachedAttrSet.Health.CurrentValue:F0}/{_cachedAttrSet.MaxHealth.CurrentValue:F0}");
            }
            if (kb[_manaDownKey].wasPressedThisFrame)
            {
                _cachedAttrSet.TryConsumeMana(_manaDelta);
                Debug.Log($"[Debug] MP -{_manaDelta} → {_cachedAttrSet.Mana.CurrentValue:F0}/{_cachedAttrSet.MaxMana.CurrentValue:F0}");
            }
            if (kb[_manaUpKey].wasPressedThisFrame)
            {
                _cachedAttrSet.RestoreMana(_manaDelta);
                Debug.Log($"[Debug] MP +{_manaDelta} → {_cachedAttrSet.Mana.CurrentValue:F0}/{_cachedAttrSet.MaxMana.CurrentValue:F0}");
            }
        }
#endif
    }
}
