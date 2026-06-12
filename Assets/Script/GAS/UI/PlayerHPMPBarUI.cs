using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace GAS.UI
{
    /// <summary>
    /// 玩家 HP / MP 長條 UI（魂類風格，畫面左上）
    /// 第一階段：僅實作血量、魔力的 Fill 平滑過渡
    /// 體力條、Buff、數字、閃白等效果由其他腳本負責
    /// </summary>
    public class PlayerHPMPBarUI : MonoBehaviour
    {
        [Header("玩家參考")]
        [Tooltip("玩家 ASC（留空則自動尋找場景中第一個）")]
        [SerializeField] private AbilitySystemComponent _asc;

        [Header("血量條")]
        [Tooltip("血量 Fill Image（須為 Filled 模式）— 前景條，立即反映當前血量")]
        [SerializeField] private Image _healthFillImage;

        [Tooltip("血量 Chip Image（須為 Filled 模式）— 殘影條，延遲追蹤")]
        [SerializeField] private Image _healthChipImage;

        [Header("魔力條")]
        [Tooltip("魔力 Fill Image（須為 Filled 模式）— 前景條，立即反映當前魔力")]
        [SerializeField] private Image _manaFillImage;

        [Tooltip("魔力 Chip Image（須為 Filled 模式）— 殘影條，延遲追蹤")]
        [SerializeField] private Image _manaChipImage;

        [Header("動畫參數")]
        [Tooltip("Fill 平滑過渡時間（秒）")]
        [SerializeField] private float _fillDuration = 0.25f;

        [Tooltip("Chip 開始追蹤前的延遲（秒）— 受傷後殘影停留的時間")]
        [SerializeField] private float _chipDelay = 0.4f;

        [Tooltip("Chip 追上前景的時間（秒）— 越長越像魂類緩慢殘影")]
        [SerializeField] private float _chipCatchupDuration = 0.5f;

        [Header("低血脈動")]
        [Tooltip("血條 CanvasGroup（脈動套用對象，建議掛在血條根節點）")]
        [SerializeField] private CanvasGroup _healthBarCanvasGroup;

        [Tooltip("觸發脈動的血量比例門檻（0~1）")]
        [SerializeField] private float _lowHealthThreshold = 0.25f;

        [Tooltip("脈動 alpha 最低值")]
        [SerializeField] private float _lowHealthMinAlpha = 0.4f;

        [Tooltip("脈動半週期（秒）— 從滿到最低需要的時間")]
        [SerializeField] private float _lowHealthPulseDuration = 0.5f;

        private CombatAttributeSet _cachedAttrSet;
        private Tween _healthFillTween;
        private Tween _healthChipTween;
        private Tween _healthPulseTween;
        private Tween _manaFillTween;
        private Tween _manaChipTween;
        private bool _isLowHealthPulsing;

        private void Start()
        {
            if (_asc == null)
            {
                _asc = FindFirstObjectByType<AbilitySystemComponent>();
            }
            if (_asc == null)
            {
                Debug.LogWarning("[PlayerHPMPBarUI] 找不到 AbilitySystemComponent，UI 不會更新。");
                return;
            }
            _cachedAttrSet = _asc.GetAttributeSet<CombatAttributeSet>();
            if (_cachedAttrSet == null)
            {
                Debug.LogWarning("[PlayerHPMPBarUI] ASC 上沒有 CombatAttributeSet，UI 不會更新。");
                return;
            }
            SubscribeEvents();
            InitializeBars();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            KillIfActive(ref _healthFillTween);
            KillIfActive(ref _healthChipTween);
            KillIfActive(ref _healthPulseTween);
            KillIfActive(ref _manaFillTween);
            KillIfActive(ref _manaChipTween);
        }

        private void SubscribeEvents()
        {
            _cachedAttrSet.OnHealthChanged += OnHealthChanged;
            _cachedAttrSet.OnManaChanged += OnManaChanged;
        }

        private void UnsubscribeEvents()
        {
            if (_cachedAttrSet == null) return;
            _cachedAttrSet.OnHealthChanged -= OnHealthChanged;
            _cachedAttrSet.OnManaChanged -= OnManaChanged;
        }

        private void InitializeBars()
        {
            float healthRatio = _cachedAttrSet.HealthPercent;
            float manaRatio = _cachedAttrSet.ManaPercent;
            SetFillImmediate(_healthFillImage, healthRatio);
            SetFillImmediate(_healthChipImage, healthRatio);
            SetFillImmediate(_manaFillImage, manaRatio);
            SetFillImmediate(_manaChipImage, manaRatio);
            UpdateLowHealthPulse(healthRatio);
        }

        private void OnHealthChanged(float oldValue, float newValue)
        {
            float max = _cachedAttrSet.MaxHealth.CurrentValue;
            float ratio = max > 0f ? newValue / max : 0f;
            AnimateFill(ref _healthFillTween, _healthFillImage, ratio);
            UpdateChip(ref _healthChipTween, _healthChipImage, ratio, newValue < oldValue);
            UpdateLowHealthPulse(ratio);
        }

        private void OnManaChanged(float oldValue, float newValue)
        {
            float max = _cachedAttrSet.MaxMana.CurrentValue;
            float ratio = max > 0f ? newValue / max : 0f;
            AnimateFill(ref _manaFillTween, _manaFillImage, ratio);
            UpdateChip(ref _manaChipTween, _manaChipImage, ratio, newValue < oldValue);
        }

        private void AnimateFill(ref Tween tween, Image fill, float targetRatio)
        {
            if (fill == null) return;
            KillIfActive(ref tween);
            tween = fill.DOFillAmount(targetRatio, _fillDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        /// <summary>
        /// Chip 殘影更新：下降時延遲追蹤、上升時立即跟上
        /// </summary>
        private void UpdateChip(ref Tween tween, Image chip, float targetRatio, bool isDecrease)
        {
            if (chip == null) return;
            KillIfActive(ref tween);
            if (!isDecrease)
            {
                chip.fillAmount = targetRatio;
                return;
            }
            Sequence seq = DOTween.Sequence();
            seq.AppendInterval(_chipDelay);
            seq.Append(chip.DOFillAmount(targetRatio, _chipCatchupDuration).SetEase(Ease.InOutQuad));
            seq.SetLink(gameObject);
            tween = seq;
        }

        /// <summary>
        /// 低血脈動狀態切換：低於門檻啟動 Yoyo 閃爍，否則停止並還原 alpha
        /// </summary>
        private void UpdateLowHealthPulse(float healthRatio)
        {
            if (_healthBarCanvasGroup == null) return;
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
            _isLowHealthPulsing = true;
            KillIfActive(ref _healthPulseTween);
            _healthPulseTween = _healthBarCanvasGroup.DOFade(_lowHealthMinAlpha, _lowHealthPulseDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject);
        }

        private void StopLowHealthPulse()
        {
            _isLowHealthPulsing = false;
            KillIfActive(ref _healthPulseTween);
            _healthBarCanvasGroup.alpha = 1f;
        }

        private static void SetFillImmediate(Image image, float ratio)
        {
            if (image != null) image.fillAmount = ratio;
        }

        private static void KillIfActive(ref Tween tween)
        {
            if (tween != null && tween.IsActive()) tween.Kill();
            tween = null;
        }
    }
}
