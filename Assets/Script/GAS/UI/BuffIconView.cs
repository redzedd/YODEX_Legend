using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace GAS.UI
{
    /// <summary>
    /// 單一 Buff 圖標顯示 — 含 DOTween 進場/退場/計時/升級動畫
    /// </summary>
    public class BuffIconView : MonoBehaviour
    {
        [Header("Display")]
        public Image iconImage;
        public TMP_Text levelText;

        [Header("Timer")]
        [Tooltip("倒數計時覆蓋層（Fill 類型 Image）")]
        [SerializeField] private Image _timerFillImage;

        [Header("Animation Settings")]
        [SerializeField] private float _popInDuration = 0.3f;
        [SerializeField] private float _popOutDuration = 0.25f;
        [SerializeField] private float _punchScale = 0.2f;

        private BuffDefinition _def;
        private int _level;
        private CanvasGroup _canvasGroup;

        // === Tween 引用 ===
        private Tween _popTween;
        private Tween _timerTween;
        private Tween _punchTween;

        private void Awake()
        {
            // 確保有 CanvasGroup（退場淡出用）
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void OnDestroy()
        {
            KillIfActive(ref _popTween);
            KillIfActive(ref _timerTween);
            KillIfActive(ref _punchTween);
        }

        /// <summary>
        /// 綁定 Buff 資料並播放進場動畫
        /// </summary>
        public void Bind(BuffDefinition def, int level)
        {
            bool isFirstBind = _def == null;
            bool isUpgrade = !isFirstBind && level > _level;
            _def = def;
            _level = Mathf.Clamp(level, 1, 3);
            BuffTierData tier = _def.GetTier(_level);
            if (iconImage != null) iconImage.sprite = tier.icon;
            if (levelText != null) levelText.text = _level > 1 ? $"Lv.{_level}" : "";
            // 進場彈出動畫
            if (isFirstBind)
            {
                PlayPopIn();
            }
            else if (isUpgrade)
            {
                PlayUpgradePulse();
            }
            // 啟動計時倒數
            if (tier.duration > 0f)
            {
                StartTimer(tier.duration);
            }
        }

        /// <summary>
        /// 播放退場動畫，完成後呼叫 onComplete
        /// </summary>
        public void PlayRemoveAnimation(System.Action onComplete)
        {
            KillIfActive(ref _popTween);
            KillIfActive(ref _timerTween);
            KillIfActive(ref _punchTween);
            Sequence seq = DOTween.Sequence();
            seq.Append(transform.DOScale(0f, _popOutDuration).SetEase(Ease.InBack));
            seq.Join(_canvasGroup.DOFade(0f, _popOutDuration));
            seq.OnComplete(() => onComplete?.Invoke());
            seq.SetLink(gameObject);
            _popTween = seq;
        }

        #region Private Animation Methods

        /// <summary>
        /// 進場彈出（OutBack 回彈效果）
        /// </summary>
        private void PlayPopIn()
        {
            KillIfActive(ref _popTween);
            transform.localScale = Vector3.zero;
            _popTween = transform.DOScale(1f, _popInDuration)
                .SetEase(Ease.OutBack)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 升級脈動
        /// </summary>
        private void PlayUpgradePulse()
        {
            KillIfActive(ref _punchTween);
            _punchTween = transform.DOPunchScale(Vector3.one * _punchScale, 0.3f, 6, 0.5f)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 啟動計時倒數（Fill 從 1 → 0）
        /// </summary>
        private void StartTimer(float duration)
        {
            if (_timerFillImage == null) return;
            KillIfActive(ref _timerTween);
            _timerFillImage.fillAmount = 1f;
            _timerTween = _timerFillImage.DOFillAmount(0f, duration)
                .SetEase(Ease.Linear)
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
