using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 組合 Cue - 同時播放多個子 Cue（VFX + SFX + HitStop 等）
    /// </summary>
    [CreateAssetMenu(fileName = "New Combined Cue", menuName = "GAS/Cues/Combined Cue")]
    public class CombinedCue : GameplayCue
    {
        [Header("Sub Cues")]
        [Tooltip("要執行的子 Cue 列表")]
        public GameplayCue[] SubCues;

        public override void OnExecute(GameplayCueParameters parameters)
        {
            if (SubCues == null) return;
            foreach (var cue in SubCues)
            {
                if (cue != null)
                {
                    cue.OnExecute(parameters);
                }
            }
        }

        public override void OnActivate(GameplayCueParameters parameters)
        {
            if (SubCues == null) return;
            foreach (var cue in SubCues)
            {
                if (cue != null)
                {
                    cue.OnActivate(parameters);
                }
            }
        }

        public override void OnDeactivate(GameplayCueParameters parameters)
        {
            if (SubCues == null) return;
            foreach (var cue in SubCues)
            {
                if (cue != null)
                {
                    cue.OnDeactivate(parameters);
                }
            }
        }
    }
}
