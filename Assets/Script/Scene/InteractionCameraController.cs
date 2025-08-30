using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Unity.Cinemachine;

public class InteractionCameraController : MonoBehaviour
{
    [Header("Cinemachine")]
    public CinemachineCamera focusCamera;
    public int cameraBoostPriority = 20;

    [Header("Timing")]
    public float cameraFocusDuration = 2f;   // A ��
    public float delayBeforeAction = 0f;     // �i�冀��
    public float delayBeforeCameraOff = 0.5f;
    public float totalLifeDuration = 3f;     // B ��G���y�{��۰� Destroy

    [Header("Optional")]
    public bool autoDestroy = true;

    public void StartCameraSequence(Action onAction)
    {
        StartCoroutine(CameraSequenceCoroutine(onAction));
    }

    private IEnumerator CameraSequenceCoroutine(Action onAction)
    {
        if (focusCamera != null)
        {
            focusCamera.Priority = cameraBoostPriority;
            focusCamera.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(cameraFocusDuration);

        // ����ǤJ�ƥ�
        onAction?.Invoke();

        yield return new WaitForSeconds(delayBeforeCameraOff);

        if (focusCamera != null)
            focusCamera.gameObject.SetActive(false);

        float remaining = totalLifeDuration - cameraFocusDuration - delayBeforeCameraOff;
        if (remaining > 0)
            yield return new WaitForSeconds(remaining);

        if (autoDestroy)
            Destroy(gameObject);
    }
}
