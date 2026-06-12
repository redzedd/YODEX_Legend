using System;
using UnityEngine;
using GAS;

namespace Boss
{
    /// <summary>
    /// Boss 通用核心控制器 — Boss 戰專用,完全獨立於雜兵系統
    /// 第 1 步骨架版只負責:HP/受擊/死亡 + 介接 GAS (IHitReceiver / CombatAttributeSet)
    /// 動畫播放、戰鬥流程編排由「同物件上的搭檔元件」(如 DragonBossController) 訂閱事件處理
    /// 後續步驟會擴充:Poise/Stagger、Armor 等級、分級受擊、Flinch 抖動、攻擊系統介接
    /// </summary>
    [RequireComponent(typeof(AbilitySystemComponent))]
    public class BossController : MonoBehaviour, IHitReceiver
    {
        #region Serialized Fields

        [Header("數值設定")]
        [SerializeField] [Tooltip("Boss 數值設定 SO — 拖入 BossConfig 資產 (Boss 戰專用)")]
        private BossConfig _config;

        [Header("音效 (選填)")]
        [SerializeField] [Tooltip("受擊時播放")]
        private AudioClip _hitSfx;

        [SerializeField] [Tooltip("死亡時播放")]
        private AudioClip _deathSfx;

        #endregion

        #region Private Fields

        private AbilitySystemComponent _asc;
        private CombatAttributeSet _combatAttributes;
        private AudioSource _audioSource;
        private bool _isDead;

        #endregion

        #region Properties

        public BossConfig Config => _config;
        public AbilitySystemComponent ASC => _asc;
        public CombatAttributeSet CombatAttributes => _combatAttributes;
        public float MaxHealth => _combatAttributes != null ? _combatAttributes.MaxHealth.CurrentValue : 0f;
        public float CurrentHealth => _combatAttributes != null ? _combatAttributes.Health.CurrentValue : 0f;
        public float HealthPercent => _combatAttributes?.HealthPercent ?? 0f;
        public bool IsDead => _isDead;

        #endregion

        #region Events

        /// <summary>受到傷害時觸發,傳入扣除的血量</summary>
        public event Action<float> OnDamaged;

        /// <summary>死亡觸發時</summary>
        public event Action OnDied;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_config == null)
            {
                Debug.LogError($"[{name}] BossController 缺少 BossConfig — 請在 Inspector 拖入 Boss 數值設定資產", this);
                enabled = false;
                return;
            }
            _asc = GetComponent<AbilitySystemComponent>();
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
        }

        private void Start()
        {
            InitializeAttributesFromASC();
        }

        private void OnDestroy()
        {
            UnsubscribeAttributes();
        }

        #endregion

        #region Public API

        public void OnHit(ref HitContext ctx)
        {
            if (_isDead)
            {
                ctx.wasBlocked = true;
                return;
            }

            if (!ctx.gasDamageApplied && _combatAttributes != null)
            {
                _combatAttributes.ApplyDamage(ctx.damage);
            }

            if (!ctx.skipHitEffects && _hitSfx != null)
            {
                PlaySfx(_hitSfx);
            }
        }

        /// <summary>直接扣血 (繞過 GameplayEffect) — 內部測試/簡單情境用</summary>
        public void TakeDamage(float damage)
        {
            if (_isDead || damage <= 0f) return;
            _combatAttributes?.ApplyDamage(damage);
        }

        /// <summary>觸發死亡 — 由 CombatAttributeSet.OnDeath 自動呼叫,也可外部主動呼叫</summary>
        public void TriggerDeath()
        {
            if (_isDead) return;
            _isDead = true;
            PlaySfx(_deathSfx);
            OnDied?.Invoke();
        }

        #endregion

        #region Private Methods

        private void InitializeAttributesFromASC()
        {
            if (_asc == null) return;
            _combatAttributes = _asc.GetAttributeSet<CombatAttributeSet>();
            if (_combatAttributes == null)
            {
                Debug.LogError($"[{name}] ASC 找不到 CombatAttributeSet — 請確認 GAS 設定", this);
                return;
            }
            if (_config != null && _config.MaxHealth > 0f)
            {
                _combatAttributes.MaxHealth.BaseValue = _config.MaxHealth;
                _combatAttributes.Health.BaseValue = _config.MaxHealth;
            }
            _combatAttributes.OnDeath -= HandleAttributeDeath;
            _combatAttributes.OnDeath += HandleAttributeDeath;
            _combatAttributes.OnDamageTaken -= HandleDamageTaken;
            _combatAttributes.OnDamageTaken += HandleDamageTaken;
        }

        private void UnsubscribeAttributes()
        {
            if (_combatAttributes == null) return;
            _combatAttributes.OnDeath -= HandleAttributeDeath;
            _combatAttributes.OnDamageTaken -= HandleDamageTaken;
        }

        private void HandleAttributeDeath() => TriggerDeath();

        private void HandleDamageTaken(AbilitySystemComponent source, float damage)
        {
            if (_isDead) return;
            OnDamaged?.Invoke(damage);
        }

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null || _audioSource == null) return;
            _audioSource.PlayOneShot(clip);
        }

        #endregion
    }
}
