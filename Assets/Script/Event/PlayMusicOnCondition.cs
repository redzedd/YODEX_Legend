using UnityEngine;

public class PlayMusicOnCondition : MonoBehaviour
{
    [Header("設定目標物件")]
    public GameObject targetObject; // 公開目標物件

    [Header("音樂設定")]
    public AudioSource currentAudioSource; // 當前播放中的音效播放器
    public AudioSource newAudioSource;     // 新的音效播放器
    public AudioClip newMusicClip;         // 要撥放的新音樂片段

    private bool hasPlayed = false; // 確保音樂只撥放一次

    void Update()
    {
        if (targetObject != null && targetObject.activeSelf && !hasPlayed)
        {
            // 停止當前的音樂
            if (currentAudioSource != null && currentAudioSource.isPlaying)
            {
                currentAudioSource.Stop();
            }

            // 撥放新的音樂
            if (newAudioSource != null && newMusicClip != null)
            {
                newAudioSource.clip = newMusicClip;
                newAudioSource.Play();
                hasPlayed = true; // 確保音樂不會重複撥放
            }
            else
            {
                Debug.LogWarning("未正確設定新 AudioSource 或音樂片段！");
            }
        }
    }
}
