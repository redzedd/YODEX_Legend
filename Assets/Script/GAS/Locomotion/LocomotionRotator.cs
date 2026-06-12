using UnityEngine;

namespace Player.Locomotion
{
    /// <summary>
    /// 向量對齊旋轉計算器。純 C#，負責讓角色朝向以可控的角速度追趕目標方向向量。
    /// </summary>
    public static class LocomotionRotator
    {
        /// <summary>
        /// 以固定角速度讓 current 旋轉朝向 desiredForward。
        /// </summary>
        public static Quaternion Step(Quaternion current, Vector3 desiredForward, float degreesPerSecond, float deltaTime)
        {
            if (desiredForward.sqrMagnitude < 0.0001f)
            {
                return current;
            }
            Quaternion target = Quaternion.LookRotation(desiredForward, Vector3.up);
            return Quaternion.RotateTowards(current, target, degreesPerSecond * deltaTime);
        }

        /// <summary>
        /// 計算角色朝向與輸入向量在水平面上的「有號」Yaw 差值（左負右正，度）。用於 Lean 混合。
        /// </summary>
        public static float GetSignedYawDelta(Transform actor, Vector3 desiredForward)
        {
            if (desiredForward.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }
            Vector3 flatForward = Vector3.ProjectOnPlane(actor.forward, Vector3.up).normalized;
            Vector3 flatDesired = Vector3.ProjectOnPlane(desiredForward, Vector3.up).normalized;
            return Vector3.SignedAngle(flatForward, flatDesired, Vector3.up);
        }

        /// <summary>
        /// 計算「無號」角度差，用於判定是否觸發快跑轉身。
        /// </summary>
        public static float GetUnsignedAngle(Transform actor, Vector3 desiredForward)
        {
            return Mathf.Abs(GetSignedYawDelta(actor, desiredForward));
        }
    }
}
