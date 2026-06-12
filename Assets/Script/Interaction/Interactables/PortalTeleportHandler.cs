using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 傳送門傳送處理器 — 玩家互動後透過 SceneTransitionManager 載入目標場景
    /// 掛在傳送門特效物件上，搭配 GenericInteractable 使用
    /// Trigger 碰撞器由 PortalActivationHandler 啟用後自動生效
    /// </summary>
    public class PortalTeleportHandler : InteractionHandler
    {
        [Header("目標場景")]
        [Tooltip("要傳送至的場景名稱（需加入 Build Settings）")]
        [SerializeField] private string _targetSceneName;

        [Header("音效")]
        [SerializeField] private AudioClip _teleportSFX;
        [SerializeField] private AudioSource _audioSource;

        private bool _isTransitioning;

        public override bool CanExecute() => !_isTransitioning
            && SceneTransitionManager.Instance != null
            && !SceneTransitionManager.Instance.IsTransitioning;

        public override void Execute()
        {
            if (!CanExecute()) return;
            if (string.IsNullOrEmpty(_targetSceneName))
            {
                Debug.LogWarning("[PortalTeleportHandler] 目標場景名稱未設定");
                return;
            }
            _isTransitioning = true;
            if (_audioSource != null && _teleportSFX != null)
                _audioSource.PlayOneShot(_teleportSFX);
            SceneTransitionManager.Instance.LoadScene(_targetSceneName);
        }
    }
}
