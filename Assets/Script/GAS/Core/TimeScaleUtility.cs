using System.Collections;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 時間縮放工具 - 提供平滑的時間縮放過渡
    /// 供 GA_Dodge（完美閃避）和 GA_DodgeAssist（子彈時間）共用
    /// </summary>
    public static class TimeScaleUtility
    {
        /// <summary>
        /// 平滑改變時間縮放（使用 realtimeSinceStartup 確保在慢動作下正常運作）
        /// </summary>
        public static IEnumerator SmoothTimeScale(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                Time.timeScale = to;
                Time.fixedDeltaTime = 0.02f * to;
                yield break;
            }
            float startRealTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed = Time.realtimeSinceStartup - startRealTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float newScale = Mathf.Lerp(from, to, t);
                Time.timeScale = newScale;
                Time.fixedDeltaTime = 0.02f * newScale;
                yield return null;
            }
            Time.timeScale = to;
            Time.fixedDeltaTime = 0.02f * to;
        }

        /// <summary>
        /// 立即恢復時間縮放至正常速度
        /// </summary>
        public static void RestoreTimeScale()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }
    }
}
