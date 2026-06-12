using System.Collections.Generic;
using UnityEngine;
using Animancer;

namespace GAS
{
    /// <summary>
    /// 閃避數據 - 定義閃避的所有可配置參數
    /// 支援前衝閃避（有輸入方向）和後撤（無輸入方向）兩種模式
    /// </summary>
    [CreateAssetMenu(fileName = "New Dodge Data", menuName = "GAS/Abilities/Dodge Data")]
    public class DodgeData : ScriptableObject
    {
        [Header("Movement Mode")]
        [Tooltip("true = 使用 Root Motion(由 Clip 內建的位移曲線驅動,需動畫勾選 Root Motion 並內含位移)\n" +
                 "false = 使用 In-Place 模式(由 DOTween 依 Distance / Duration / Curve 強制推進 CharacterController)")]
        public bool UseRootMotion = true;

        [Header("Dodge Mode（有輸入方向時）")]
        [Tooltip("前衝閃避動畫")]
        public ClipTransition DodgeClip;

        [Tooltip("【IP 模式專用】前衝閃避距離。RM 模式下忽略,位移由 Clip 決定")]
        public float DodgeDistance = 5.0f;

        [Tooltip("【IP 模式專用】前衝閃避持續時間。RM 模式下忽略")]
        public float DodgeDuration = 0.4f;

        [Tooltip("【IP 模式專用】前衝閃避移動曲線。RM 模式下忽略")]
        public AnimationCurve DodgeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Backstep Mode（無輸入方向時）")]
        [Tooltip("後撤動畫；若未指定則使用前衝動畫")]
        public ClipTransition BackstepClip;

        [Tooltip("【IP 模式專用】後撤距離。RM 模式下忽略")]
        public float BackstepDistance = 3.0f;

        [Tooltip("【IP 模式專用】後撤持續時間。RM 模式下忽略")]
        public float BackstepDuration = 0.35f;

        [Tooltip("【IP 模式專用】後撤移動曲線。RM 模式下忽略")]
        public AnimationCurve BackstepCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Timing")]
        [Tooltip("最早允許輸入下一個動作的時間")]
        public float AllowInputTime = 0.2f;

        [Tooltip("允許被其他能力取消的時間")]
        public float AllowCancelTime = 0.2f;

        [Tooltip("移動輸入可取消閃避的時間（-1 表示不啟用）")]
        public float SheatheCancelTime = -1f;

        [Header("Invincibility")]
        [Tooltip("無敵效果（可選）")]
        public GameplayEffect InvincibilityEffect;

        [Tooltip("無敵開始時間（相對於動畫開始）")]
        public float InvincibilityStartTime = 0f;

        [Tooltip("無敵持續時間")]
        public float InvincibilityDuration = 0.3f;

        [Header("Cues")]
        [Tooltip("閃避開始時的 Cue")]
        public GameplayTag DodgeStartCue;

        [Tooltip("閃避結束時的 Cue")]
        public GameplayTag DodgeEndCue;

        [Header("Dodge Timeline Events")]
        [Tooltip("前衝閃避的時間軸事件（VFX/SFX）")]
        public List<TimelineEvent> DodgeTimelineEvents = new();

        [Header("Backstep Timeline Events")]
        [Tooltip("後撤的時間軸事件（VFX/SFX）")]
        public List<TimelineEvent> BackstepTimelineEvents = new();

        [Header("Perfect Dodge（完美閃避）")]
        [Tooltip("完美閃避數據（null = 不啟用完美閃避）")]
        public PerfectDodgeData PerfectDodgeData;

        /// <summary>
        /// 取得指定模式的動畫片段
        /// </summary>
        public AnimationClip GetPrimaryAnimationClip(bool isBackstep)
        {
            if (isBackstep)
            {
                return BackstepClip != null && BackstepClip.Clip != null
                    ? BackstepClip.Clip
                    : DodgeClip?.Clip;
            }
            return DodgeClip?.Clip;
        }

        /// <summary>
        /// 取得指定模式的持續時間
        /// </summary>
        public float GetDuration(bool isBackstep)
        {
            return isBackstep ? BackstepDuration : DodgeDuration;
        }

        /// <summary>
        /// 取得指定模式的距離
        /// </summary>
        public float GetDistance(bool isBackstep)
        {
            return isBackstep ? BackstepDistance : DodgeDistance;
        }

        /// <summary>
        /// 取得指定模式的移動曲線
        /// </summary>
        public AnimationCurve GetCurve(bool isBackstep)
        {
            return isBackstep ? BackstepCurve : DodgeCurve;
        }

        /// <summary>
        /// 取得指定模式的 ClipTransition
        /// </summary>
        public ClipTransition GetClipTransition(bool isBackstep)
        {
            if (isBackstep && BackstepClip != null)
            {
                return BackstepClip;
            }
            return DodgeClip;
        }

        /// <summary>
        /// 取得指定模式的時間軸事件
        /// </summary>
        public List<TimelineEvent> GetTimelineEvents(bool isBackstep)
        {
            return isBackstep ? BackstepTimelineEvents : DodgeTimelineEvents;
        }
    }
}
