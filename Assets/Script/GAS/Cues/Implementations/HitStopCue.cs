using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 頓幀 Cue (Hit Stop)
    /// </summary>
    [CreateAssetMenu(fileName = "New Hit Stop Cue", menuName = "GAS/Cues/Hit Stop Cue")]
    public class HitStopCue : GameplayCue
    {
        [Header("Hit Stop Settings")]
        [Tooltip("頓幀持續時間")]
        public float Duration = 0.1f;

        [Tooltip("頓幀期間的時間縮放")]
        [Range(0f, 1f)]
        public float TimeScale = 0f;

        public override void OnExecute(GameplayCueParameters parameters)
        {
            // 使用場景中的 HitStop 單例
            if (HitStop.Instance != null)
            {
                HitStop.Instance.Trigger(Duration);
            }
            else
            {
                // Fallback: 使用全局時間縮放
                if (parameters.Instigator != null)
                {
                    parameters.Instigator.StartCoroutine(HitStopCoroutine(Duration, TimeScale));
                }
            }
        }

        private System.Collections.IEnumerator HitStopCoroutine(float duration, float scale)
        {
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(duration);
            // 期間若被 UI 暫停改過 timeScale,不可蓋回,否則背包開著遊戲仍在跑
            if (Mathf.Approximately(Time.timeScale, scale))
                Time.timeScale = 1f;
        }
    }
}
