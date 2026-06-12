#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace GAS.Editor
{
    /// <summary>
    /// GAS 閃避數據可視化編輯器
    /// 提供時間軸編輯、動畫預覽、無敵窗口可視化
    /// 支援前衝閃避與後撤兩種模式
    /// </summary>
    public class GASDodgeDataEditorWindow : EditorWindow
    {
        private DodgeData _currentData;
        private TimelineEvent _selectedEvent;

        private VisualElement _timelineContentArea;
        private VisualElement _scrubberOverlay;
        private VisualElement _inspectorContainer;
        private VisualElement _eventInspectorContainer;
        private ScrollView _timelineScrollView;
        private Label _timeLabel;
        private ObjectField _dataObjectField;
        private FloatField _startTimeField;

        private float _currentTime;
        private bool _isPlaying;
        private double _lastEditorTime;
        private bool _isBackstepMode;

        private readonly Dictionary<TimelineEvent, GameObject> _previewVFXs = new();
        private readonly Dictionary<object, VisualElement> _clipMap = new();
        private bool _isDragging;

        private AbilitySystemComponent _previewTarget;
        private GameObject _animationTarget;
        private Vector3 _initialPos;
        private Quaternion _initialRot;
        private bool _isPreviewing;

        // VFX Cue 查找緩存
        private Dictionary<string, VFXCue> _vfxCueCache = new();

        private const float PIXELS_PER_SECOND = 400f;
        private const float BASE_TRACK_HEIGHT = 40f;
        private const float LANE_HEIGHT = 26f;
        private const float LEFT_PANEL_WIDTH = 350f;

        [MenuItem("GAS/Dodge Data Editor")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<GASDodgeDataEditorWindow>();
            wnd.titleContent = new GUIContent("GAS Dodge Editor");
            wnd.minSize = new Vector2(1200, 600);
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            FullResetPreview();
        }

        private void OnUndoRedo()
        {
            RefreshAll();
            UpdatePreviewState(true);
        }

        public void CreateGUI()
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Script/GAS/Editor/GASEditorStyles.uss");
            if (styleSheet != null) rootVisualElement.styleSheets.Add(styleSheet);
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            var mainSplitView = new TwoPaneSplitView(0, LEFT_PANEL_WIDTH, TwoPaneSplitViewOrientation.Horizontal);
            rootVisualElement.Add(mainSplitView);

            // 左側面板
            var leftPane = new VisualElement { style = { backgroundColor = new Color(0.22f, 0.22f, 0.22f) } };
            var leftSplitView = new TwoPaneSplitView(1, 200, TwoPaneSplitViewOrientation.Vertical);
            leftSplitView.style.flexGrow = 1;

            // 上半部：閃避設定
            var topSection = new VisualElement { style = { backgroundColor = new Color(0.22f, 0.22f, 0.22f) } };
            topSection.Add(new Label("Dodge Data Settings")
            {
                style =
                {
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = Color.white,
                    marginBottom = 5,
                    paddingLeft = 5,
                    paddingTop = 5,
                    backgroundColor = new Color(0.25f, 0.25f, 0.25f),
                    paddingBottom = 5
                }
            });
            var scroll1 = new ScrollView();
            _inspectorContainer = new VisualElement();
            scroll1.Add(_inspectorContainer);
            scroll1.style.flexGrow = 1;
            topSection.Add(scroll1);

            // 下半部：選中元素設定
            var bottomSection = new VisualElement { style = { backgroundColor = new Color(0.22f, 0.22f, 0.22f) } };
            bottomSection.Add(new Label("Selected Element Settings")
            {
                style =
                {
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = Color.white,
                    marginBottom = 5,
                    paddingLeft = 5,
                    paddingTop = 5,
                    backgroundColor = new Color(0.25f, 0.25f, 0.25f),
                    paddingBottom = 5
                }
            });
            var scroll2 = new ScrollView();
            _eventInspectorContainer = new VisualElement { style = { paddingBottom = 20, paddingLeft = 5, paddingRight = 5 } };
            scroll2.Add(_eventInspectorContainer);
            scroll2.style.flexGrow = 1;
            bottomSection.Add(scroll2);

            leftSplitView.Add(topSection);
            leftSplitView.Add(bottomSection);
            leftPane.Add(leftSplitView);
            mainSplitView.Add(leftPane);

            // 右側面板 - 時間軸區域
            var rightPane = new VisualElement { style = { backgroundColor = new Color(0.18f, 0.18f, 0.18f) } };

            // 資產選擇器列
            var selectorBar = new VisualElement { style = { flexDirection = FlexDirection.Row, height = 28, backgroundColor = new Color(0.2f, 0.2f, 0.2f), alignItems = Align.Center, paddingLeft = 10, paddingRight = 10 } };
            selectorBar.Add(new Label("Dodge Data") { style = { color = new Color(0.8f, 0.8f, 0.8f), marginRight = 5, unityFontStyleAndWeight = FontStyle.Bold } });
            var dataField = new ObjectField { objectType = typeof(DodgeData), allowSceneObjects = false, style = { flexGrow = 1 } };
            dataField.value = _currentData;
            dataField.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is DodgeData newData)
                {
                    FullResetPreview();
                    _currentData = newData;
                    _selectedEvent = null;
                    Selection.activeObject = newData;
                    RefreshAll();
                }
                else if (e.newValue == null)
                {
                    FullResetPreview();
                    _currentData = null;
                    _selectedEvent = null;
                    RefreshAll();
                }
            });
            _dataObjectField = dataField;
            selectorBar.Add(dataField);
            rightPane.Add(selectorBar);

            // 工具列
            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, height = 30, backgroundColor = new Color(0.25f, 0.25f, 0.25f), alignItems = Align.Center, paddingLeft = 10 } };
            toolbar.Add(new Button(TogglePlay) { text = "Play / Pause", style = { width = 100 } });
            toolbar.Add(new Button(() => SetTime(0)) { text = "Reset", style = { width = 60, marginLeft = 5 } });
            _timeLabel = new Label("Time: 0.00s") { style = { marginLeft = 20, color = Color.white } };
            toolbar.Add(_timeLabel);

            // 模式切換
            var modeLabel = new Label("Mode:") { style = { marginLeft = 30, color = new Color(0.8f, 0.8f, 0.8f) } };
            toolbar.Add(modeLabel);
            var dodgeModeBtn = new Button(() => { _isBackstepMode = false; RefreshAll(); })
            {
                text = "Dodge",
                style = { marginLeft = 5, backgroundColor = new Color(0.2f, 0.5f, 0.2f) }
            };
            var backstepModeBtn = new Button(() => { _isBackstepMode = true; RefreshAll(); })
            {
                text = "Backstep",
                style = { marginLeft = 2, backgroundColor = new Color(0.2f, 0.3f, 0.6f) }
            };
            toolbar.Add(dodgeModeBtn);
            toolbar.Add(backstepModeBtn);

            // 新增事件按鈕
            toolbar.Add(new Button(AddNewTimelineEvent) { text = "+ Timeline Event", style = { marginLeft = 20 } });
            rightPane.Add(toolbar);

            var ruler = new VisualElement { style = { height = 20, flexDirection = FlexDirection.Row } };
            rightPane.Add(ruler);

            _timelineScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _timelineContentArea = new VisualElement { style = { paddingTop = 10 } };
            _timelineContentArea.RegisterCallback<MouseDownEvent>(OnTimelineMouseDown);
            _timelineScrollView.Add(_timelineContentArea);
            _timelineContentArea.AddManipulator(new ScrubberManipulator(_timelineContentArea, 120f, SetTimeFromPixel));

            _scrubberOverlay = new VisualElement { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, width = 2f, top = 0, bottom = 0 } };
            _scrubberOverlay.Add(new VisualElement { style = { width = 10, height = 10, backgroundColor = Color.red, marginLeft = -4 } });
            _scrubberOverlay.Add(new VisualElement { style = { flexGrow = 1, width = 2, backgroundColor = Color.red } });
            _timelineContentArea.Add(_scrubberOverlay);
            rightPane.Add(_timelineScrollView);
            mainSplitView.Add(rightPane);

            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.actionKey && e.keyCode == KeyCode.Z)
            {
                if (e.shiftKey) Undo.PerformRedo();
                else Undo.PerformUndo();
                e.StopPropagation();
            }
            if (e.keyCode == KeyCode.Delete)
            {
                DeleteSelectedItem();
                e.StopPropagation();
            }
        }

        private void OnSelectionChanged()
        {
            if (Selection.activeObject != _currentData)
            {
                if (Selection.activeObject is DodgeData newData)
                {
                    FullResetPreview();
                    _currentData = newData;
                    _selectedEvent = null;
                    if (_dataObjectField != null) _dataObjectField.SetValueWithoutNotify(newData);
                    RefreshAll();
                }
            }
        }

        private void RefreshAll()
        {
            RefreshInspector();
            RefreshTimeline();
            RefreshEventInspector();
        }

        /// <summary>
        /// 取得當前預覽模式的動畫片段長度
        /// </summary>
        private float GetClipLength()
        {
            if (_currentData == null) return 1.0f;
            var clip = _currentData.GetPrimaryAnimationClip(_isBackstepMode);
            return clip != null ? clip.length : 1.0f;
        }

        #region Left Panel - Inspector

        private void RefreshInspector()
        {
            _inspectorContainer.Clear();
            if (_currentData == null) return;

            // 類型標籤
            Color typeLabelColor = new Color(0.3f, 0.8f, 0.5f);
            _inspectorContainer.Add(new Label("Dodge Data")
            {
                style =
                {
                    fontSize = 12,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = typeLabelColor,
                    marginBottom = 5,
                    paddingLeft = 5,
                    paddingTop = 3,
                    paddingBottom = 3,
                    backgroundColor = new Color(typeLabelColor.r * 0.2f, typeLabelColor.g * 0.2f, typeLabelColor.b * 0.2f, 0.5f)
                }
            });

            var so = new SerializedObject(_currentData);
            var prop = so.GetIterator();
            prop.NextVisible(true);

            // 時間定位按鈕的欄位
            var timingFieldNames = new HashSet<string>
            {
                "AllowInputTime", "AllowCancelTime", "SheatheCancelTime",
                "InvincibilityStartTime"
            };

            // 跳過在時間軸中編輯的列表
            var skipFields = new HashSet<string> { "DodgeTimelineEvents", "BackstepTimelineEvents" };

            while (prop.NextVisible(false))
            {
                if (skipFields.Contains(prop.name)) continue;

                var field = new PropertyField(prop);
                field.Bind(so);
                field.RegisterValueChangeCallback(_ => RefreshTimeline());

                if (timingFieldNames.Contains(prop.name))
                {
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                    field.style.flexGrow = 1;
                    row.Add(field);

                    string capturedName = prop.name;
                    var locatorBtn = new Button(() =>
                    {
                        var btnSo = new SerializedObject(_currentData);
                        var btnProp = btnSo.FindProperty(capturedName);
                        if (btnProp != null)
                        {
                            Undo.RecordObject(_currentData, $"Set {capturedName} to Current Time");
                            btnProp.floatValue = _currentTime;
                            btnSo.ApplyModifiedProperties();
                            EditorUtility.SetDirty(_currentData);
                            RefreshTimeline();
                        }
                    })
                    {
                        text = "\u25C9",
                        tooltip = "定位到時間軸當前時間",
                        style = { width = 22, height = 18, fontSize = 12, marginLeft = 2, paddingLeft = 0, paddingRight = 0 }
                    };
                    row.Add(locatorBtn);
                    _inspectorContainer.Add(row);
                }
                else
                {
                    _inspectorContainer.Add(field);
                }
            }
        }

        #endregion

        #region Timeline

        private void RefreshTimeline()
        {
            if (_isDragging) return;
            _clipMap.Clear();
            for (int i = _timelineContentArea.childCount - 1; i >= 0; i--)
            {
                var child = _timelineContentArea.ElementAt(i);
                if (child != _scrubberOverlay) _timelineContentArea.Remove(child);
            }
            if (_currentData == null) return;

            float clipLen = GetClipLength();
            float totalWidth = clipLen * PIXELS_PER_SECOND;
            _timelineContentArea.style.width = totalWidth + 200;

            // Timing Markers
            DrawTimingMarker("AllowInput", _currentData.AllowInputTime, new Color(0, 1, 0, 0.7f));
            DrawTimingMarker("AllowCancel", _currentData.AllowCancelTime, new Color(1, 1, 0, 0.7f));
            if (_currentData.SheatheCancelTime >= 0f)
            {
                DrawTimingMarker("Sheathe", _currentData.SheatheCancelTime, new Color(0.6f, 0.4f, 1f, 0.7f));
            }

            // 軌道 1: Dodge 動畫
            float dodgeClipLen = _currentData.DodgeClip != null && _currentData.DodgeClip.Clip != null
                ? _currentData.DodgeClip.Clip.length : 0f;
            if (dodgeClipLen > 0)
            {
                DrawTrack("Dodge Animation", new List<VisualElement> {
                    CreateClipVisual("Dodge", 0, dodgeClipLen, new Color(0.2f, 0.6f, 0.2f, 0.5f), null, null, false)
                });
            }

            // 軌道 2: Backstep 動畫
            float backstepClipLen = _currentData.BackstepClip != null && _currentData.BackstepClip.Clip != null
                ? _currentData.BackstepClip.Clip.length : 0f;
            if (backstepClipLen > 0)
            {
                DrawTrack("Backstep Animation", new List<VisualElement> {
                    CreateClipVisual("Backstep", 0, backstepClipLen, new Color(0.2f, 0.4f, 0.8f, 0.5f), null, null, false)
                });
            }

            // 軌道 3: 無敵窗口
            if (_currentData.InvincibilityDuration > 0)
            {
                float invStart = _currentData.InvincibilityStartTime;
                float invEnd = invStart + _currentData.InvincibilityDuration;
                var invClip = CreateClipVisual(
                    $"Invincible ({_currentData.InvincibilityDuration:F2}s)",
                    invStart, invEnd,
                    new Color(1f, 0.6f, 0f, 0.6f),
                    (s, e) =>
                    {
                        Undo.RecordObject(_currentData, "Edit Invincibility Window");
                        _currentData.InvincibilityStartTime = Mathf.Max(0, s);
                        _currentData.InvincibilityDuration = Mathf.Max(0.01f, e - s);
                        EditorUtility.SetDirty(_currentData);
                    }, null, true);
                DrawTrack("Invincibility", new List<VisualElement> { invClip });
            }

            // 軌道 4: Dodge 位移
            if (_currentData.DodgeDuration > 0)
            {
                var moveClip = CreateClipVisual(
                    $"Dodge ({_currentData.DodgeDistance:F1}m / {_currentData.DodgeDuration:F2}s)",
                    0, _currentData.DodgeDuration,
                    new Color(0.6f, 0.3f, 0.9f, 0.6f),
                    (s, e) =>
                    {
                        Undo.RecordObject(_currentData, "Edit Dodge Movement");
                        _currentData.DodgeDuration = Mathf.Max(0.05f, e - s);
                        EditorUtility.SetDirty(_currentData);
                    }, null, true);
                DrawTrack("Dodge Movement", new List<VisualElement> { moveClip });
            }

            // 軌道 5: Backstep 位移
            if (_currentData.BackstepDuration > 0)
            {
                var bsMoveClip = CreateClipVisual(
                    $"Backstep ({_currentData.BackstepDistance:F1}m / {_currentData.BackstepDuration:F2}s)",
                    0, _currentData.BackstepDuration,
                    new Color(0.5f, 0.3f, 0.7f, 0.4f),
                    (s, e) =>
                    {
                        Undo.RecordObject(_currentData, "Edit Backstep Movement");
                        _currentData.BackstepDuration = Mathf.Max(0.05f, e - s);
                        EditorUtility.SetDirty(_currentData);
                    }, null, true);
                DrawTrack("Backstep Movement", new List<VisualElement> { bsMoveClip });
            }

            // 軌道 6: Dodge 事件
            DrawEventTrack("Dodge Events", _currentData.DodgeTimelineEvents, new Color(0.2f, 0.5f, 0.8f), false);

            // 軌道 7: Backstep 事件
            DrawEventTrack("Backstep Events", _currentData.BackstepTimelineEvents, new Color(0.2f, 0.7f, 0.7f), true);

            _scrubberOverlay.BringToFront();
            UpdateScrubberUI();

            var rulerContainer = _timelineContentArea.parent?.parent?.Query<VisualElement>()
                .Where(x => x.parent != null && x.parent.style.flexDirection == FlexDirection.Row && x.style.height == 20).First();
            if (rulerContainer != null) DrawRuler(rulerContainer, clipLen);
        }

        private void DrawEventTrack(string trackName, List<TimelineEvent> events, Color color, bool isBackstep)
        {
            if (events == null) return;
            var clips = new List<VisualElement>();
            var laneMap = new Dictionary<VisualElement, Vector2>();
            foreach (var evt in events)
            {
                string evtName = string.IsNullOrEmpty(evt.Name) ? "Event" : evt.Name;
                float start = evt.TriggerTime;
                float end = evt.TriggerTime + 0.1f;
                var clip = CreateClipVisual(evtName, start, end, color,
                    (s, e) =>
                    {
                        Undo.RecordObject(_currentData, "Edit Timeline Event");
                        evt.TriggerTime = s;
                        if (_selectedEvent == evt) UpdateInspectorValues();
                        UpdatePreviewState(true);
                    }, () => { SelectEvent(evt); }, true);
                _clipMap[evt] = clip;
                SetClipSelectionStyle(clip, _selectedEvent == evt);
                clips.Add(clip);
                laneMap[clip] = new Vector2(start, end);
            }
            DrawTrack(trackName, clips, laneMap);
        }

        private void DrawTimingMarker(string label, float time, Color color)
        {
            if (time < 0) return;
            float xPos = 120 + (time * PIXELS_PER_SECOND);
            var marker = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = xPos,
                    width = 2,
                    height = Length.Percent(100),
                    backgroundColor = color,
                    opacity = 0.5f
                },
                pickingMode = PickingMode.Ignore
            };
            marker.Add(new Label(label) { style = { color = color, fontSize = 9, marginLeft = 4 } });
            _timelineContentArea.Add(marker);
        }

        private void DrawTrack(string name, List<VisualElement> clips, Dictionary<VisualElement, Vector2> timeRanges = null)
        {
            float trackHeight = BASE_TRACK_HEIGHT;
            if (timeRanges != null && timeRanges.Count > 0)
            {
                List<float> laneEndTimes = new();
                var sortedClips = clips.OrderBy(c => timeRanges[c].x).ToList();
                foreach (var clip in sortedClips)
                {
                    Vector2 range = timeRanges[clip];
                    int assignedLane = -1;
                    for (int i = 0; i < laneEndTimes.Count; i++)
                    {
                        if (laneEndTimes[i] <= range.x) { assignedLane = i; laneEndTimes[i] = range.y; break; }
                    }
                    if (assignedLane == -1) { assignedLane = laneEndTimes.Count; laneEndTimes.Add(range.y); }
                    clip.style.top = 5 + (assignedLane * LANE_HEIGHT);
                }
                if (laneEndTimes.Count > 1) trackHeight = 10 + (laneEndTimes.Count * LANE_HEIGHT);
            }
            var container = new VisualElement { style = { flexDirection = FlexDirection.Row, height = trackHeight, marginTop = 2, backgroundColor = new Color(0.22f, 0.22f, 0.22f) } };
            container.AddToClassList("track-container");
            container.Add(new Label(name) { style = { width = 120, paddingLeft = 5, paddingTop = 10, alignSelf = Align.FlexStart, color = new Color(0.8f, 0.8f, 0.8f) } });
            var bg = new VisualElement { style = { flexGrow = 1, position = Position.Relative, backgroundColor = new Color(0.15f, 0.15f, 0.15f) } };
            for (int i = 0; i <= 20; i++)
            {
                bg.Add(new VisualElement { style = { position = Position.Absolute, left = i * (PIXELS_PER_SECOND / 2f), width = 1, height = Length.Percent(100), backgroundColor = new Color(1, 1, 1, 0.1f) } });
            }
            foreach (var c in clips) bg.Add(c);
            container.Add(bg);
            _timelineContentArea.Add(container);
        }

        private VisualElement CreateClipVisual(string name, float start, float end, Color color, System.Action<float, float> onChange, System.Action onSelect, bool editable, System.Action<float, float> onComplete = null)
        {
            var clip = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = start * PIXELS_PER_SECOND,
                    width = (end - start) * PIXELS_PER_SECOND,
                    height = 24,
                    top = 5,
                    backgroundColor = color,
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.5f), borderBottomColor = new Color(1, 1, 1, 0.5f),
                    borderLeftColor = new Color(1, 1, 1, 0.5f), borderRightColor = new Color(1, 1, 1, 0.5f),
                    borderTopLeftRadius = 3, borderTopRightRadius = 3, borderBottomLeftRadius = 3, borderBottomRightRadius = 3
                }
            };
            clip.Add(new Label(name) { pickingMode = PickingMode.Ignore, style = { color = Color.black, fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter, flexGrow = 1, marginLeft = 12, marginRight = 12 } });
            if (editable)
            {
                clip.Add(new VisualElement
                {
                    name = "left-handle",
                    style = { position = Position.Absolute, left = 0, width = 8, height = Length.Percent(100), backgroundColor = new Color(1, 1, 1, 0.4f), borderTopLeftRadius = 3, borderBottomLeftRadius = 3 },
                    pickingMode = PickingMode.Ignore
                });
                clip.Add(new VisualElement
                {
                    name = "right-handle",
                    style = { position = Position.Absolute, right = 0, width = 8, height = Length.Percent(100), backgroundColor = new Color(1, 1, 1, 0.4f), borderTopRightRadius = 3, borderBottomRightRadius = 3 },
                    pickingMode = PickingMode.Ignore
                });
                System.Action<float, float> wrappedOnChange = (s, e) =>
                {
                    _isDragging = true;
                    onChange?.Invoke(s, e);
                };
                System.Action<float, float> wrappedOnComplete = (s, e) =>
                {
                    _isDragging = false;
                    onComplete?.Invoke(s, e);
                    RefreshTimeline();
                    RefreshInspector();
                };
                clip.AddManipulator(new UnifiedClipDragManipulator(clip, wrappedOnChange, onSelect, PIXELS_PER_SECOND, wrappedOnComplete));
            }
            return clip;
        }

        private void DrawRuler(VisualElement container, float maxTime)
        {
            container.Clear();
            container.Add(new VisualElement { style = { width = 120 } });
            var ruler = new VisualElement { style = { width = maxTime * PIXELS_PER_SECOND, position = Position.Relative, flexDirection = FlexDirection.Row } };
            for (float t = 0; t <= maxTime; t += 0.1f)
            {
                var tick = new Label($"{t:F1}") { style = { position = Position.Absolute, left = t * PIXELS_PER_SECOND, fontSize = 10, color = Color.gray } };
                ruler.Add(tick);
            }
            container.Add(ruler);
        }

        #endregion

        #region Selection & Event Inspector

        private void SelectEvent(TimelineEvent evt)
        {
            if (_selectedEvent == evt) return;
            var oldEvent = _selectedEvent;
            _selectedEvent = evt;
            if (oldEvent != null && _clipMap.TryGetValue(oldEvent, out var oldVis)) SetClipSelectionStyle(oldVis, false);
            if (evt != null && _clipMap.TryGetValue(evt, out var newVis)) SetClipSelectionStyle(newVis, true);
            RefreshEventInspector();
            RefreshTimeline();
            SceneView.RepaintAll();
        }

        private void SetClipSelectionStyle(VisualElement clip, bool selected)
        {
            Color c = selected ? Color.yellow : new Color(1, 1, 1, 0.5f);
            int w = selected ? 2 : 1;
            clip.style.borderTopColor = c; clip.style.borderBottomColor = c;
            clip.style.borderLeftColor = c; clip.style.borderRightColor = c;
            clip.style.borderTopWidth = w; clip.style.borderBottomWidth = w;
            clip.style.borderLeftWidth = w; clip.style.borderRightWidth = w;
        }

        private void RefreshEventInspector()
        {
            _eventInspectorContainer.Clear();
            _startTimeField = null;
            if (_currentData == null) return;

            if (_selectedEvent == null)
            {
                _eventInspectorContainer.Add(new Label("Select a timeline event to edit.") { style = { color = new Color(0.7f, 0.7f, 0.7f) } });
                return;
            }

            _startTimeField = new FloatField("Trigger Time") { value = _selectedEvent.TriggerTime };
            _startTimeField.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(_currentData, "Edit Time");
                _selectedEvent.TriggerTime = Mathf.Max(0, e.newValue);
                RefreshTimeline();
                UpdatePreviewState(true);
            });
            _eventInspectorContainer.Add(_startTimeField);

            // 判斷事件屬於哪個列表
            bool isInBackstep = _currentData.BackstepTimelineEvents != null && _currentData.BackstepTimelineEvents.Contains(_selectedEvent);
            string listPropName = isInBackstep ? "BackstepTimelineEvents" : "DodgeTimelineEvents";
            var list = isInBackstep ? _currentData.BackstepTimelineEvents : _currentData.DodgeTimelineEvents;

            var so = new SerializedObject(_currentData);
            var listProp = so.FindProperty(listPropName);
            int index = list.IndexOf(_selectedEvent);
            if (index < 0) return;

            var evtProp = listProp.GetArrayElementAtIndex(index);

            _eventInspectorContainer.Add(new Label("基本設定") { style = { marginTop = 5, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.8f, 0.8f, 1f) } });
            CreatePropField(evtProp, "Name", true);
            CreatePropField(evtProp, "CueTag");

            _eventInspectorContainer.Add(new Label("綁定設定") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.8f, 0.8f, 1f) } });
            CreatePropField(evtProp, "SocketName");
            CreatePropField(evtProp, "AttachToBody");
            CreatePropField(evtProp, "StopOnInterrupt");
            CreatePropField(evtProp, "InterruptBehavior");

            _eventInspectorContainer.Add(new Label("Transform 設定（傳遞給 Cue）") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(1f, 1f, 0.6f) } });
            CreatePropField(evtProp, "PositionOffset");
            CreatePropField(evtProp, "RotationOffset");
            CreatePropField(evtProp, "Scale");

            // VFX Cue 資訊
            DrawVFXCueInfo(_selectedEvent);
        }

        private void DrawVFXCueInfo(TimelineEvent evt)
        {
            var vfxCue = FindVFXCueByCueTag(evt.CueTag);
            if (vfxCue != null)
            {
                _eventInspectorContainer.Add(new Label("對應的 VFX Cue:") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.6f, 1f, 0.6f) } });
                var vfxCueField = new ObjectField("VFX Cue") { value = vfxCue, objectType = typeof(VFXCue), allowSceneObjects = false };
                vfxCueField.SetEnabled(false);
                _eventInspectorContainer.Add(vfxCueField);
                if (vfxCue.VFXPrefab != null)
                {
                    var prefabField = new ObjectField("VFX Prefab") { value = vfxCue.VFXPrefab, objectType = typeof(GameObject), allowSceneObjects = false };
                    prefabField.SetEnabled(false);
                    _eventInspectorContainer.Add(prefabField);
                }
            }
            else if (evt.CueTag.IsValid)
            {
                _eventInspectorContainer.Add(new Label($"! 找不到對應的 VFX Cue: {evt.CueTag.TagName}")
                {
                    style =
                    {
                        marginTop = 10, fontSize = 9, color = new Color(1f, 0.5f, 0f),
                        whiteSpace = WhiteSpace.Normal,
                        backgroundColor = new Color(0.3f, 0.15f, 0f, 0.3f),
                        paddingLeft = 5, paddingTop = 5, paddingBottom = 5, paddingRight = 5,
                        borderTopWidth = 1, borderTopColor = new Color(1f, 0.5f, 0f)
                    }
                });
            }
        }

        private void UpdateInspectorValues()
        {
            if (_selectedEvent != null)
            {
                _startTimeField?.SetValueWithoutNotify(_selectedEvent.TriggerTime);
            }
        }

        private void CreatePropField(SerializedProperty root, string relName, bool refreshTimeline = false, System.Action onValueChange = null)
        {
            var p = root.FindPropertyRelative(relName);
            if (p != null)
            {
                var f = new PropertyField(p);
                f.Bind(root.serializedObject);
                f.RegisterValueChangeCallback(_ =>
                {
                    if (refreshTimeline) RefreshTimeline();
                    onValueChange?.Invoke();
                    // Inspector 改 Position / Rotation / Scale / Axes 等任何欄位 → 主動跑一次 UpdatePreviewState,
                    // 內部會把當下 evt 數值 EditorSync 到 follower,VFX 立即反映新值。
                    UpdatePreviewState(true);
                });
                _eventInspectorContainer.Add(f);
            }
        }

        #endregion

        #region Add/Delete

        private void AddNewTimelineEvent()
        {
            if (_currentData == null) return;
            Undo.RecordObject(_currentData, "Add Timeline Event");
            var newEvent = new TimelineEvent
            {
                Name = "New Event",
                TriggerTime = _currentTime,
                Scale = Vector3.one
            };
            // 根據當前模式加入對應列表
            if (_isBackstepMode)
            {
                _currentData.BackstepTimelineEvents ??= new List<TimelineEvent>();
                _currentData.BackstepTimelineEvents.Add(newEvent);
            }
            else
            {
                _currentData.DodgeTimelineEvents ??= new List<TimelineEvent>();
                _currentData.DodgeTimelineEvents.Add(newEvent);
            }
            EditorUtility.SetDirty(_currentData);
            RefreshTimeline();
            SelectEvent(newEvent);
        }

        private void DeleteSelectedItem()
        {
            if (_currentData == null || _selectedEvent == null) return;
            Undo.RecordObject(_currentData, "Delete Timeline Event");
            // 嘗試從兩個列表中移除
            _currentData.DodgeTimelineEvents?.Remove(_selectedEvent);
            _currentData.BackstepTimelineEvents?.Remove(_selectedEvent);
            _selectedEvent = null;
            EditorUtility.SetDirty(_currentData);
            RefreshAll();
        }

        #endregion

        #region Playback

        private void TogglePlay()
        {
            _isPlaying = !_isPlaying;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            if (!_isPlaying) UpdatePreviewState(true);
        }

        private void SetTime(float t)
        {
            _currentTime = t;
            UpdateScrubberUI();
            UpdatePreviewState(true);
        }

        private void SetTimeFromPixel(float pixelX)
        {
            float t = pixelX / PIXELS_PER_SECOND;
            float maxTime = GetClipLength();
            SetTime(Mathf.Clamp(t, 0, maxTime));
        }

        private void OnTimelineMouseDown(MouseDownEvent e)
        {
            if (e.button == 0)
            {
                float x = e.localMousePosition.x - 120f;
                if (x < 0) x = 0;
                SetTimeFromPixel(x);
                e.StopPropagation();
            }
        }

        private void OnEditorUpdate()
        {
            if (_isPlaying)
            {
                double currentTime = EditorApplication.timeSinceStartup;
                double dt = currentTime - _lastEditorTime;
                _lastEditorTime = currentTime;
                float clipLen = GetClipLength();
                _currentTime += (float)dt;
                if (_currentTime > clipLen)
                {
                    _currentTime = 0f;
                    FullResetPreview();
                }
                UpdateScrubberUI();
                UpdatePreviewState(false, (float)dt);
                Repaint();
                SceneView.RepaintAll();
            }
        }

        private void UpdateScrubberUI()
        {
            if (_scrubberOverlay != null && _timeLabel != null)
            {
                float x = 120 + (_currentTime * PIXELS_PER_SECOND);
                _scrubberOverlay.style.left = x;
                _timeLabel.text = $"Time: {_currentTime:F2}s";
            }
        }

        #endregion

        #region Preview

        private void UpdatePreviewState(bool isScrubbing, float deltaTime = 0f, bool forceSyncEvt = false)
        {
            if (_currentData == null) return;
            var primaryClip = _currentData.GetPrimaryAnimationClip(_isBackstepMode);
            if (primaryClip == null) return;

            if (_previewTarget == null || _animationTarget == null)
            {
                var playerObj = GameObject.FindGameObjectWithTag("Player");
                _previewTarget = playerObj != null
                    ? playerObj.GetComponent<AbilitySystemComponent>()
                    : FindFirstObjectByType<AbilitySystemComponent>();
                if (_previewTarget != null)
                {
                    _initialPos = _previewTarget.transform.position;
                    _initialRot = _previewTarget.transform.rotation;
                    var animator = _previewTarget.GetComponentInChildren<Animator>();
                    _animationTarget = animator != null ? animator.gameObject : _previewTarget.gameObject;
                }
            }
            if (_previewTarget == null || _animationTarget == null) return;

            if (!_isPreviewing)
            {
                _isPreviewing = true;
                AnimationMode.StartAnimationMode();
            }

            // 計算位移預覽
            float duration = _currentData.GetDuration(_isBackstepMode);
            float distance = _currentData.GetDistance(_isBackstepMode);
            AnimationCurve curve = _currentData.GetCurve(_isBackstepMode);

            Vector3 cumulativeOffset = Vector3.zero;
            if (duration > 0 && curve != null)
            {
                float progress = Mathf.Clamp01(_currentTime / duration);
                float val = curve.Evaluate(progress);
                // 前衝閃避沿前方，後撤沿後方
                Vector3 dir = _isBackstepMode ? Vector3.back : Vector3.forward;
                cumulativeOffset = dir * (distance * val);
            }

            Vector3 targetPos = _initialPos + (_previewTarget.transform.rotation * cumulativeOffset);
            primaryClip.SampleAnimation(_animationTarget, _currentTime);
            _previewTarget.transform.position = targetPos;

            UpdatePreviewVFX(isScrubbing, deltaTime, forceSyncEvt);

            // 強制驅動 player loop + 重繪 Scene — 拖時間軸時 follower 的 LateUpdate(ExecuteAlways)
            // 需要 player loop tick 才會跑,沒這行光靠 RepaintAll 仍可能看不到 VFX 即時跟隨。
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private void UpdatePreviewVFX(bool isScrubbing, float deltaTime, bool forceSyncEvt = false)
        {
            if (_previewTarget == null) return;
            var events = _currentData.GetTimelineEvents(_isBackstepMode);
            if (events == null) return;
            foreach (var evt in events)
            {
                float previewDuration = 2.0f;
                bool shouldBeActive = _currentTime >= evt.TriggerTime && _currentTime <= evt.TriggerTime + previewDuration;
                if (shouldBeActive)
                {
                    bool isNewInstance = false;
                    if (!_previewVFXs.ContainsKey(evt) || _previewVFXs[evt] == null)
                    {
                        var vfxCue = FindVFXCueByCueTag(evt.CueTag);
                        GameObject prefab = vfxCue?.VFXPrefab;
                        if (prefab != null)
                        {
                            Transform spawnSocket = FindChildRecursive(_previewTarget.transform, evt.SocketName);
                            if (spawnSocket == null) spawnSocket = _previewTarget.transform;
                            Vector3 spawnPos = spawnSocket.TransformPoint(evt.PositionOffset);
                            Quaternion spawnRot = spawnSocket.rotation * Quaternion.Euler(evt.RotationOffset);

                            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                            inst.hideFlags = HideFlags.HideAndDontSave;
                            inst.transform.SetPositionAndRotation(spawnPos, spawnRot);
                            _previewVFXs[evt] = inst;
                            isNewInstance = true;

                            var follower = inst.AddComponent<GAS.TimelineVFXFollower>();
                            follower.Setup(spawnSocket, evt.Axes, evt.PositionOffset, evt.RotationOffset, evt.Scale);

                            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>())
                            {
                                ps.Clear();
                            }
                        }
                        else
                        {
                            var previewObj = new GameObject($"Preview_{evt.Name}_NoPrefab");
                            previewObj.hideFlags = HideFlags.HideAndDontSave;
                            _previewVFXs[evt] = previewObj;
                        }
                    }
                    var previewInstance = _previewVFXs[evt];
                    if (previewInstance == null) continue;
                    Transform syncSocket = FindChildRecursive(_previewTarget.transform, evt.SocketName);
                    if (syncSocket == null) syncSocket = _previewTarget.transform;
                    // forceSyncEvt=true(Inspector 改 PositionOffset/Axes 等)→ EditorSync 重算 initial,VFX 立刻反映新值;
                    // forceSyncEvt=false(拖時間軸 / 一般 update)→ Sample 維持 initial,保留 Axes=None 凍結等差異。
                    var existingFollower = previewInstance.GetComponent<GAS.TimelineVFXFollower>();
                    if (existingFollower != null)
                    {
                        if (forceSyncEvt) existingFollower.EditorSync(syncSocket, evt.Axes, evt.PositionOffset, evt.RotationOffset, evt.Scale);
                        else existingFollower.Sample();
                    }
                    var particleSystems = previewInstance.GetComponentsInChildren<ParticleSystem>();
                    if (particleSystems.Length > 0)
                    {
                        foreach (var ps in particleSystems)
                        {
                            var main = ps.main;
                            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                        }
                        if (isScrubbing)
                        {
                            float localTime = _currentTime - evt.TriggerTime;
                            foreach (var ps in particleSystems)
                            {
                                ps.Simulate(localTime, true, true);
                            }
                        }
                        else
                        {
                            if (isNewInstance)
                            {
                                float localTime = _currentTime - evt.TriggerTime;
                                if (localTime < 0) localTime = 0;
                                foreach (var ps in particleSystems)
                                {
                                    ps.Simulate(localTime, true, true);
                                }
                            }
                            else
                            {
                                foreach (var ps in particleSystems)
                                {
                                    ps.Simulate(deltaTime, true, false);
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (_previewVFXs.ContainsKey(evt) && _previewVFXs[evt] != null)
                    {
                        DestroyImmediate(_previewVFXs[evt]);
                        _previewVFXs.Remove(evt);
                    }
                }
            }
        }

        private void FullResetPreview()
        {
            _isPreviewing = false;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            if (_previewTarget != null)
            {
                _previewTarget.transform.position = _initialPos;
                _previewTarget.transform.rotation = _initialRot;
            }
            foreach (var kvp in _previewVFXs)
            {
                if (kvp.Value != null) DestroyImmediate(kvp.Value);
            }
            _previewVFXs.Clear();
        }

        #endregion

        #region Scene GUI

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_currentData == null || _previewTarget == null) return;

            // 無敵窗口可視化
            float invStart = _currentData.InvincibilityStartTime;
            float invEnd = invStart + _currentData.InvincibilityDuration;
            bool isInvincible = _currentTime >= invStart && _currentTime <= invEnd;

            if (isInvincible)
            {
                Handles.color = new Color(1f, 0.6f, 0f, 0.5f);
                Vector3 center = _previewTarget.transform.position + Vector3.up * 1f;
                // 繪製膠囊線框表示無敵狀態
                Handles.DrawWireDisc(center + Vector3.up * 0.5f, Vector3.up, 0.5f);
                Handles.DrawWireDisc(center - Vector3.up * 0.5f, Vector3.up, 0.5f);
                Handles.DrawWireDisc(center, Vector3.forward, 1f);
                Handles.DrawWireDisc(center, Vector3.right, 1f);
                Handles.Label(center + Vector3.up * 1.5f, "INVINCIBLE", new GUIStyle
                {
                    normal = { textColor = new Color(1f, 0.6f, 0f) },
                    fontSize = 14,
                    fontStyle = UnityEngine.FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                });
            }

            // Timeline Event 可視化
            var events = _currentData.GetTimelineEvents(_isBackstepMode);
            if (events == null) return;

            foreach (var evt in events)
            {
                bool isSelected = evt == _selectedEvent;
                bool isActive = _currentTime >= evt.TriggerTime && _currentTime <= evt.TriggerTime + 2.0f;
                if (!isSelected && !isActive) continue;

                Transform socket = FindChildRecursive(_previewTarget.transform, evt.SocketName);
                if (socket == null) socket = _previewTarget.transform;

                Vector3 worldPos = socket.TransformPoint(evt.PositionOffset);
                Quaternion worldRot = socket.rotation * Quaternion.Euler(evt.RotationOffset);

                Handles.color = isSelected ? Color.yellow : (isActive ? Color.cyan : new Color(0.5f, 0.5f, 0.5f, 0.5f));
                Handles.ArrowHandleCap(0, worldPos, worldRot, 0.3f, EventType.Repaint);
                Handles.DrawWireCube(worldPos, evt.Scale * 0.2f);
                Handles.Label(worldPos + Vector3.up * 0.3f, string.IsNullOrEmpty(evt.Name) ? "Event" : evt.Name);

                if (isSelected)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPos = Handles.PositionHandle(worldPos, worldRot);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_currentData, "Move Event");
                        evt.PositionOffset = socket.InverseTransformPoint(newPos);
                        EditorUtility.SetDirty(_currentData);
                        RefreshEventInspector();
                    }
                }
            }
        }

        #endregion

        #region Utilities

        private Transform FindChildRecursive(Transform parent, string childName)
        {
            if (string.IsNullOrEmpty(childName)) return null;
            Transform result = parent.Find(childName);
            if (result != null) return result;
            foreach (Transform child in parent)
            {
                result = FindChildRecursive(child, childName);
                if (result != null) return result;
            }
            return null;
        }

        private VFXCue FindVFXCueByCueTag(GameplayTag tag)
        {
            if (!tag.IsValid) return null;
            string key = tag.TagName;
            if (_vfxCueCache.TryGetValue(key, out var cached)) return cached;
            var guids = AssetDatabase.FindAssets("t:VFXCue");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var cue = AssetDatabase.LoadAssetAtPath<VFXCue>(path);
                if (cue != null && cue.CueTag.TagName == key)
                {
                    _vfxCueCache[key] = cue;
                    return cue;
                }
            }
            _vfxCueCache[key] = null;
            return null;
        }

        #endregion
    }
}
#endif
