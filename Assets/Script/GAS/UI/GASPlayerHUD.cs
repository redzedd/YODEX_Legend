using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using GAS.UI;

namespace GAS
{
    /// <summary>
    /// GAS 玩家 HUD — 事件驅動 DOTween 動畫版本
    /// 訂閱 CombatAttributeSet 事件，以 DOTween 驅動所有條動畫
    /// 包含：Fill 平滑、Chip 延遲追蹤、受傷閃白、大傷害震動、低血脈動、數字計數
    /// </summary>
    public class GASPlayerHUD : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Player Reference")]
        [Tooltip("玩家 ASC（自動尋找)")]
        [SerializeField] private AbilitySystemComponent _asc;

        [Header("Health Bar")]
        [SerializeField] private Image _healthFillImage;
        [SerializeField] private Image _healthChipImage;
        [Tooltip("受傷閃白覆蓋層")]
        [SerializeField] private Image _healthFlashImage;
        [Tooltip("血量數字")]
        [SerializeField] private TMP_Text _healthText;
        [Tooltip("血條容器（用於震動）")]
        [SerializeField] private RectTransform _healthBarRoot;
        [Tooltip("血條 CanvasGroup（用於低血脈動）")]
        [SerializeField] private CanvasGroup _healthBarCanvasGroup;

        [Header("Stamina Bar")]
        [SerializeField] private Image _staminaFillImage;
        [SerializeField] private Image _staminaChipImage;
        [Tooltip("體力閃白覆蓋層")]
        [SerializeField] private Image _staminaFlashImage;
        [Tooltip("體力數字")]
        [SerializeField] private TMP_Text _staminaText;

        [Header("Mana Bar")]
        [SerializeField] private Image _manaFillImage;
        [SerializeField] private Image _manaChipImage;
        [Tooltip("魔力閃白覆蓋層")]
        [SerializeField] private Image _manaFlashImage;
        [Tooltip("魔力數字")]
        [SerializeField] private TMP_Text _manaText;

        [Header("Animation Timing")]
        [Tooltip("Fill 過渡時間")]
        [SerializeField] private float _fillDuration = 0.25f;
        [Tooltip("Chip 開始追蹤前的延遲")]
        [SerializeField] private float _chipDelay = 0.15f;
        [Tooltip("Chip 追上前景的時間")]
        [SerializeField] private float _chipCatchupDuration = 0.4f;
        [Tooltip("閃白淡出時間")]
        [SerializeField] private float _flashFadeDuration = 0.12f;
        [Tooltip("大傷害震動門檻（超過此比例觸發）")]
        [SerializeField] private float _bigDamageThreshold = 0.2f;
        [Tooltip("低血脈動門檻")]
        [SerializeField] private float _lowHealthThreshold = 0.25f;

        [Header("Buff Bar (optional)")]
        [SerializeField] private BuffBarUI _buffBarUI;

        [Header("Health Regen Goal (optional)")]
        [SerializeField] private Image _healthRegenGoalImage;
        [SerializeField] private float _regenGoalDuration = 0.15f;

        #endregion

        #region Private Fields

        private CombatAttributeSet _cachedAttrSet;

        // === 條動畫 Tween 引用 ===
        private Tween _healthFillTween;
        private Tween _healthChipTween;
        private Tween _healthFlashTween;
        private Tween _healthShakeTween;
        private Tween _healthTextTween;
        private Tween _healthPulseTween;

        private Tween _staminaFillTween;
        private Tween _staminaChipTween;
        private Tween _staminaFlashTween;
        private Tween _staminaTextTween;

        private Tween _manaFillTween;
        private Tween _manaChipTween;
        private Tween _manaFlashTween;
        private Tween _manaTextTween;

        private Tween _regenGoalTween;

        // === 數字顯示用 ===
        private float _displayedHealth;
        private float _displayedStamina;
        private float _displayedMana;

        // === 低血脈動狀態 ===
        private bool _isLowHealthPulsing;

        #endregion

        #region Properties

        /// <summary>
        /// 向後相容：公開 Health Fill Image
        /// </summary>
        public Image HealthFillImage => _healthFillImage;

        /// <summary>
        /// 向後相容：公開 Health Chip Image
        /// </summary>
        public Image HealthChipImage => _healthChipImage;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            if (_asc == null)
            {
                _asc = FindFirstObjectByType<AbilitySystemComponent>();
            }
            if (_asc != null)
            {
                _cachedAttrSet = _asc.GetAttributeSet<CombatAttributeSet>();
                if (_cachedAttrSet != null)
                {
                    SubscribeEvents();
                    InitializeBars();
                }
            }
            else
            {
                Debug.LogWarning("[GASPlayerHUD] AbilitySystemComponent not found. HUD will not update.");
            }
            // 初始化閃白為透明
            InitFlashImage(_healthFlashImage);
            InitFlashImage(_staminaFlashImage);
            InitFlashImage(_manaFlashImage);
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            KillAllTweens();
        }

        #endregion

        #region Event Subscribe / Unsubscribe

        private void SubscribeEvents()
        {
            if (_cachedAttrSet == null) return;
            _cachedAttrSet.OnHealthChanged += OnHealthChanged;
            _cachedAttrSet.OnStaminaChanged += OnStaminaChanged;
            _cachedAttrSet.OnManaChanged += OnManaChanged;
            _cachedAttrSet.OnDamageTaken += OnDamageTaken;
        }

        private void UnsubscribeEvents()
        {
            if (_cachedAttrSet == null) return;
            _cachedAttrSet.OnHealthChanged -= OnHealthChanged;
            _cachedAttrSet.OnStaminaChanged -= OnStaminaChanged;
            _cachedAttrSet.OnManaChanged -= OnManaChanged;
            _cachedAttrSet.OnDamageTaken -= OnDamageTaken;
        }

        #endregion

        #region Initialization

        private void InitializeBars()
        {
            if (_cachedAttrSet == null) return;
            float healthRatio = _cachedAttrSet.HealthPercent;
            float staminaRatio = _cachedAttrSet.StaminaPercent;
            float manaRatio = _cachedAttrSet.ManaPercent;
            // 立即設定（不播動畫）
            SetFillImmediate(_healthFillImage, healthRatio);
            SetFillImmediate(_healthChipImage, healthRatio);
            SetFillImmediate(_staminaFillImage, staminaRatio);
            SetFillImmediate(_staminaChipImage, staminaRatio);
            SetFillImmediate(_manaFillImage, manaRatio);
            SetFillImmediate(_manaChipImage, manaRatio);
            // 數字
            _displayedHealth = _cachedAttrSet.Health.CurrentValue;
            _displayedStamina = _cachedAttrSet.Stamina.CurrentValue;
            _displayedMana = _cachedAttrSet.Mana.CurrentValue;
            UpdateHealthText(_displayedHealth, _cachedAttrSet.MaxHealth.CurrentValue);
            UpdateStaminaText(_displayedStamina, _cachedAttrSet.MaxStamina.CurrentValue);
            UpdateManaText(_displayedMana, _cachedAttrSet.MaxMana.CurrentValue);
            // 檢查是否需要低血脈動
            CheckLowHealthPulse(healthRatio);
        }

        private static void InitFlashImage(Image flash)
        {
            if (flash == null) return;
            Color c = flash.color;
            c.a = 0f;
            flash.color = c;
        }

        #endregion

        #region Event Handlers

        private void OnHealthChanged(float oldValue, float newValue)
        {
            if (_cachedAttrSet == null) return;
            float max = _cachedAttrSet.MaxHealth.CurrentValue;
            float newRatio = max > 0f ? newValue / max : 0f;
            bool isDecrease = newValue < oldValue;
            // Fill 動畫
            AnimateFill(ref _healthFillTween, _healthFillImage, newRatio, _fillDuration);
            // Chip 動畫（只在下降時延遲追蹤）
            if (isDecrease)
            {
                AnimateChipDecrease(ref _healthChipTween, _healthChipImage, newRatio);
                PlayFlash(_healthFlashImage, ref _healthFlashTween);
            }
            else
            {
                // 上升時 chip 立即跟上
                AnimateChipIncrease(ref _healthChipTween, _healthChipImage, newRatio);
            }
            // 數字計數動畫
            AnimateHealthNumber(newValue, max);
            // 低血脈動檢查
            CheckLowHealthPulse(newRatio);
        }

        private void OnStaminaChanged(float oldValue, float newValue)
        {
            if (_cachedAttrSet == null) return;
            float max = _cachedAttrSet.MaxStamina.CurrentValue;
            float newRatio = max > 0f ? newValue / max : 0f;
            bool isDecrease = newValue < oldValue;
            AnimateFill(ref _staminaFillTween, _staminaFillImage, newRatio, _fillDuration);
            if (isDecrease)
            {
                AnimateChipDecrease(ref _staminaChipTween, _staminaChipImage, newRatio);
                PlayFlash(_staminaFlashImage, ref _staminaFlashTween);
            }
            else
            {
                AnimateChipIncrease(ref _staminaChipTween, _staminaChipImage, newRatio);
            }
            AnimateStaminaNumber(newValue, max);
        }

        private void OnManaChanged(float oldValue, float newValue)
        {
            if (_cachedAttrSet == null) return;
            float max = _cachedAttrSet.MaxMana.CurrentValue;
            float newRatio = max > 0f ? newValue / max : 0f;
            bool isDecrease = newValue < oldValue;
            AnimateFill(ref _manaFillTween, _manaFillImage, newRatio, _fillDuration);
            if (isDecrease)
            {
                AnimateChipDecrease(ref _manaChipTween, _manaChipImage, newRatio);
                PlayFlash(_manaFlashImage, ref _manaFlashTween);
            }
            else
            {
                AnimateChipIncrease(ref _manaChipTween, _manaChipImage, newRatio);
            }
            AnimateManaNumber(newValue, max);
        }

        /// <summary>
        /// 受傷事件 — 大傷害時觸發血條震動
        /// </summary>
        private void OnDamageTaken(AbilitySystemComponent source, float damage)
        {
            if (_cachedAttrSet == null) return;
            float max = _cachedAttrSet.MaxHealth.CurrentValue;
            if (max <= 0f) return;
            float damageRatio = damage / max;
            if (damageRatio >= _bigDamageThreshold && _healthBarRoot != null)
            {
                KillIfActive(ref _healthShakeTween);
                _healthShakeTween = _healthBarRoot.DOShakeAnchorPos(0.3f, 8f, 20, 90f, false, true)
                    .SetLink(gameObject);
            }
        }

        #endregion

        #region Bar Animation Helpers

        /// <summary>
        /// Fill 平滑過渡
        /// </summary>
        private void AnimateFill(ref Tween tween, Image fill, float targetRatio, float duration)
        {
            if (fill == null) return;
            KillIfActive(ref tween);
            tween = fill.DOFillAmount(targetRatio, duration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        /// <summary>
        /// Chip 延遲追蹤（數值下降時）
        /// </summary>
        private void AnimateChipDecrease(ref Tween tween, Image chip, float targetRatio)
        {
            if (chip == null) return;
            KillIfActive(ref tween);
            Sequence seq = DOTween.Sequence();
            seq.AppendInterval(_chipDelay);
            seq.Append(chip.DOFillAmount(targetRatio, _chipCatchupDuration).SetEase(Ease.InOutQuad));
            seq.SetLink(gameObject);
            tween = seq;
        }

        /// <summary>
        /// Chip 立即跟上（數值上升時）
        /// </summary>
        private void AnimateChipIncrease(ref Tween tween, Image chip, float targetRatio)
        {
            if (chip == null) return;
            KillIfActive(ref tween);
            // 上升時 chip 立即追上 fill
            chip.fillAmount = targetRatio;
        }

        /// <summary>
        /// 閃白效果
        /// </summary>
        private void PlayFlash(Image flash, ref Tween tween)
        {
            if (flash == null) return;
            KillIfActive(ref tween);
            Color c = flash.color;
            c.a = 1f;
            flash.color = c;
            tween = flash.DOFade(0f, _flashFadeDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 數字計數動畫 — Health
        /// </summary>
        private void AnimateHealthNumber(float target, float max)
        {
            KillIfActive(ref _healthTextTween);
            _healthTextTween = DOTween.To(
                    () => _displayedHealth, x => { _displayedHealth = x; UpdateHealthText(x, max); },
                    target, _fillDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 數字計數動畫 — Stamina
        /// </summary>
        private void AnimateStaminaNumber(float target, float max)
        {
            KillIfActive(ref _staminaTextTween);
            _staminaTextTween = DOTween.To(
                    () => _displayedStamina, x => { _displayedStamina = x; UpdateStaminaText(x, max); },
                    target, _fillDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 數字計數動畫 — Mana
        /// </summary>
        private void AnimateManaNumber(float target, float max)
        {
            KillIfActive(ref _manaTextTween);
            _manaTextTween = DOTween.To(
                    () => _displayedMana, x => { _displayedMana = x; UpdateManaText(x, max); },
                    target, _fillDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        #endregion

        #region Low Health Pulse

        private void CheckLowHealthPulse(float healthRatio)
        {
            bool shouldPulse = healthRatio > 0f && healthRatio <= _lowHealthThreshold;
            if (shouldPulse && !_isLowHealthPulsing)
            {
                StartLowHealthPulse();
            }
            else if (!shouldPulse && _isLowHealthPulsing)
            {
                StopLowHealthPulse();
            }
        }

        private void StartLowHealthPulse()
        {
            if (_healthBarCanvasGroup == null) return;
            _isLowHealthPulsing = true;
            KillIfActive(ref _healthPulseTween);
            _healthPulseTween = _healthBarCanvasGroup.DOFade(0.4f, 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject);
        }

        private void StopLowHealthPulse()
        {
            _isLowHealthPulsing = false;
            KillIfActive(ref _healthPulseTween);
            if (_healthBarCanvasGroup != null)
            {
                _healthBarCanvasGroup.alpha = 1f;
            }
        }

        #endregion

        #region Text Helpers

        private void UpdateHealthText(float current, float max)
        {
            if (_healthText == null) return;
            _healthText.text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}";
        }

        private void UpdateStaminaText(float current, float max)
        {
            if (_staminaText == null) return;
            _staminaText.text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}";
        }

        private void UpdateManaText(float current, float max)
        {
            if (_manaText == null) return;
            _manaText.text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}";
        }

        #endregion

        #region Regen Goal

        /// <summary>
        /// 顯示血量回復目標線
        /// </summary>
        public void ShowHealthRegenGoal(float currentPlusPending, float max)
        {
            if (_healthRegenGoalImage == null) return;
            float targetRatio = max > 0f ? currentPlusPending / max : 0f;
            _healthRegenGoalImage.gameObject.SetActive(true);
            KillIfActive(ref _regenGoalTween);
            _regenGoalTween = _healthRegenGoalImage.DOFillAmount(targetRatio, _regenGoalDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 隱藏血量回復目標線
        /// </summary>
        public void HideHealthRegenGoal()
        {
            if (_healthRegenGoalImage == null) return;
            KillIfActive(ref _regenGoalTween);
            _healthRegenGoalImage.gameObject.SetActive(false);
        }

        /// <summary>
        /// 用 0~1 比例設定回復目標線
        /// </summary>
        public void ShowHealthRegenGoal01(float ratio01)
        {
            if (_healthRegenGoalImage == null) return;
            float v = Mathf.Clamp01(ratio01);
            _healthRegenGoalImage.gameObject.SetActive(true);
            KillIfActive(ref _regenGoalTween);
            _regenGoalTween = _healthRegenGoalImage.DOFillAmount(v, _regenGoalDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        #endregion

        #region Utility

        private static void SetFillImmediate(Image image, float ratio)
        {
            if (image != null) image.fillAmount = ratio;
        }

        private static void KillIfActive(ref Tween tween)
        {
            if (tween != null && tween.IsActive()) tween.Kill();
            tween = null;
        }

        private void KillAllTweens()
        {
            KillIfActive(ref _healthFillTween);
            KillIfActive(ref _healthChipTween);
            KillIfActive(ref _healthFlashTween);
            KillIfActive(ref _healthShakeTween);
            KillIfActive(ref _healthTextTween);
            KillIfActive(ref _healthPulseTween);
            KillIfActive(ref _staminaFillTween);
            KillIfActive(ref _staminaChipTween);
            KillIfActive(ref _staminaFlashTween);
            KillIfActive(ref _staminaTextTween);
            KillIfActive(ref _manaFillTween);
            KillIfActive(ref _manaChipTween);
            KillIfActive(ref _manaFlashTween);
            KillIfActive(ref _manaTextTween);
            KillIfActive(ref _regenGoalTween);
        }

        #endregion
    }
}
