using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Item
{
    /// <summary>
    /// 拾取通知項目 — 掛在通知 Prefab 上
    /// 提供直接引用取代 transform.Find 字串查找
    /// </summary>
    public class PickupNotificationEntry : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _amountText;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _rectTransform;
        [Tooltip("內層 Content RectTransform（負責 X 滑動動畫）")]
        [SerializeField] private RectTransform _contentRect;
        [Tooltip("外層 LayoutElement（負責高度歸零讓其他通知平滑填補）")]
        [SerializeField] private LayoutElement _layoutElement;

        public CanvasGroup CanvasGroup => _canvasGroup;
        public RectTransform RectTransform => _rectTransform;
        public RectTransform ContentRect => _contentRect;
        public LayoutElement LayoutElement => _layoutElement;

        /// <summary>初始化通知內容</summary>
        public void Initialize(Sprite icon, string itemName, int amount)
        {
            if (_icon != null) _icon.sprite = icon;
            if (_nameText != null) _nameText.text = itemName;
            if (_amountText != null) _amountText.text = $"x{amount}";
        }
    }
}
