#if UNITY_EDITOR
using UnityEngine.UIElements;
using UnityEngine;

namespace GAS.Editor
{
    /// <summary>
    /// 拖動模式
    /// </summary>
    public enum DragMode { Move, ResizeLeft, ResizeRight }

    /// <summary>
    /// 統一的 Clip 拖動處理器 - 根據鼠標位置決定拖動模式（整體移動/左邊界縮放/右邊界縮放）
    /// </summary>
    public class UnifiedClipDragManipulator : MouseManipulator
    {
        private readonly System.Action<float, float> _callback;
        private readonly System.Action<float, float> _onComplete;
        private readonly System.Action _onSelect;
        private readonly VisualElement _clipVisual;
        private readonly float _pixelsPerSecond;
        private bool _isActive;
        private DragMode _currentMode;
        private float _startMouseX;
        private float _startLeft;
        private float _startWidth;
        private float _lastStart;
        private float _lastEnd;

        private const float EDGE_THRESHOLD = 12f;

        public UnifiedClipDragManipulator(
            VisualElement clip,
            System.Action<float, float> callback,
            System.Action onSelect,
            float pixelsPerSecond = 400f,
            System.Action<float, float> onComplete = null)
        {
            _clipVisual = clip;
            _callback = callback;
            _onSelect = onSelect;
            _onComplete = onComplete;
            _pixelsPerSecond = pixelsPerSecond;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            target.RegisterCallback<MouseOverEvent>(OnMouseOver);
            target.RegisterCallback<MouseOutEvent>(OnMouseOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            target.UnregisterCallback<MouseOverEvent>(OnMouseOver);
            target.UnregisterCallback<MouseOutEvent>(OnMouseOut);
        }

        private DragMode GetModeFromPosition(float localX, float clipWidth)
        {
            if (localX < EDGE_THRESHOLD) return DragMode.ResizeLeft;
            if (localX > clipWidth - EDGE_THRESHOLD) return DragMode.ResizeRight;
            return DragMode.Move;
        }

        private void UpdateCursor(DragMode mode)
        {
            var leftHandle = _clipVisual.Q("left-handle");
            var rightHandle = _clipVisual.Q("right-handle");
            if (leftHandle != null)
            {
                leftHandle.style.backgroundColor = mode == DragMode.ResizeLeft
                    ? new Color(1, 1, 0, 0.8f)
                    : new Color(1, 1, 1, 0.4f);
            }
            if (rightHandle != null)
            {
                rightHandle.style.backgroundColor = mode == DragMode.ResizeRight
                    ? new Color(1, 1, 0, 0.8f)
                    : new Color(1, 1, 1, 0.4f);
            }
        }

        private void OnMouseOver(MouseOverEvent e)
        {
            float clipWidth = _clipVisual.resolvedStyle.width;
            var mode = GetModeFromPosition(e.localMousePosition.x, clipWidth);
            UpdateCursor(mode);
        }

        private void OnMouseOut(MouseOutEvent e)
        {
            if (!_isActive) UpdateCursor(DragMode.Move);
        }

        private void OnMouseDown(MouseDownEvent e)
        {
            if (_isActive || e.button != 0) return;
            _onSelect?.Invoke();
            float clipWidth = _clipVisual.resolvedStyle.width;
            _currentMode = GetModeFromPosition(e.localMousePosition.x, clipWidth);
            _isActive = true;
            _startMouseX = e.mousePosition.x;
            _startLeft = _clipVisual.resolvedStyle.left;
            _startWidth = _clipVisual.resolvedStyle.width;
            target.CaptureMouse();
            e.StopPropagation();
        }

        private void OnMouseMove(MouseMoveEvent e)
        {
            if (!_isActive)
            {
                float clipWidth = _clipVisual.resolvedStyle.width;
                var mode = GetModeFromPosition(e.localMousePosition.x, clipWidth);
                UpdateCursor(mode);
                return;
            }
            if (!target.HasMouseCapture()) return;
            float delta = e.mousePosition.x - _startMouseX;
            float curLeft = _startLeft;
            float curWidth = _startWidth;
            if (_currentMode == DragMode.Move)
            {
                curLeft += delta;
            }
            else if (_currentMode == DragMode.ResizeLeft)
            {
                curLeft += delta;
                curWidth -= delta;
            }
            else if (_currentMode == DragMode.ResizeRight)
            {
                curWidth += delta;
            }
            if (curWidth < 20)
            {
                if (_currentMode == DragMode.ResizeLeft)
                {
                    curLeft = _startLeft + _startWidth - 20;
                }
                curWidth = 20;
            }
            if (curLeft < 0) curLeft = 0;
            _clipVisual.style.left = curLeft;
            _clipVisual.style.width = curWidth;
            float start = curLeft / _pixelsPerSecond;
            float end = (curLeft + curWidth) / _pixelsPerSecond;
            _lastStart = start;
            _lastEnd = end;
            _callback?.Invoke(start, end);
        }

        private void OnMouseUp(MouseUpEvent e)
        {
            if (_isActive)
            {
                _isActive = false;
                target.ReleaseMouse();
                _onComplete?.Invoke(_lastStart, _lastEnd);
                e.StopPropagation();
            }
        }
    }

    /// <summary>
    /// 播放頭拖動處理器 - 拖動紅色播放頭改變當前播放時間
    /// </summary>
    public class ScrubberManipulator : MouseManipulator
    {
        private readonly float _offset;
        private readonly System.Action<float> _onSetTimePixel;
        private bool _isActive;

        public ScrubberManipulator(VisualElement container, float offset, System.Action<float> onSetTimePixel)
        {
            _offset = offset;
            _onSetTimePixel = onSetTimePixel;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
        }

        private void OnMouseDown(MouseDownEvent e)
        {
            _isActive = true;
            target.CaptureMouse();
            Update(e.localMousePosition.x);
            e.StopPropagation();
        }

        private void OnMouseMove(MouseMoveEvent e)
        {
            if (_isActive && target.HasMouseCapture())
            {
                Update(e.localMousePosition.x);
            }
        }

        private void OnMouseUp(MouseUpEvent e)
        {
            if (_isActive)
            {
                _isActive = false;
                target.ReleaseMouse();
                e.StopPropagation();
            }
        }

        private void Update(float localX)
        {
            float x = localX - _offset;
            if (x < 0) x = 0;
            _onSetTimePixel?.Invoke(x);
        }
    }
}
#endif
