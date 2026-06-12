using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 提示訊息觸發器 — 玩家進入 Trigger 範圍時，透過 InteractionHintUI 顯示一段自訂文字
    /// 用於區域進入提示（例如「歡迎來到 XX 城」「危險：高溫地帶」「按 E 拾取」等）
    /// 不需要玩家按互動鍵，純粹進入即觸發
    /// 物件需有 Collider（勾選 Is Trigger）與適當的 Rigidbody（建議 Kinematic）
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HintMessageTrigger : MonoBehaviour
    {
        [Header("觸發內容")]
        [Tooltip("玩家進入時顯示的提示文字")]
        [TextArea(2, 4)]
        [SerializeField] private string _message = "提示訊息";

        [Tooltip("提示音效（留空 = 使用 InteractionHintUI 的預設音效）")]
        [SerializeField] private AudioClip _sfx;

        [Header("觸發條件")]
        [Tooltip("用來辨識玩家的 Tag")]
        [SerializeField] private string _playerTag = "Player";

        [Tooltip("勾選 = 只觸發一次（首次後物件自動停用）；不勾 = 每次進入都會觸發")]
        [SerializeField] private bool _triggerOnce = false;

        [Tooltip("再次觸發前的冷卻秒數（避免邊緣徘徊重複觸發，建議 1~3 秒）。Trigger Once 勾選時無作用")]
        [SerializeField] private float _retriggerCooldown = 1.5f;

        [Header("觸發完成後")]
        [Tooltip("勾選 = 觸發後直接銷毀本 GameObject（Trigger Once 為 true 時才有意義）")]
        [SerializeField] private bool _destroyAfterTrigger = false;

        private bool _hasTriggered;
        private float _lastTriggerTime = -999f;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            if (_triggerOnce && _hasTriggered) return;
            if (!_triggerOnce && Time.time - _lastTriggerTime < _retriggerCooldown) return;
            ShowHint();
        }

        private void ShowHint()
        {
            if (InteractionHintUI.Instance == null)
            {
                Debug.LogWarning("[HintMessageTrigger] 場景中找不到 InteractionHintUI，提示無法顯示", this);
                return;
            }
            InteractionHintUI.Instance.Show(_message, _sfx);
            _hasTriggered = true;
            _lastTriggerTime = Time.time;
            if (_triggerOnce && _destroyAfterTrigger)
                Destroy(gameObject);
            else if (_triggerOnce)
                gameObject.SetActive(false);
        }
    }
}
