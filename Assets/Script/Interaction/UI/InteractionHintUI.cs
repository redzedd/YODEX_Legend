using UnityEngine;
using TMPro;
using DG.Tweening;

namespace Interaction
{
    /// <summary>
    /// 互動提示 UI — 可復用的螢幕上方提示文字系統（Singleton）
    /// 淡入出現於螢幕上方 → 持續顯示 → 向上淡出消失
    /// 新提示觸發時自動替換前一則（舊提示立即向上淡出）
    /// 使用方式：在場景中放置此 Prefab，任何腳本呼叫 InteractionHintUI.Instance.Show("提示文字") 即可
    /// </summary>
    public class InteractionHintUI : MonoBehaviour
    {
        public static InteractionHintUI Instance { get; private set; }

        [Header("UI 參考")]
        [Tooltip("提示文字 TMP_Text")]
        [SerializeField] private TMP_Text _hintText;
        [Tooltip("整體 CanvasGroup（控制淡入淡出）")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [Tooltip("提示文字的 RectTransform（控制向上滑出）")]
        [SerializeField] private RectTransform _hintRect;

        [Header("動畫設定")]
        [Tooltip("淡入時間")]
        [SerializeField] private float _fadeInDuration = 0.3f;
        [Tooltip("顯示持續時間")]
        [SerializeField] private float _displayDuration = 2f;
        [Tooltip("淡出時間")]
        [SerializeField] private float _fadeOutDuration = 0.5f;
        [Tooltip("向上滑出距離（像素）")]
        [SerializeField] private float _slideUpDistance = 50f;

        [Header("音效")]
        [Tooltip("音源（可選，若不指定則自動建立）")]
        [SerializeField] private AudioSource _audioSource;
        [Tooltip("預設提示音效（Show 未傳入 sfx 時使用）")]
        [SerializeField] private AudioClip _defaultSFX;

        private Sequence _currentSequence;
        private Vector2 _originalPosition;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (_hintRect != null)
                _originalPosition = _hintRect.anchoredPosition;
            if (_canvasGroup != null)
                _canvasGroup.alpha = 0f;
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();
        }

        /// <summary>
        /// 顯示提示文字（自動替換前一則）
        /// </summary>
        /// <param name="message">提示文字內容</param>
        /// <param name="sfx">自訂音效（null 時使用預設音效）</param>
        public void Show(string message, AudioClip sfx = null)
        {
            KillCurrentSequence();
            // 播放音效
            AudioClip clip = sfx != null ? sfx : _defaultSFX;
            if (_audioSource != null && clip != null)
                _audioSource.PlayOneShot(clip);
            // 設定文字
            if (_hintText != null)
                _hintText.text = message;
            // 重置位置與透明度
            if (_hintRect != null)
                _hintRect.anchoredPosition = _originalPosition;
            if (_canvasGroup != null)
                _canvasGroup.alpha = 0f;
            // 建立動畫序列：淡入 → 停留 → 向上淡出
            _currentSequence = DOTween.Sequence()
                .Append(_canvasGroup.DOFade(1f, _fadeInDuration).SetEase(Ease.OutCubic))
                .AppendInterval(_displayDuration)
                .Append(_canvasGroup.DOFade(0f, _fadeOutDuration).SetEase(Ease.InCubic))
                .Join(
                    _hintRect.DOAnchorPosY(
                        _originalPosition.y + _slideUpDistance,
                        _fadeOutDuration
                    ).SetEase(Ease.InCubic)
                )
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        private void KillCurrentSequence()
        {
            if (_currentSequence != null && _currentSequence.IsActive())
                _currentSequence.Kill();
            _currentSequence = null;
        }

        private void OnDestroy()
        {
            KillCurrentSequence();
        }
    }
}
