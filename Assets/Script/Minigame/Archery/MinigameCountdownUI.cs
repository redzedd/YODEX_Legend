using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Minigame.Archery
{
    /// <summary>
    /// 射箭小遊戲 — 螢幕上方倒數計時 UI
    /// 控制 CanvasGroup 淡入淡出、TMP 文字更新、時間不足時染紅 + 脈動
    /// 由 ArcheryMinigameController 驅動：Show(time) → Tick(time) 每秒 → Hide()
    /// 整顆 UI 物件預設不啟用，由 Controller 開啟
    /// </summary>
    public class MinigameCountdownUI : MonoBehaviour
    {
        [Header("UI 元件")]
        [Tooltip("倒數秒數的 TMP_Text")]
        [SerializeField] private TMP_Text _timerText;

        [Tooltip("整體 CanvasGroup（控制淡入淡出）")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Tooltip("狀態說明文字（例如『擊落所有靶心！』）")]
        [SerializeField] private TMP_Text _stageText;

        [Header("動畫")]
        [Tooltip("淡入秒數")]
        [SerializeField] private float _fadeInDuration = 0.3f;

        [Tooltip("淡出秒數")]
        [SerializeField] private float _fadeOutDuration = 0.5f;

        [Header("低時間警示")]
        [Tooltip("剩餘秒數低於此值時觸發紅色 + 脈動警示")]
        [SerializeField] private float _warningThreshold = 5f;

        [Tooltip("正常顏色")]
        [SerializeField] private Color _normalColor = Color.white;

        [Tooltip("警示顏色")]
        [SerializeField] private Color _warningColor = Color.red;

        private Tween _fadeTween;
        private Tween _pulseTween;
        private bool _isWarning;

        private void Awake()
        {
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        }

        private void OnDestroy()
        {
            _fadeTween?.Kill();
            _pulseTween?.Kill();
        }

        public void Show(float initialSeconds, string stageMessage = "")
        {
            gameObject.SetActive(true);
            _isWarning = false;
            if (_timerText != null) _timerText.color = _normalColor;
            if (_stageText != null) _stageText.text = stageMessage;
            UpdateTime(initialSeconds);
            _fadeTween?.Kill();
            _fadeTween = _canvasGroup
                .DOFade(1f, _fadeInDuration)
                .SetEase(Ease.OutCubic)
                .SetLink(gameObject);
        }

        public void Hide()
        {
            _pulseTween?.Kill();
            _fadeTween?.Kill();
            _fadeTween = _canvasGroup
                .DOFade(0f, _fadeOutDuration)
                .SetEase(Ease.InCubic)
                .OnComplete(() => gameObject.SetActive(false))
                .SetLink(gameObject);
        }

        public void UpdateTime(float remainingSeconds)
        {
            if (_timerText == null) return;
            float clamped = Mathf.Max(0f, remainingSeconds);
            _timerText.text = clamped.ToString("F1");
            if (!_isWarning && clamped <= _warningThreshold)
                EnterWarning();
        }

        public void UpdateStageMessage(string message)
        {
            if (_stageText != null) _stageText.text = message;
        }

        private void EnterWarning()
        {
            _isWarning = true;
            if (_timerText != null) _timerText.color = _warningColor;
            _pulseTween?.Kill();
            _pulseTween = _timerText.transform
                .DOScale(1.2f, 0.4f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject);
        }
    }
}
