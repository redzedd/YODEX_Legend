using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace GAS.UI
{
    /// <summary>
    /// 支援點數量表 UI — 訂閱 CombatAttributeSet.OnAssistPointsChanged
    /// 包含：Fill 動畫、獲得發光、消耗閃光、滿格脈動與變色、數字動畫
    /// </summary>
    public class AssistPointsBarUI : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Player Reference")]
        [Tooltip("玩家 ASC（自動尋找)")]
        [SerializeField] private AbilitySystemComponent _asc;

        [Header("Bar Images")]
        [Tooltip("主填充條")]
        [SerializeField] private Image _fillImage;
        [Tooltip("獲得時發光覆蓋層")]
        [SerializeField] private Image _glowImage;
        [Tooltip("消耗時閃白覆蓋層")]
        [SerializeField] private Image _flashImage;

        [Header("Text")]
        [Tooltip("點數數字")]
        [SerializeField] private TMP_Text _pointsText;

        [Header("Colors")]
        [Tooltip("正常狀態顏色")]
        [SerializeField] private Color _normalColor = new(0.3f, 0.6f, 1f, 1f);
        [Tooltip("滿格就緒顏色")]
        [SerializeField] private Color _readyColor = new(1f, 0.85f, 0.2f, 1f);

        [Header("Animation Timing")]
        [SerializeField] private float _fillDuration = 0.3f;
        [SerializeField] private float _consumeDuration = 0.15f;
        [SerializeField] private float _glowFadeDuration = 0.25f;
        [SerializeField] private float _flashFadeDuration = 0.15f;

        #endregion

        #region Private Fields

        private CombatAttributeSet _cachedAttrSet;
        private float _displayedPoints;
        private bool _isFullPulsing;

        // === Tween 引用 ===
        private Tween _fillTween;
        private Tween _glowTween;
        private Tween _flashTween;
        private Tween _textTween;
        private Tween _pulseTween;

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
                    _cachedAttrSet.OnAssistPointsChanged += OnAssistPointsChanged;
                    InitializeBar();
                }
            }
            // 初始化覆蓋層為透明
            InitOverlay(_glowImage);
            InitOverlay(_flashImage);
        }

        private void OnDestroy()
        {
            if (_cachedAttrSet != null)
            {
                _cachedAttrSet.OnAssistPointsChanged -= OnAssistPointsChanged;
            }
            KillAllTweens();
        }

        #endregion

        #region Initialization

        private void InitializeBar()
        {
            float ratio = _cachedAttrSet.AssistPointsPercent;
            if (_fillImage != null) _fillImage.fillAmount = ratio;
            _displayedPoints = _cachedAttrSet.AssistPoints.CurrentValue;
            UpdateText(_displayedPoints, _cachedAttrSet.MaxAssistPoints.CurrentValue);
            UpdateBarColor(ratio);
        }

        private static void InitOverlay(Image overlay)
        {
            if (overlay == null) return;
            Color c = overlay.color;
            c.a = 0f;
            overlay.color = c;
        }

        #endregion

        #region Event Handler

        private void OnAssistPointsChanged(float oldValue, float newValue)
        {
            if (_cachedAttrSet == null) return;
            float max = _cachedAttrSet.MaxAssistPoints.CurrentValue;
            float newRatio = max > 0f ? newValue / max : 0f;
            bool isIncrease = newValue > oldValue;
            bool isDecrease = newValue < oldValue;
            bool isFull = Mathf.Approximately(newValue, max);
            // Fill 動畫（消耗時較快）
            float duration = isDecrease ? _consumeDuration : _fillDuration;
            Ease ease = isDecrease ? Ease.InQuart : Ease.OutQuad;
            KillIfActive(ref _fillTween);
            if (_fillImage != null)
            {
                _fillTween = _fillImage.DOFillAmount(newRatio, duration)
                    .SetEase(ease)
                    .SetLink(gameObject);
            }
            // 獲得發光
            if (isIncrease)
            {
                PlayGlow();
            }
            // 消耗閃光
            if (isDecrease)
            {
                PlayFlash();
            }
            // 數字動畫
            KillIfActive(ref _textTween);
            _textTween = DOTween.To(
                    () => _displayedPoints, x => { _displayedPoints = x; UpdateText(x, max); },
                    newValue, duration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
            // 滿格狀態
            UpdateBarColor(newRatio);
            if (isFull && !_isFullPulsing)
            {
                StartFullPulse();
            }
            else if (!isFull && _isFullPulsing)
            {
                StopFullPulse();
            }
        }

        #endregion

        #region Animation Methods

        private void PlayGlow()
        {
            if (_glowImage == null) return;
            KillIfActive(ref _glowTween);
            Color c = _glowImage.color;
            c.a = 0.8f;
            _glowImage.color = c;
            _glowTween = _glowImage.DOFade(0f, _glowFadeDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        private void PlayFlash()
        {
            if (_flashImage == null) return;
            KillIfActive(ref _flashTween);
            Color c = _flashImage.color;
            c.a = 1f;
            _flashImage.color = c;
            _flashTween = _flashImage.DOFade(0f, _flashFadeDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        private void StartFullPulse()
        {
            if (_glowImage == null) return;
            _isFullPulsing = true;
            KillIfActive(ref _pulseTween);
            _pulseTween = _glowImage.DOFade(0.5f, 0.6f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject);
        }

        private void StopFullPulse()
        {
            _isFullPulsing = false;
            KillIfActive(ref _pulseTween);
            if (_glowImage != null)
            {
                Color c = _glowImage.color;
                c.a = 0f;
                _glowImage.color = c;
            }
        }

        private void UpdateBarColor(float ratio)
        {
            if (_fillImage == null) return;
            _fillImage.color = Mathf.Approximately(ratio, 1f) ? _readyColor : _normalColor;
        }

        #endregion

        #region Text

        private void UpdateText(float current, float max)
        {
            if (_pointsText == null) return;
            _pointsText.text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}";
        }

        #endregion

        #region Utility

        private static void KillIfActive(ref Tween tween)
        {
            if (tween != null && tween.IsActive()) tween.Kill();
            tween = null;
        }

        private void KillAllTweens()
        {
            KillIfActive(ref _fillTween);
            KillIfActive(ref _glowTween);
            KillIfActive(ref _flashTween);
            KillIfActive(ref _textTween);
            KillIfActive(ref _pulseTween);
        }

        #endregion
    }
}
