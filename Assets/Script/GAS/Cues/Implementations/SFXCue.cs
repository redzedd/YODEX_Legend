using UnityEngine;

namespace GAS
{
    /// <summary>
    /// SFX Cue - 音效 Cue 實現
    /// </summary>
    [CreateAssetMenu(fileName = "New SFX Cue", menuName = "GAS/Cues/SFX Cue")]
    public class SFXCue : GameplayCue
    {
        [Header("SFX Settings")]
        [Tooltip("音效片段")]
        public AudioClip AudioClip;

        [Tooltip("多個音效時隨機選擇")]
        public AudioClip[] RandomClips;

        [Header("Audio Settings")]
        [Range(0f, 1f)]
        [Tooltip("音量")]
        public float Volume = 1f;

        [Range(0.5f, 2f)]
        [Tooltip("音調")]
        public float Pitch = 1f;

        [Tooltip("音調隨機範圍")]
        public float PitchVariation = 0f;

        [Tooltip("是否循環")]
        public bool Loop = false;

        [Tooltip("3D 空間音效")]
        public bool Spatial = true;

        [Header("Fallback")]
        [Tooltip("如果沒有 AudioManager，使用 PlayClipAtPoint")]
        public bool UseFallback = true;

        public override void OnExecute(GameplayCueParameters parameters)
        {
            AudioClip clip = GetClip();
            if (clip == null) return;
            Vector3 position = GetSpawnPosition(parameters);
            float finalPitch = Pitch + Random.Range(-PitchVariation, PitchVariation);
            // 使用 Unity 內建的 AudioSource.PlayClipAtPoint
            // 注意：不支援 Loop 和自訂 Pitch，如需進階功能請使用外部 AudioManager
            AudioSource.PlayClipAtPoint(clip, position, Volume);
        }

        public override void OnActivate(GameplayCueParameters parameters)
        {
            OnExecute(parameters);
        }

        /// <summary>
        /// 獲取要播放的音效
        /// </summary>
        private AudioClip GetClip()
        {
            if (RandomClips != null && RandomClips.Length > 0)
            {
                return RandomClips[Random.Range(0, RandomClips.Length)];
            }
            return AudioClip;
        }
    }
}
