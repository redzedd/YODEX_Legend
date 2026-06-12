using UnityEngine;

/// <summary>
/// 宣傳片演示用:讓掛載此腳本的物件持續面向指定目標。
/// 支援 Y 軸鎖定(避免往上下看)、平滑旋轉、Forward 偏移補正。
/// </summary>
public class TestFaceTarget : MonoBehaviour
{
    [Header("目標")]
    [SerializeField, Tooltip("要面對的目標 Transform")]
    private Transform _target;

    [Header("旋轉設定")]
    [SerializeField, Tooltip("僅鎖 Y 軸旋轉 (角色/站立物件建議開啟,避免往上下看)")]
    private bool _yAxisOnly = true;
    [SerializeField, Tooltip("平滑旋轉 (關閉則瞬間對準)")]
    private bool _smooth = true;
    [SerializeField, Tooltip("平滑旋轉速度 (度/秒)"), Min(0f)]
    private float _rotationSpeed = 360f;
    [SerializeField, Tooltip("額外旋轉偏移 (模型 Forward 不是 +Z 時用來補正,例如填 (0,90,0))")]
    private Vector3 _eulerOffset;

    [Header("執行時機")]
    [SerializeField, Tooltip("於 LateUpdate 執行 (建議,避免目標於同一幀移動造成抖動)")]
    private bool _useLateUpdate = true;

    public Transform Target
    {
        get => _target;
        set => _target = value;
    }

    private void Update()
    {
        if (!_useLateUpdate)
        {
            FaceTick();
        }
    }

    private void LateUpdate()
    {
        if (_useLateUpdate)
        {
            FaceTick();
        }
    }

    private void FaceTick()
    {
        if (_target == null) return;
        Vector3 direction = _target.position - transform.position;
        if (_yAxisOnly)
        {
            direction.y = 0f;
        }
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion targetRot = Quaternion.LookRotation(direction) * Quaternion.Euler(_eulerOffset);
        if (_smooth)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, _rotationSpeed * Time.deltaTime);
        }
        else
        {
            transform.rotation = targetRot;
        }
    }
}
