using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace GAS.UI
{
    /// <summary>
    /// 三槽位武器切換輪盤 UI
    /// 左（前一把, 縮小半透明）、中（當前, 放大不透明）、右（下一把, 縮小半透明）
    /// 訂閱 WeaponManager 事件驅動所有動畫
    /// </summary>
    public class WeaponSwitchUI : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Player Reference")]
        [Tooltip("WeaponManager（自動尋找）")]
        [SerializeField] private WeaponManager _weaponManager;

        [Header("Slot References")]
        [Tooltip("左槽位圖示（前一把武器）")]
        [SerializeField] private Image _leftSlotIcon;
        [Tooltip("中槽位圖示（當前武器）")]
        [SerializeField] private Image _centerSlotIcon;
        [Tooltip("右槽位圖示（下一把武器）")]
        [SerializeField] private Image _rightSlotIcon;

        [Header("Slot Containers")]
        [Tooltip("左槽位 RectTransform")]
        [SerializeField] private RectTransform _leftSlotRect;
        [Tooltip("中槽位 RectTransform")]
        [SerializeField] private RectTransform _centerSlotRect;
        [Tooltip("右槽位 RectTransform")]
        [SerializeField] private RectTransform _rightSlotRect;

        [Header("Slot CanvasGroups")]
        [Tooltip("左槽位 CanvasGroup")]
        [SerializeField] private CanvasGroup _leftSlotGroup;
        [Tooltip("中槽位 CanvasGroup")]
        [SerializeField] private CanvasGroup _centerSlotGroup;
        [Tooltip("右槽位 CanvasGroup")]
        [SerializeField] private CanvasGroup _rightSlotGroup;

        [Header("Weapon Name")]
        [Tooltip("武器名稱文字")]
        [SerializeField] private TMP_Text _weaponNameText;
        [Tooltip("武器名稱 CanvasGroup")]
        [SerializeField] private CanvasGroup _weaponNameGroup;

        [Header("Highlight")]
        [Tooltip("中槽位高亮覆蓋層")]
        [SerializeField] private Image _centerHighlight;

        [Header("Layout Settings")]
        [Tooltip("側邊槽位縮放")]
        [SerializeField] private float _sideScale = 0.7f;
        [Tooltip("中間槽位縮放")]
        [SerializeField] private float _centerScale = 1.2f;
        [Tooltip("側邊槽位透明度")]
        [SerializeField] private float _sideAlpha = 0.5f;

        [Header("Animation Timing")]
        [SerializeField] private float _switchDuration = 0.3f;
        [SerializeField] private float _nameFadeInDuration = 0.15f;
        [SerializeField] private float _nameDisplayDuration = 1.5f;
        [SerializeField] private float _nameFadeOutDuration = 0.3f;

        #endregion

        #region Private Fields

        private Tween _switchTween;
        private Tween _nameTween;
        private Tween _highlightTween;

        // 記錄各槽位的初始位置
        private Vector2 _leftAnchorPos;
        private Vector2 _centerAnchorPos;
        private Vector2 _rightAnchorPos;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            if (_weaponManager == null)
            {
                _weaponManager = FindFirstObjectByType<WeaponManager>();
            }
            if (_weaponManager != null)
            {
                _weaponManager.OnWeaponSwitchComplete += OnWeaponSwitchComplete;
                _weaponManager.OnPreselectionChanged += OnPreselectionChanged;
            }
            // 記錄初始位置
            if (_leftSlotRect != null) _leftAnchorPos = _leftSlotRect.anchoredPosition;
            if (_centerSlotRect != null) _centerAnchorPos = _centerSlotRect.anchoredPosition;
            if (_rightSlotRect != null) _rightAnchorPos = _rightSlotRect.anchoredPosition;
            // 初始化顯示
            InitializeSlots();
            // 初始化武器名為隱藏
            if (_weaponNameGroup != null) _weaponNameGroup.alpha = 0f;
            // 高亮脈動
            StartHighlightPulse();
        }

        private void OnDestroy()
        {
            if (_weaponManager != null)
            {
                _weaponManager.OnWeaponSwitchComplete -= OnWeaponSwitchComplete;
                _weaponManager.OnPreselectionChanged -= OnPreselectionChanged;
            }
            KillIfActive(ref _switchTween);
            KillIfActive(ref _nameTween);
            KillIfActive(ref _highlightTween);
        }

        #endregion

        #region Initialization

        private void InitializeSlots()
        {
            if (_weaponManager == null || _weaponManager.WeaponCount == 0) return;
            // 設定各槽位圖示
            UpdateSlotIcon(_centerSlotIcon, _weaponManager.CurrentWeapon);
            UpdateSlotIcon(_rightSlotIcon, _weaponManager.PreselectedWeapon);
            // 左槽位 = 前一把（offset -1）
            WeaponData prevWeapon = _weaponManager.GetWeaponAtOffset(-1);
            UpdateSlotIcon(_leftSlotIcon, prevWeapon);
            // 設定初始縮放與透明度
            SetSlotState(_leftSlotRect, _leftSlotGroup, _sideScale, _sideAlpha);
            SetSlotState(_centerSlotRect, _centerSlotGroup, _centerScale, 1f);
            SetSlotState(_rightSlotRect, _rightSlotGroup, _sideScale, _sideAlpha);
        }

        private static void SetSlotState(RectTransform rect, CanvasGroup group, float scale, float alpha)
        {
            if (rect != null) rect.localScale = Vector3.one * scale;
            if (group != null) group.alpha = alpha;
        }

        private static void UpdateSlotIcon(Image icon, WeaponData weapon)
        {
            if (icon == null) return;
            icon.sprite = weapon != null ? weapon.Icon : null;
            icon.enabled = weapon != null && weapon.Icon != null;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// 武器切換完成 — 播放三槽位滑動動畫
        /// </summary>
        private void OnWeaponSwitchComplete(WeaponData newWeapon)
        {
            KillIfActive(ref _switchTween);
            Sequence seq = DOTween.Sequence();
            // 中槽 → 左槽（縮小 + 半透明）
            if (_centerSlotRect != null && _centerSlotGroup != null)
            {
                seq.Join(_centerSlotRect.DOAnchorPos(_leftAnchorPos, _switchDuration).SetEase(Ease.OutQuad));
                seq.Join(_centerSlotRect.DOScale(_sideScale, _switchDuration));
                seq.Join(_centerSlotGroup.DOFade(_sideAlpha, _switchDuration));
            }
            // 右槽 → 中槽（放大 + 不透明）
            if (_rightSlotRect != null && _rightSlotGroup != null)
            {
                seq.Join(_rightSlotRect.DOAnchorPos(_centerAnchorPos, _switchDuration).SetEase(Ease.OutQuad));
                seq.Join(_rightSlotRect.DOScale(_centerScale, _switchDuration));
                seq.Join(_rightSlotGroup.DOFade(1f, _switchDuration));
            }
            // 左槽先隱藏再出現在右邊
            if (_leftSlotRect != null && _leftSlotGroup != null)
            {
                seq.Join(_leftSlotGroup.DOFade(0f, _switchDuration * 0.3f));
            }
            seq.OnComplete(() =>
            {
                // 重置所有位置並更新圖示
                ResetSlotPositions();
                RefreshAllSlotIcons();
            });
            seq.SetLink(gameObject);
            _switchTween = seq;
            // 武器名彈出
            ShowWeaponName(newWeapon);
        }

        /// <summary>
        /// 預選武器變更 — 更新右槽圖示
        /// </summary>
        private void OnPreselectionChanged(WeaponData preselected)
        {
            UpdateSlotIcon(_rightSlotIcon, preselected);
            // 右槽小彈跳提示
            if (_rightSlotRect != null)
            {
                _rightSlotRect.DOPunchScale(Vector3.one * 0.1f, 0.2f, 6, 0.5f)
                    .SetLink(gameObject);
            }
        }

        #endregion

        #region Animation Methods

        /// <summary>
        /// 重置槽位位置到初始狀態
        /// </summary>
        private void ResetSlotPositions()
        {
            if (_leftSlotRect != null) _leftSlotRect.anchoredPosition = _leftAnchorPos;
            if (_centerSlotRect != null) _centerSlotRect.anchoredPosition = _centerAnchorPos;
            if (_rightSlotRect != null) _rightSlotRect.anchoredPosition = _rightAnchorPos;
            SetSlotState(_leftSlotRect, _leftSlotGroup, _sideScale, _sideAlpha);
            SetSlotState(_centerSlotRect, _centerSlotGroup, _centerScale, 1f);
            SetSlotState(_rightSlotRect, _rightSlotGroup, _sideScale, _sideAlpha);
        }

        /// <summary>
        /// 刷新所有槽位圖示
        /// </summary>
        private void RefreshAllSlotIcons()
        {
            if (_weaponManager == null) return;
            UpdateSlotIcon(_centerSlotIcon, _weaponManager.CurrentWeapon);
            UpdateSlotIcon(_rightSlotIcon, _weaponManager.PreselectedWeapon);
            WeaponData prevWeapon = _weaponManager.GetWeaponAtOffset(-1);
            UpdateSlotIcon(_leftSlotIcon, prevWeapon);
        }

        /// <summary>
        /// 武器名稱彈出：淡入 → 停留 → 淡出
        /// </summary>
        private void ShowWeaponName(WeaponData weapon)
        {
            if (_weaponNameGroup == null || _weaponNameText == null) return;
            KillIfActive(ref _nameTween);
            _weaponNameText.text = weapon != null ? weapon.WeaponName : "";
            Sequence seq = DOTween.Sequence();
            seq.Append(_weaponNameGroup.DOFade(1f, _nameFadeInDuration));
            seq.AppendInterval(_nameDisplayDuration);
            seq.Append(_weaponNameGroup.DOFade(0f, _nameFadeOutDuration));
            seq.SetLink(gameObject);
            _nameTween = seq;
        }

        /// <summary>
        /// 中槽位高亮脈動（常駐效果）
        /// </summary>
        private void StartHighlightPulse()
        {
            if (_centerHighlight == null) return;
            KillIfActive(ref _highlightTween);
            Color c = _centerHighlight.color;
            c.a = 0.3f;
            _centerHighlight.color = c;
            _highlightTween = _centerHighlight.DOFade(0.1f, 0.8f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject);
        }

        #endregion

        #region Utility

        private static void KillIfActive(ref Tween tween)
        {
            if (tween != null && tween.IsActive()) tween.Kill();
            tween = null;
        }

        #endregion
    }
}
