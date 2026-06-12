using System.Collections;
using DG.Tweening;
using GAS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Boss
{
    /// <summary>
    /// Boss 血條 (類魂風格) — 訂閱 BossController 的血量,平滑填充 + 延遲的「掉血殘影」白條。
    /// 初始隱藏 (CanvasGroup alpha 0),由開場過場結束時呼叫 Show() 顯示;Boss 死亡可自動隱藏。
    /// 設計師自行做血條視覺 (Filled 橫向 Image),把對應欄位拖進來即可。
    /// </summary>
    public class BossHealthBarUI : MonoBehaviour
    {
        #region Serialized Fields

        [Header("引用")]
        [SerializeField] [Tooltip("這條血條對應的 BossController — 拖飛龍 Boss 物件。留空 Awake 自動找場景第一個")]
        private BossController _boss;

        [SerializeField] [Tooltip("整條血條的 CanvasGroup (控制顯示/隱藏淡入淡出)。留空則血條一直顯示、不淡入淡出")]
        private CanvasGroup _canvasGroup;

        [SerializeField] [Tooltip("血量填充 Image — Image Type 必須設 Filled / Horizontal,程式改 fillAmount")]
        private Image _fillImage;

        [SerializeField] [Tooltip("(選填) 掉血殘影 Image — 放在填充後面、顏色淺一點。掉血時延遲跟上做出類魂「白條」。Type 同樣 Filled / Horizontal")]
        private Image _damageTrailImage;

        [SerializeField] [Tooltip("(選填) Boss 名稱文字")]
        private TMP_Text _bossNameText;

        [SerializeField] [Tooltip("Boss 名稱 — 顯示在名稱文字上")]
        private string _bossName = "飛龍";

        [Header("顯示 / 隱藏")]
        [SerializeField] [Tooltip("顯示淡入秒數。建議 0.3~0.6")]
        private float _showDuration = 0.5f;

        [SerializeField] [Tooltip("隱藏淡出秒數。建議 0.3~0.5")]
        private float _hideDuration = 0.4f;

        [Header("血量動畫")]
        [SerializeField] [Tooltip("主血條追上目標的秒數 (越小越快)。建議 0.1~0.3")]
        private float _fillLerpDuration = 0.2f;

        [SerializeField] [Tooltip("掉血殘影開始跟上前的延遲秒數。建議 0.3~0.7")]
        private float _trailDelay = 0.5f;

        [SerializeField] [Tooltip("掉血殘影追上的秒數。建議 0.4~0.8")]
        private float _trailLerpDuration = 0.6f;

        [Header("死亡")]
        [SerializeField] [Tooltip("Boss 死亡後自動隱藏血條")]
        private bool _hideOnDeath = true;

        [SerializeField] [Tooltip("死亡到隱藏的延遲秒數 (讓玩家看到血條歸零)。建議 1.5~3")]
        private float _hideOnDeathDelay = 2f;

        #endregion

        #region Private Fields

        private CombatAttributeSet _attributes;
        private Tween _fillTween;
        private Tween _trailTween;
        private Tween _showTween;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_boss == null) _boss = FindFirstObjectByType<BossController>();
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
            if (_bossNameText != null) _bossNameText.text = _bossName;
        }

        private void Start()
        {
            StartCoroutine(BindWhenReady());
        }

        private void OnDestroy()
        {
            if (_attributes != null) _attributes.OnHealthChanged -= HandleHealthChanged;
            if (_boss != null) _boss.OnDied -= HandleDied;
            KillTween(ref _fillTween);
            KillTween(ref _trailTween);
            KillTween(ref _showTween);
        }

        #endregion

        #region Public API

        /// <summary>顯示血條 (開場演出結束時呼叫)</summary>
        public void Show()
        {
            if (_canvasGroup == null) return;
            KillTween(ref _showTween);
            _showTween = _canvasGroup.DOFade(1f, _showDuration).SetUpdate(true).SetLink(gameObject);
        }

        /// <summary>隱藏血條</summary>
        public void Hide()
        {
            if (_canvasGroup == null) return;
            KillTween(ref _showTween);
            _showTween = _canvasGroup.DOFade(0f, _hideDuration).SetUpdate(true).SetLink(gameObject);
        }

        #endregion

        #region Private Methods

        private IEnumerator BindWhenReady()
        {
            // BossController.CombatAttributes 在它的 Start 才就緒 — 等到再訂閱,避免 Start 執行順序問題
            float elapsed = 0f;
            while (_boss == null || _boss.CombatAttributes == null)
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed > 5f)
                {
                    Debug.LogWarning($"[{name}] BossHealthBarUI 等不到 BossController 的 CombatAttributeSet — 血條不會更新。請確認有拖入 Boss", this);
                    yield break;
                }
                yield return null;
            }
            _attributes = _boss.CombatAttributes;
            _attributes.OnHealthChanged += HandleHealthChanged;
            _boss.OnDied += HandleDied;
            SetFillImmediate(_boss.HealthPercent);
        }

        private void HandleHealthChanged(float oldValue, float newValue)
        {
            UpdateFill(_boss.HealthPercent);
        }

        private void HandleDied()
        {
            UpdateFill(0f);
            if (_hideOnDeath) StartCoroutine(HideAfterDelay());
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSecondsRealtime(_hideOnDeathDelay);
            Hide();
        }

        private void UpdateFill(float pct)
        {
            pct = Mathf.Clamp01(pct);

            if (_fillImage != null)
            {
                KillTween(ref _fillTween);
                _fillTween = DOTween.To(() => _fillImage.fillAmount, x => _fillImage.fillAmount = x, pct, _fillLerpDuration)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .SetLink(gameObject);
            }

            if (_damageTrailImage != null)
            {
                KillTween(ref _trailTween);
                if (pct >= _damageTrailImage.fillAmount)
                {
                    // 補血 / 初始化:殘影立即跟上,不延遲
                    _damageTrailImage.fillAmount = pct;
                }
                else
                {
                    // 掉血:殘影延遲後緩緩追上,露出底下的主血條變化
                    _trailTween = DOTween.To(() => _damageTrailImage.fillAmount, x => _damageTrailImage.fillAmount = x, pct, _trailLerpDuration)
                        .SetDelay(_trailDelay)
                        .SetEase(Ease.OutCubic)
                        .SetUpdate(true)
                        .SetLink(gameObject);
                }
            }
        }

        private void SetFillImmediate(float pct)
        {
            pct = Mathf.Clamp01(pct);
            KillTween(ref _fillTween);
            KillTween(ref _trailTween);
            if (_fillImage != null) _fillImage.fillAmount = pct;
            if (_damageTrailImage != null) _damageTrailImage.fillAmount = pct;
        }

        private static void KillTween(ref Tween tween)
        {
            if (tween == null) return;
            if (tween.IsActive()) tween.Kill();
            tween = null;
        }

        #endregion
    }
}
