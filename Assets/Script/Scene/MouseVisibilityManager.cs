using System.Collections;
using UnityEngine;

public class MouseVisibilityManager : MonoBehaviour
{
    public static MouseVisibilityManager Instance { get; private set; }

    [Header("設定")]
    public float hideAfterSeconds = 3f;
    public bool enableDynamicMouse = false; // 只有進入 UI 時才打開

    private float inactivityTimer = 0f;
    private Vector3 lastMousePosition;
    private bool isVisible = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        lastMousePosition = Input.mousePosition;
        HideCursorImmediate();
    }

    private void Update()
    {
        if (!enableDynamicMouse)
            return;

        if (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.01f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.01f)
        {
            ShowCursor();
            inactivityTimer = 0f;
            lastMousePosition = Input.mousePosition;
        }
        else
        {
            inactivityTimer += Time.unscaledDeltaTime;

            if (isVisible && inactivityTimer >= hideAfterSeconds)
            {
                HideCursorImmediate();
            }
        }
    }

    public void ShowCursor()
    {
        if (!isVisible)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            isVisible = true;
        }
    }

    public void HideCursorImmediate()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        isVisible = false;
    }
}
