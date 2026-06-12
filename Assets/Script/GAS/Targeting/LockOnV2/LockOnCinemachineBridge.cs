using Unity.Cinemachine;
using UnityEngine;
using CameraSystem;

namespace GAS.Targeting.LockOnV2
{
    /// <summary>
    /// 鎖定攝影機橋接 — 連接 LockOnController 與 CameraDirector。
    /// 訂閱 OnTargetChanged 事件：鎖定時切換 LockOnCam 的 LookAt 並向 Director 請求啟用；解鎖時 Release ticket。
    /// 訂閱 Director.OnStackChanged 事件：當 LockOn 鏡頭被任何更高層覆蓋（Aim/Action/Cinematic）時，
    ///   自動觸發 LockOnController.Unlock() — 避免 LockOnAnchor 持續把玩家拉向敵人但鏡頭已切走的 bug。
    /// 攝影機本身的 Body / Aim / FollowOffset 等構圖參數由 Inspector 設定，本元件不干涉 Priority。
    /// </summary>
    [DisallowMultipleComponent]
    public class LockOnCinemachineBridge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        [Tooltip("鎖定來源 Controller (通常掛在玩家 root);留空則自父物件搜尋")]
        private LockOnController _controller;

        [SerializeField]
        [Tooltip("鎖定攝影機;Body 建議用 CinemachineFollow + FollowOffset (例 0,1.5,-3.5),Aim 用 CinemachineRotationComposer。\n該鏡頭物件另需掛 CameraEntry，ID=LockOn, Layer=LockOn")]
        private CinemachineCamera _lockOnCam;

        [Header("Auto Setup")]
        [SerializeField]
        [Tooltip("Start 時自動將 LockOnCam.Follow 綁到 Controller.PlayerAnchor")]
        private bool _autoBindFollow = true;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("輸出事件接收與 ticket 狀態的診斷訊息")]
        private bool _verboseLog = false;

        private CameraTicket _lockTicket;

        private void Awake()
        {
            if (_controller == null) _controller = GetComponentInParent<LockOnController>();
        }

        private void OnEnable()
        {
            if (_controller != null) _controller.OnTargetChanged += HandleTargetChanged;
            CameraDirector director = CameraDirector.Instance;
            if (director != null) director.OnStackChanged += HandleStackChanged;
        }

        private void OnDisable()
        {
            if (_controller != null) _controller.OnTargetChanged -= HandleTargetChanged;
            CameraDirector director = CameraDirector.Instance;
            if (director != null) director.OnStackChanged -= HandleStackChanged;
            ReleaseTicket();
        }

        private void Start()
        {
            AutoBindFollow();
            if (_controller != null && _controller.IsLocked)
            {
                ApplyLockedState(_controller.CurrentTarget);
            }
            else
            {
                ApplyUnlockedState();
            }
        }

        private void AutoBindFollow()
        {
            if (!_autoBindFollow) return;
            if (_lockOnCam == null || _controller == null) return;
            LockOnAnchor anchor = _controller.PlayerAnchor;
            if (anchor == null) return;
            _lockOnCam.Follow = anchor.transform;
        }

        private void HandleTargetChanged(LockOnTarget target)
        {
            if (_verboseLog) Debug.Log($"[LockOnV2 Bridge] HandleTargetChanged target={(target == null ? "null" : target.name)}");
            if (target == null)
            {
                ApplyUnlockedState();
                return;
            }
            ApplyLockedState(target);
        }

        private void ApplyLockedState(LockOnTarget target)
        {
            if (_lockOnCam == null || target == null)
            {
                if (_verboseLog) Debug.LogWarning($"[LockOnV2 Bridge] ApplyLockedState aborted: cam={_lockOnCam} target={target}");
                return;
            }
            _lockOnCam.LookAt = target.AnchorTransform;
            // 切換目標時釋放舊 ticket、發新的（push 順序前進 → 同 layer 內取得最高優先級）
            ReleaseTicket();
            CameraDirector director = CameraDirector.Instance;
            if (director != null)
            {
                _lockTicket = director.Request(CameraId.LockOn);
            }
            if (_verboseLog) Debug.Log($"[LockOnV2 Bridge] 鎖定: LookAt={target.AnchorTransform.name}");
            // 請求後立刻檢查：若鎖定瞬間就被高層鏡頭覆蓋（例：Aim 已啟用時按 Lock）→ 立刻取消鎖定
            // 否則 LockOnAnchor 會持續把玩家拉向敵人但鏡頭並沒切過來，造成「無法瞄準只面對敵人」bug
            UnlockIfShadowed();
        }

        private void ApplyUnlockedState()
        {
            // 不重設 LookAt：RotationComposer 失去 LookAt 會讓本機 pose 變無效,Brain 因此找不到 blend 起點而直接 cut
            // 保留上一次目標的 LookAt,Brain 可從「仍對著舊目標」的 pose 平滑 blend 回主視角
            ReleaseTicket();
            if (_verboseLog) Debug.Log("[LockOnV2 Bridge] 解鎖 (LookAt 保留)");
        }

        // 重要：先清 null 後 Release — 因 Release 會觸發 Director.OnStackChanged 事件，
        // 事件處理器 UnlockIfShadowed() 會檢查 _lockTicket，若仍指向已釋放但欄位未清的舊 ticket，
        // 會誤判 winner 不是我而觸發 _controller.Unlock() → 清掉剛綁定的新鎖定 anchor target
        private void ReleaseTicket()
        {
            if (_lockTicket == null) return;
            CameraTicket ticket = _lockTicket;
            _lockTicket = null;
            ticket.Release();
        }

        // Director Stack 變動時呼叫 — 處理「鎖定後其他更高鏡頭(Aim/Cinematic)進場」的覆蓋偵測
        private void HandleStackChanged()
        {
            UnlockIfShadowed();
        }

        // 若 LockOn 鏡頭不再是 winner（被更高層覆蓋）→ 觸發完整 Unlock 流程，清掉 LockOnAnchor 副作用
        private void UnlockIfShadowed()
        {
            if (_lockTicket == null) return;
            CameraDirector director = CameraDirector.Instance;
            if (director == null) return;
            if (director.CurrentWinner == _lockTicket.Entry) return;
            if (_verboseLog) Debug.Log("[LockOnV2 Bridge] LockOn 鏡頭被高層覆蓋 → 取消鎖定");
            ReleaseTicket();
            if (_controller != null) _controller.Unlock();
        }
    }
}
