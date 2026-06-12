using UnityEngine;

namespace GAS
{
    /// <summary>
    /// TimelineEvent 觸發後的執行階段追蹤物件。
    /// Spawn 完成後交給觸發端保存,等待 Cleanup 時依「StopOnInterrupt / AttachToBody」決定銷毀或脫離骨骼繼續播放。
    /// 一個 instance 只會走其中一條路徑 — VFXPrefab 直拉(SpawnedVFX) 或 CueTag fallback(CueHandler),不會同時非空。
    /// </summary>
    public class TimelineEventInstance
    {
        /// <summary>觸發來源,Cleanup 時用來讀 StopOnInterrupt / AttachToBody / InterruptBehavior</summary>
        public TimelineEvent Event;

        /// <summary>VFXPrefab 路徑生成的特效實例(直接拉 Prefab 流程)</summary>
        public GameObject SpawnedVFX;

        /// <summary>VFXPrefab 路徑掛載的軸跟隨元件 — Cleanup 時呼叫 StopFollowing 凍結特效</summary>
        public TimelineVFXFollower Follower;

        /// <summary>CueTag 路徑追蹤的 Cue Handler(fallback 流程,僅當 VFXPrefab 與 SFX 皆未設定時走)</summary>
        public GameplayCueHandler CueHandler;
    }
}
