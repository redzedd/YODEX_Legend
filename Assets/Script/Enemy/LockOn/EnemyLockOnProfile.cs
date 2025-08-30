using UnityEngine;

[CreateAssetMenu(menuName = "LockOn/Enemy Profile", fileName = "EnemyLockOnProfile")]
public class EnemyLockOnProfile : ScriptableObject
{
    [Header("Info")]
    public string enemyName = "Enemy";

    [Header("Indicator (when locked)")]
    public float indicatorHeight = 2.0f;     // �Y�S���� indicatorAnchor�A�N�γo�Ӱ���
    public string indicatorAnchorPath = "";  // ��: "Armature/Hips/Spine/Chest/Head/IndicatorAnchor"

    [Header("Lock Anchor")]
    public string lockAnchorPath = "";       // ��: "Armature/Hips/Spine/Chest/Head/LockAnchor"

    [Header("Cinemachine TargetGroup")]
    public float groupWeight = 1.0f;         // �v��
    public float groupRadius = 1.0f;         // �b�|�]�V�j���Y�V�|�Ի��h�ˤU�^

    [Header("Rotation Composer (Screen Position)")]
    [Tooltip("��w�ɮM�Ψ� CinemachineRotationComposer �� ScreenPosition.X")]
    public float screenPosX = 0f;
    [Tooltip("��w�ɮM�Ψ� CinemachineRotationComposer �� ScreenPosition.Y")]
    public float screenPosY = 0f;
}
