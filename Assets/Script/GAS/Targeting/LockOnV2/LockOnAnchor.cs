using UnityEngine;

namespace GAS.Targeting.LockOnV2
{
    /// <summary>
    /// 鎖定錨點 — 雙方共用元件。
    /// 玩家身上掛一顆面向敵人，敵人身上掛一顆面向玩家；
    /// 配合 CinemachineCamera 的 Follow=PlayerAnchor、LookAt=TargetAnchor 即可形成穩定的肩後鎖定鏡頭。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class LockOnAnchor : MonoBehaviour
    {
        public enum AxisLockMode
        {
            FullLook,
            YawOnly,
            PitchClamped
        }

        [Header("Target")]
        [SerializeField]
        [Tooltip("要面向的目標；可由腳本動態設定 (SetTarget)")]
        private Transform _target;

        [Header("Rotation Mode")]
        [SerializeField]
        [Tooltip("旋轉模式：YawOnly 適合避免相機翻滾；PitchClamped 限制俯仰；FullLook 完全朝向")]
        private AxisLockMode _axisMode = AxisLockMode.YawOnly;

        [SerializeField, Range(0f, 1440f)]
        [Tooltip("旋轉速度 (度/秒)；0 表示瞬間對齊")]
        private float _rotationSpeed = 720f;

        [Header("Pitch Clamp (僅 PitchClamped 模式)")]
        [SerializeField, Range(0f, 89f)]
        [Tooltip("最大仰角")]
        private float _maxPitch = 50f;

        [SerializeField, Range(0f, 89f)]
        [Tooltip("最大俯角")]
        private float _maxDownPitch = 35f;

        [Header("Up Reference")]
        [SerializeField]
        [Tooltip("計算朝向時使用的世界 up 向量；通常為 (0,1,0)")]
        private Vector3 _worldUp = Vector3.up;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("是否在 Scene 視圖繪製朝向 Gizmo")]
        private bool _drawGizmo = true;

        public Transform Target
        {
            get => _target;
            set => _target = value;
        }

        public bool HasTarget => _target != null;

        public AxisLockMode Mode
        {
            get => _axisMode;
            set => _axisMode = value;
        }

        public void SetTarget(Transform target) => _target = target;

        public void ClearTarget() => _target = null;

        /// <summary>
        /// 立即對齊到目標 (跳過插值)，用於剛鎖定的瞬間避免鏡頭滑入過長
        /// </summary>
        public void SnapToTarget()
        {
            if (_target == null) return;
            Quaternion desired = ComputeDesiredRotation();
            transform.rotation = desired;
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            Quaternion desired = ComputeDesiredRotation();
            if (_rotationSpeed <= 0f)
            {
                transform.rotation = desired;
                return;
            }
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desired, _rotationSpeed * Time.deltaTime);
        }

        private Quaternion ComputeDesiredRotation()
        {
            Vector3 toTarget = _target.position - transform.position;
            Vector3 dir = ResolveLookDirection(toTarget);
            if (dir.sqrMagnitude < 0.0001f) return transform.rotation;
            return Quaternion.LookRotation(dir, _worldUp);
        }

        private Vector3 ResolveLookDirection(Vector3 toTarget)
        {
            if (_axisMode == AxisLockMode.YawOnly)
            {
                return new Vector3(toTarget.x, 0f, toTarget.z);
            }
            if (_axisMode == AxisLockMode.PitchClamped)
            {
                return ClampPitch(toTarget);
            }
            return toTarget;
        }

        private Vector3 ClampPitch(Vector3 toTarget)
        {
            float horizontal = new Vector2(toTarget.x, toTarget.z).magnitude;
            if (horizontal < 0.0001f) return toTarget;
            float pitchDeg = Mathf.Atan2(toTarget.y, horizontal) * Mathf.Rad2Deg;
            float clampedDeg = Mathf.Clamp(pitchDeg, -_maxDownPitch, _maxPitch);
            if (Mathf.Approximately(pitchDeg, clampedDeg)) return toTarget;
            float newY = Mathf.Tan(clampedDeg * Mathf.Deg2Rad) * horizontal;
            return new Vector3(toTarget.x, newY, toTarget.z);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmo) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, transform.forward * 1.5f);
            if (_target == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, _target.position);
            Gizmos.DrawWireSphere(_target.position, 0.15f);
        }
#endif
    }
}
