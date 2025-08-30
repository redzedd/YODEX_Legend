using UnityEngine;

public enum SpecialEffectType
{
    None,
    Regeneration,   // �A�͡G�ݭn tickInterval�BpercentPerTick
    FireResistance, // �d�ҡG�u�� duration / magnitude
    AttackBoost     // �d�ҡG�u�� duration / magnitude
}

public enum BuffAffectType
{
    Health,   // ��q
    Mana,     // �]�O
    Stamina,  // ��O
    General   // �@��W�q
}

[System.Serializable]
public class BuffTierData
{
    [Range(1, 3)] public int level = 1;

    [Header("���")]
    public Sprite icon;                // �Ӷ��q�M�ݹϥ�

    [Header("�ɧǡ]�j�h�� Buff �|�Ψ�^")]
    public float duration = 0f;        // �`����ɶ��]��^

    [Header("�A��( Regeneration )�M��")]
    public float tickInterval = 0f;    // ���ʶ��j�]��^
    [Range(0f, 100f)] public float percentPerTick = 0f; // �C���^ max%�]0~100�^

    [Header("�@��W�q(Attack/Resistance...)")]
    public float magnitude = 0f;       // �Ҧp�[��%�B���% ��
}

[CreateAssetMenu(fileName = "BuffDefinition", menuName = "Game/Buffs/Buff Definition")]
public class BuffDefinition : ScriptableObject
{
    [Header("�ѧO/�Ƨ�")]
    [Tooltip("�ΨӱƧ���ܡF�Ʀr�V�p�V�a��/�W")]
    public int buffId = 1;

    [Header("�ݩ� / �@�����O")]
    public BuffAffectType affectType = BuffAffectType.General;

    [Header("�ĪG����")]
    public SpecialEffectType specialEffect = SpecialEffectType.None;

    [Header("�T�����(�T�w 1~3)")]
    public BuffTierData tier1;
    public BuffTierData tier2;
    public BuffTierData tier3;

    public BuffTierData GetTier(int level)
    {
        if (level <= 1) return tier1;
        if (level == 2) return tier2;
        return tier3;
    }
}