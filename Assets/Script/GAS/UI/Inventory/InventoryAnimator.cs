using System;
using UnityEngine;
using DG.Tweening;

namespace GAS.UI.Inventory
{
    /// <summary>
    /// 背包 UI 動畫控制器 — 所有 DOTween 面板動畫集中管理
    /// 包含：面板開關滑動/淡入淡出、格子懸浮縮放、翻頁過場
    /// 所有動畫使用 SetUpdate(true) 因為背包開啟時 Time.timeScale = 0
    /// </summary>
    public class InventoryAnimator : MonoBehaviour
    {
        #region Serialized Fields

        [Header("面板")]
        [Tooltip("背包面板 RectTransform（用於滑動動畫）")]
        [SerializeField] private RectTransform _panelRect;
        [Tooltip("背包面板 CanvasGroup（用於淡入淡出）")]
        [SerializeField] private CanvasGroup _panelCanvasGroup;

        [Header("開關動畫")]
        [SerializeField] private float _openDuration = 0.35f;
        [SerializeField] private float _closeDuration = 0.25f;
        [SerializeField] private Ease _openEase = Ease.OutBack;
        [SerializeField] private Ease _closeEase = Ease.InQuad;

        [Header("滑動偏移")]
        [Tooltip("面板隱藏時的錨點偏移量")]
        [SerializeField] private Vector2 _panelHiddenOffset = new Vector2(0, -600);

        [Header("格子懸浮")]
        [SerializeField] private float _slotHoverScale = 1.1f;
        [SerializeField] private float _slotHoverDuration = 0.1f;

        [Header("翻頁過場")]
        [Tooltip("格子容器 RectTransform（翻頁滑動）；需掛有 CanvasGroup 組件")]
        [SerializeField] private RectTransform _slotsContainer;
        [Tooltip("格子容器 CanvasGroup（翻頁淡入淡出）")]
        [SerializeField] private CanvasGroup _slotsCanvasGroup;
        [Tooltip("翻頁過場總時長（秒），淡出與淡入各佔一半")]
        [SerializeField] private float _pageTransitionDuration = 0.3f;
        [Tooltip("翻頁滑動距離（像素）")]
        [SerializeField] private float _pageSlideDistance = 200f;
        [SerializeField] private Ease _pageOutEase = Ease.InQuad;
        [SerializeField] private Ease _pageInEase = Ease.OutCubic;

        [Header("音效")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _pageFlipSound;

        #endregion

        #region Private Fields

        private Tween _panelMoveTween;
        private Tween _panelFadeTween;
        private Vector2 _panelOriginalPos;
        private bool _isInitialized;

        private Sequence _pageTransitionSequence;
        private Vector2 _slotsOriginalPos;
        private bool _slotsInitialized;

        #endregion

        #region 生命週期

        private void Awake()
        {
            CacheOriginalPosition();
            CacheSlotsContainerPosition();
        }

        private void OnDestroy()
        {
            KillAllTweens();
        }

        #endregion

        #region 面板開關動畫

        /// <summary>播放面板開啟動畫（滑入 + 淡入）</summary>
        /// <param name="onComplete">動畫完成回呼</param>
        public void PlayOpen(Action onComplete = null)
        {
            CacheOriginalPosition();
            KillPanelTweens();
            // 初始狀態：偏移 + 透明
            _panelRect.anchoredPosition = _panelOriginalPos + _panelHiddenOffset;
            _panelCanvasGroup.alpha = 0f;
            // 滑入
            _panelMoveTween = _panelRect
                .DOAnchorPos(_panelOriginalPos, _openDuration)
                .SetEase(_openEase)
                .SetUpdate(true)
                .SetLink(gameObject);
            // 淡入
            _panelFadeTween = _panelCanvasGroup
                .DOFade(1f, _openDuration * 0.6f)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>播放面板關閉動畫（滑出 + 淡出）</summary>
        /// <param name="onComplete">動畫完成回呼（通常用於 SetActive(false)）</param>
        public void PlayClose(Action onComplete = null)
        {
            KillPanelTweens();
            // 滑出
            _panelMoveTween = _panelRect
                .DOAnchorPos(_panelOriginalPos + _panelHiddenOffset, _closeDuration)
                .SetEase(_closeEase)
                .SetUpdate(true)
                .SetLink(gameObject);
            // 淡出
            _panelFadeTween = _panelCanvasGroup
                .DOFade(0f, _closeDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    // 還原位置避免下次開啟時閃爍
                    _panelRect.anchoredPosition = _panelOriginalPos;
                    onComplete?.Invoke();
                });
        }

        #endregion

        #region 格子懸浮動畫

        /// <summary>格子進入懸浮（放大）</summary>
        public void PlaySlotHoverEnter(RectTransform slotRect)
        {
            if (slotRect == null) return;
            slotRect.DOKill();
            slotRect
                .DOScale(_slotHoverScale, _slotHoverDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .SetLink(slotRect.gameObject);
        }

        /// <summary>格子離開懸浮（還原）</summary>
        public void PlaySlotHoverExit(RectTransform slotRect)
        {
            if (slotRect == null) return;
            slotRect.DOKill();
            slotRect
                .DOScale(1f, _slotHoverDuration)
                .SetEase(Ease.InOutQuad)
                .SetUpdate(true)
                .SetLink(slotRect.gameObject);
        }

        #endregion

        #region 翻頁過場動畫

        /// <summary>
        /// 播放翻頁過場：本頁滑出淡出 → onMidpoint 渲染新頁 → 新頁滑入淡入
        /// </summary>
        /// <param name="direction">翻頁方向：+1 下一頁（本頁向左離開）, -1 上一頁（本頁向右離開）</param>
        /// <param name="onMidpoint">過場中點回呼，於此渲染新頁面內容</param>
        /// <param name="onComplete">動畫全部完成後回呼</param>
        public void PlayPageTransition(int direction, Action onMidpoint, Action onComplete = null)
        {
            // 若未設定容器則直接執行回呼（降級處理）
            if (_slotsContainer == null || _slotsCanvasGroup == null)
            {
                onMidpoint?.Invoke();
                onComplete?.Invoke();
                return;
            }
            CacheSlotsContainerPosition();
            KillPageTween();
            PlayPageSound();
            float halfDuration = _pageTransitionDuration * 0.5f;
            // direction > 0 → 下一頁：本頁向左滑出，新頁從右滑入
            // direction < 0 → 上一頁：本頁向右滑出，新頁從左滑入
            float sign = Mathf.Sign(direction);
            Vector2 slideOutTarget = _slotsOriginalPos + new Vector2(-_pageSlideDistance * sign, 0f);
            Vector2 slideInStart = _slotsOriginalPos + new Vector2(_pageSlideDistance * sign, 0f);
            Sequence seq = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject);
            // Phase 1：本頁滑出 + 淡出
            seq.Append(
                _slotsContainer.DOAnchorPos(slideOutTarget, halfDuration)
                    .SetEase(_pageOutEase)
                    .SetUpdate(true)
            );
            seq.Join(
                _slotsCanvasGroup.DOFade(0f, halfDuration)
                    .SetEase(Ease.InQuad)
                    .SetUpdate(true)
            );
            // 中點：瞬移到滑入起點，渲染新頁內容
            seq.AppendCallback(() =>
            {
                _slotsContainer.anchoredPosition = slideInStart;
                onMidpoint?.Invoke();
            });
            // Phase 2：新頁滑入 + 淡入
            seq.Append(
                _slotsContainer.DOAnchorPos(_slotsOriginalPos, halfDuration)
                    .SetEase(_pageInEase)
                    .SetUpdate(true)
            );
            seq.Join(
                _slotsCanvasGroup.DOFade(1f, halfDuration)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
            );
            seq.OnComplete(() => onComplete?.Invoke());
            _pageTransitionSequence = seq;
        }

        #endregion

        #region 工具方法

        private void CacheOriginalPosition()
        {
            if (_isInitialized || _panelRect == null) return;
            _panelOriginalPos = _panelRect.anchoredPosition;
            _isInitialized = true;
        }

        private void CacheSlotsContainerPosition()
        {
            if (_slotsInitialized || _slotsContainer == null) return;
            _slotsOriginalPos = _slotsContainer.anchoredPosition;
            _slotsInitialized = true;
        }

        private void KillPanelTweens()
        {
            if (_panelMoveTween != null && _panelMoveTween.IsActive())
                _panelMoveTween.Kill();
            if (_panelFadeTween != null && _panelFadeTween.IsActive())
                _panelFadeTween.Kill();
            _panelMoveTween = null;
            _panelFadeTween = null;
        }

        private void KillPageTween()
        {
            if (_pageTransitionSequence != null && _pageTransitionSequence.IsActive())
                _pageTransitionSequence.Kill();
            _pageTransitionSequence = null;
            // 強制還原位置與透明度，避免快速翻頁時殘留偏移
            if (_slotsContainer != null)
                _slotsContainer.anchoredPosition = _slotsOriginalPos;
            if (_slotsCanvasGroup != null)
                _slotsCanvasGroup.alpha = 1f;
        }

        private void PlayPageSound()
        {
            if (_audioSource == null || _pageFlipSound == null) return;
            _audioSource.PlayOneShot(_pageFlipSound);
        }

        private void KillAllTweens()
        {
            KillPanelTweens();
            KillPageTween();
        }

        #endregion
    }
}
