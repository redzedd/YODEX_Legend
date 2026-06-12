using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace Item
{
    /// <summary>
    /// 拾取通知管理器 — DOTween 驅動的滑入/淡出通知系統
    /// 超過上限時以快速退場動畫取代即時銷毀，避免 VerticalLayoutGroup 跳躍
    /// </summary>
    public class PickupNotificationManager : MonoBehaviour
    {
        [Header("通知設定")]
        [SerializeField] private GameObject _notificationPrefab;
        [SerializeField] private Transform _notificationContainer;
        [SerializeField] private float _displayDuration = 3f;
        [SerializeField] private int _maxNotifications = 3;

        [Header("動畫")]
        [SerializeField] private float _slideInDuration = 0.3f;
        [SerializeField] private float _slideOutDuration = 0.2f;
        [SerializeField] private float _slideInOffset = 300f;
        [Tooltip("超過上限時的快速退場時間（秒）")]
        [SerializeField] private float _fastExitDuration = 0.15f;

        private readonly LinkedList<PickupNotificationEntry> _activeNotifications = new();

        public static PickupNotificationManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            // 清理所有活躍通知的 Tween
            foreach (PickupNotificationEntry entry in _activeNotifications)
            {
                if (entry != null)
                {
                    DOTween.Kill(entry.RectTransform);
                    DOTween.Kill(entry.ContentRect);
                }
            }
            _activeNotifications.Clear();
        }

        /// <summary>顯示拾取通知</summary>
        public void ShowNotification(Sprite icon, string itemName, int amount)
        {
            // 超過上限時：播放快速退場動畫（不阻塞新通知）
            while (_activeNotifications.Count >= _maxNotifications)
            {
                PickupNotificationEntry oldest = _activeNotifications.First.Value;
                _activeNotifications.RemoveFirst();
                PlayFastExitAndDestroy(oldest);
            }
            // 生成新通知
            GameObject instance = Instantiate(_notificationPrefab, _notificationContainer);
            instance.transform.SetAsLastSibling();
            PickupNotificationEntry entry = instance.GetComponent<PickupNotificationEntry>();
            if (entry == null)
            {
                Destroy(instance);
                return;
            }
            entry.Initialize(icon, itemName, amount);
            _activeNotifications.AddLast(entry);
            PlayEntryAnimation(entry);
        }

        private void PlayEntryAnimation(PickupNotificationEntry entry)
        {
            RectTransform contentRect = entry.ContentRect;
            CanvasGroup cg = entry.CanvasGroup;
            // 初始狀態：Content 偏移到右側 + 透明
            if (contentRect != null)
                contentRect.anchoredPosition = new Vector2(_slideInOffset, 0);
            if (cg != null) cg.alpha = 0f;
            // 進場序列
            Sequence seq = DOTween.Sequence();
            if (contentRect != null)
                seq.Append(contentRect.DOAnchorPos(Vector2.zero, _slideInDuration).SetEase(Ease.OutCubic));
            if (cg != null)
                seq.Join(cg.DOFade(1f, _slideInDuration * 0.6f));
            // 停留
            seq.AppendInterval(_displayDuration);
            // 退場：Content 向右滑出 + 淡出 + 外層高度歸零讓其他通知平滑填補空間
            PlayExitAnimation(seq, entry);
            seq.SetLink(entry.gameObject);
            seq.SetUpdate(true);
        }

        private void PlayExitAnimation(Sequence seq, PickupNotificationEntry entry)
        {
            RectTransform contentRect = entry.ContentRect;
            CanvasGroup cg = entry.CanvasGroup;
            LayoutElement le = entry.LayoutElement;
            // 同步：Content 右滑 + 淡出
            if (contentRect != null)
                seq.Append(contentRect.DOAnchorPosX(_slideInOffset, _slideOutDuration).SetEase(Ease.InQuad));
            if (cg != null)
                seq.Join(cg.DOFade(0f, _slideOutDuration));
            // 高度歸零（讓 VerticalLayoutGroup 平滑填補空間）
            if (le != null)
                seq.Join(DOTween.To(() => le.preferredHeight, x => le.preferredHeight = x, 0f, _slideOutDuration));
            seq.OnComplete(() =>
            {
                _activeNotifications.Remove(entry);
                if (entry != null) Destroy(entry.gameObject);
            });
        }

        /// <summary>
        /// 快速退場動畫 — 取代即時銷毀
        /// 淡出 + 高度歸零讓 VerticalLayoutGroup 平滑填補空間
        /// </summary>
        private void PlayFastExitAndDestroy(PickupNotificationEntry entry)
        {
            if (entry == null) return;
            // 殺掉現有的動畫序列（停留/正常退場）
            DOTween.Kill(entry.RectTransform);
            DOTween.Kill(entry.ContentRect);
            if (entry.CanvasGroup != null)
                DOTween.Kill(entry.CanvasGroup);
            // 建立快速退場序列
            Sequence fastExit = DOTween.Sequence();
            // 淡出
            if (entry.CanvasGroup != null)
                fastExit.Append(entry.CanvasGroup.DOFade(0f, _fastExitDuration));
            // 高度歸零（讓 VerticalLayoutGroup 平滑填補）
            LayoutElement le = entry.LayoutElement;
            if (le != null)
            {
                fastExit.Join(DOTween.To(
                    () => le.preferredHeight,
                    x => le.preferredHeight = x,
                    0f,
                    _fastExitDuration));
            }
            fastExit.SetUpdate(true);
            fastExit.SetLink(entry.gameObject);
            fastExit.OnComplete(() =>
            {
                if (entry != null) Destroy(entry.gameObject);
            });
        }
    }
}
