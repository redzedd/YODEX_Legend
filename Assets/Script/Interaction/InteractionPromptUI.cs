using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace Interaction
{
    /// <summary>
    /// 互動類型與圖示的配對結構 — 在 Inspector 中自由設定類型名稱與圖示
    /// </summary>
    [Serializable]
    public struct InteractionTypeIcon
    {
        [Tooltip("自訂互動類型名稱（需與互動物件上的類型名稱一致）")]
        public string typeName;
        public Sprite icon;
    }

    /// <summary>
    /// 集中式互動提示 UI — 訂閱 InteractionManager.OnFocusChanged
    /// 根據當前聚焦的 IInteractable 顯示/隱藏提示文字與圖示
    /// 帶有淡入淡出 + 持續脈動縮放動畫效果
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        #region Serialized Fields

        [Header("UI 元件")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_Text _promptText;
        [SerializeField] private Image _typeIcon;
        [Tooltip("脈動動畫的目標 RectTransform（整個提示面板）")]
        [SerializeField] private RectTransform _promptRect;

        [Header("圖示對應")]
        [Tooltip("自由設定每種互動類型對應的圖示，不需填滿所有類型")]
        [SerializeField] private InteractionTypeIcon[] _typeIconMappings;

        [Header("淡入淡出")]
        [SerializeField] private float _fadeInDuration = 0.2f;
        [SerializeField] private float _fadeOutDuration = 0.15f;
        [SerializeField] private Ease _fadeInEase = Ease.OutQuad;
        [SerializeField] private Ease _fadeOutEase = Ease.InQuad;

        [Header("脈動動畫")]
        [Tooltip("脈動最大縮放比例")]
        [SerializeField] private float _pulseScale = 1.08f;
        [Tooltip("單次脈動週期（秒）")]
        [SerializeField] private float _pulseDuration = 0.8f;

        #endregion

        #region Private Fields

        private Tween _fadeTween;
        private Tween _pulseTween;
        private bool _isSubscribed;
        private Dictionary<string, Sprite> _iconLookup;

        // 避免跨互動物件切換時的閃爍：低於此值才播淡入動畫
        private const float VISIBLE_THRESHOLD = 0.5f;

        #endregion

        #region 生命週期

        private void Awake()
        {
            BuildIconLookup();
            if (_canvasGroup != null)
                _canvasGroup.alpha = 0f;
        }
        private void BuildIconLookup()
        {
            _iconLookup = new Dictionary<string, Sprite>();
            if (_typeIconMappings == null) return;
            foreach (InteractionTypeIcon mapping in _typeIconMappings)
            {
                if (!string.IsNullOrEmpty(mapping.typeName) && mapping.icon != null)
                    _iconLookup[mapping.typeName] = mapping.icon;
            }
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            // 補救：若 OnEnable 執行時 InteractionManager.Instance 尚未初始化則在此重新訂閱
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (!_isSubscribed) return;
            if (InteractionManager.Instance != null)
                InteractionManager.Instance.OnFocusChanged -= HandleFocusChanged;
            _isSubscribed = false;
        }

        private void TrySubscribe()
        {
            if (_isSubscribed) return;
            if (InteractionManager.Instance == null) return;
            InteractionManager.Instance.OnFocusChanged += HandleFocusChanged;
            _isSubscribed = true;
            // 訂閱後立即同步當前焦點狀態（補充可能遺漏的初始事件）
            SyncCurrentFocus();
        }

        private void OnDestroy()
        {
            KillFadeTween();
            KillPulseTween();
        }

        #endregion

        #region 焦點變更處理

        private void HandleFocusChanged(IInteractable newFocus, IInteractable oldFocus)
        {
            if (newFocus != null)
                ShowPrompt(newFocus);
            else
                HidePrompt();
        }

        /// <summary>
        /// 訂閱後同步目前焦點狀態，避免初始事件遺漏導致圖示不顯示
        /// </summary>
        private void SyncCurrentFocus()
        {
            IInteractable current = InteractionManager.Instance?.CurrentFocused;
            if (current != null)
                ShowPrompt(current);
        }

        private void ShowPrompt(IInteractable interactable)
        {
            // 更新文字
            if (_promptText != null)
                _promptText.text = interactable.PromptText;
            // 更新圖示
            if (_typeIcon != null)
            {
                if (_iconLookup != null && _iconLookup.TryGetValue(interactable.InteractionTypeName, out Sprite icon))
                {
                    _typeIcon.sprite = icon;
                    _typeIcon.enabled = true;
                }
                else
                {
                    _typeIcon.enabled = false;
                }
            }
            // 淡入動畫
            // 若已接近完全可見（連續拾取切換焦點時），直接設為 1 避免閃爍
            KillFadeTween();
            if (_canvasGroup != null)
            {
                if (_canvasGroup.alpha >= VISIBLE_THRESHOLD)
                {
                    _canvasGroup.alpha = 1f;
                }
                else
                {
                    _fadeTween = _canvasGroup
                        .DOFade(1f, _fadeInDuration)
                        .SetEase(_fadeInEase)
                        .SetUpdate(true)
                        .SetLink(gameObject);
                }
            }
            // 啟動脈動動畫
            StartPulse();
        }

        private void HidePrompt()
        {
            // 停止脈動
            KillPulseTween();
            // 淡出動畫
            KillFadeTween();
            if (_canvasGroup != null)
            {
                _fadeTween = _canvasGroup
                    .DOFade(0f, _fadeOutDuration)
                    .SetEase(_fadeOutEase)
                    .SetUpdate(true)
                    .SetLink(gameObject);
            }
        }

        #endregion

        #region 脈動動畫

        /// <summary>持續縮放脈動動畫（放大縮小循環）</summary>
        private void StartPulse()
        {
            KillPulseTween();
            if (_promptRect == null) return;
            _promptRect.localScale = Vector3.one;
            _pulseTween = _promptRect
                .DOScale(_pulseScale, _pulseDuration * 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        private void KillPulseTween()
        {
            if (_pulseTween != null && _pulseTween.IsActive())
                _pulseTween.Kill();
            _pulseTween = null;
            // 重置縮放
            if (_promptRect != null)
                _promptRect.localScale = Vector3.one;
        }

        #endregion

        #region 工具方法

        private void KillFadeTween()
        {
            if (_fadeTween != null && _fadeTween.IsActive())
                _fadeTween.Kill();
            _fadeTween = null;
        }

        #endregion
    }
}
