using UnityEngine;

namespace EnemyAI
{
    /// <summary>
    /// 動畫子物件 → 父物件 EnemyController 的 Root Motion 中繼
    ///
    /// 為什麼需要：Unity 只在「Animator 所在 GameObject 上的 MonoBehaviour」呼叫 OnAnimatorMove，
    /// 邏輯腳本（EnemyController）放父物件、Animator 放子物件時，OnAnimatorMove 不會在父物件被呼叫。
    /// 本 script 掛在子物件接收 OnAnimatorMove，把 Animator.deltaPosition 轉發給父物件的 EnemyController 處理重力 + 位移
    ///
    /// 掛載位置：跟 Animator / AnimancerComponent 同一個子物件
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class EnemyAnimatorRelay : MonoBehaviour
    {
        private EnemyController _controller;
        private Animator _animator;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _controller = GetComponentInParent<EnemyController>();
            if (_controller == null)
            {
                Debug.LogWarning($"[{name}] EnemyAnimatorRelay 找不到父物件的 EnemyController — Root Motion 不會被處理（敵人會穿地或浮空）。請確認本物件是 EnemyController 的子物件", this);
                return;
            }
            if (_controller.gameObject == gameObject)
            {
                // 跟 EnemyController 在同物件（單體架構）— Relay 多餘，EnemyController 自身的 OnAnimatorMove 會處理
                // 不停用會 double：Unity 對同物件的兩個 MonoBehaviour 都會呼叫 OnAnimatorMove，導致 root motion 套兩次
                Debug.LogWarning($"[{name}] EnemyAnimatorRelay 跟 EnemyController 在同一物件 — 單體架構不需要 Relay，已自動停用以避免 root motion 重複處理。請移除本 component，或把 Animator 移到 EnemyController 的子物件", this);
                _controller = null;
                enabled = false;
            }
        }

        private void OnAnimatorMove()
        {
            if (_controller == null || _animator == null) return;
            _controller.ApplyAnimatorRootMotion(_animator.deltaPosition);
        }
    }
}
