using UnityEngine;

[CreateAssetMenu(fileName = "BossAttackSet", menuName = "Boss/Boss Attack Set")]
public class BossAttackSet : ScriptableObject
{
    public BossAttackData[] attacks;
}
