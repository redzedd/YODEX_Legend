using UnityEngine;

[CreateAssetMenu(fileName = "BossAttackData", menuName = "Boss/BossAttackData")]
public class BossAttackData : ScriptableObject
{
    public string animationName;       // Animator 中對應的動畫名稱
    [Header("旋轉允許時間區間 (0~1)")]
    [Range(0f, 1f)] public float canTurnStart = 0.2f;
    [Range(0f, 1f)] public float canTurnEnd = 0.7f;

}
