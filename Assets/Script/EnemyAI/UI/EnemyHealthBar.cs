using DG.Tweening;
using GAS;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyAI.UI
{
    /// <summary>
    /// 怪物頭頂世界空間血量 + 韌性條。掛在 EnemyController 底下的子物件（World Space Canvas）即可，
    /// 會自動往上找到 EnemyController，不需手動拖引用。平時隱藏，受擊或戰鬥中才淡入，脫戰後淡出。
    /// 血量與韌性都有「殘影掉血」效果：主條瞬間到位，殘影條延遲後緩慢追上。
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class EnemyHealthBar : MonoBehaviour
    {
        [Header("血量條 — 主條放上層、殘影條放下層")]
        [Tooltip("主血條（Image 設為 Filled / Horizontal）。受擊時瞬間下降")]
        [SerializeField] private Image _healthFill;
        [Tooltip("血量殘影條（放在主條下層）。受擊後延遲再緩慢追上，做出掉血殘影")]
        [SerializeField] private Image _healthDelayedFill;

        [Header("韌性條 — 韌性滿時整組自動隱藏")]
        [Tooltip("主韌性條（Image 設為 Filled / Horizontal）")]
        [SerializeField] private Image _poiseFill;
        [Tooltip("韌性殘影條（放在主條下層）")]
        [SerializeField] private Image _poiseDelayedFill;
        [Tooltip("韌性條整組的容器物件。韌性滿時會 SetActive(false) 自動隱藏；可留空")]
        [SerializeField] private GameObject _poiseGroup;

        [Header("殘影設定（血量與韌性共用）")]
        [Tooltip("受擊後殘影條停留多久才開始追上主條（秒）。建議 0.2~0.5")]
        [SerializeField] private float _chipStartDelay = 0.35f;
        [Tooltip("殘影條追上主條花費的時間（秒）。建議 0.3~0.8，越大殘影掉得越慢")]
        [SerializeField] private float _chipDuration = 0.5f;
        [Tooltip("殘影條追趕的緩動曲線")]
        [SerializeField] private Ease _chipEase = Ease.OutCubic;

        [Header("顯示 / 淡出")]
        [Tooltip("控制整條血條透明度的 CanvasGroup。留空會自動在本物件上取得")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [Tooltip("淡入時間（秒）。建議 0.1~0.3")]
        [SerializeField] private float _fadeInDuration = 0.15f;
        [Tooltip("淡出時間（秒）。建議 0.3~0.6")]
        [SerializeField] private float _fadeOutDuration = 0.4f;
        [Tooltip("脫離戰鬥後，過多久自動淡出（秒）。建議 3~6")]
        [SerializeField] private float _hideDelayAfterCombat = 4f;

        [Header("Billboard（面向攝影機）")]
        [Tooltip("是否每幀面向攝影機")]
        [SerializeField] private bool _billboard = true;
        [Tooltip("勾選後只繞 Y 軸旋轉（血條永遠保持直立，不會上下傾斜）；取消則完全對齊攝影機")]
        [SerializeField] private bool _yAxisOnly = false;

        [Header("韌性歸零回饋（選填）")]
        [Tooltip("韌性被擊破（觸發硬直）時，要做縮放彈跳的內容物件。留空則不做")]
        [SerializeField] private RectTransform _contentRoot;
        [Tooltip("擊破時的縮放彈跳強度。建議 0.2~0.4")]
        [SerializeField] private float _staggerPunchScale = 0.25f;

        private EnemyController _controller;
        private Camera _camera;
        private CombatAttributeSet _attributes;
        private Tween _healthDelayedTween;
        private Tween _poiseDelayedTween;
        private Tween _fadeTween;
        private Tween _punchTween;
        private bool _visible;
        private bool _subscribed;
        private float _hideTimer;

        private void Awake()
        {
            _controller = GetComponentInParent<EnemyController>();
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
            if (_controller == null)
            {
                Debug.LogWarning($"[{name}] EnemyHealthBar 找不到父層的 EnemyController，血條不會運作。請確認血條掛在怪物底下", this);
            }
        }

        private void OnEnable()
        {
            _visible = false;
            _hideTimer = 0f;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            _healthDelayedTween?.Kill();
            _poiseDelayedTween?.Kill();
            _fadeTween?.Kill();
            _punchTween?.Kill();
        }

        private void LateUpdate()
        {
            if (!_subscribed)
            {
                TrySubscribe();
            }
            Billboard();
            if (_controller == null || _controller.IsDead)
            {
                return;
            }
            if (_controller.IsInCombat)
            {
                if (!_visible)
                {
                    Show();
                }
                _hideTimer = _hideDelayAfterCombat;
            }
            else if (_visible)
            {
                _hideTimer -= Time.deltaTime;
                if (_hideTimer <= 0f)
                {
                    Hide();
                }
            }
        }

        private void TrySubscribe()
        {
            if (_controller == null)
            {
                return;
            }
            CombatAttributeSet attributes = _controller.CombatAttributes;
            if (attributes == null)
            {
                return;
            }
            _attributes = attributes;
            _attributes.OnHealthChanged += HandleHealthChanged;
            _attributes.OnPoiseChanged += HandlePoiseChanged;
            _controller.OnDied += HandleDied;
            _controller.OnStaggered += HandleStaggered;
            _subscribed = true;
            float healthPercent = _controller.HealthPercent;
            float poisePercent = _attributes.PoisePercent;
            SetFill(_healthFill, healthPercent);
            SetFill(_healthDelayedFill, healthPercent);
            SetFill(_poiseFill, poisePercent);
            SetFill(_poiseDelayedFill, poisePercent);
            if (_poiseGroup != null)
            {
                _poiseGroup.SetActive(poisePercent < 0.999f);
            }
        }

        private void Unsubscribe()
        {
            if (_attributes != null)
            {
                _attributes.OnHealthChanged -= HandleHealthChanged;
                _attributes.OnPoiseChanged -= HandlePoiseChanged;
            }
            if (_controller != null)
            {
                _controller.OnDied -= HandleDied;
                _controller.OnStaggered -= HandleStaggered;
            }
            _attributes = null;
            _subscribed = false;
        }

        private void HandleHealthChanged(float oldValue, float newValue)
        {
            ApplyChip(_healthFill, _healthDelayedFill, ref _healthDelayedTween, _controller.HealthPercent);
            if (newValue < oldValue)
            {
                Show();
            }
        }

        private void HandlePoiseChanged(float oldValue, float newValue)
        {
            if (_attributes == null)
            {
                return;
            }
            float poisePercent = _attributes.PoisePercent;
            if (_poiseGroup != null)
            {
                _poiseGroup.SetActive(poisePercent < 0.999f);
            }
            ApplyChip(_poiseFill, _poiseDelayedFill, ref _poiseDelayedTween, poisePercent);
            if (newValue < oldValue)
            {
                Show();
            }
        }

        private void HandleStaggered()
        {
            Show();
            PlayStaggerPunch();
        }

        private void HandleDied()
        {
            _visible = false;
            _healthDelayedTween?.Kill();
            _poiseDelayedTween?.Kill();
            _fadeTween?.Kill();
            if (_canvasGroup == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _fadeTween = _canvasGroup.DOFade(0f, _fadeOutDuration)
                .SetLink(gameObject)
                .OnComplete(() => gameObject.SetActive(false));
        }

        private void ApplyChip(Image main, Image delayed, ref Tween tween, float percent)
        {
            SetFill(main, percent);
            if (delayed == null)
            {
                return;
            }
            if (percent < delayed.fillAmount)
            {
                tween?.Kill();
                tween = DOTween.To(() => delayed.fillAmount, value => delayed.fillAmount = value, percent, _chipDuration)
                    .SetDelay(_chipStartDelay)
                    .SetEase(_chipEase)
                    .SetLink(gameObject);
            }
            else
            {
                tween?.Kill();
                delayed.fillAmount = percent;
            }
        }

        private void Show()
        {
            _hideTimer = _hideDelayAfterCombat;
            if (_visible || _canvasGroup == null)
            {
                return;
            }
            _visible = true;
            _fadeTween?.Kill();
            _fadeTween = _canvasGroup.DOFade(1f, _fadeInDuration).SetLink(gameObject);
        }

        private void Hide()
        {
            if (!_visible || _canvasGroup == null)
            {
                return;
            }
            _visible = false;
            _fadeTween?.Kill();
            _fadeTween = _canvasGroup.DOFade(0f, _fadeOutDuration).SetLink(gameObject);
        }

        private void PlayStaggerPunch()
        {
            if (_contentRoot == null)
            {
                return;
            }
            _punchTween?.Kill();
            _contentRoot.localScale = Vector3.one;
            _punchTween = _contentRoot.DOPunchScale(Vector3.one * _staggerPunchScale, 0.3f, 6, 0.8f).SetLink(gameObject);
        }

        private void Billboard()
        {
            if (!_billboard)
            {
                return;
            }
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }
            Transform cameraTransform = _camera.transform;
            if (_yAxisOnly)
            {
                Vector3 direction = transform.position - cameraTransform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    return;
                }
                transform.rotation = Quaternion.LookRotation(direction);
            }
            else
            {
                transform.rotation = cameraTransform.rotation;
            }
        }

        private static void SetFill(Image image, float amount)
        {
            if (image != null)
            {
                image.fillAmount = amount;
            }
        }
    }
}
