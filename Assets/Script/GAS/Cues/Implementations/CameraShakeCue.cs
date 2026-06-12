using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 相機震動 Cue
    /// </summary>
    [CreateAssetMenu(fileName = "New Camera Shake Cue", menuName = "GAS/Cues/Camera Shake Cue")]
    public class CameraShakeCue : GameplayCue
    {
        [Header("Shake Settings")]
        [Tooltip("震動強度")]
        public float ShakeForce = 1f;

        [Tooltip("震動持續時間")]
        public float Duration = 0.2f;

        [Tooltip("使用參數中的強度")]
        public bool UseMagnitudeParameter = true;

        public override void OnExecute(GameplayCueParameters parameters)
        {
            float force = UseMagnitudeParameter && parameters.Magnitude > 0f
                ? ShakeForce * parameters.Magnitude
                : ShakeForce;
            // 嘗試使用 Cinemachine Impulse
            var impulseSource = parameters.TargetObject?.GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
            if (impulseSource != null)
            {
                Vector3 shakeVelocity = Vector3.down * force;
                shakeVelocity += Random.insideUnitSphere * (force * 0.3f);
                impulseSource.GenerateImpulse(shakeVelocity);
            }
            else
            {
                // Fallback: 尋找場景中的 Impulse Source
                var anyImpulse = FindFirstObjectByType<Unity.Cinemachine.CinemachineImpulseSource>();
                if (anyImpulse != null)
                {
                    Vector3 shakeVelocity = Vector3.down * force;
                    anyImpulse.GenerateImpulse(shakeVelocity);
                }
            }
        }
    }
}
