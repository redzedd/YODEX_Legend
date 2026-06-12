using UnityEngine;

namespace Enemy.AttackSystem.Test
{
    /// <summary>
    /// 測試用：訂閱 ParryableTargetRegistry 事件，在 Console 印出註冊/移除狀況。
    /// 場景任意 GameObject 掛一個即可，Step 6 完成後可移除。
    /// </summary>
    public class ParryableTargetDebugger : MonoBehaviour
    {
        private void OnEnable()
        {
            ParryableTargetRegistry.OnTargetEntered += HandleEntered;
            ParryableTargetRegistry.OnTargetExited += HandleExited;
        }

        private void OnDisable()
        {
            ParryableTargetRegistry.OnTargetEntered -= HandleEntered;
            ParryableTargetRegistry.OnTargetExited -= HandleExited;
        }

        private void HandleEntered(EnemyAttackExecutor target)
        {
            Debug.Log($"[招架清單] +「{target.name}」進入可招架（清單目前 {ParryableTargetRegistry.Count} 個目標）", target);
        }

        private void HandleExited(EnemyAttackExecutor target)
        {
            Debug.Log($"[招架清單] -「{target.name}」離開可招架（清單目前 {ParryableTargetRegistry.Count} 個目標）", target);
        }
    }
}
