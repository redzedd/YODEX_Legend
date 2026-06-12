using System;
using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

namespace Interaction
{
    /// <summary>
    /// 可重用的互動相機演出序列
    /// 聚焦 Cinemachine 相機 → 執行回呼 → 恢復相機 → 自動銷毀
    /// </summary>
    public class InteractionCameraSequence : MonoBehaviour
    {
        [Header("Cinemachine")]
        [SerializeField] private CinemachineCamera _focusCamera;
        [SerializeField] private int _cameraBoostPriority = 20;

        [Header("時序")]
        [SerializeField] private float _cameraFocusDuration = 2f;
        [SerializeField] private float _delayBeforeAction;
        [SerializeField] private float _delayBeforeCameraOff = 0.5f;
        [SerializeField] private float _totalLifeDuration = 3f;

        [Header("選項")]
        [SerializeField] private bool _autoDestroy = true;

        /// <summary>啟動相機演出序列</summary>
        /// <param name="onAction">聚焦結束後執行的動作回呼</param>
        public void StartCameraSequence(Action onAction)
        {
            StartCoroutine(CameraSequenceRoutine(onAction));
        }

        private IEnumerator CameraSequenceRoutine(Action onAction)
        {
            // 啟動聚焦
            if (_focusCamera != null)
            {
                _focusCamera.Priority = _cameraBoostPriority;
                _focusCamera.gameObject.SetActive(true);
            }
            yield return new WaitForSeconds(_cameraFocusDuration);
            // 延遲後觸發動作
            if (_delayBeforeAction > 0f)
                yield return new WaitForSeconds(_delayBeforeAction);
            onAction?.Invoke();
            // 關閉聚焦相機
            yield return new WaitForSeconds(_delayBeforeCameraOff);
            if (_focusCamera != null)
                _focusCamera.gameObject.SetActive(false);
            // 等待剩餘時間
            float remaining = _totalLifeDuration - _cameraFocusDuration
                - _delayBeforeAction - _delayBeforeCameraOff;
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);
            if (_autoDestroy)
                Destroy(gameObject);
        }
    }
}
