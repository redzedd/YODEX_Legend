using UnityEngine;

[CreateAssetMenu(fileName = "AttackProfile", menuName = "Combat/Attack Profile")]
public class AttackProfile : ScriptableObject
{
    [Header("Basic")]
    public string attackName = "LightSlash";
    public float damage = 10f;
    public float poiseDamage = 25f;
    [Tooltip("擊退漸近距離(公尺)— Poise 擊破後的水平位移;衰減採 HitReactionData.ExternalVelocityDecayTau")]
    public float knockback = 1f;
    [Tooltip("攻擊類型:\nNormal — 一般攻擊（打斷 Idle/Walk，被攻擊霸體擋）\nLight — 輕攻擊（只抖動，不打斷任何狀態）\nHeavy — 重攻擊（打斷攻擊霸體；Poise 擊破走 Knockback 倒地）")]
    public AttackTier attackTier = AttackTier.Normal;

    [Tooltip("【已棄用，但保留兼容】— 寫入端會自動依 attackTier == Heavy 同步此欄位。新攻擊請改設 attackTier。")]
    public bool isHeavyAttack = false;

    [Header("Feel / Juice")]
    [Tooltip("�R���ɪ��y�V�ɶ� (��)�C��������ĳ 0.08~0.12�A������ 0.15~0.25")]
    public float hitStopDuration = 0.1f;

    [Tooltip("�R���ɪ����Y�_�ʱj�סC��ĳ�G������ 0.5~1.5�A������ 2.0~4.0")]
    public float cameraShakeIntensity = 1.0f;

    [Tooltip("�O�_���L�����S�ĻP���� (�Ҧp���Χ����ίº骺���z���O)")]
    public bool skipHitEffects = false; // �� �s�W

    [Header("Hit Rules")]
    public bool allowMultipleHitsPerTargetInOneActivation = false;
    public float perTargetHitCooldown = 0.1f;
}