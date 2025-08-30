using UnityEngine;

[CreateAssetMenu(fileName = "BossAttackData", menuName = "Boss/BossAttackData")]
public class BossAttackData : ScriptableObject
{
    public string animationName;       // Animator ���������ʵe�W��
    [Header("���ह�\�ɶ��϶� (0~1)")]
    [Range(0f, 1f)] public float canTurnStart = 0.2f;
    [Range(0f, 1f)] public float canTurnEnd = 0.7f;

}
