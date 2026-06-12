using UnityEngine;
using UnityEngine.InputSystem;
using GAS;

namespace EnemyAI.Test
{
    /// <summary>
    /// 敵人受擊測試器 — 用鍵盤模擬各種類型的攻擊命中，驗證 EnemyController 的分級受擊邏輯
    /// 攻擊方向自動從測試者朝敵人計算；可選的 DodgeBeforeHit 模擬玩家招架時序（之後再加）
    /// </summary>
    public class DamageTester : MonoBehaviour
    {
        #region Serialized Fields

        [Header("目標")]
        [SerializeField] [Tooltip("要測試的敵人 EnemyController")]
        private EnemyController _target;

        [Header("輕攻擊（B）— 只抖動不切動畫（AttackTier.Light）")]
        [SerializeField] [Tooltip("輕攻擊傷害值")]
        private float _lightAttackDamage = 5f;

        [SerializeField] [Tooltip("輕攻擊 Poise 扣量")]
        private float _lightAttackPoiseDamage = 5f;

        [Header("一般攻擊（K）— 打斷 Idle/Walk 不打斷攻擊（AttackTier.Normal）")]
        [SerializeField] [Tooltip("一般攻擊傷害值")]
        private float _lightDamage = 10f;

        [SerializeField] [Tooltip("一般攻擊 Poise 扣量 — 多次累積後觸發 Break")]
        private float _lightPoiseDamage = 15f;

        [Header("重攻擊（J）— 能打斷攻擊霸體（AttackTier.Heavy）")]
        [SerializeField] [Tooltip("重攻擊傷害值")]
        private float _heavyDamage = 30f;

        [SerializeField] [Tooltip("重攻擊 Poise 扣量")]
        private float _heavyPoiseDamage = 40f;

        [Header("直接觸發（L）— 一擊扣滿 Poise")]
        [SerializeField] [Tooltip("一擊扣的 Poise 數量 — 設大於 MaxPoise 直接觸發 Stagger")]
        private float _instantStaggerPoiseDamage = 200f;

        [Header("純扣血（H）— 不走 IHitReceiver")]
        [SerializeField] [Tooltip("純扣血傷害量 — 直接呼叫 EnemyController.TakeDamage，跳過受擊反應")]
        private float _pureDamage = 20f;

        [Header("快捷鍵")]
        [SerializeField] [Tooltip("輕攻擊（只抖動不切動畫）")]
        private Key _lightAttackKey = Key.B;

        [SerializeField] [Tooltip("一般攻擊（打斷 Idle/Walk）")]
        private Key _lightKey = Key.K;

        [SerializeField] [Tooltip("重攻擊（會打斷攻擊霸體）")]
        private Key _heavyKey = Key.J;

        [SerializeField] [Tooltip("一擊觸發失衡（Stagger）")]
        private Key _staggerKey = Key.L;

        [SerializeField] [Tooltip("純扣血不觸發受擊反應")]
        private Key _pureDamageKey = Key.H;

        [SerializeField] [Tooltip("強制觸發死亡")]
        private Key _killKey = Key.M;

        [SerializeField] [Tooltip("純抖動測試 — 直接呼叫 PlayFlinchShake，跳過 OnHit 流程")]
        private Key _flinchKey = Key.F;

        [Header("Debug")]
        [SerializeField] [Tooltip("勾選後印出每次操作的結果與目標當前狀態")]
        private bool _logEvents = true;

        #endregion

        #region Unity Lifecycle

        private void Update()
        {
            if (_target == null || Keyboard.current == null) return;

            if (Keyboard.current[_lightAttackKey].wasPressedThisFrame)
            {
                SimulateHit(_lightAttackDamage, _lightAttackPoiseDamage, AttackTier.Light);
            }
            if (Keyboard.current[_lightKey].wasPressedThisFrame)
            {
                SimulateHit(_lightDamage, _lightPoiseDamage, AttackTier.Normal);
            }
            if (Keyboard.current[_heavyKey].wasPressedThisFrame)
            {
                SimulateHit(_heavyDamage, _heavyPoiseDamage, AttackTier.Heavy);
            }
            if (Keyboard.current[_staggerKey].wasPressedThisFrame)
            {
                TriggerInstantStagger();
            }
            if (Keyboard.current[_pureDamageKey].wasPressedThisFrame)
            {
                _target.TakeDamage(_pureDamage);
                Log($"純扣血 {_pureDamage} | HP {_target.CurrentHealth:F0}/{_target.MaxHealth:F0}");
            }
            if (Keyboard.current[_killKey].wasPressedThisFrame)
            {
                _target.TriggerDeath();
                Log("強制觸發死亡");
            }
            if (Keyboard.current[_flinchKey].wasPressedThisFrame)
            {
                _target.PlayFlinchShake();
                Log("純抖動測試（PlayFlinchShake）");
            }
        }

        #endregion

        #region Private Methods

        private void SimulateHit(float damage, float poiseDamage, AttackTier tier)
        {
            IHitReceiver receiver = _target.GetComponent<IHitReceiver>();
            if (receiver == null)
            {
                Debug.LogWarning($"[DamageTester] {_target.name} 沒實作 IHitReceiver", this);
                return;
            }
            Vector3 attackDir = _target.transform.position - transform.position;
            attackDir.y = 0f;
            if (attackDir.sqrMagnitude > 0.0001f)
            {
                attackDir.Normalize();
            }
            else
            {
                attackDir = -_target.transform.forward;
            }
            HitContext ctx = new HitContext
            {
                damage = damage,
                poiseDamage = poiseDamage,
                attackTier = tier,
                isHeavyAttack = tier == AttackTier.Heavy,
                knockbackForce = 0f,
                attackDirection = attackDir,
                hitPoint = _target.transform.position + Vector3.up * 1f,
                hitNormal = -attackDir,
                skipHitEffects = false,
                gasDamageApplied = false,
            };
            receiver.OnHit(ref ctx);
            CombatAttributeSet combatAttributes = _target.CombatAttributes;
            float poisePercent = combatAttributes != null ? combatAttributes.PoisePercent * 100f : 0f;
            Log($"{tier} 攻擊 → dmg={damage}, poise={poiseDamage} | " +
                $"HP {_target.CurrentHealth:F0}/{_target.MaxHealth:F0}, Poise {poisePercent:F0}% | " +
                $"Armor={_target.CurrentArmor}");
        }

        private void TriggerInstantStagger()
        {
            CombatAttributeSet combatAttributes = _target.CombatAttributes;
            if (combatAttributes == null)
            {
                Debug.LogWarning($"[DamageTester] {_target.name} 找不到 CombatAttributeSet", this);
                return;
            }
            combatAttributes.ApplyPoiseDamage(_instantStaggerPoiseDamage);
            Log($"一擊扣 {_instantStaggerPoiseDamage} Poise → 應觸發 Stagger");
        }

        private void Log(string msg)
        {
            if (!_logEvents) return;
            Debug.Log($"[DamageTester] {msg}", this);
        }

        #endregion
    }
}
