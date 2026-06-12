using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GearPiece : MonoBehaviour, IHitReceiver
{
    [Header("Settings")]
    public int gearID;
    [Tooltip("�ثe���ਤ�ׯ��� (0=0��, 1=90��, 2=180��, 3=270��)")]
    public int currentRotationIndex = 0;

    [Header("Interaction")]
    [Tooltip("�P�������r�X����L����")]
    public List<GearPiece> connectedGears;

    [Header("Visual")]
    public Transform gearMesh;

    [Tooltip("����b�V (�Ь� Scene�������Žu�O�_��L��������)")]
    // �w�]�]�� Y �b�A�]���ܦh���񪺾����ҫ��O�¤W��
    public Vector3 rotationAxis = new Vector3(0, 1, 0);
    public float rotateDuration = 0.3f;

    [Header("Audio & VFX")]
    public AudioClip rotateSFX;
    public ParticleSystem successVFX;

    private bool isRotating = false;
    private GearPuzzleManager manager;

    public void Initialize(GearPuzzleManager mgr)
    {
        manager = mgr;
        // ���A�j��]���סA�����H�����W�\�񪺼ˤl�� Index 0
    }

    public void OnHit(ref HitContext ctx)
    {
        // ���a�V���AĲ�o���ɰw���� (1)
        TriggerRotate(1, null);
    }

    /// <summary>
    /// Ĳ�o����
    /// </summary>
    /// <param name="direction">1 �����ɰw, -1 ���f�ɰw</param>
    /// <param name="source">�ӷ������A�קK���^���o�_��</param>
    public void TriggerRotate(int direction, GearPiece source)
    {
        if (isRotating || (manager != null && manager.IsSolved)) return;

        // ��s�޿���� (�O���b 0~3 ����)
        currentRotationIndex = (currentRotationIndex + direction);
        if (currentRotationIndex > 3) currentRotationIndex = 0;
        if (currentRotationIndex < 0) currentRotationIndex = 3;

        // �}�l��ı����
        StartCoroutine(RotateRoutine(direction));

        // �s������G���ʬ۾F���� (��V�ۤϡG�A���ɰw���ڡA�ڴN�f�ɰw��)
        if (connectedGears != null)
        {
            foreach (var neighbor in connectedGears)
            {
                if (neighbor != null && neighbor != source)
                {
                    neighbor.TriggerRotate(direction * -1, this);
                }
            }
        }

        // �ˬd����
        if (manager != null) manager.CheckSolution();
    }

    private IEnumerator RotateRoutine(int direction)
    {
        isRotating = true;

        // �� �֤߭ץ��G�ϥά۹����
        // ���޲{�b���צh�֡A�������W�@�ӡu�W�q�v
        Quaternion startRot = gearMesh.localRotation;
        Quaternion rotAmount = Quaternion.AngleAxis(90f * direction, rotationAxis);
        Quaternion endRot = startRot * rotAmount;

        if (rotateSFX) AudioSource.PlayClipAtPoint(rotateSFX, transform.position);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / rotateDuration;
            // ���ƴ��Ȩ�ؼШ���
            gearMesh.localRotation = Quaternion.Slerp(startRot, endRot, t);
            yield return null;
        }
        // �T�O��T���
        gearMesh.localRotation = endRot;

        isRotating = false;
    }

    public void PlaySuccessEffect()
    {
        if (successVFX) successVFX.Play();
    }

    // �� ��ı�����G�b Scene �����e�X����b
    private void OnDrawGizmosSelected()
    {
        if (gearMesh)
        {
            Gizmos.color = Color.cyan;
            // �e�X����b���� 2 ��
            Vector3 axisDir = gearMesh.TransformDirection(rotationAxis).normalized;
            Gizmos.DrawLine(gearMesh.position - axisDir, gearMesh.position + axisDir);
            Gizmos.DrawWireSphere(gearMesh.position + axisDir, 0.1f);
        }
    }
}