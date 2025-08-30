using UnityEngine;

public class PlayMusicOnCondition : MonoBehaviour
{
    [Header("�]�w�ؼЪ���")]
    public GameObject targetObject; // ���}�ؼЪ���

    [Header("���ֳ]�w")]
    public AudioSource currentAudioSource; // ��e���񤤪����ļ���
    public AudioSource newAudioSource;     // �s�����ļ���
    public AudioClip newMusicClip;         // �n���񪺷s���֤��q

    private bool hasPlayed = false; // �T�O���֥u����@��

    void Update()
    {
        if (targetObject != null && targetObject.activeSelf && !hasPlayed)
        {
            // �����e������
            if (currentAudioSource != null && currentAudioSource.isPlaying)
            {
                currentAudioSource.Stop();
            }

            // ����s������
            if (newAudioSource != null && newMusicClip != null)
            {
                newAudioSource.clip = newMusicClip;
                newAudioSource.Play();
                hasPlayed = true; // �T�O���֤��|���Ƽ���
            }
            else
            {
                Debug.LogWarning("�����T�]�w�s AudioSource �έ��֤��q�I");
            }
        }
    }
}
