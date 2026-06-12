// FollowAnchorSmooth.cs (�������)
using UnityEngine;

namespace GAS.UI
{
[DefaultExecutionOrder(1000)] // �����b�j�h�ƪF�褧��~�]
public class FollowAnchorSmooth : MonoBehaviour
{
    [Tooltip("�n�l�ܪ��ؼ� (�Ҧp���a�Y���Ū���)")]
    public Transform target;

    [Tooltip("�۹��ؼЮy�Шt�������q")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("��m�����t�� (�V�j�V��o��A��ĳ 8~20)")]
    public float positionDamp = 12f;

    [Tooltip("�O�_���ƦP�B����")]
    public bool smoothRotation = true;

    [Tooltip("��������t�� (�V�j�V��o��A��ĳ 8~20)")]
    public float rotationDamp = 12f;

    void LateUpdate()
    {
        if (!target) return;

        // ����@�ɮy�С]�ΥؼЪ��y�Шt�ⰾ���^
        Vector3 desiredPos = target.TransformPoint(positionOffset);

        // ���ƪ����]�V�v�W�ߡ^
        float kPos = 1f - Mathf.Exp(-positionDamp * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPos, kPos);

        if (smoothRotation)
        {
            Quaternion desiredRot = target.rotation;
            float kRot = 1f - Mathf.Exp(-rotationDamp * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, kRot);
        }
    }
}
}
