using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace GAS.UI
{
    /// <summary>
    /// 玩家體力環 UI — 薩爾達曠野之息風格
    /// 含 Fill 平滑、Chip 殘影、淡入淡出、低體力紅色警示、依設定顯示在畫面左/右側
    /// </summary>
    public class PlayerStaminaRingUI : MonoBehaviour
    {
        [Header("玩家參考")]
        [Tooltip("玩家 ASC（留空則自動尋找場景中第一個）")]
        [SerializeField] private AbilitySystemComponent _asc;

        [Header("體力環")]
        [Tooltip("體力環前景 Image（須為 Filled / Radial 360 模式）")]
        [SerializeField] private Image _staminaRingImage;

        [Tooltip("體力環 Chip Image（殘影條，須為 Filled / Radial 360 模式）")]
        [SerializeField] private Image _staminaChipImage;

        [Tooltip("環整體 CanvasGroup（用於淡入淡出，建議掛在環根節點）")]
        [SerializeField] private CanvasGroup _ringCanvasGroup;

        [Header("Fill 動畫")]
        [Tooltip("Fill 平滑過渡時間（秒）")]
        [SerializeField] private float _fillDuration = 0.15f;

        [Header("Chip 殘影")]
        [Tooltip("Chip 開始追蹤前的延遲（秒）")]
        [SerializeField] private float _chipDelay = 0.3f;

        [Tooltip("Chip 追上前景的時間（秒）")]
        [SerializeField] private float _chipCatchupDuration = 0.4f;

        [Header("淡入淡出")]
        [Tooltip("體力非滿時淡入時間（秒）")]
        [SerializeField] private float _fadeInDuration = 0.15f;

        [Tooltip("體力回滿後等候多久才淡出（秒）")]
        [SerializeField] private float _fadeOutDelay = 0.6f;

        [Tooltip("淡出時間（秒）")]
        [SerializeField] private float _fadeOutDuration = 0.4f;

        [Header("耗盡警示")]
        [Tooltip("觸發紅色警示的體力比例門檻")]
        [SerializeField, Range(0f, 1f)] private float _lowStaminaThreshold = 0.2f;

        [Tooltip("Front 正常顏色")]
        [SerializeField] private Color _normalColor = Color.green;

        [Tooltip("Front 耗盡警示顏色")]
        [SerializeField] private Color _lowColor = Color.red;

        [Tooltip("Chip 正常顏色（建議與 Front 對比，用於看清殘影）")]
        [SerializeField] private Color _chipNormalColor = Color.white;

        [Tooltip("Chip 耗盡警示顏色（建議偏亮黃/橘以保持可見）")]
        [SerializeField] private Color _chipLowColor = new Color(1f, 0.6f, 0.2f, 1f);

        [Tooltip("顏色切換時間（秒）")]
        [SerializeField] private float _colorTransitionDuration = 0.2f;

        [Header("畫面側邊跟隨")]
        [Tooltip("跟隨目標 Transform（留空則使用 ASC 所在物件）")]
        [SerializeField] private Transform _followTarget;

        [Tooltip("參考相機（留空則使用 Camera.main）")]
        [SerializeField] private Camera _camera;

        [Tooltip("環顯示在畫面的哪一側")]
        [SerializeField] private ScreenSide _screenSide = ScreenSide.Right;

        [Tooltip("距離角色螢幕投影點的橫向偏移（像素）")]
        [SerializeField] private float _sideOffsetPixels = 120f;

        [Tooltip("距離角色螢幕投影點的垂直偏移（像素，正值為向上）")]
        [SerializeField] private float _verticalOffsetPixels = 0f;

        [Tooltip("世界座標額外抬高（公尺）— 通常設角色腰/胸高度，避免投影到腳底")]
        [SerializeField] private float _worldHeightOffset = 1.0f;

        [Tooltip("是否平滑跟隨（吸收幀間抖動）")]
        [SerializeField] private bool _smoothFollow = true;

        [Tooltip("平滑時間（秒）— 越大延遲越明顯，0.05~0.1 通常剛好")]
        [SerializeField] private float _smoothTime = 0.06f;

        private CombatAttributeSet _cachedAttrSet;
        private RectTransform _rect;
        private Tween _fillTween;
        private Tween _chipTween;
        private Tween _fadeTween;
        private Tween _colorTween;
        private Tween _chipColorTween;
        private Vector2 _followVelocity;
        private bool _hasFollowSnapshot;
        private bool _isLowStamina;
        private bool _wasBehindCamera;

        public enum ScreenSide
        {
            Left,
            Right,
        }

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
            _rect = transform as RectTransform;
        }

        private void Start()
        {
            if (_asc == null) _asc = FindFirstObjectByType<AbilitySystemComponent>();
            if (_asc == null)
            {
                Debug.LogWarning("[PlayerStaminaRingUI] 找不到 AbilitySystemComponent，UI 不會更新。");
                return;
            }
            _cachedAttrSet = _asc.GetAttributeSet<CombatAttributeSet>();
            if (_cachedAttrSet == null)
            {
                Debug.LogWarning("[PlayerStaminaRingUI] ASC 上沒有 CombatAttributeSet，UI 不會更新。");
                return;
            }
            if (_followTarget == null) _followTarget = _asc.transform;
            SubscribeEvents();
            InitializeRing();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            KillIfActive(ref _fillTween);
            KillIfActive(ref _chipTween);
            KillIfActive(ref _fadeTween);
            KillIfActive(ref _colorTween);
            KillIfActive(ref _chipColorTween);
        }

        private void LateUpdate()
        {
            if (_followTarget == null || _camera == null || _rect == null) return;
            Vector3 worldPos = _followTarget.position + Vector3.up * _worldHeightOffset;
            Vector3 screenPos = _camera.WorldToScreenPoint(worldPos);
            bool behind = screenPos.z < 0f;
            if (behind)
            {
                if (!_wasBehindCamera)
                {
                    KillIfActive(ref _fadeTween);
                    if (_ringCanvasGroup != null) _ringCanvasGroup.alpha = 0f;
                    _wasBehindCamera = true;
                    _hasFollowSnapshot = false;
                }
                return;
            }
            if (_wasBehindCamera)
            {
                _wasBehindCamera = false;
                UpdateVisibility(_cachedAttrSet != null ? _cachedAttrSet.StaminaPercent : 1f);
            }
            float sign = _screenSide == ScreenSide.Right ? 1f : -1f;
            Vector2 target = new Vector2(
                screenPos.x + sign * _sideOffsetPixels,
                screenPos.y + _verticalOffsetPixels);
            Vector2 final;
            if (!_smoothFollow || !_hasFollowSnapshot)
            {
                final = target;
                _followVelocity = Vector2.zero;
                _hasFollowSnapshot = true;
            }
            else
            {
                Vector2 current = _rect.position;
                final = Vector2.SmoothDamp(current, target, ref _followVelocity, _smoothTime);
            }
            _rect.position = new Vector3(final.x, final.y, 0f);
        }

        private void SubscribeEvents()
        {
            _cachedAttrSet.OnStaminaChanged += OnStaminaChanged;
        }

        private void UnsubscribeEvents()
        {
            if (_cachedAttrSet == null) return;
            _cachedAttrSet.OnStaminaChanged -= OnStaminaChanged;
        }

        private void InitializeRing()
        {
            float ratio = _cachedAttrSet.StaminaPercent;
            if (_staminaRingImage != null) _staminaRingImage.fillAmount = ratio;
            if (_staminaChipImage != null) _staminaChipImage.fillAmount = ratio;
            if (_ringCanvasGroup != null) _ringCanvasGroup.alpha = ratio < 1f ? 1f : 0f;
            _isLowStamina = ratio > 0f && ratio <= _lowStaminaThreshold;
            ApplyColors(_isLowStamina, instant: true);
        }

        private void OnStaminaChanged(float oldValue, float newValue)
        {
            float max = _cachedAttrSet.MaxStamina.CurrentValue;
            float ratio = max > 0f ? newValue / max : 0f;
            bool isDecrease = newValue < oldValue;
            AnimateFill(ratio);
            UpdateChip(ratio, isDecrease);
            UpdateVisibility(ratio);
            UpdateLowStaminaColor(ratio);
        }

        private void AnimateFill(float targetRatio)
        {
            if (_staminaRingImage == null) return;
            KillIfActive(ref _fillTween);
            _fillTween = _staminaRingImage.DOFillAmount(targetRatio, _fillDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        /// <summary>
        /// Chip 殘影：下降時延遲追蹤、上升時立即跟上
        /// </summary>
        private void UpdateChip(float targetRatio, bool isDecrease)
        {
            if (_staminaChipImage == null) return;
            KillIfActive(ref _chipTween);
            if (!isDecrease)
            {
                _staminaChipImage.fillAmount = targetRatio;
                return;
            }
            Sequence seq = DOTween.Sequence();
            seq.AppendInterval(_chipDelay);
            seq.Append(_staminaChipImage.DOFillAmount(targetRatio, _chipCatchupDuration).SetEase(Ease.InOutQuad));
            seq.SetLink(gameObject);
            _chipTween = seq;
        }

        private void UpdateVisibility(float ratio)
        {
            if (_ringCanvasGroup == null) return;
            if (ratio < 1f)
            {
                FadeIn();
            }
            else
            {
                FadeOutWithDelay();
            }
        }

        private void FadeIn()
        {
            KillIfActive(ref _fadeTween);
            if (Mathf.Approximately(_ringCanvasGroup.alpha, 1f)) return;
            _fadeTween = _ringCanvasGroup.DOFade(1f, _fadeInDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        private void FadeOutWithDelay()
        {
            KillIfActive(ref _fadeTween);
            Sequence seq = DOTween.Sequence();
            seq.AppendInterval(_fadeOutDelay);
            seq.Append(_ringCanvasGroup.DOFade(0f, _fadeOutDuration).SetEase(Ease.InQuad));
            seq.SetLink(gameObject);
            _fadeTween = seq;
        }

        private void UpdateLowStaminaColor(float ratio)
        {
            bool shouldWarn = ratio > 0f && ratio <= _lowStaminaThreshold;
            if (shouldWarn == _isLowStamina) return;
            _isLowStamina = shouldWarn;
            ApplyColors(shouldWarn, instant: false);
        }

        private void ApplyColors(bool low, bool instant)
        {
            KillIfActive(ref _colorTween);
            KillIfActive(ref _chipColorTween);
            Color front = low ? _lowColor : _normalColor;
            Color chip = low ? _chipLowColor : _chipNormalColor;
            if (instant)
            {
                if (_staminaRingImage != null) _staminaRingImage.color = front;
                if (_staminaChipImage != null) _staminaChipImage.color = chip;
                return;
            }
            if (_staminaRingImage != null)
            {
                _colorTween = _staminaRingImage.DOColor(front, _colorTransitionDuration).SetLink(gameObject);
            }
            if (_staminaChipImage != null)
            {
                _chipColorTween = _staminaChipImage.DOColor(chip, _colorTransitionDuration).SetLink(gameObject);
            }
        }

        private static void KillIfActive(ref Tween tween)
        {
            if (tween != null && tween.IsActive()) tween.Kill();
            tween = null;
        }
    }
}
