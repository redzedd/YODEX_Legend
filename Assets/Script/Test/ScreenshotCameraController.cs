using UnityEngine;

/// <summary>
/// 簡易場景截圖用攝影機控制器
/// WASD 移動、滑鼠右鍵旋轉、滾輪調整速度、空白鍵上升、左Shift下降
/// </summary>
public class ScreenshotCameraController : MonoBehaviour
{
    [Header("移動設定")]
    [SerializeField] private float _moveSpeed = 10f;
    [SerializeField] private float _fastMultiplier = 3f;
    [SerializeField] private float _scrollSensitivity = 5f;

    [Header("旋轉設定")]
    [SerializeField] private float _mouseSensitivity = 3f;

    [Header("截圖設定")]
    [SerializeField] private KeyCode _screenshotKey = KeyCode.F12;
    [SerializeField] private int _superSize = 2;

    private float _rotationX;
    private float _rotationY;

    private void Start()
    {
        Vector3 euler = transform.eulerAngles;
        _rotationX = euler.y;
        _rotationY = euler.x;
    }

    private void Update()
    {
        HandleRotation();
        HandleMovement();
        HandleSpeedScroll();
        HandleScreenshot();
    }

    private void HandleRotation()
    {
        if (!Input.GetMouseButton(1)) return;
        _rotationX += Input.GetAxis("Mouse X") * _mouseSensitivity;
        _rotationY -= Input.GetAxis("Mouse Y") * _mouseSensitivity;
        _rotationY = Mathf.Clamp(_rotationY, -90f, 90f);
        transform.rotation = Quaternion.Euler(_rotationY, _rotationX, 0f);
    }

    private void HandleMovement()
    {
        float speed = _moveSpeed;
        if (Input.GetKey(KeyCode.LeftControl)) speed *= _fastMultiplier;
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        float up = 0f;
        if (Input.GetKey(KeyCode.Space)) up = 1f;
        else if (Input.GetKey(KeyCode.LeftShift)) up = -1f;
        Vector3 direction = new Vector3(h, up, v).normalized;
        transform.Translate(direction * (speed * Time.unscaledDeltaTime), Space.Self);
    }

    private void HandleSpeedScroll()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(scroll, 0f)) return;
        _moveSpeed = Mathf.Clamp(_moveSpeed + scroll * _scrollSensitivity, 0.5f, 100f);
    }

    private void HandleScreenshot()
    {
        if (!Input.GetKeyDown(_screenshotKey)) return;
        string filename = $"Screenshot_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        ScreenCapture.CaptureScreenshot(filename, _superSize);
        Debug.Log($"截圖已儲存: {filename}");
    }
}
