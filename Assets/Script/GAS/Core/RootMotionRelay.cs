using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 掛在持有 Animator 的子物件上,攔截 OnAnimatorMove 並將 Root Motion 資料
    /// 轉發給父層級的 NewGASPlayerController。
    /// 解決 Animator 與 CharacterController 不在同一 GameObject 的常見層級問題。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class RootMotionRelay : MonoBehaviour
    {
        private Animator _animator;
        private NewGASPlayerController _controller;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.applyRootMotion = true;
            _controller = GetComponentInParent<NewGASPlayerController>();
            if (_controller == null)
            {
                Debug.LogError("[RootMotionRelay] 父層級找不到 NewGASPlayerController。", this);
                enabled = false;
            }
        }

        private void OnAnimatorMove()
        {
            if (_controller != null)
            {
                _controller.OnRootMotionUpdate(_animator.deltaPosition, _animator.deltaRotation);
            }
        }

        /// <summary>
        /// 在 LateUpdate 歸零子物件位移/旋轉,時機晚於 Animancer 的骨骼計算與 VFX 生成。
        /// 這樣 TimelineEvent 等在 Update / OnAnimatorMove 中計算骨骼位置的系統能拿到正確座標,
        /// 而模型不會因 AnimancerComponent 的 OnAnimatorMove 累加 deltaPosition 而漂移。
        /// </summary>
        private void LateUpdate()
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
    }
}
