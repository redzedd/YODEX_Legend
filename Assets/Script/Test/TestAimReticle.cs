using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 宣傳片演示用瞄準準星 UI:
/// 進入拉弓時中央點淡入,開始蓄力時圓環淡入並在 1.5 秒內從外圈收攏到與點同大。
/// 由 TestPlayerDemo 在狀態轉換時呼叫 OnAimEnter / StartCharge / EndCharge / OnAimExit。
/// </summary>
public class TestAimReticle : MonoBehaviour
{
    [Header("圖片資產")]
    [SerializeField, Tooltip("中央點 Sprite")]
    private Sprite _dotSprite;
    [SerializeField, Tooltip("圓環 Sprite")]
    private Sprite _ringSprite;
    [SerializeField, Tooltip("點顏色")]
    private Color _dotColor = Color.white;
    [SerializeField, Tooltip("圓環顏色")]
    private Color _ringColor = Color.white;

    [Header("尺寸")]
    [SerializeField, Tooltip("點的螢幕尺寸 (像素)")]
    private Vector2 _dotSize = new Vector2(16f, 16f);
    [SerializeField, Tooltip("圓環剛出現時的尺寸 (像素)")]
    private Vector2 _ringStartSize = new Vector2(180f, 180f);

    [Header("時間")]
    [SerializeField, Tooltip("蓄力時間 (秒),圓環收攏到和點同大")]
    private float _chargeDuration = 1.5f;
    [SerializeField, Tooltip("點淡入/淡出時間")]
    private float _dotFadeTime = 0.2f;
    [SerializeField, Tooltip("圓環淡入/淡出時間")]
    private float _ringFadeTime = 0.15f;

    [Header("Canvas")]
    [SerializeField, Tooltip("排序順序 (數字越大越上層)")]
    private int _sortingOrder = 100;

    private Canvas _canvas;
    private RectTransform _dotRect;
    private RectTransform _ringRect;
    private CanvasGroup _dotGroup;
    private CanvasGroup _ringGroup;

    private float _dotTargetAlpha;
    private float _ringTargetAlpha;
    private float _chargeTimer;
    private bool _charging;

    private void Awake()
    {
        BuildCanvas();
    }

    private void BuildCanvas()
    {
        GameObject canvasGo = new GameObject("TestAimReticle_Canvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = _sortingOrder;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasGo.AddComponent<GraphicRaycaster>();
        _ringRect = CreateImage(canvasGo.transform, "Ring", _ringSprite, _ringColor, _ringStartSize, out _ringGroup);
        _dotRect = CreateImage(canvasGo.transform, "Dot", _dotSprite, _dotColor, _dotSize, out _dotGroup);
    }

    private static RectTransform CreateImage(Transform parent, string name, Sprite sprite, Color color, Vector2 size, out CanvasGroup group)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.preserveAspect = true;
        group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        return rt;
    }

    private void Update()
    {
        // Alpha 淡入淡出
        float dotStep = _dotFadeTime > 0f ? Time.deltaTime / _dotFadeTime : 1f;
        float ringStep = _ringFadeTime > 0f ? Time.deltaTime / _ringFadeTime : 1f;
        _dotGroup.alpha = Mathf.MoveTowards(_dotGroup.alpha, _dotTargetAlpha, dotStep);
        _ringGroup.alpha = Mathf.MoveTowards(_ringGroup.alpha, _ringTargetAlpha, ringStep);
        // 蓄力收攏
        if (_charging)
        {
            _chargeTimer = Mathf.Min(_chargeTimer + Time.deltaTime, _chargeDuration);
            float t = _chargeDuration > 0f ? _chargeTimer / _chargeDuration : 1f;
            _ringRect.sizeDelta = Vector2.Lerp(_ringStartSize, _dotSize, t);
        }
    }

    /// <summary>進入 Aim 攝影機 — 點淡入。</summary>
    public void OnAimEnter()
    {
        _dotTargetAlpha = 1f;
    }

    /// <summary>離開 Aim 攝影機 — 點與圓環都淡出,停止蓄力。</summary>
    public void OnAimExit()
    {
        _dotTargetAlpha = 0f;
        _ringTargetAlpha = 0f;
        _charging = false;
    }

    /// <summary>開始蓄力 — 圓環淡入,從 _ringStartSize 收攏到 _dotSize。</summary>
    public void StartCharge()
    {
        _ringRect.sizeDelta = _ringStartSize;
        _chargeTimer = 0f;
        _charging = true;
        _ringTargetAlpha = 1f;
    }

    /// <summary>結束蓄力 (放箭或取消) — 圓環淡出,停止收攏。</summary>
    public void EndCharge()
    {
        _ringTargetAlpha = 0f;
        _charging = false;
    }

    /// <summary>蓄力完成度 (0 = 剛開始,1 = 完全蓄滿),供外部邏輯使用(例如決定傷害)。</summary>
    public float ChargeNormalized => _chargeDuration > 0f ? Mathf.Clamp01(_chargeTimer / _chargeDuration) : 1f;
}
