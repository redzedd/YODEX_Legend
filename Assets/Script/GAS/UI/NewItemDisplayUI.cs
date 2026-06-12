using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using DG.Tweening;
using Item;
using Player.Input;

namespace GAS.UI
{
    /// <summary>
    /// 新道具展示 UI — 第一次獲得道具時的全畫面展示
    /// Normal/Rare: 從下往上滑入（同背包面板風格）+ 淡入，退場滑下 + 淡出
    /// Legend: 放大淡入（大→正常）→ 放大淡出（正常→大），保留 Prefab 原始 Scale
    /// 所有動畫使用 SetUpdate(true) 在 Time.timeScale=0 下運作
    /// </summary>
    public class NewItemDisplayUI : MonoBehaviour
    {
        #region Serialized Fields

        [Header("UI 元件")]
        [Tooltip("物品大圖（fullSizeImage）")]
        [SerializeField] private Image _fullSizeImage;
        [Tooltip("物品小圖示（icon）")]
        [SerializeField] private Image _iconImage;
        [SerializeField] private Text _itemName;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Text _effectDescriptionText;
        [Tooltip("用於淡入淡出的 CanvasGroup")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [Tooltip("用於動畫的 RectTransform（字卡根物件）")]
        [SerializeField] private RectTransform _cardRect;

        [Header("時序")]
        [SerializeField] private float _minDisplayDuration = 0.5f;

        [Header("Normal/Rare 動畫（從下往上滑入）")]
        [Tooltip("進場時間")]
        [SerializeField] private float _slideInDuration = 0.35f;
        [Tooltip("退場時間")]
        [SerializeField] private float _slideOutDuration = 0.25f;
        [Tooltip("隱藏時的 Y 軸偏移量（負值 = 在下方）")]
        [SerializeField] private float _hiddenOffsetY = -300f;
        [Tooltip("進場 Ease")]
        [SerializeField] private Ease _slideInEase = Ease.OutBack;
        [Tooltip("退場 Ease")]
        [SerializeField] private Ease _slideOutEase = Ease.InQuad;

        [Header("Legend 動畫（縮放 + 淡入淡出）")]
        [Tooltip("傳奇進場時間")]
        [SerializeField] private float _legendInDuration = 0.6f;
        [Tooltip("傳奇退場時間")]
        [SerializeField] private float _legendOutDuration = 0.4f;
        [Tooltip("傳奇進場初始縮放倍率（相對於 Prefab 原始 Scale，> 1 = 比原始大）")]
        [SerializeField] private float _legendStartScaleMultiplier = 1.5f;
        [Tooltip("傳奇退場最終縮放倍率（相對於 Prefab 原始 Scale，> 1 = 比原始大）")]
        [SerializeField] private float _legendEndScaleMultiplier = 1.3f;

        [Header("音效")]
        [Tooltip("字卡出現音效")]
        [SerializeField] private AudioClip _appearSFX;
        [Tooltip("玩家按下關閉的音效")]
        [SerializeField] private AudioClip _closeSFX;
        [SerializeField] private AudioSource _audioSource;

        #endregion

        #region Private Fields

        private GameObject _previousSelected;
        private RareLevel _rareLevel;
        private Sequence _currentSequence;
        private Vector3 _originalScale;
        private Vector2 _originalAnchoredPos;

        // 進場前狀態快照 — 字卡可能由烹飪/背包等 UI 觸發,
        // 那些 UI 已將 timeScale=0 / 玩家輸入停用; 字卡離場時必須回到快照狀態,
        // 而非寫死 timeScale=1 + EnablePlayerInput, 否則背景 UI 的模態狀態會被破壞。
        private float _prevTimeScale = 1f;
        private bool _prevPlayerInputEnabled = true;
        private bool _didSnapshot;

        #endregion

        /// <summary>目前場上字卡數量 — 其他 UI 在 Update 中應該在 > 0 時跳過輸入處理,
        /// 避免關閉字卡的按鍵同幀被背景 UI 誤讀。</summary>
        public static int ActiveCardCount { get; private set; }
        public static bool IsAnyCardActive => ActiveCardCount > 0;

        private void Awake()
        {
            // 記錄 Prefab 設定的原始比例與位置，確保動畫目標正確
            if (_cardRect != null)
            {
                _originalScale = _cardRect.localScale;
                _originalAnchoredPos = _cardRect.anchoredPosition;
            }
        }

        public void Setup(ItemData data)
        {
            // 設定顯示內容
            if (_iconImage != null) _iconImage.sprite = data.icon;
            if (_fullSizeImage != null && data.fullSizeImage != null)
                _fullSizeImage.sprite = data.fullSizeImage;
            if (_itemName != null) _itemName.text = data.itemName;
            if (_descriptionText != null) _descriptionText.text = data.description;
            if (_effectDescriptionText != null) _effectDescriptionText.text = data.effectDescription;
            _rareLevel = data.rareLevel;
            // 設定初始狀態
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            if (_cardRect != null)
            {
                if (_rareLevel == RareLevel.Legend)
                {
                    // Legend: 保留原始比例乘上倍率，從大開始縮小到原始大小
                    _cardRect.localScale = _originalScale * _legendStartScaleMultiplier;
                    _cardRect.anchoredPosition = _originalAnchoredPos;
                }
                else
                {
                    // Normal/Rare: 保持原始比例，從下方偏移開始滑入
                    _cardRect.localScale = _originalScale;
                    _cardRect.anchoredPosition = _originalAnchoredPos + new Vector2(0, _hiddenOffsetY);
                }
            }
            // 記錄顯示前選取的 UI
            _previousSelected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            // 快照進場前狀態,離場時還原 — 避免覆寫背景 UI 的模態狀態
            if (!_didSnapshot)
            {
                _prevTimeScale = Time.timeScale;
                _prevPlayerInputEnabled = SystemInputReader.Instance != null
                    && SystemInputReader.Instance.IsPlayerInputEnabled;
                _didSnapshot = true;
                ActiveCardCount++;
            }
            Time.timeScale = 0f;
            // 停用 Player ActionMap,避免字卡期間仍能拾取/攻擊/移動
            // (timeScale=0 不會阻擋 Input System 回呼,必須顯式停用)
            if (SystemInputReader.Instance != null)
                SystemInputReader.Instance.DisablePlayerInput();
            // 播放進場音效 + 動畫
            PlaySFX(_appearSFX);
            PlayEnterAnimation(() => StartCoroutine(WaitAndCloseCoroutine()));
        }

        #region 動畫

        private void PlayEnterAnimation(TweenCallback onComplete)
        {
            KillSequence();
            _currentSequence = DOTween.Sequence();
            if (_rareLevel == RareLevel.Legend)
            {
                // Legend: 縮放大→原始大小 + 淡入
                if (_cardRect != null)
                    _currentSequence.Append(
                        _cardRect.DOScale(_originalScale, _legendInDuration).SetEase(Ease.OutCubic));
                if (_canvasGroup != null)
                    _currentSequence.Join(
                        _canvasGroup.DOFade(1f, _legendInDuration * 0.7f));
            }
            else
            {
                // Normal/Rare: 從下往上滑入 + 淡入
                if (_cardRect != null)
                    _currentSequence.Append(
                        _cardRect.DOAnchorPos(_originalAnchoredPos, _slideInDuration)
                            .SetEase(_slideInEase));
                if (_canvasGroup != null)
                    _currentSequence.Join(
                        _canvasGroup.DOFade(1f, _slideInDuration * 0.6f));
            }
            _currentSequence.SetUpdate(true);
            _currentSequence.SetLink(gameObject);
            _currentSequence.OnComplete(onComplete);
        }

        private void PlayExitAnimation(TweenCallback onComplete)
        {
            KillSequence();
            _currentSequence = DOTween.Sequence();
            PlaySFX(_closeSFX);
            if (_rareLevel == RareLevel.Legend)
            {
                // Legend: 縮放原始大小→大 + 淡出
                if (_cardRect != null)
                    _currentSequence.Append(
                        _cardRect.DOScale(_originalScale * _legendEndScaleMultiplier, _legendOutDuration)
                            .SetEase(Ease.InCubic));
                if (_canvasGroup != null)
                    _currentSequence.Join(
                        _canvasGroup.DOFade(0f, _legendOutDuration));
            }
            else
            {
                // Normal/Rare: 滑回下方 + 淡出
                Vector2 hiddenPos = _originalAnchoredPos + new Vector2(0, _hiddenOffsetY);
                if (_cardRect != null)
                    _currentSequence.Append(
                        _cardRect.DOAnchorPos(hiddenPos, _slideOutDuration)
                            .SetEase(_slideOutEase));
                if (_canvasGroup != null)
                    _currentSequence.Join(
                        _canvasGroup.DOFade(0f, _slideOutDuration));
            }
            _currentSequence.SetUpdate(true);
            _currentSequence.SetLink(gameObject);
            _currentSequence.OnComplete(onComplete);
        }

        private void KillSequence()
        {
            if (_currentSequence != null && _currentSequence.IsActive())
                _currentSequence.Kill();
            _currentSequence = null;
        }

        #endregion

        #region 輸入等待

        private IEnumerator WaitAndCloseCoroutine()
        {
            float startTime = Time.unscaledTime;
            // 等最短展示時間
            while (Time.unscaledTime - startTime < _minDisplayDuration)
                yield return null;
            // 等玩家按任意鍵 — 立即觸發退場,不等放開
            // (舊版會 WaitUntil 全部放開,導致玩家按住時字卡卡在場上不消失)
            yield return new WaitUntil(() => Input.anyKeyDown);
            // 播放退場動畫,等待完成
            bool exitDone = false;
            PlayExitAnimation(() => exitDone = true);
            yield return new WaitUntil(() => exitDone);
            // 短暫封鎖開背包鍵 — 避免關閉字卡的鍵被誤讀為開背包
            if (SystemInputReader.Instance != null)
            {
                SystemInputReader.Instance.BlockOpenInventoryFor(0.12f);
                // 僅在進場前 Player Input 是啟用的才恢復; 若由烹飪/背包 UI 觸發進場,
                // 維持停用,後續由該 UI 自行管理。
                // 用 Deferred 版本等玩家放開關閉鍵 — 避免同一個物理鍵也綁定為跳躍/互動時誤觸發。
                if (_prevPlayerInputEnabled)
                    SystemInputReader.Instance.EnablePlayerInputDeferred(0.3f);
            }
            Time.timeScale = _prevTimeScale;
            if (_previousSelected != null)
            {
                yield return null;
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(_previousSelected);
            }
            Destroy(gameObject);
        }

        #endregion

        private void PlaySFX(AudioClip clip)
        {
            if (_audioSource == null || clip == null) return;
            _audioSource.PlayOneShot(clip);
        }

        private void OnDestroy()
        {
            KillSequence();
            if (!_didSnapshot) return;
            // 保險:無論 Coroutine 是否走完都把狀態還原到進場前快照
            // (場景切換等情境下 Coroutine 可能未跑完就被銷毀)
            if (Time.timeScale != _prevTimeScale)
                Time.timeScale = _prevTimeScale;
            if (SystemInputReader.Instance != null
                && _prevPlayerInputEnabled
                && !SystemInputReader.Instance.IsPlayerInputEnabled)
                SystemInputReader.Instance.EnablePlayerInput();
            ActiveCardCount = Mathf.Max(0, ActiveCardCount - 1);
        }
    }
}
