using UnityEngine;

namespace Minigame.Archery
{
    /// <summary>
    /// 射箭小遊戲 — 啟動 Trigger
    /// 玩家進入觸發區 → 呼叫 Controller.OnPlayerEnteredZone()
    /// 玩家離開觸發區 → 呼叫 Controller.OnPlayerExitedZone()
    /// 重啟、中斷、Win 終局判定都由 Controller 內部狀態機處理，本元件只負責轉發進出事件
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MinigameTrigger : MonoBehaviour
    {
        [Header("控制器")]
        [Tooltip("小遊戲主控制器（拖場景中 ArcheryMinigameController 實例）")]
        [SerializeField] private ArcheryMinigameController _controller;

        [Header("觸發條件")]
        [Tooltip("用來辨識玩家的 Tag")]
        [SerializeField] private string _playerTag = "Player";

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            if (_controller == null)
            {
                Debug.LogWarning("[MinigameTrigger] Controller 未設定，無法啟動小遊戲", this);
                return;
            }
            _controller.OnPlayerEnteredZone();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            if (_controller == null) return;
            _controller.OnPlayerExitedZone();
        }
    }
}
