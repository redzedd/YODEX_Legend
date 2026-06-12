using System.Collections;
using DG.Tweening;
using UnityEngine;
using GAS.UI.Inventory;
using Item;
using Player.Input;

namespace Interaction
{
    /// <summary>
    /// 場景內傳送處理器 — 互動後將玩家傳送到同場景的指定目標點
    /// 首次互動需要鑰匙道具，解鎖後永久可用（每次互動都會傳送）
    /// 演出流程：禁用輸入 → 淡入全白 → 傳送玩家 → 淡出全白 → 啟用輸入
    /// 由 GenericInteractable 委派呼叫
    /// </summary>
    public class InSceneTeleportHandler : InteractionHandler
    {
        [Header("傳送目標")]
        [Tooltip("傳送的目標點（拖場景中一個空 GameObject，會同步套用其 position 與 rotation）")]
        [SerializeField] private Transform _destination;

        [Tooltip("用來尋找玩家的 Tag（預設 Player）")]
        [SerializeField] private string _playerTag = "Player";

        [Header("鑰匙需求")]
        [Tooltip("解鎖傳送門需要的道具（留空 = 不需要解鎖，初始就可使用）")]
        [SerializeField] private ItemData _requiredKeyItem;

        [Tooltip("首次解鎖時是否消耗鑰匙")]
        [SerializeField] private bool _consumeKey = true;

        [Tooltip("初始就處於已解鎖狀態（true = 不需要鑰匙就能使用）")]
        [SerializeField] private bool _startUnlocked = false;

        [Header("傳送門視覺")]
        [Tooltip("解鎖後才顯示的傳送門特效物件（可留空）")]
        [SerializeField] private GameObject _unlockedVFX;

        [Header("失敗提示")]
        [Tooltip("缺少鑰匙時的提示文字（傳給 InteractionHintUI）")]
        [SerializeField] private string _denyMessage = "需要鑰匙才能啟用傳送門";

        [Tooltip("失敗提示音效")]
        [SerializeField] private AudioClip _denySFX;

        [Header("音效")]
        [Tooltip("首次解鎖時播放的音效")]
        [SerializeField] private AudioClip _unlockSFX;

        [Tooltip("每次傳送時播放的音效（建議在淡入全白瞬間響）")]
        [SerializeField] private AudioClip _teleportSFX;

        [SerializeField] private AudioSource _audioSource;

        [Header("淡入淡出（全白過場）")]
        [Tooltip("全螢幕白屏 CanvasGroup（建議共用一個放在 UI Canvas 下，初始 Alpha = 0、Blocks Raycasts 勾選）。留空則無過場直接傳送")]
        [SerializeField] private CanvasGroup _fadeOverlay;

        [Tooltip("淡入全白秒數（建議 0.3~0.8 秒）")]
        [SerializeField] private float _fadeOutDuration = 0.5f;

        [Tooltip("全白停留秒數，傳送在此期間瞬間發生（建議 0.1~0.3 秒）")]
        [SerializeField] private float _holdWhiteDuration = 0.15f;

        [Tooltip("從全白淡出秒數（建議 0.4~1 秒）")]
        [SerializeField] private float _fadeInDuration = 0.6f;

        [Tooltip("過場期間是否關閉玩家輸入")]
        [SerializeField] private bool _disablePlayerInput = true;

        private bool _isUnlocked;
        private bool _isPlaying;
        private Tween _fadeTween;

        private void Awake()
        {
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();
            _isUnlocked = _startUnlocked;
            if (_unlockedVFX != null)
                _unlockedVFX.SetActive(_isUnlocked);
        }

        private void OnDestroy()
        {
            _fadeTween?.Kill();
        }

        /// <summary>演出中不可再次觸發；其他情況都允許嘗試（缺鑰匙會走失敗提示）</summary>
        public override bool CanExecute() => !_isPlaying;

        public override void Execute()
        {
            if (_isPlaying) return;
            if (_destination == null)
            {
                Debug.LogWarning("[InSceneTeleportHandler] 目標點未設定，無法傳送", this);
                return;
            }
            if (!_isUnlocked && !HasRequiredKey())
            {
                ShowDenyHint();
                return;
            }
            StartCoroutine(PlayTeleportSequence());
        }

        private bool HasRequiredKey()
        {
            if (_requiredKeyItem == null) return true;
            return InventoryManager.Instance != null
                && InventoryManager.Instance.HasItemByName(_requiredKeyItem.itemName);
        }

        private void ShowDenyHint()
        {
            if (InteractionHintUI.Instance != null)
                InteractionHintUI.Instance.Show(_denyMessage, _denySFX);
        }

        private IEnumerator PlayTeleportSequence()
        {
            _isPlaying = true;
            SystemInputReader input = SystemInputReader.Instance;
            if (_disablePlayerInput && input != null)
                input.DisablePlayerInput();
            // 首次解鎖時的扣鑰匙 / 視覺 / 音效（在淡入前處理，使視覺切換被白屏蓋住）
            HandleFirstUnlock();
            if (_audioSource != null && _teleportSFX != null)
                _audioSource.PlayOneShot(_teleportSFX);
            yield return FadeOverlayTo(1f, _fadeOutDuration);
            // 全白覆蓋畫面 — 傳送在此瞬間完成，玩家完全看不到位置跳變
            TeleportPlayer();
            if (_holdWhiteDuration > 0f)
                yield return new WaitForSeconds(_holdWhiteDuration);
            yield return FadeOverlayTo(0f, _fadeInDuration);
            if (_disablePlayerInput && input != null)
                input.EnablePlayerInput();
            _isPlaying = false;
        }

        private IEnumerator FadeOverlayTo(float targetAlpha, float duration)
        {
            if (_fadeOverlay == null || duration <= 0f)
            {
                if (_fadeOverlay != null) _fadeOverlay.alpha = targetAlpha;
                yield break;
            }
            _fadeOverlay.blocksRaycasts = targetAlpha > 0f;
            _fadeTween?.Kill();
            _fadeTween = _fadeOverlay
                .DOFade(targetAlpha, duration)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true)
                .SetLink(gameObject);
            yield return _fadeTween.WaitForCompletion();
        }

        private void HandleFirstUnlock()
        {
            if (_isUnlocked) return;
            if (_consumeKey && _requiredKeyItem != null && InventoryManager.Instance != null)
                InventoryManager.Instance.RemoveItemByName(_requiredKeyItem.itemName);
            _isUnlocked = true;
            if (_unlockedVFX != null) _unlockedVFX.SetActive(true);
            if (_audioSource != null && _unlockSFX != null)
                _audioSource.PlayOneShot(_unlockSFX);
        }

        private void TeleportPlayer()
        {
            GameObject playerGo = GameObject.FindWithTag(_playerTag);
            if (playerGo == null)
            {
                Debug.LogWarning($"[InSceneTeleportHandler] 找不到 Tag = {_playerTag} 的玩家 GameObject", this);
                return;
            }
            // CharacterController 會在 enabled 時鎖定 transform；必須先關才能直接寫入位置
            CharacterController cc = playerGo.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                playerGo.transform.SetPositionAndRotation(_destination.position, _destination.rotation);
                cc.enabled = true;
            }
            else
            {
                playerGo.transform.SetPositionAndRotation(_destination.position, _destination.rotation);
            }
            // 用 transform 直接寫入位置不會觸發 OnTriggerExit / OnTriggerEnter，
            // 必須先把物理同步到新位置，再強制本傳送門重新校驗 Trigger 狀態，
            // 否則 InteractableTriggerBase 仍以為玩家在範圍內 → 互動提示陰魂不散
            Physics.SyncTransforms();
            GenericInteractable sourceInteractable = GetComponentInParent<GenericInteractable>();
            if (sourceInteractable != null)
                sourceInteractable.ResyncTriggerState();
        }
    }
}
