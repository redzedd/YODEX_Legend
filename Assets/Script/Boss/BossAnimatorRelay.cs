using UnityEngine;

namespace Boss
{
    /// <summary>
    /// 動畫子物件 → 父物件 BossGroundLocomotion 的 Root Motion 中繼
    ///
    /// Unity 只在「Animator 所在的 GameObject」上呼叫 OnAnimatorMove。
    /// Boss 邏輯腳本放父物件、Animator / AnimancerComponent 放視覺模型子物件時,
    /// 父物件收不到 OnAnimatorMove。本元件掛在子物件接收,把 Animator.deltaPosition
    /// 轉發給父物件的 BossGroundLocomotion 處理重力 + CharacterController 位移。
    ///
    /// 由 BossGroundLocomotion.Awake 自動掛上並綁定,設計師不需要手動加。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class BossAnimatorRelay : MonoBehaviour
    {
        private BossGroundLocomotion _locomotion;
        private Animator _animator;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        /// <summary>由 BossGroundLocomotion 自動掛上後呼叫,綁定回父物件</summary>
        public void Initialize(BossGroundLocomotion locomotion)
        {
            _locomotion = locomotion;
            if (_animator == null) _animator = GetComponent<Animator>();
        }

        private void OnAnimatorMove()
        {
            if (_locomotion == null || _animator == null) return;
            _locomotion.ApplyRootMotion(_animator.deltaPosition);
        }
    }
}
