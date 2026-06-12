using Unity.Cinemachine;
using UnityEngine;

namespace GAS.Targeting.LockOnV2
{
    /// <summary>
    /// 暫時除錯用 — 鍵盤觸發 Lock / Unlock / 方向切換,排除 ContextMenu 時機問題
    /// 同時印出 Brain 的 ActiveBlend 狀態
    /// 測完可刪
    /// </summary>
    public class _LockOnDebugTester : MonoBehaviour
    {
        [SerializeField] private LockOnController _controller;
        [SerializeField] private CinemachineBrain _brain;

        [Header("Lock / Unlock")]
        [SerializeField] private KeyCode _lockKey = KeyCode.K;
        [SerializeField] private KeyCode _unlockKey = KeyCode.U;
        [SerializeField] private KeyCode _toggleKey = KeyCode.T;

        [Header("Directional Switch (8 dir)")]
        [SerializeField] private KeyCode _leftKey = KeyCode.LeftArrow;
        [SerializeField] private KeyCode _rightKey = KeyCode.RightArrow;
        [SerializeField] private KeyCode _upKey = KeyCode.UpArrow;
        [SerializeField] private KeyCode _downKey = KeyCode.DownArrow;

        private bool _wasBlending;

        private void Update()
        {
            if (Input.GetKeyDown(_lockKey)) DoLockBest();
            if (Input.GetKeyDown(_unlockKey)) DoUnlock();
            if (Input.GetKeyDown(_toggleKey)) DoToggle();
            HandleDirectional();
            ReportBlend();
        }

        private void DoLockBest()
        {
            bool ok = _controller.TryLockBest();
            Debug.Log($"[LockOn 測試] TryLockBest => {ok} (target={SafeName(_controller.CurrentTarget)})");
        }

        private void DoUnlock()
        {
            Debug.Log($"[LockOn 測試] 解鎖前 IsLocked={_controller.IsLocked}");
            _controller.Unlock();
        }

        private void DoToggle()
        {
            bool result = _controller.ToggleBestLock();
            Debug.Log($"[LockOn 測試] ToggleBestLock => locked={result} target={SafeName(_controller.CurrentTarget)}");
        }

        private void HandleDirectional()
        {
            if (Input.GetKeyDown(_leftKey)) TryDir(Vector2.left);
            if (Input.GetKeyDown(_rightKey)) TryDir(Vector2.right);
            if (Input.GetKeyDown(_upKey)) TryDir(Vector2.up);
            if (Input.GetKeyDown(_downKey)) TryDir(Vector2.down);
        }

        private void TryDir(Vector2 dir)
        {
            bool ok = _controller.TryLockDirectional(dir);
            Debug.Log($"[LockOn 測試] TryLockDirectional({dir}) => {ok} (target={SafeName(_controller.CurrentTarget)})");
        }

        private void ReportBlend()
        {
            if (_brain == null) return;
            bool isBlending = _brain.IsBlending;
            if (isBlending && !_wasBlending)
            {
                CinemachineBlend blend = _brain.ActiveBlend;
                string from = blend?.CamA?.Name ?? "null";
                string to = blend?.CamB?.Name ?? "null";
                float dur = blend?.Duration ?? 0f;
                Debug.Log($"[LockOn 測試] Blend 開始: {from} → {to} (duration={dur:F2}s)");
            }
            if (!isBlending && _wasBlending)
            {
                Debug.Log("[LockOn 測試] Blend 結束");
            }
            _wasBlending = isBlending;
        }

        private static string SafeName(LockOnTarget t) => t == null ? "null" : t.name;
    }
}
