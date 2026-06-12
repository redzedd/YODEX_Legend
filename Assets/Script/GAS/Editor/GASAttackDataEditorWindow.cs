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
    /// GAS 攻擊數據可視化編輯器
    /// 提供時間軸編輯、動畫預覽、Hitbox/VFX 可視化
    /// 同時支援 MeleeAttackData 和 RangedAttackData
    /// </summary>
    public class GASAttackDataEditorWindow : EditorWindow
    {
        private AttackDataBase _currentData;
        private TimelineEvent _selectedEvent;
        private MeleeHitWindow _selectedHitWindow;

        private VisualElement _timelineContentArea;
        private VisualElement _scrubberOverlay;
        private VisualElement _inspectorContainer;
        private VisualElement _eventInspectorContainer;
        private ScrollView _timelineScrollView;
        private Label _timeLabel;
        private ObjectField _dataObjectField;

        private FloatField _startTimeField;
        private FloatField _endTimeField;

        private float _currentTime = 0f;
        private bool _isPlaying = false;
        private double _lastEditorTime;

        // 預覽階段(蓄力模式專用):決定 Play/Pause 與時間軸刷新時取樣哪一段動畫
        // 預設 Fire,代表「Charge Fire」階段(發射動畫);可切到 ChargeStart / ChargeLoop 預覽蓄力動作
        private TimelineEventPhase _previewPhase = TimelineEventPhase.Fire;
        private VisualElement _previewPhaseSelectorContainer;

        private readonly Dictionary<TimelineEvent, GameObject> _previewVFXs = new();
        private readonly Dictionary<object, VisualElement> _clipMap = new();
        private bool _isDragging;

        private AbilitySystemComponent _previewTarget;
        private GameObject _animationTarget; // 實際播放動畫的 GameObject（含 Animator 的子物件）
        private Vector3 _initialPos;
        private Quaternion _initialRot;
        private bool _isPreviewing = false;

        // Humanoid Root Motion 取樣器(封裝 PlayableGraph)
        private HumanoidRootMotionSampler _rmSampler;
        private AnimationClip _cachedClip;
        private Animator _cachedAnimator;

        // VFX Cue 查找緩存(Tag → Cue)。null value 表示 "已掃過但無對應 Cue",避免重複掃。
        private Dictionary<string, VFXCue> _vfxCueCache = new();
        // 是否已執行一次全專案 VFXCue 掃描。設為 false → 下次 FindVFXCueByCueTag 會重建 cache。
        private bool _vfxCueCacheBuilt;

        // 投射物預覽（多發支援）
        private readonly List<ProjectilePreviewData> _previewProjectiles = new();

        // 發射特效預覽（FireCueTag 對應的 VFX）
        private readonly List<GameObject> _previewFireCueVFXs = new();

        // AoE 預覽:整段生命週期共用同一個 AoE Prefab instance(內部 _indicatorRoot/_effectRoot 由 runtime 切換,編輯器只負責顯示在不在)
        private GameObject _previewAoEEffectInstance;
        private GameObject _previewAoEIndicatorInstance; // legacy 防衛性清理用,目前不會被賦值

        private class ProjectilePreviewData
        {
            public GameObject Instance;
            public Vector3 SpawnPos;
            public Vector3 FireDir;
            public float Speed;
            public float Gravity;
            public float FireTime;
            public ParticleSystem[] ParticleSystems;
        }

        private const float PIXELS_PER_SECOND = 400f;
        private const float BASE_TRACK_HEIGHT = 40f;
        private const float LANE_HEIGHT = 26f;
        private const float LEFT_PANEL_WIDTH = 350f;

        // 類型快取
        private MeleeAttackData CurrentMelee => _currentData as MeleeAttackData;
        private RangedAttackData CurrentRanged => _currentData as RangedAttackData;
        private bool IsMelee => _currentData is MeleeAttackData;
        private bool IsRanged => _currentData is RangedAttackData;

        [MenuItem("GAS/Attack Data Editor")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<GASAttackDataEditorWindow>();
            wnd.titleContent = new GUIContent("GAS Attack Editor");
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

            // 左側面板 - 使用垂直分割視圖分成上下兩個區域
            var leftPane = new VisualElement { style = { backgroundColor = new Color(0.22f, 0.22f, 0.22f) } };

            // 創建垂直分割視圖（上：基本設定，下：選中元素設定）
            var leftSplitView = new TwoPaneSplitView(1, 200, TwoPaneSplitViewOrientation.Vertical);
            leftSplitView.style.flexGrow = 1;

            // 上半部：Attack Data Settings
            var topSection = new VisualElement { style = { backgroundColor = new Color(0.22f, 0.22f, 0.22f) } };
            topSection.Add(new Label("Attack Data Settings")
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

            // 下半部：Selected Element Settings
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
            selectorBar.Add(new Label("Attack Data") { style = { color = new Color(0.8f, 0.8f, 0.8f), marginRight = 5, unityFontStyleAndWeight = FontStyle.Bold } });
            var dataField = new ObjectField { objectType = typeof(AttackDataBase), allowSceneObjects = false, style = { flexGrow = 1 } };
            dataField.value = _currentData;
            dataField.RegisterValueChangedCallback(e =>
            {
                if (e.newValue is AttackDataBase newData)
                {
                    FullResetPreview();
                    _currentData = newData;
                    _selectedEvent = null;
                    _selectedHitWindow = null;
                    Selection.activeObject = newData;
                    RefreshAll();
                }
                else if (e.newValue == null)
                {
                    FullResetPreview();
                    _currentData = null;
                    _selectedEvent = null;
                    _selectedHitWindow = null;
                    RefreshAll();
                }
            });
            _dataObjectField = dataField;
            selectorBar.Add(dataField);
            rightPane.Add(selectorBar);

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, height = 30, backgroundColor = new Color(0.25f, 0.25f, 0.25f), alignItems = Align.Center, paddingLeft = 10 } };
            toolbar.Add(new Button(TogglePlay) { text = "Play / Pause", style = { width = 100 } });
            toolbar.Add(new Button(() => SetTime(0)) { text = "Reset", style = { width = 60, marginLeft = 5 } });
            _timeLabel = new Label("Time: 0.00s") { style = { marginLeft = 20, color = Color.white } };
            toolbar.Add(_timeLabel);

            // 預覽階段選擇器(蓄力模式專用,動態顯示/隱藏)
            _previewPhaseSelectorContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 20 } };
            _previewPhaseSelectorContainer.Add(new Label("Preview:") { style = { color = Color.white, marginRight = 4 } });
            var phaseDropdown = new EnumField(TimelineEventPhase.Fire) { style = { width = 110 } };
            phaseDropdown.RegisterValueChangedCallback(evt =>
            {
                _previewPhase = (TimelineEventPhase)evt.newValue;
                _currentTime = 0f;
                _isPlaying = false;
                // 動畫片段變了 → 強制重建 sampler 並重畫時間軸
                _cachedClip = null;
                RefreshTimeline();
                UpdatePreviewState(true);
            });
            _previewPhaseSelectorContainer.Add(phaseDropdown);
            toolbar.Add(_previewPhaseSelectorContainer);

            // 添加新事件按鈕
            toolbar.Add(new Button(AddNewHitWindow) { text = "+ Hit Window", style = { marginLeft = 20 } });
            toolbar.Add(new Button(ShowAddTimelineEventMenu) { text = "+ Timeline Event ▾", style = { marginLeft = 5 } });

            rightPane.Add(toolbar);

            var ruler = new VisualElement { style = { height = 20, flexDirection = FlexDirection.Row } };
            rightPane.Add(ruler);

            _timelineScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _timelineContentArea = new VisualElement { style = { paddingTop = 10 } };
            _timelineContentArea.RegisterCallback<MouseDownEvent>(OnTimelineMouseDown);
            _timelineScrollView.Add(_timelineContentArea);

            _timelineContentArea.AddManipulator(new ScrubberManipulator(_timelineContentArea, 120f, SetTimeFromPixel));

            _scrubberOverlay = new VisualElement { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, width = 2f, top = 0, bottom = 0 } };
            _scrubberOverlay.Add(new VisualElement
            {
                style = { width = 10, height = 10, backgroundColor = Color.red, marginLeft = -4 }
            });
            _scrubberOverlay.Add(new VisualElement
            {
                style = { flexGrow = 1, width = 2, backgroundColor = Color.red }
            });
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
                if (e.shiftKey)
                    Undo.PerformRedo();
                else
                    Undo.PerformUndo();
                e.StopPropagation();
            }

            // Delete selected item
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
                if (Selection.activeObject is AttackDataBase newData)
                {
                    FullResetPreview();
                    _currentData = newData;
                    _selectedEvent = null;
                    _selectedHitWindow = null;
                    if (_dataObjectField != null) _dataObjectField.SetValueWithoutNotify(newData);
                    RefreshAll();
                }
            }
        }

        [MenuItem("GAS/Tools/Clear VFX Cue Cache")]
        private static void ClearVFXCueCache()
        {
            var window = GetWindow<GASAttackDataEditorWindow>(false);
            if (window != null)
            {
                window.InvalidateVFXCueCache();
                Debug.Log("[GAS Attack Editor] VFX Cue cache cleared.");
            }
        }

        private void RefreshAll()
        {
            RefreshInspector();
            RefreshTimeline();
            RefreshEventInspector();
        }

        /// <summary>
        /// 取得當前要預覽的動畫片段(根據 _previewPhase 決定)
        /// 蓄力模式:依階段選 ChargeStart / ChargeLoop / ChargeFire(回退 FireAnimation)
        /// 非蓄力模式:沿用 GetPrimaryAnimationClip
        /// </summary>
        private AnimationClip GetPreviewClip()
        {
            if (_currentData == null) return null;
            if (IsRanged && CurrentRanged.Charge != ChargeMode.None)
            {
                switch (_previewPhase)
                {
                    case TimelineEventPhase.ChargeStart:
                        return CurrentRanged.ChargeStartAnimation?.Clip;
                    case TimelineEventPhase.ChargeLoop:
                        return CurrentRanged.ChargeLoopAnimation?.Clip;
                    case TimelineEventPhase.Fire:
                    default:
                        return CurrentRanged.ChargeFireAnimation?.Clip
                            ?? CurrentRanged.FireAnimation?.Clip;
                }
            }
            return _currentData.GetPrimaryAnimationClip();
        }

        /// <summary>
        /// 取得當前預覽動畫片段長度
        /// </summary>
        private float GetClipLength()
        {
            var clip = GetPreviewClip();
            return clip != null ? clip.length : 1.0f;
        }

        private void RefreshInspector()
        {
            _inspectorContainer.Clear();
            if (_currentData == null) return;

            // 顯示類型標籤
            string typeLabel = IsMelee ? "Melee Attack Data" : "Ranged Attack Data";
            Color typeLabelColor = IsMelee ? new Color(0.9f, 0.4f, 0.3f) : new Color(0.3f, 0.7f, 0.9f);
            var typeLabelEl = new Label(typeLabel)
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
            };
            _inspectorContainer.Add(typeLabelEl);

            var so = new SerializedObject(_currentData);
            var prop = so.GetIterator();
            prop.NextVisible(true);

            // 需要時間定位按鈕的欄位名稱
            var timingFieldNames = new HashSet<string>
            {
                "FireTime", "AllowInputTime", "ComboResetTime", "AllowCancelTime", "SheatheCancelTime",
                "MinChargeTime", "MaxChargeTime"
            };

            while (prop.NextVisible(false))
            {
                // 跳過時間軸相關的列表，因為它們在時間軸中編輯
                if (prop.name == "TimelineEvents") continue;
                // 近戰的 HitWindows 也在時間軸中編輯
                if (prop.name == "HitWindows" && IsMelee) continue;

                var field = new PropertyField(prop);
                field.Bind(so);

                field.RegisterValueChangeCallback(evt =>
                {
                    RefreshTimeline();
                });

                // 時間相關欄位：加入「定位到當前時間」按鈕
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
                        style =
                        {
                            width = 22,
                            height = 18,
                            fontSize = 12,
                            marginLeft = 2,
                            paddingLeft = 0,
                            paddingRight = 0
                        }
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

            // 蓄力模式 (HoldToCharge / HoldToAim) → 三個獨立階段時間軸
            // QuickFire / 近戰 → 單一動畫時間軸 + 共用事件軌
            bool isChargeMode = IsRanged && CurrentRanged.Charge != ChargeMode.None;

            // 只有蓄力模式才顯示 Phase 預覽選擇器
            if (_previewPhaseSelectorContainer != null)
            {
                _previewPhaseSelectorContainer.style.display = isChargeMode
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
            // 非蓄力模式強制 reset 到 Fire phase,避免殘留切換狀態
            if (!isChargeMode && _previewPhase != TimelineEventPhase.Fire)
            {
                _previewPhase = TimelineEventPhase.Fire;
                _cachedClip = null;
            }

            // Timing 參考線 — 蓄力模式時不畫成全域 overlay,改成只在 Fire row 內顯示(見下方 DrawTrack 之後)
            // 非蓄力模式維持舊行為:畫成跨所有 row 的全 height 標線
            if (!isChargeMode)
            {
                DrawTimingMarker("Input", _currentData.AllowInputTime, new Color(0, 1, 0, 0.7f));
                DrawTimingMarker("Cancel", _currentData.AllowCancelTime, new Color(1, 1, 0, 0.7f));
                DrawTimingMarker("Reset", _currentData.ComboResetTime, new Color(1, 0, 0, 0.7f));
                if (_currentData.SheatheCancelTime >= 0f)
                {
                    DrawTimingMarker("Sheathe", _currentData.SheatheCancelTime, new Color(0.6f, 0.4f, 1f, 0.7f));
                }
                if (IsRanged)
                {
                    DrawTimingMarker("Fire", CurrentRanged.FireTime, new Color(1, 0.6f, 0, 0.9f));
                }
            }

            if (isChargeMode)
            {
                // === Charge Start 階段 ===
                float startLen = CurrentRanged.ChargeStartAnimation?.Clip != null
                    ? CurrentRanged.ChargeStartAnimation.Clip.length : 0f;
                if (startLen > 0f)
                {
                    var startClips = new List<VisualElement>();
                    var startLaneMap = new Dictionary<VisualElement, Vector2>();
                    startClips.Add(CreateClipVisual("Charge Start", 0, startLen,
                        new Color(0.6f, 0.4f, 0.8f, 0.5f), null, null, false));
                    AddPhaseEventClips(startClips, startLaneMap, TimelineEventPhase.ChargeStart);
                    DrawTrack("Charge Start", startClips, startLaneMap);
                }

                // === Charge Loop 階段 ===
                // Loop 動畫長度當作時間軸長度(若沒設動畫則用 1 秒當預設顯示寬)
                float loopLen = CurrentRanged.ChargeLoopAnimation?.Clip != null
                    ? CurrentRanged.ChargeLoopAnimation.Clip.length : 1f;
                var loopClips = new List<VisualElement>();
                var loopLaneMap = new Dictionary<VisualElement, Vector2>();
                loopClips.Add(CreateClipVisual("Charge Loop", 0, loopLen,
                    new Color(0.5f, 0.3f, 0.7f, 0.5f), null, null, false));
                AddPhaseEventClips(loopClips, loopLaneMap, TimelineEventPhase.ChargeLoop);
                DrawTrack("Charge Loop", loopClips, loopLaneMap);

                // === Charge Fire (發射) 階段 ===
                // 優先用 ChargeFireAnimation,沒設則 fallback 到 FireAnimation
                AnimationClip fireClip = CurrentRanged.ChargeFireAnimation?.Clip != null
                    ? CurrentRanged.ChargeFireAnimation.Clip
                    : CurrentRanged.FireAnimation?.Clip;
                float fireLen = fireClip != null ? fireClip.length : clipLen;
                var fireClips = new List<VisualElement>();
                var fireLaneMap = new Dictionary<VisualElement, Vector2>();
                fireClips.Add(CreateClipVisual("Charge Fire", 0, fireLen,
                    new Color(0.2f, 0.5f, 0.8f, 0.5f), null, null, false));
                AddPhaseEventClips(fireClips, fireLaneMap, TimelineEventPhase.Fire);
                var fireBg = DrawTrack("Charge Fire", fireClips, fireLaneMap);
                // Timing 參考線只畫在 Fire row 內(因為 runtime 也只在 Fire 階段檢查這些 timing)
                DrawTimingMarker("Input", _currentData.AllowInputTime, new Color(0, 1, 0, 0.7f), fireBg);
                DrawTimingMarker("Cancel", _currentData.AllowCancelTime, new Color(1, 1, 0, 0.7f), fireBg);
                DrawTimingMarker("Reset", _currentData.ComboResetTime, new Color(1, 0, 0, 0.7f), fireBg);
                if (_currentData.SheatheCancelTime >= 0f)
                {
                    DrawTimingMarker("Sheathe", _currentData.SheatheCancelTime, new Color(0.6f, 0.4f, 1f, 0.7f), fireBg);
                }
                DrawTimingMarker("Fire", CurrentRanged.FireTime, new Color(1, 0.6f, 0, 0.9f), fireBg);
            }
            else
            {
                // QuickFire / 近戰 — 維持原本單一時間軸 + 全部事件混在一起
                Color animColor = IsMelee ? new Color(0.2f, 0.6f, 0.2f, 0.5f) : new Color(0.2f, 0.5f, 0.8f, 0.5f);
                string animLabel = IsMelee ? "Animation" : "Fire Animation";
                DrawTrack("Animation Base", new List<VisualElement> {
                    CreateClipVisual(animLabel, 0, clipLen, animColor, null, null, false)
                });
            }

            // Hit Windows 軌道（僅近戰）
            if (IsMelee && CurrentMelee.HitWindows != null)
            {
                var clips = new List<VisualElement>();
                var laneMap = new Dictionary<VisualElement, Vector2>();
                for (int i = 0; i < CurrentMelee.HitWindows.Count; i++)
                {
                    var hw = CurrentMelee.HitWindows[i];
                    var clip = CreateClipVisual($"HitBox {i}", hw.StartTime, hw.EndTime, new Color(0.8f, 0.2f, 0.2f),
                        (s, e) => {
                            Undo.RecordObject(_currentData, "Edit HitWindow");
                            hw.StartTime = s; hw.EndTime = e;
                            if (_selectedHitWindow == hw) UpdateInspectorValues();
                            UpdatePreviewState(true);
                        }, () => { SelectHitWindow(hw); }, true);
                    clips.Add(clip);
                    laneMap[clip] = new Vector2(hw.StartTime, hw.EndTime);
                    _clipMap[hw] = clip;
                    SetClipSelectionStyle(clip, _selectedHitWindow == hw);
                }
                DrawTrack("Hit Windows", clips, laneMap);
            }

            // 非蓄力模式 → Timeline Events 共用軌(向後相容)
            if (!isChargeMode && _currentData.TimelineEvents != null)
            {
                var clips = new List<VisualElement>();
                var laneMap = new Dictionary<VisualElement, Vector2>();
                AddPhaseEventClips(clips, laneMap, TimelineEventPhase.Fire);
                if (clips.Count > 0)
                {
                    DrawTrack("Timeline Events", clips, laneMap);
                }
            }

            // 遠程攻擊位移軌道
            if (IsRanged && CurrentRanged.AttackMovements != null && CurrentRanged.AttackMovements.Count > 0)
            {
                var moveClips = new List<VisualElement>();
                var moveLaneMap = new Dictionary<VisualElement, Vector2>();
                for (int i = 0; i < CurrentRanged.AttackMovements.Count; i++)
                {
                    var moveCfg = CurrentRanged.AttackMovements[i];
                    float moveStart = moveCfg.StartTime;
                    float moveEnd = moveCfg.StartTime + moveCfg.Duration;
                    string dirLabel = moveCfg.Distance >= 0 ? "\u2192" : "\u2190";
                    string moveLabel = moveCfg.Enabled
                        ? $"Move{i + 1} {dirLabel} {moveCfg.Distance:F1}m"
                        : $"Move{i + 1} (disabled)";
                    Color moveColor = moveCfg.Enabled
                        ? new Color(0.6f, 0.3f, 0.9f, 0.6f)
                        : new Color(0.4f, 0.4f, 0.4f, 0.4f);

                    int capturedIndex = i;
                    var moveClip = CreateClipVisual(moveLabel, moveStart, moveEnd, moveColor,
                        (s, e) =>
                        {
                            Undo.RecordObject(_currentData, "Edit Attack Movement");
                            CurrentRanged.AttackMovements[capturedIndex].StartTime = s;
                            CurrentRanged.AttackMovements[capturedIndex].Duration = e - s;
                            EditorUtility.SetDirty(_currentData);
                            UpdatePreviewState(true);
                        }, null, true);
                    moveClips.Add(moveClip);
                    moveLaneMap[moveClip] = new Vector2(moveStart, moveEnd);
                }
                DrawTrack("Attack Movement", moveClips, moveLaneMap);
            }

            _scrubberOverlay.BringToFront();
            UpdateScrubberUI();

            var rulerContainer = _timelineContentArea.parent.parent.Query<VisualElement>().Where(x => x.parent != null && x.parent.style.flexDirection == FlexDirection.Row && x.style.height == 20).First();
            if (rulerContainer != null) DrawRuler(rulerContainer, clipLen);
        }

        /// <summary>
        /// 繪製 Timing 參考線。
        /// targetBg 為 null → 畫成全 height 跨 row 的 overlay(舊行為,適合近戰/QuickFire 單一 timeline)
        /// targetBg 指定某 row 的 bg → 只在該 row 內顯示(蓄力模式下把 timing 限定於 Fire row)
        /// </summary>
        private void DrawTimingMarker(string label, float time, Color color, VisualElement targetBg = null)
        {
            if (time < 0) return;
            VisualElement parent;
            float xPos;
            if (targetBg != null)
            {
                parent = targetBg;
                xPos = time * PIXELS_PER_SECOND;
            }
            else
            {
                parent = _timelineContentArea;
                xPos = 120 + (time * PIXELS_PER_SECOND);
            }
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

            var labelEl = new Label(label)
            {
                style =
                {
                    color = color,
                    fontSize = 9,
                    marginLeft = 4
                }
            };
            marker.Add(labelEl);
            parent.Add(marker);
        }

        /// <summary>
        /// 把指定 phase 的 TimelineEvents 加入 clips/laneMap,給 RefreshTimeline 的各階段 row 用。
        /// 事件的 TriggerTime 一律解釋為「該 phase 動畫起始為 0 的本地時間」。
        /// </summary>
        private void AddPhaseEventClips(List<VisualElement> clips, Dictionary<VisualElement, Vector2> laneMap, TimelineEventPhase phase)
        {
            if (_currentData == null || _currentData.TimelineEvents == null) return;
            foreach (var evt in _currentData.TimelineEvents)
            {
                if (evt.Phase != phase) continue;
                string label = string.IsNullOrEmpty(evt.Name) ? "Event" : evt.Name;
                float start = evt.TriggerTime;
                float end = evt.TriggerTime + 0.1f;

                var capturedEvt = evt;
                var clip = CreateClipVisual(label, start, end, new Color(0.2f, 0.5f, 0.8f),
                    (s, _) =>
                    {
                        Undo.RecordObject(_currentData, "Edit Timeline Event");
                        capturedEvt.TriggerTime = s;
                        if (_selectedEvent == capturedEvt) UpdateInspectorValues();
                        UpdatePreviewState(true);
                    },
                    () => SelectEvent(capturedEvt),
                    true);
                _clipMap[evt] = clip;
                SetClipSelectionStyle(clip, _selectedEvent == evt);
                clips.Add(clip);
                laneMap[clip] = new Vector2(start, end);
            }
        }

        /// <summary>
        /// 繪製單一軌道,回傳該軌道的 bg(供呼叫端在其上額外加 timing 標記)
        /// </summary>
        private VisualElement DrawTrack(string name, List<VisualElement> clips, Dictionary<VisualElement, Vector2> timeRanges = null)
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
            return bg;
        }

        private VisualElement CreateClipVisual(string name, float start, float end, Color color, System.Action<float, float> onChange, System.Action onSelect, bool editable, System.Action<float, float> onComplete = null)
        {
            var clip = new VisualElement { style = { position = Position.Absolute, left = start * PIXELS_PER_SECOND, width = (end - start) * PIXELS_PER_SECOND, height = 24, top = 5, backgroundColor = color, borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1, borderTopColor = new Color(1, 1, 1, 0.5f), borderBottomColor = new Color(1, 1, 1, 0.5f), borderLeftColor = new Color(1, 1, 1, 0.5f), borderRightColor = new Color(1, 1, 1, 0.5f), borderTopLeftRadius = 3, borderTopRightRadius = 3, borderBottomLeftRadius = 3, borderBottomRightRadius = 3 } };
            clip.Add(new Label(name) { pickingMode = PickingMode.Ignore, style = { color = Color.black, fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter, flexGrow = 1, marginLeft = 12, marginRight = 12 } });
            if (editable)
            {
                var leftHandle = new VisualElement {
                    name = "left-handle",
                    style = {
                        position = Position.Absolute, left = 0, width = 8, height = Length.Percent(100),
                        backgroundColor = new Color(1, 1, 1, 0.4f),
                        borderTopLeftRadius = 3, borderBottomLeftRadius = 3
                    },
                    pickingMode = PickingMode.Ignore
                };
                clip.Add(leftHandle);

                var rightHandle = new VisualElement {
                    name = "right-handle",
                    style = {
                        position = Position.Absolute, right = 0, width = 8, height = Length.Percent(100),
                        backgroundColor = new Color(1, 1, 1, 0.4f),
                        borderTopRightRadius = 3, borderBottomRightRadius = 3
                    },
                    pickingMode = PickingMode.Ignore
                };
                clip.Add(rightHandle);

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
                clip.AddManipulator(new UnifiedClipDragManipulator(clip, wrappedOnChange, onSelect, 400f, wrappedOnComplete));
            }
            return clip;
        }

        private void SelectEvent(TimelineEvent evt)
        {
            if (_selectedEvent == evt) return;
            var oldEvent = _selectedEvent; _selectedEvent = evt; _selectedHitWindow = null;
            if (oldEvent != null && _clipMap.TryGetValue(oldEvent, out var oldVis)) SetClipSelectionStyle(oldVis, false);
            if (evt != null && _clipMap.TryGetValue(evt, out var newVis)) SetClipSelectionStyle(newVis, true);
            RefreshEventInspector(); RefreshTimeline(); SceneView.RepaintAll();
        }

        private void SelectHitWindow(MeleeHitWindow hw)
        {
            if (_selectedHitWindow == hw) return;
            _selectedHitWindow = hw; _selectedEvent = null;
            RefreshEventInspector(); RefreshTimeline(); SceneView.RepaintAll();
        }

        private void SetClipSelectionStyle(VisualElement clip, bool selected)
        {
            Color c = selected ? Color.yellow : new Color(1, 1, 1, 0.5f);
            int w = selected ? 2 : 1;
            clip.style.borderTopColor = c; clip.style.borderBottomColor = c; clip.style.borderLeftColor = c; clip.style.borderRightColor = c;
            clip.style.borderTopWidth = w; clip.style.borderBottomWidth = w; clip.style.borderLeftWidth = w; clip.style.borderRightWidth = w;
        }

        private void RefreshEventInspector()
        {
            _eventInspectorContainer.Clear();
            _startTimeField = null;
            _endTimeField = null;

            if (_currentData == null) return;

            float startTime = 0;
            float endTime = 0;
            System.Action<float> setStart = null;
            System.Action<float> setEnd = null;

            if (_selectedEvent != null)
            {
                startTime = _selectedEvent.TriggerTime;
                endTime = _selectedEvent.TriggerTime;
                setStart = v => { _selectedEvent.TriggerTime = Mathf.Max(0, v); };
                setEnd = v => { };
            }
            else if (_selectedHitWindow != null)
            {
                startTime = _selectedHitWindow.StartTime;
                endTime = _selectedHitWindow.EndTime;
                setStart = v => { _selectedHitWindow.StartTime = Mathf.Max(0, v); };
                setEnd = v => { _selectedHitWindow.EndTime = Mathf.Max(v, _selectedHitWindow.StartTime); };
            }
            else
            {
                _eventInspectorContainer.Add(new Label("Select an item to edit.") { style = { color = new Color(0.7f, 0.7f, 0.7f) } });
                return;
            }

            _startTimeField = new FloatField("Start Time") { value = startTime };
            _startTimeField.RegisterValueChangedCallback(e => {
                Undo.RecordObject(_currentData, "Edit Time");
                setStart(e.newValue);
                RefreshTimeline();
                UpdatePreviewState(true);
            });
            _eventInspectorContainer.Add(_startTimeField);

            if (_selectedHitWindow != null)
            {
                _endTimeField = new FloatField("End Time") { value = endTime };
                _endTimeField.RegisterValueChangedCallback(e => {
                    Undo.RecordObject(_currentData, "Edit Time");
                    setEnd(e.newValue);
                    RefreshTimeline();
                    UpdatePreviewState(true);
                });
                _eventInspectorContainer.Add(_endTimeField);
            }

            var so = new SerializedObject(_currentData);
            if (_selectedEvent != null)
            {
                var listProp = so.FindProperty("TimelineEvents");
                int index = _currentData.TimelineEvents.IndexOf(_selectedEvent);
                if (index >= 0)
                {
                    var prop = listProp.GetArrayElementAtIndex(index);

                    _eventInspectorContainer.Add(new Label("基本設定") { style = { marginTop = 5, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.8f, 0.8f, 1f) } });
                    CreatePropField(prop, "Name", true);
                    CreatePropField(prop, "Phase");

                    _eventInspectorContainer.Add(new Label("特效 (主要 — 直接拉 Prefab)") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.6f, 1f, 0.8f) } });
                    CreatePropField(prop, "VFXPrefab");
                    CreatePropField(prop, "SFX");

                    _eventInspectorContainer.Add(new Label("Cue (進階 fallback — VFX/SFX 未設定時才用)") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.7f, 0.7f, 0.85f) } });
                    CreatePropField(prop, "CueTag");

                    _eventInspectorContainer.Add(new Label("綁定設定") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.8f, 0.8f, 1f) } });
                    CreatePropField(prop, "SocketName");
                    CreatePropField(prop, "Axes");
                    CreatePropField(prop, "StopOnInterrupt");
                    CreatePropField(prop, "InterruptBehavior");

                    var transformLabel = new Label("Transform 設定（將傳遞給 Cue）") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(1f, 1f, 0.6f) } };
                    _eventInspectorContainer.Add(transformLabel);

                    var infoLabel = new Label("這些設定會通過 GameplayCueParameters 傳遞給 Cue，\nVFX Cue 將會使用這些值來定位和縮放特效。")
                    {
                        style = {
                            marginTop = 2,
                            marginBottom = 5,
                            fontSize = 9,
                            color = new Color(0.7f, 0.7f, 0.7f),
                            whiteSpace = WhiteSpace.Normal
                        }
                    };
                    _eventInspectorContainer.Add(infoLabel);

                    CreatePropField(prop, "PositionOffset");
                    CreatePropField(prop, "RotationOffset");
                    CreatePropField(prop, "Scale");

                    // VFX Cue 資訊
                    DrawVFXCueInfo(_selectedEvent);
                }
            }
            else if (_selectedHitWindow != null && IsMelee)
            {
                var listProp = so.FindProperty("HitWindows");
                int index = CurrentMelee.HitWindows.IndexOf(_selectedHitWindow);
                if (index >= 0)
                {
                    var prop = listProp.GetArrayElementAtIndex(index);

                    _eventInspectorContainer.Add(new Label("Hitbox") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = Color.white } });
                    CreatePropField(prop, "Shape", false, () => SceneView.RepaintAll());
                    CreatePropField(prop, "Offset", false, () => SceneView.RepaintAll());
                    CreatePropField(prop, "Size", false, () => SceneView.RepaintAll());
                    CreatePropField(prop, "SocketName", false, () => SceneView.RepaintAll());
                    CreatePropField(prop, "AttachToBody", false, () => SceneView.RepaintAll());
                    CreatePropField(prop, "StopOnInterrupt");

                    _eventInspectorContainer.Add(new Label("Damage") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = Color.white } });
                    CreatePropField(prop, "BaseDamage");
                    CreatePropField(prop, "DamageMultiplier");
                    CreatePropField(prop, "HitEffect");
                    CreatePropField(prop, "HitCueTag");
                    CreatePropField(prop, "HitVFXPrefab");
                    CreatePropField(prop, "HitSFX");
                    CreatePropField(prop, "HitVFXLifetime");
                    CreatePropField(prop, "AttachHitVFXToSurface");
                    CreatePropField(prop, "HitVFXScale");
                    CreatePropField(prop, "HitVFXScaleAllChildren");

                    _eventInspectorContainer.Add(new Label("Feedback") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = Color.white } });
                    CreatePropField(prop, "HitStopDuration");
                    CreatePropField(prop, "HitStopSpeed");
                    CreatePropField(prop, "ScreenShakeForce");

                    _eventInspectorContainer.Add(new Label("Target Tracking") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = Color.white } });
                    CreatePropField(prop, "MarkTargetOnHit");
                    CreatePropField(prop, "AutoFaceMarkedTarget");

                    _eventInspectorContainer.Add(new Label("Raycast Trail") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.4f, 1f, 0.8f) } });
                    CreatePropField(prop, "UseRaycastTrail", false, () => SceneView.RepaintAll());
                    CreatePropField(prop, "TrailStartOffset", false, () => SceneView.RepaintAll());
                    CreatePropField(prop, "TrailEndOffset", false, () => SceneView.RepaintAll());
                    CreatePropField(prop, "TrailSegments", false, () => SceneView.RepaintAll());
                    CreatePropField(prop, "TrailRayRadius", false, () => SceneView.RepaintAll());

                    _eventInspectorContainer.Add(new Label("Movement") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = Color.white } });
                    CreatePropField(prop, "TriggerMovement");
                    CreatePropField(prop, "MovementType");
                    CreatePropField(prop, "SnapRange");
                    CreatePropField(prop, "SnapStopDistance");
                    CreatePropField(prop, "MoveDuration");
                    CreatePropField(prop, "MoveCurve");
                }
            }
        }

        /// <summary>
        /// 繪製 VFX Cue 相關資訊區塊。
        /// 主要流程(直接拉 Prefab)時顯示綠字確認;否則沿用 CueTag 查找邏輯。
        /// </summary>
        private void DrawVFXCueInfo(TimelineEvent evt)
        {
            // 直接拉 Prefab 流程 — 不查 Cue,直接給綠字確認訊息
            if (evt.VFXPrefab != null || evt.SFX != null)
            {
                _eventInspectorContainer.Add(new Label("✓ 將直接預覽 VFX Prefab / 播放 SFX (跳過 Cue 系統)")
                {
                    style = { marginTop = 10, fontSize = 9, color = new Color(0.6f, 1f, 0.6f), whiteSpace = WhiteSpace.Normal }
                });
                return;
            }

            var vfxCue = FindVFXCueByCueTag(evt.CueTag);
            if (vfxCue != null)
            {
                var vfxInfoLabel = new Label("對應的 VFX Cue:") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.6f, 1f, 0.6f) } };
                _eventInspectorContainer.Add(vfxInfoLabel);

                var vfxCueField = new ObjectField("VFX Cue")
                {
                    value = vfxCue,
                    objectType = typeof(VFXCue),
                    allowSceneObjects = false
                };
                vfxCueField.SetEnabled(false);
                _eventInspectorContainer.Add(vfxCueField);

                if (vfxCue.VFXPrefab != null)
                {
                    var prefabField = new ObjectField("VFX Prefab")
                    {
                        value = vfxCue.VFXPrefab,
                        objectType = typeof(GameObject),
                        allowSceneObjects = false
                    };
                    prefabField.SetEnabled(false);
                    _eventInspectorContainer.Add(prefabField);

                    _eventInspectorContainer.Add(new Label("V 預覽特效會顯示在場景中")
                    {
                        style = { fontSize = 9, color = new Color(0.6f, 1f, 0.6f), marginTop = 5 }
                    });
                }
                else
                {
                    _eventInspectorContainer.Add(new Label("! VFX Cue 中沒有設定 VFX Prefab，無法預覽特效")
                    {
                        style = { fontSize = 9, color = new Color(1f, 0.6f, 0f), marginTop = 5, whiteSpace = WhiteSpace.Normal }
                    });
                }
            }
            else if (evt.CueTag.IsValid)
            {
                var warningBox = new Label($"! 找不到對應的 VFX Cue: {evt.CueTag.TagName}\n請確認是否有創建對應的 VFX Cue 資源")
                {
                    style = {
                        marginTop = 10, fontSize = 9, color = new Color(1f, 0.5f, 0f),
                        whiteSpace = WhiteSpace.Normal,
                        backgroundColor = new Color(0.3f, 0.15f, 0f, 0.3f),
                        paddingLeft = 5, paddingTop = 5, paddingBottom = 5, paddingRight = 5,
                        borderTopWidth = 1, borderTopColor = new Color(1f, 0.5f, 0f)
                    }
                };
                _eventInspectorContainer.Add(warningBox);

                var createButton = new Button(() => CreateVFXCueForEvent(evt))
                {
                    text = "創建對應的 VFX Cue",
                    style = { marginTop = 5 }
                };
                _eventInspectorContainer.Add(createButton);
            }

            // 實時預覽資訊
            if (_previewTarget != null)
            {
                Transform socket = FindChildRecursive(_previewTarget.transform, evt.SocketName);
                if (socket == null) socket = _previewTarget.transform;

                Vector3 worldPos = socket.TransformPoint(evt.PositionOffset);
                Quaternion worldRot = socket.rotation * Quaternion.Euler(evt.RotationOffset);

                var previewInfo = new Label($"預覽世界座標:\n位置: {worldPos:F2}\n旋轉: {worldRot.eulerAngles:F1}\n縮放: {evt.Scale:F2}")
                {
                    style = {
                        marginTop = 10, fontSize = 9, color = new Color(0.6f, 1f, 0.6f),
                        whiteSpace = WhiteSpace.Normal,
                        backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.5f),
                        paddingLeft = 5, paddingTop = 5, paddingBottom = 5,
                        borderTopWidth = 1, borderTopColor = new Color(0.3f, 0.3f, 0.3f)
                    }
                };
                _eventInspectorContainer.Add(previewInfo);
            }
        }

        private void UpdateInspectorValues()
        {
            if (_currentData == null) return;
            if (_selectedEvent != null)
            {
                _startTimeField?.SetValueWithoutNotify(_selectedEvent.TriggerTime);
            }
            else if (_selectedHitWindow != null)
            {
                _startTimeField?.SetValueWithoutNotify(_selectedHitWindow.StartTime);
                _endTimeField?.SetValueWithoutNotify(_selectedHitWindow.EndTime);
            }
        }

        private void CreatePropField(SerializedProperty root, string relName, bool refreshTimeline = false, System.Action onValueChange = null)
        {
            var p = root.FindPropertyRelative(relName);
            if (p != null)
            {
                var f = new PropertyField(p); f.Bind(root.serializedObject);
                f.RegisterValueChangeCallback(e => {
                    if (refreshTimeline) RefreshTimeline();
                    onValueChange?.Invoke();
                    // Inspector 改 Position / Rotation / Scale / Axes 等欄位 → forceSyncEvt=true 觸發 EditorSync,
                    // 重算 initial 讓 VFX 立刻移到新位置。一般 update 走 Sample 路徑保留 Axes=None 凍結效果。
                    UpdatePreviewState(true, 0f, forceSyncEvt: true);
                });
                _eventInspectorContainer.Add(f);
            }
        }

        #region Add/Delete Items

        private void AddNewHitWindow()
        {
            if (_currentData == null) return;
            // HitWindow 僅適用於近戰
            if (!IsMelee)
            {
                Debug.LogWarning("[GAS Attack Editor] Hit Windows 僅適用於 MeleeAttackData。");
                return;
            }

            Undo.RecordObject(_currentData, "Add Hit Window");

            var newHW = new MeleeHitWindow
            {
                StartTime = _currentTime,
                EndTime = _currentTime + 0.2f,
                Shape = HitboxShape.Box,
                Offset = new Vector3(0, 1, 1),
                Size = Vector3.one,
                BaseDamage = 10f,
                DamageMultiplier = 1f,
                HitStopDuration = 0.1f,
                ScreenShakeForce = 1f
            };

            CurrentMelee.HitWindows ??= new List<MeleeHitWindow>();
            CurrentMelee.HitWindows.Add(newHW);

            EditorUtility.SetDirty(_currentData);
            RefreshTimeline();
            SelectHitWindow(newHW);
        }

        /// <summary>
        /// 顯示「+ Timeline Event」下拉選單,讓使用者選擇要加到哪個 phase。
        /// 蓄力模式 (HoldToCharge/HoldToAim) 才有 ChargeStart / ChargeLoop 選項。
        /// </summary>
        private void ShowAddTimelineEventMenu()
        {
            if (_currentData == null) return;
            var menu = new GenericMenu();
            bool isChargeMode = IsRanged && CurrentRanged.Charge != ChargeMode.None;
            if (isChargeMode)
            {
                menu.AddItem(new GUIContent("Charge Start"), false, () => AddNewTimelineEvent(TimelineEventPhase.ChargeStart));
                menu.AddItem(new GUIContent("Charge Loop"), false, () => AddNewTimelineEvent(TimelineEventPhase.ChargeLoop));
                menu.AddItem(new GUIContent("Charge Fire"), false, () => AddNewTimelineEvent(TimelineEventPhase.Fire));
            }
            else
            {
                menu.AddItem(new GUIContent("Fire (Default)"), false, () => AddNewTimelineEvent(TimelineEventPhase.Fire));
            }
            menu.ShowAsContext();
        }

        private void AddNewTimelineEvent() => AddNewTimelineEvent(TimelineEventPhase.Fire);

        private void AddNewTimelineEvent(TimelineEventPhase phase)
        {
            if (_currentData == null) return;

            Undo.RecordObject(_currentData, "Add Timeline Event");

            var newEvent = new TimelineEvent
            {
                Name = "New Event",
                TriggerTime = _currentTime,
                Scale = Vector3.one,
                Phase = phase
            };

            _currentData.TimelineEvents ??= new List<TimelineEvent>();
            _currentData.TimelineEvents.Add(newEvent);

            EditorUtility.SetDirty(_currentData);
            RefreshTimeline();
            SelectEvent(newEvent);
        }

        private void DeleteSelectedItem()
        {
            if (_currentData == null) return;

            if (_selectedHitWindow != null && IsMelee)
            {
                Undo.RecordObject(_currentData, "Delete Hit Window");
                CurrentMelee.HitWindows.Remove(_selectedHitWindow);
                _selectedHitWindow = null;
                EditorUtility.SetDirty(_currentData);
                RefreshAll();
            }
            else if (_selectedEvent != null)
            {
                Undo.RecordObject(_currentData, "Delete Timeline Event");
                _currentData.TimelineEvents.Remove(_selectedEvent);
                _selectedEvent = null;
                EditorUtility.SetDirty(_currentData);
                RefreshAll();
            }
        }

        #endregion

        #region Playback

        private void TogglePlay() { _isPlaying = !_isPlaying; _lastEditorTime = EditorApplication.timeSinceStartup; if (!_isPlaying) UpdatePreviewState(true); }

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

        private void OnTimelineMouseDown(MouseDownEvent e) { if (e.button == 0) { float x = e.localMousePosition.x - 120f; if (x < 0) x = 0; SetTimeFromPixel(x); e.StopPropagation(); } }

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
            var primaryClip = GetPreviewClip();
            if (primaryClip == null) return;

            if (_previewTarget == null || _animationTarget == null)
            {
                // 優先用 Player tag 找，找不到再 fallback 到任意 ASC
                var playerObj = GameObject.FindGameObjectWithTag("Player");
                _previewTarget = playerObj != null
                    ? playerObj.GetComponent<AbilitySystemComponent>()
                    : FindFirstObjectByType<AbilitySystemComponent>();
                if (_previewTarget != null)
                {
                    _initialPos = _previewTarget.transform.position;
                    _initialRot = _previewTarget.transform.rotation;
                    // 跳過父物件的 Animator,優先找子物件的(模型的 Animator 才有正確的 Avatar 匹配 RM 資料)
                    Animator childAnimator = null;
                    foreach (var anim in _previewTarget.GetComponentsInChildren<Animator>())
                    {
                        if (anim.gameObject != _previewTarget.gameObject)
                        {
                            childAnimator = anim;
                            break;
                        }
                    }
                    if (childAnimator != null)
                    {
                        _animationTarget = childAnimator.gameObject;
                    }
                    else
                    {
                        // Fallback:Edit Mode 時模型可能尚未生成,退回父物件的 Animator
                        var fallback = _previewTarget.GetComponentInChildren<Animator>();
                        _animationTarget = fallback != null ? fallback.gameObject : _previewTarget.gameObject;
                    }
                    Debug.Log($"[AttackDataEditor] 預覽目標: {_previewTarget.name}, 動畫目標: {_animationTarget.name}");
                }
                else
                {
                    Debug.LogWarning("[AttackDataEditor] 場景中找不到 Player 或 AbilitySystemComponent，無法預覽動畫");
                }
            }

            if (_previewTarget == null || _animationTarget == null) return;

            if (!_isPreviewing)
            {
                _isPreviewing = true;
                AnimationMode.StartAnimationMode();
            }

            // 當 clip 或 Animator 變動時重建 Sampler(PlayableGraph)
            Animator targetAnimator = _animationTarget.GetComponent<Animator>();
            if (_rmSampler == null || _cachedClip != primaryClip || _cachedAnimator != targetAnimator)
            {
                _rmSampler?.Dispose();
                _rmSampler = new HumanoidRootMotionSampler();
                if (targetAnimator != null)
                {
                    _rmSampler.Initialize(targetAnimator, primaryClip);
                }
                _cachedClip = primaryClip;
                _cachedAnimator = targetAnimator;
            }

            // 以 PlayableGraph + AnimationMode.SamplePlayableGraph 取樣,支援 Humanoid Root Motion
            Vector3 rmDisplacement;
            if (_rmSampler != null && _rmSampler.IsValid)
            {
                (rmDisplacement, _) = _rmSampler.Sample(_currentTime);
            }
            else
            {
                // Fallback:無 Animator 時退回原生 SampleAnimation(僅對 Generic 或非 RM 動畫有效)
                primaryClip.SampleAnimation(_animationTarget, _currentTime);
                rmDisplacement = _animationTarget.transform.localPosition;
            }

            // 遠程多段位移(程式碼驅動,RM 之外的額外偏移)
            Vector3 codeDisplacement = Vector3.zero;
            if (IsRanged && CurrentRanged.AttackMovements != null)
            {
                foreach (var moveCfg in CurrentRanged.AttackMovements)
                {
                    if (!moveCfg.Enabled || moveCfg.Curve == null) continue;
                    float timeSinceStart = _currentTime - moveCfg.StartTime;
                    if (timeSinceStart > 0)
                    {
                        float progress = Mathf.Clamp01(timeSinceStart / moveCfg.Duration);
                        float val = moveCfg.Curve.Evaluate(progress);
                        codeDisplacement += Vector3.forward * (moveCfg.Distance * val);
                    }
                }
            }

            // 套用 RM + 程式碼位移到父物件
            Vector3 totalOffset = rmDisplacement + codeDisplacement;
            Vector3 targetPos = _initialPos + (_initialRot * totalOffset);
            _previewTarget.transform.position = targetPos;

            // 重置子物件 localPosition,防止模型漂移(與 Runtime 的 RootMotionRelay 邏輯一致)
            _animationTarget.transform.localPosition = Vector3.zero;
            _animationTarget.transform.localRotation = Quaternion.identity;

            UpdatePreviewVFX(isScrubbing, deltaTime, forceSyncEvt);
            UpdatePreviewProjectile(isScrubbing, deltaTime);
            UpdatePreviewAoE(isScrubbing, deltaTime);

            // 強制驅動 player loop + 重繪 Scene — 拖時間軸時 follower 的 LateUpdate(ExecuteAlways)
            // 需要 player loop tick 才會跑,沒這行光靠 RepaintAll 仍可能看不到 VFX 即時跟隨。
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private void UpdatePreviewVFX(bool isScrubbing, float deltaTime = 0f, bool forceSyncEvt = false)
        {
            if (_currentData?.TimelineEvents == null || _previewTarget == null) return;

            foreach (var evt in _currentData.TimelineEvents)
            {
                float previewDuration = 2.0f;
                bool shouldBeActive = _currentTime >= evt.TriggerTime && _currentTime <= evt.TriggerTime + previewDuration;

                if (shouldBeActive)
                {
                    bool isNewInstance = false;

                    Transform socket = FindChildRecursive(_previewTarget.transform, evt.SocketName);
                    if (socket == null) socket = _previewTarget.transform;

                    if (!_previewVFXs.ContainsKey(evt) || _previewVFXs[evt] == null)
                    {
                        // 優先讀 evt.VFXPrefab(直接拉 Prefab 流程);沒設才 fallback 查 Cue
                        GameObject prefab = evt.VFXPrefab;
                        if (prefab == null)
                        {
                            var vfxCue = FindVFXCueByCueTag(evt.CueTag);
                            prefab = vfxCue?.VFXPrefab;
                        }

                        if (prefab != null)
                        {
                            Vector3 spawnPos = socket.TransformPoint(evt.PositionOffset);
                            Quaternion spawnRot = socket.rotation * Quaternion.Euler(evt.RotationOffset);
                            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                            inst.hideFlags = HideFlags.HideAndDontSave;
                            inst.transform.SetPositionAndRotation(spawnPos, spawnRot);
                            _previewVFXs[evt] = inst;
                            isNewInstance = true;

                            // 對齊 runtime — 加 follower + Hierarchy scalingMode
                            var follower = inst.AddComponent<GAS.TimelineVFXFollower>();
                            follower.Setup(socket, evt.Axes, evt.PositionOffset, evt.RotationOffset, evt.Scale);

                            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>())
                            {
                                var main = ps.main;
                                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
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

                    // forceSyncEvt=true(Inspector 改 PositionOffset/Axes 等)→ EditorSync 重算 initial,VFX 立刻反映新值;
                    // forceSyncEvt=false(拖時間軸 / 一般 update)→ Sample 維持 initial,保留 Axes=None 凍結等差異。
                    var existingFollower = previewInstance.GetComponent<GAS.TimelineVFXFollower>();
                    if (existingFollower != null)
                    {
                        if (forceSyncEvt) existingFollower.EditorSync(socket, evt.Axes, evt.PositionOffset, evt.RotationOffset, evt.Scale);
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

        /// <summary>
        /// 更新投射物預覽（遠程攻擊專用）
        /// 在 FireTime 時生成投射物 Prefab 並模擬飛行軌跡，同時模擬粒子特效與發射 Cue
        /// </summary>
        private void UpdatePreviewProjectile(bool isScrubbing, float deltaTime = 0f)
        {
            if (CurrentRanged == null || _previewTarget == null)
            {
                CleanupPreviewProjectile();
                return;
            }
            // AoE / Hitscan 攻擊不該生投射物預覽 — 否則會看到橘色膠囊 fallback
            if (CurrentRanged.AttackType != RangedAttackType.Projectile)
            {
                CleanupPreviewProjectile();
                return;
            }
            var projConfig = CurrentRanged.ProjectileConfig;
            bool hasPrefab = projConfig != null && projConfig.Prefab != null;
            var fireEvents = CurrentRanged.GetResolvedFireEvents();
            Transform defaultSocket = FindChildRecursive(_previewTarget.transform, CurrentRanged.SpawnSocketName);
            if (defaultSocket == null) defaultSocket = _previewTarget.transform;
            // 檢查是否所有投射物都已過期
            float latestFireTime = 0f;
            foreach (var evt in fireEvents)
            {
                if (evt.FireTime > latestFireTime) latestFireTime = evt.FireTime;
            }
            float fireCueDuration = 2f;
            float projectileLifetime = hasPrefab ? projConfig.Lifetime : 3f;
            float maxVisibleTime = latestFireTime + Mathf.Max(projectileLifetime, fireCueDuration);
            if (_currentTime < fireEvents[0].FireTime || _currentTime > maxVisibleTime)
            {
                CleanupPreviewProjectile();
                return;
            }
            // 逐發處理投射物
            for (int i = 0; i < fireEvents.Count; i++)
            {
                var evt = fireEvents[i];
                float timeSinceFire = _currentTime - evt.FireTime;
                bool shouldBeActive = timeSinceFire >= 0f && timeSinceFire < projectileLifetime;
                // 確保 list 有足夠的 slot
                while (_previewProjectiles.Count <= i) _previewProjectiles.Add(null);
                if (!shouldBeActive)
                {
                    // 清除此發
                    if (_previewProjectiles[i] != null && _previewProjectiles[i].Instance != null)
                    {
                        DestroyImmediate(_previewProjectiles[i].Instance);
                        _previewProjectiles[i] = null;
                    }
                    continue;
                }
                // 生成此發投射物
                bool isNewInstance = false;
                if (_previewProjectiles[i] == null || _previewProjectiles[i].Instance == null)
                {
                    Transform evtSocket = ResolveFireEventSocket(evt, defaultSocket);
                    Vector3 spawnPos = evtSocket.TransformPoint(evt.SpawnOffset);
                    Vector3 fireDir = evt.DirectionOffset != Vector3.zero
                        ? _previewTarget.transform.rotation * Quaternion.Euler(evt.DirectionOffset) * Vector3.forward
                        : _previewTarget.transform.forward;
                    GameObject inst;
                    if (hasPrefab)
                    {
                        inst = (GameObject)PrefabUtility.InstantiatePrefab(projConfig.Prefab);
                    }
                    else
                    {
                        inst = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        inst.transform.localScale = new Vector3(0.05f, 0.2f, 0.05f);
                        var renderer = inst.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            renderer.sharedMaterial = new Material(Shader.Find("Unlit/Color"))
                            {
                                color = new Color(1f, 0.6f, 0f)
                            };
                        }
                        var col = inst.GetComponent<Collider>();
                        if (col != null) DestroyImmediate(col);
                    }
                    inst.hideFlags = HideFlags.HideAndDontSave;
                    // 停用腳本避免 runtime 邏輯執行，但保留 ParticleSystem
                    foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>())
                    {
                        mb.enabled = false;
                    }
                    var particleSystems = inst.GetComponentsInChildren<ParticleSystem>();
                    // 初始化粒子系統
                    foreach (var ps in particleSystems)
                    {
                        var main = ps.main;
                        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                        ps.Clear();
                    }
                    _previewProjectiles[i] = new ProjectilePreviewData
                    {
                        Instance = inst,
                        SpawnPos = spawnPos,
                        FireDir = fireDir,
                        Speed = hasPrefab ? projConfig.Speed : 20f,
                        Gravity = hasPrefab ? projConfig.Gravity : 0f,
                        FireTime = evt.FireTime,
                        ParticleSystems = particleSystems
                    };
                    isNewInstance = true;
                }
                // 更新位置
                var data = _previewProjectiles[i];
                Vector3 pos = data.SpawnPos
                    + data.FireDir * (data.Speed * timeSinceFire)
                    + Vector3.down * (0.5f * data.Gravity * timeSinceFire * timeSinceFire);
                Quaternion rot = data.Gravity > 0
                    ? Quaternion.LookRotation(data.FireDir * data.Speed + Vector3.down * (data.Gravity * timeSinceFire))
                    : Quaternion.LookRotation(data.FireDir);
                data.Instance.transform.SetPositionAndRotation(pos, rot);
                // 模擬投射物上的粒子特效
                if (data.ParticleSystems != null && data.ParticleSystems.Length > 0)
                {
                    if (isScrubbing)
                    {
                        foreach (var ps in data.ParticleSystems)
                        {
                            ps.Simulate(timeSinceFire, true, true);
                        }
                    }
                    else
                    {
                        if (isNewInstance)
                        {
                            float localTime = timeSinceFire > 0 ? timeSinceFire : 0;
                            foreach (var ps in data.ParticleSystems)
                            {
                                ps.Simulate(localTime, true, true);
                            }
                        }
                        else
                        {
                            foreach (var ps in data.ParticleSystems)
                            {
                                ps.Simulate(deltaTime, true, false);
                            }
                        }
                    }
                }
            }
            // 更新發射特效預覽（FireCueTag）
            UpdatePreviewFireCueVFX(fireEvents, defaultSocket, isScrubbing, deltaTime);
        }

        /// <summary>
        /// 依 FireEvent 的 SpawnSocketNameOverride 解析骨骼;空時回退到預設 socket
        /// </summary>
        private Transform ResolveFireEventSocket(RangedFireEvent evt, Transform defaultSocket)
        {
            if (evt == null || string.IsNullOrEmpty(evt.SpawnSocketNameOverride)) return defaultSocket;
            Transform overrideSocket = FindChildRecursive(_previewTarget.transform, evt.SpawnSocketNameOverride);
            return overrideSocket != null ? overrideSocket : defaultSocket;
        }

        /// <summary>
        /// 更新發射 Cue 特效預覽（槍口閃光、發射煙霧等）
        /// </summary>
        private void UpdatePreviewFireCueVFX(List<RangedFireEvent> fireEvents, Transform defaultSocket, bool isScrubbing, float deltaTime)
        {
            VFXCue fireCue = FindVFXCueByCueTag(CurrentRanged.FireCueTag);
            GameObject fireCuePrefab = fireCue != null ? fireCue.VFXPrefab : null;
            if (fireCuePrefab == null)
            {
                CleanupFireCueVFXs();
                return;
            }
            // 計算發射特效持續時間
            float cueAutoLifetime = GetParticleSystemDuration(fireCuePrefab);
            float cueDuration = cueAutoLifetime > 0 ? cueAutoLifetime : 1.5f;
            for (int i = 0; i < fireEvents.Count; i++)
            {
                var evt = fireEvents[i];
                float timeSinceFire = _currentTime - evt.FireTime;
                bool shouldBeActive = timeSinceFire >= 0f && timeSinceFire < cueDuration;
                while (_previewFireCueVFXs.Count <= i) _previewFireCueVFXs.Add(null);
                if (!shouldBeActive)
                {
                    if (_previewFireCueVFXs[i] != null)
                    {
                        DestroyImmediate(_previewFireCueVFXs[i]);
                        _previewFireCueVFXs[i] = null;
                    }
                    continue;
                }
                bool isNewCueInstance = false;
                if (_previewFireCueVFXs[i] == null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(fireCuePrefab);
                    inst.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>())
                    {
                        mb.enabled = false;
                    }
                    foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>())
                    {
                        var main = ps.main;
                        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                        ps.Clear();
                    }
                    _previewFireCueVFXs[i] = inst;
                    isNewCueInstance = true;
                }
                // 定位到生成點
                var cueInst = _previewFireCueVFXs[i];
                Transform evtSocket = ResolveFireEventSocket(evt, defaultSocket);
                Vector3 spawnPos = evtSocket.TransformPoint(evt.SpawnOffset);
                Quaternion spawnRot = evt.DirectionOffset != Vector3.zero
                    ? _previewTarget.transform.rotation * Quaternion.Euler(evt.DirectionOffset)
                    : _previewTarget.transform.rotation;
                // 套用 VFXCue 額外偏移
                if (fireCue.UseParameterTransform)
                {
                    spawnPos += spawnRot * fireCue.AdditionalPositionOffset;
                    spawnRot *= Quaternion.Euler(fireCue.AdditionalRotationOffset);
                }
                cueInst.transform.SetPositionAndRotation(spawnPos, spawnRot);
                cueInst.transform.localScale = fireCue.AdditionalScale != Vector3.zero
                    ? fireCue.AdditionalScale
                    : Vector3.one;
                // 模擬粒子
                var particleSystems = cueInst.GetComponentsInChildren<ParticleSystem>();
                if (particleSystems.Length > 0)
                {
                    if (isScrubbing)
                    {
                        foreach (var ps in particleSystems)
                        {
                            ps.Simulate(timeSinceFire, true, true);
                        }
                    }
                    else
                    {
                        if (isNewCueInstance)
                        {
                            float localTime = timeSinceFire > 0 ? timeSinceFire : 0;
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
        }

        /// <summary>
        /// 取得 Prefab 上粒子系統的最大持續時間
        /// </summary>
        private float GetParticleSystemDuration(GameObject prefab)
        {
            float maxDuration = 0f;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                float total = main.duration + main.startLifetime.constantMax;
                if (total > maxDuration) maxDuration = total;
            }
            return maxDuration;
        }

        private void CleanupPreviewProjectile()
        {
            foreach (var data in _previewProjectiles)
            {
                if (data != null && data.Instance != null)
                {
                    DestroyImmediate(data.Instance);
                }
            }
            _previewProjectiles.Clear();
            CleanupFireCueVFXs();
        }

        private void CleanupFireCueVFXs()
        {
            foreach (var vfx in _previewFireCueVFXs)
            {
                if (vfx != null) DestroyImmediate(vfx);
            }
            _previewFireCueVFXs.Clear();
        }

        /// <summary>
        /// 預覽 AoE 攻擊在時間軸上的視覺化:
        /// - OneShot: 跨越 FireTime 後實例化 EffectPrefab,EffectLifetime 後清除
        /// - Persistent: FireTime → 顯示 AoE Prefab(整段 Delay+Duration+Lifetime)
        /// </summary>
        private void UpdatePreviewAoE(bool isScrubbing, float deltaTime = 0f)
        {
            if (CurrentRanged == null || _previewTarget == null)
            {
                CleanupPreviewAoE();
                return;
            }
            if (CurrentRanged.AttackType != RangedAttackType.AoETargeted
                && CurrentRanged.AttackType != RangedAttackType.AoEAtTarget)
            {
                CleanupPreviewAoE();
                return;
            }
            GameObject aoePrefab = CurrentRanged.AoEPrefab;
            if (aoePrefab == null)
            {
                CleanupPreviewAoE();
                return;
            }
            AoEBehaviour prefabBehaviour = aoePrefab.GetComponent<AoEBehaviour>();
            if (prefabBehaviour == null)
            {
                CleanupPreviewAoE();
                return;
            }

            float fireTime = CurrentRanged.FireTime;
            float timeSinceFire = _currentTime - fireTime;

            Vector3 center = ResolveAoEPreviewCenter();
            // 編輯器時間軸 _currentTime 視為 chargeTime,用分段曲線取得 visualRatio
            float chargeRatio = 0f;
            if (CurrentRanged.Charge != ChargeMode.None)
            {
                chargeRatio = CurrentRanged.ComputeVisualChargeRatio(_currentTime);
            }
            switch (prefabBehaviour.TickMode)
            {
                case AoETickMode.OneShot:
                    UpdatePreviewAoEOneShot(aoePrefab, prefabBehaviour, center, chargeRatio, timeSinceFire);
                    break;
                case AoETickMode.MeteorRain:
                    UpdatePreviewAoEMeteorRain(aoePrefab, prefabBehaviour, center, chargeRatio, timeSinceFire);
                    break;
                case AoETickMode.Persistent:
                default:
                    UpdatePreviewAoEPersistent(aoePrefab, prefabBehaviour, center, chargeRatio, timeSinceFire, isScrubbing, deltaTime);
                    break;
            }

            SimulateAoEParticles(_previewAoEEffectInstance, timeSinceFire, isScrubbing, deltaTime);
            SimulateAoEParticles(_previewAoEIndicatorInstance, Mathf.Max(0f, timeSinceFire), isScrubbing, deltaTime);
        }

        /// <summary>
        /// OneShot 模式預覽:超過 FireTime 後立即生 AoE Prefab,EffectLifetime 後消失
        /// </summary>
        private void UpdatePreviewAoEOneShot(GameObject prefab, AoEBehaviour beh, Vector3 center, float chargeRatio, float timeSinceFire)
        {
            bool shouldShowEffect = timeSinceFire >= 0f && timeSinceFire < beh.EffectLifetime;
            if (!shouldShowEffect)
            {
                if (_previewAoEEffectInstance != null)
                {
                    DestroyImmediate(_previewAoEEffectInstance);
                    _previewAoEEffectInstance = null;
                }
                if (_previewAoEIndicatorInstance != null)
                {
                    DestroyImmediate(_previewAoEIndicatorInstance);
                    _previewAoEIndicatorInstance = null;
                }
                return;
            }
            Quaternion rotation = Quaternion.LookRotation(_previewTarget.transform.forward, Vector3.up);
            EnsurePreviewEffectInstance(prefab, center, rotation, chargeRatio);
        }

        /// <summary>
        /// MeteorRain 模式預覽:整段 (MeteorRainDuration + EffectLifetime) 顯示 AoE Prefab
        /// 粒子由 prefab 內 ParticleSystem 自行播放,Collision 在編輯器無效(無物理),只看視覺
        /// </summary>
        private void UpdatePreviewAoEMeteorRain(GameObject prefab, AoEBehaviour beh, Vector3 center, float chargeRatio, float timeSinceFire)
        {
            float totalLifetime = beh.MeteorRainDuration + beh.EffectLifetime;
            if (timeSinceFire < 0f || timeSinceFire >= totalLifetime)
            {
                CleanupPreviewAoE();
                return;
            }
            Quaternion rotation = Quaternion.LookRotation(_previewTarget.transform.forward, Vector3.up);
            EnsurePreviewEffectInstance(prefab, center, rotation, chargeRatio);
        }

        /// <summary>
        /// Persistent 模式預覽:整段 (Delay+Duration+Lifetime) 顯示 AoE Prefab
        /// 內部 _indicatorRoot/_effectRoot 切換在 runtime AoEBehaviour 才會發生,編輯器預覽只負責「prefab 在不在」
        /// </summary>
        private void UpdatePreviewAoEPersistent(GameObject prefab, AoEBehaviour beh, Vector3 center, float chargeRatio, float timeSinceFire, bool isScrubbing, float deltaTime)
        {
            float totalLifetime = beh.Delay + beh.Duration + beh.EffectLifetime;
            if (timeSinceFire < 0f || timeSinceFire >= totalLifetime)
            {
                CleanupPreviewAoE();
                return;
            }
            Quaternion rotation = Quaternion.LookRotation(_previewTarget.transform.forward, Vector3.up);
            EnsurePreviewEffectInstance(prefab, center, rotation, chargeRatio);
        }

        /// <summary>
        /// 透過 AoEBehaviour.UpdatePreview 套用 transform + DecalProjector.size 縮放
        /// 走相同 API 確保編輯器與 runtime 行為一致(DecalScaleMode 是 ScaleInvariant,
        /// 必須走 SyncDecalSize 才能讓貼花真的跟著蓄力放大)
        /// </summary>
        private void EnsurePreviewEffectInstance(GameObject prefab, Vector3 center, Quaternion rotation, float chargeRatio)
        {
            if (prefab == null)
            {
                if (_previewAoEEffectInstance != null)
                {
                    DestroyImmediate(_previewAoEEffectInstance);
                    _previewAoEEffectInstance = null;
                }
                return;
            }
            if (_previewAoEEffectInstance == null)
            {
                _previewAoEEffectInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                _previewAoEEffectInstance.hideFlags = HideFlags.HideAndDontSave;
                foreach (var mb in _previewAoEEffectInstance.GetComponentsInChildren<MonoBehaviour>())
                {
                    mb.enabled = false;
                }
                foreach (var ps in _previewAoEEffectInstance.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    ps.Clear();
                }
            }
            // AoEBehaviour 是 MonoBehaviour,被 disable 也能呼叫 public API(UpdatePreview 內部只動 transform 與 size)
            var aoe = _previewAoEEffectInstance.GetComponent<AoEBehaviour>();
            if (aoe != null)
            {
                aoe.UpdatePreview(center, rotation, chargeRatio);
            }
            else
            {
                _previewAoEEffectInstance.transform.SetPositionAndRotation(center, rotation);
            }
        }

        private void SimulateAoEParticles(GameObject instance, float time, bool isScrubbing, float deltaTime)
        {
            if (instance == null) return;
            var systems = instance.GetComponentsInChildren<ParticleSystem>();
            if (systems.Length == 0) return;
            if (isScrubbing)
            {
                foreach (var ps in systems)
                {
                    ps.Simulate(Mathf.Max(0f, time), true, true);
                }
            }
            else
            {
                foreach (var ps in systems)
                {
                    ps.Simulate(deltaTime, true, false);
                }
            }
        }

        private void CleanupPreviewAoE()
        {
            if (_previewAoEEffectInstance != null)
            {
                DestroyImmediate(_previewAoEEffectInstance);
                _previewAoEEffectInstance = null;
            }
            if (_previewAoEIndicatorInstance != null)
            {
                DestroyImmediate(_previewAoEIndicatorInstance);
                _previewAoEIndicatorInstance = null;
            }
        }

        /// <summary>
        /// 根據 CueTag 查找對應的 VFX Cue
        /// </summary>
        private VFXCue FindVFXCueByCueTag(GameplayTag cueTag)
        {
            if (!cueTag.IsValid) return null;

            // 第一次呼叫時掃一次全專案,之後純查 cache(包含 negative entry,避免反覆掃)。
            // 卡頓元兇修復: 過去若 cueTag 無對應 Cue,cache 從不命中,每次 OnGUI Repaint
            // 都重新 FindAssets("t:VFXCue") 掃整個專案 → 卡到跳 "Hold on" 對話框。
            if (!_vfxCueCacheBuilt)
            {
                RebuildVFXCueCache();
            }

            string tagName = cueTag.TagName;
            if (_vfxCueCache.TryGetValue(tagName, out VFXCue cachedCue))
            {
                return cachedCue; // 可能是 null(代表「掃過但無對應 Cue」),這也是有效答案
            }
            return null;
        }

        /// <summary>
        /// 全專案掃一次 VFXCue 並建 Tag → Cue 字典。供 FindVFXCueByCueTag 用。
        /// 加新 / 刪舊 VFXCue 後需呼叫 InvalidateVFXCueCache() 才會生效。
        /// </summary>
        private void RebuildVFXCueCache()
        {
            _vfxCueCache.Clear();
            string[] guids = AssetDatabase.FindAssets("t:VFXCue");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                VFXCue cue = AssetDatabase.LoadAssetAtPath<VFXCue>(path);
                if (cue != null && cue.CueTag.IsValid)
                {
                    _vfxCueCache[cue.CueTag.TagName] = cue;
                }
            }
            _vfxCueCacheBuilt = true;
        }

        private void InvalidateVFXCueCache()
        {
            _vfxCueCache.Clear();
            _vfxCueCacheBuilt = false;
        }

        /// <summary>
        /// 為 TimeLineEvent 創建對應的 VFX Cue
        /// </summary>
        private void CreateVFXCueForEvent(TimelineEvent evt)
        {
            if (!evt.CueTag.IsValid)
            {
                EditorUtility.DisplayDialog("錯誤", "TimeLineEvent 的 CueTag 無效，無法創建 VFX Cue。", "確定");
                return;
            }

            string suggestedPath = "Assets/Script/GAS/Cues/Implementations/";
            if (!AssetDatabase.IsValidFolder(suggestedPath))
            {
                suggestedPath = "Assets/";
            }

            string fileName = evt.CueTag.TagName.Replace(".", "_");
            string fullPath = EditorUtility.SaveFilePanelInProject(
                "創建 VFX Cue",
                $"Cue_{fileName}.asset",
                "asset",
                "選擇保存 VFX Cue 的位置",
                suggestedPath);

            if (string.IsNullOrEmpty(fullPath)) return;

            var newCue = ScriptableObject.CreateInstance<VFXCue>();
            newCue.CueTag = evt.CueTag;
            newCue.UseParameterTransform = true;
            newCue.AdditionalScale = Vector3.one;
            newCue.AttachToTarget = evt.IsAttached;
            newCue.AutoDestroyTime = 2.0f;
            newCue.IsParticleSystem = true;
            newCue.DestroyOnParticleComplete = true;

            AssetDatabase.CreateAsset(newCue, fullPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            InvalidateVFXCueCache();
            EditorGUIUtility.PingObject(newCue);
            Selection.activeObject = newCue;

            EditorUtility.DisplayDialog("成功",
                $"VFX Cue 已創建：{fullPath}\n\n請設定 VFX Prefab 屬性以顯示特效。",
                "確定");

            RefreshEventInspector();
        }

        private void FullResetPreview()
        {
            _isPreviewing = false;
            // 先釋放 Sampler(PlayableGraph 必須在 AnimationMode 結束前 Destroy)
            _rmSampler?.Dispose();
            _rmSampler = null;
            _cachedClip = null;
            _cachedAnimator = null;

            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();

            if (_previewTarget != null)
            {
                _previewTarget.transform.SetPositionAndRotation(_initialPos, _initialRot);
            }
            _previewTarget = null;
            _animationTarget = null;

            foreach (var kvp in _previewVFXs)
            {
                if (kvp.Value != null) DestroyImmediate(kvp.Value);
            }
            _previewVFXs.Clear();
            CleanupPreviewProjectile();
            CleanupPreviewAoE();
        }

        #endregion

        #region Ruler

        private void DrawRuler(VisualElement container, float maxTime)
        {
            container.Clear();
            container.Add(new VisualElement { style = { width = 120 } });

            var ruler = new VisualElement { style = { width = maxTime * PIXELS_PER_SECOND } };
            int steps = Mathf.CeilToInt(maxTime * 10);
            for (int i = 0; i <= steps; i++)
            {
                float t = i * 0.1f;
                var lbl = new Label(t.ToString("F1"))
                {
                    style =
                    {
                        position = Position.Absolute,
                        left = t * PIXELS_PER_SECOND,
                        color = Color.gray,
                        fontSize = 10
                    }
                };
                ruler.Add(lbl);
            }
            container.Add(ruler);
        }

        #endregion

        #region Scene GUI

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_previewTarget == null) return;

            // 繪製所有 HitWindow 的 Hitbox（僅近戰）
            if (IsMelee && CurrentMelee?.HitWindows != null)
            {
                foreach (var hw in CurrentMelee.HitWindows)
                {
                    bool isActive = _currentTime >= hw.StartTime && _currentTime <= hw.EndTime;
                    bool isSelected = hw == _selectedHitWindow;

                    Color boxColor = isSelected ? Color.yellow : (isActive ? Color.red : new Color(1, 0, 0, 0.3f));

                    Transform origin = FindChildRecursive(_previewTarget.transform, hw.SocketName);
                    if (origin == null) origin = _previewTarget.transform;

                    // 角色被父物件放大時，Hitbox 框框跟著放大（runtime 物理判定也是用 lossyScale 放大的 box，需保持視覺一致）
                    Vector3 originLossy = origin.lossyScale;
                    Vector3 worldBoxSize = Vector3.Scale(hw.Size, originLossy);
                    float worldDiscRadius = hw.Size.x * Mathf.Max(Mathf.Abs(originLossy.x), Mathf.Abs(originLossy.z));
                    Matrix4x4 mtx = Matrix4x4.TRS(origin.TransformPoint(hw.Offset), origin.rotation, Vector3.one);

                    using (new Handles.DrawingScope(boxColor, mtx))
                    {
                        if (hw.Shape == HitboxShape.Box)
                            Handles.DrawWireCube(Vector3.zero, worldBoxSize);
                        else
                            Handles.DrawWireDisc(Vector3.zero, Vector3.up, worldDiscRadius);
                    }

                    // Raycast Trail 視覺化
                    if (hw.UseRaycastTrail && (isSelected || isActive))
                    {
                        int segments = hw.TrailSegments;
                        Color trailColor = isSelected ? new Color(0.4f, 1f, 0.8f) : new Color(0.4f, 1f, 0.8f, 0.5f);
                        Vector3 trailStartWorld = origin.TransformPoint(hw.TrailStartOffset);
                        Vector3 trailEndWorld = origin.TransformPoint(hw.TrailEndOffset);
                        using (new Handles.DrawingScope(trailColor))
                        {
                            // 繪製武器軸線上的各取樣點
                            for (int s = 0; s < segments; s++)
                            {
                                float t = segments > 1 ? (float)s / (segments - 1) : 0f;
                                Vector3 localPos = Vector3.Lerp(hw.TrailStartOffset, hw.TrailEndOffset, t);
                                Vector3 worldSegPos = origin.TransformPoint(localPos);
                                float sphereSize = HandleUtility.GetHandleSize(worldSegPos) * 0.04f;
                                if (hw.TrailRayRadius > 0f)
                                {
                                    Handles.DrawWireDisc(worldSegPos, sceneView.camera.transform.forward, hw.TrailRayRadius);
                                }
                                else
                                {
                                    Handles.SphereHandleCap(0, worldSegPos, Quaternion.identity, sphereSize, EventType.Repaint);
                                }
                            }
                            // 武器軸線（根部→末端）
                            Handles.DrawLine(trailStartWorld, trailEndWorld, 2f);
                            // 標籤
                            Handles.Label(trailStartWorld + Vector3.up * 0.1f, "Trail Start",
                                new GUIStyle("WhiteLabel") { fontSize = 9, normal = { textColor = trailColor } });
                            Handles.Label(trailEndWorld + Vector3.up * 0.1f, "Trail End",
                                new GUIStyle("WhiteLabel") { fontSize = 9, normal = { textColor = trailColor } });
                        }
                        // 選中時可拖動起點和終點
                        if (isSelected)
                        {
                            EditorGUI.BeginChangeCheck();
                            Vector3 newStartWorld = Handles.PositionHandle(trailStartWorld, origin.rotation);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(_currentData, "Move Trail Start");
                                hw.TrailStartOffset = origin.InverseTransformPoint(newStartWorld);
                                EditorUtility.SetDirty(_currentData);
                            }
                            EditorGUI.BeginChangeCheck();
                            Vector3 newEndWorld = Handles.PositionHandle(trailEndWorld, origin.rotation);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(_currentData, "Move Trail End");
                                hw.TrailEndOffset = origin.InverseTransformPoint(newEndWorld);
                                EditorUtility.SetDirty(_currentData);
                            }
                        }
                    }

                    if (isSelected)
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 worldPos = origin.TransformPoint(hw.Offset);
                        Vector3 newWorldPos = Handles.PositionHandle(worldPos, origin.rotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(_currentData, "Move Hitbox");
                            hw.Offset = origin.InverseTransformPoint(newWorldPos);
                            EditorUtility.SetDirty(_currentData);
                        }
                    }
                }
            }

            // 遠程攻擊：繪製投射物生成點
            if (IsRanged)
            {
                DrawRangedSpawnPointGizmo();
                DrawAoEPreviewGizmo();
            }

            // 繪製所有 Timeline Event 的位置指示（共用）
            if (_currentData?.TimelineEvents != null)
            {
                foreach (var evt in _currentData.TimelineEvents)
                {
                    bool isSelected = evt == _selectedEvent;
                    bool isActive = _currentTime >= evt.TriggerTime && _currentTime <= evt.TriggerTime + 2.0f;

                    if (!isSelected && !isActive) continue;

                    Transform socket = FindChildRecursive(_previewTarget.transform, evt.SocketName);
                    if (socket == null) socket = _previewTarget.transform;

                    Vector3 worldPos = socket.TransformPoint(evt.PositionOffset);
                    Quaternion worldRot = socket.rotation * Quaternion.Euler(evt.RotationOffset);

                    var vfxCue = FindVFXCueByCueTag(evt.CueTag);
                    bool hasPrefab = vfxCue != null && vfxCue.VFXPrefab != null;

                    Color handleColor = isSelected ? Color.yellow : (isActive ? Color.cyan : new Color(0.5f, 0.5f, 0.5f, 0.5f));
                    if (!hasPrefab && isSelected) handleColor = Color.red;

                    float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.15f;

                    using (new Handles.DrawingScope(handleColor))
                    {
                        Handles.ArrowHandleCap(0, worldPos, worldRot, handleSize, EventType.Repaint);
                        Handles.ArrowHandleCap(0, worldPos, worldRot * Quaternion.Euler(0, 90, 0), handleSize * 0.8f, EventType.Repaint);
                        Handles.ArrowHandleCap(0, worldPos, worldRot * Quaternion.Euler(-90, 0, 0), handleSize * 0.8f, EventType.Repaint);

                        // gizmo 立方體跟隨父物件 lossyScale 一起放大，視覺大小與實際 VFX (Follower 用 evt.Scale × lossyScale) 一致
                        Vector3 socketLossy = socket.lossyScale;
                        Vector3 visualScale = Vector3.Scale(evt.Scale, socketLossy);
                        Matrix4x4 matrix = Matrix4x4.TRS(worldPos, worldRot, visualScale * 0.2f);
                        using (new Handles.DrawingScope(matrix))
                        {
                            Handles.DrawWireCube(Vector3.zero, Vector3.one);
                        }
                    }

                    string label = string.IsNullOrEmpty(evt.Name) ? "Event" : evt.Name;
                    if (isActive) label += " [ACTIVE]";
                    if (!hasPrefab) label += " [NO VFX PREFAB]";

                    GUIStyle labelStyle = new GUIStyle("WhiteLabel") { fontSize = 10 };
                    if (!hasPrefab) labelStyle.normal.textColor = Color.red;

                    Handles.Label(worldPos + Vector3.up * (handleSize + 0.2f), label, labelStyle);

                    if (isSelected)
                    {
                        EditorGUI.BeginChangeCheck();

                        Vector3 newWorldPos = Handles.PositionHandle(worldPos, worldRot);
                        Quaternion newWorldRot = Handles.RotationHandle(worldRot, worldPos);
                        float scaleHandleSize = HandleUtility.GetHandleSize(newWorldPos);
                        // Scale handle 顯示「視覺世界大小」(evt.Scale × lossyScale) 而不是 evt.Scale,
                        // 拖完反算回 evt.Scale = newVisual ÷ lossyScale。讓設計師拖手把時直覺對應實際 VFX 大小。
                        Vector3 socketLossy = socket.lossyScale;
                        Vector3 currentVisual = Vector3.Scale(evt.Scale, socketLossy);
                        Vector3 newVisual = Handles.ScaleHandle(currentVisual, newWorldPos, newWorldRot, scaleHandleSize);

                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(_currentData, "Edit Event Transform");

                            evt.PositionOffset = socket.InverseTransformPoint(newWorldPos);
                            evt.RotationOffset = (Quaternion.Inverse(socket.rotation) * newWorldRot).eulerAngles;
                            evt.Scale = new Vector3(
                                Mathf.Abs(socketLossy.x) > 0.0001f ? newVisual.x / socketLossy.x : evt.Scale.x,
                                Mathf.Abs(socketLossy.y) > 0.0001f ? newVisual.y / socketLossy.y : evt.Scale.y,
                                Mathf.Abs(socketLossy.z) > 0.0001f ? newVisual.z / socketLossy.z : evt.Scale.z);

                            EditorUtility.SetDirty(_currentData);
                            UpdatePreviewVFX(true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 繪製遠程攻擊的投射物生成點 Gizmo（可互動拖動）
        /// </summary>
        private void DrawRangedSpawnPointGizmo()
        {
            if (CurrentRanged == null || _previewTarget == null) return;

            Transform defaultSocket = FindChildRecursive(_previewTarget.transform, CurrentRanged.SpawnSocketName);
            if (defaultSocket == null) defaultSocket = _previewTarget.transform;

            Vector3 spawnPos = defaultSocket.TransformPoint(CurrentRanged.SpawnOffset);
            Vector3 fireDir = _previewTarget.transform.forward;

            float handleSize = HandleUtility.GetHandleSize(spawnPos) * 0.1f;

            // 生成點（橘色球）
            using (new Handles.DrawingScope(new Color(1f, 0.6f, 0f)))
            {
                Handles.SphereHandleCap(0, spawnPos, Quaternion.identity, handleSize * 2, EventType.Repaint);
                // 發射方向箭頭
                Handles.ArrowHandleCap(0, spawnPos, Quaternion.LookRotation(fireDir), handleSize * 5, EventType.Repaint);
            }

            Handles.Label(spawnPos + Vector3.up * 0.3f, "Spawn Point",
                new GUIStyle("WhiteLabel") { fontSize = 10, normal = { textColor = new Color(1f, 0.6f, 0f) } });

            // 可互動位置把手
            EditorGUI.BeginChangeCheck();
            Vector3 newSpawnPos = Handles.PositionHandle(spawnPos, defaultSocket.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_currentData, "Move Spawn Point");
                CurrentRanged.SpawnOffset = defaultSocket.InverseTransformPoint(newSpawnPos);
                EditorUtility.SetDirty(_currentData);
            }

            // 多發射擊：繪製每個 FireEvent 的生成點與方向
            if (CurrentRanged.FireEvents != null)
            {
                for (int i = 0; i < CurrentRanged.FireEvents.Count; i++)
                {
                    var evt = CurrentRanged.FireEvents[i];
                    Transform evtSocket = ResolveFireEventSocket(evt, defaultSocket);
                    Vector3 evtPos = evtSocket.TransformPoint(evt.SpawnOffset);
                    float evtHandleSize = HandleUtility.GetHandleSize(evtPos) * 0.08f;
                    // 計算發射方向
                    Vector3 evtFireDir = evt.DirectionOffset != Vector3.zero
                        ? _previewTarget.transform.rotation * Quaternion.Euler(evt.DirectionOffset) * Vector3.forward
                        : _previewTarget.transform.forward;
                    // 每發覆寫用不同顏色標示
                    bool hasSocketOverride = !string.IsNullOrEmpty(evt.SpawnSocketNameOverride);
                    bool hasOverride = evt.HitEffectOverride != null || evt.BaseDamageOverride > 0f || evt.HitCueTagOverride.IsValid || hasSocketOverride;
                    Color evtColor = hasOverride ? new Color(0.3f, 1f, 0.6f) : new Color(1f, 0.8f, 0.3f);
                    using (new Handles.DrawingScope(evtColor))
                    {
                        Handles.SphereHandleCap(0, evtPos, Quaternion.identity, evtHandleSize * 2, EventType.Repaint);
                        // 發射方向箭頭
                        Handles.ArrowHandleCap(0, evtPos, Quaternion.LookRotation(evtFireDir), evtHandleSize * 5, EventType.Repaint);
                        // DirectionOffset 不為零時額外標記
                        if (evt.DirectionOffset != Vector3.zero)
                        {
                            Handles.DrawDottedLine(evtPos, evtPos + evtFireDir * 1.5f, 3f);
                        }
                    }
                    // 標籤（包含覆寫資訊）
                    string evtLabel = $"Fire {i + 1}";
                    if (evt.BaseDamageOverride > 0f) evtLabel += $" DMG:{evt.BaseDamageOverride}";
                    if (hasSocketOverride) evtLabel += $" @{evt.SpawnSocketNameOverride}";
                    if (hasOverride) evtLabel += " [Override]";
                    Handles.Label(evtPos + Vector3.up * 0.2f, evtLabel,
                        new GUIStyle("WhiteLabel") { fontSize = 9, normal = { textColor = evtColor } });
                    EditorGUI.BeginChangeCheck();
                    Vector3 newEvtPos = Handles.PositionHandle(evtPos, evtSocket.rotation);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_currentData, $"Move Fire Event {i + 1}");
                        evt.SpawnOffset = evtSocket.InverseTransformPoint(newEvtPos);
                        EditorUtility.SetDirty(_currentData);
                    }
                }
            }
        }

        /// <summary>
        /// 繪製 AoE 預覽 gizmo — 依 RangedAttackData.AoEOriginMode 解析中心,從 prefab 上 AoEBehaviour 讀範圍
        /// </summary>
        private void DrawAoEPreviewGizmo()
        {
            if (CurrentRanged == null || _previewTarget == null) return;
            if (CurrentRanged.AttackType != RangedAttackType.AoETargeted
                && CurrentRanged.AttackType != RangedAttackType.AoEAtTarget) return;

            GameObject aoePrefab = CurrentRanged.AoEPrefab;
            if (aoePrefab == null) return;
            AoEBehaviour beh = aoePrefab.GetComponent<AoEBehaviour>();
            if (beh == null) return;

            Vector3 center = ResolveAoEPreviewCenter();

            float chargeRatio = 0f;
            if (CurrentRanged.Charge != ChargeMode.None)
            {
                chargeRatio = CurrentRanged.ComputeVisualChargeRatio(_currentTime);
            }

            // 走 AoEBehaviour 的分段曲線(與 runtime/UpdateAoEPreview 全部一致)
            float radius = beh.Radius * beh.GetEffectiveScaleMultiplier(chargeRatio);

            Color ringColor;
            if (chargeRatio >= 0.999f) ringColor = new Color(1f, 0.3f, 0.2f, 0.9f);
            else if (chargeRatio > 0f) ringColor = new Color(1f, 0.6f, 0.1f, 0.85f);
            else ringColor = new Color(1f, 0.6f, 0.1f, 0.35f);

            using (new Handles.DrawingScope(ringColor))
            {
                Handles.DrawWireDisc(center + Vector3.up * 0.02f, Vector3.up, radius);
                Handles.DrawWireDisc(center + Vector3.up * 0.02f, Vector3.up, radius * 0.5f);
                float dotSize = HandleUtility.GetHandleSize(center) * 0.1f;
                Handles.SphereHandleCap(0, center, Quaternion.identity, dotSize, EventType.Repaint);
            }

            string label = $"AoE [{CurrentRanged.AoEOriginMode}] r={radius:F1}m";
            if (beh.ScaleRadiusWithCharge && chargeRatio > 0f)
            {
                label += $"  charge={chargeRatio:P0}";
            }
            Handles.Label(center + Vector3.up * (radius * 0.1f + 0.3f), label,
                new GUIStyle("WhiteLabel") { fontSize = 10, normal = { textColor = ringColor } });
        }

        /// <summary>
        /// 預覽模式下解析 AoE 中心(以 _previewTarget 代表玩家) — 讀 RangedAttackData.AoEOriginMode
        /// </summary>
        private Vector3 ResolveAoEPreviewCenter()
        {
            Transform owner = _previewTarget.transform;
            switch (CurrentRanged.AoEOriginMode)
            {
                case AoEOriginMode.PlayerForward:
                    return owner.position + owner.forward * CurrentRanged.AoEForwardDistance;
                case AoEOriginMode.LockedTarget:
                case AoEOriginMode.ScreenAim:
                default:
                    // 預覽環境沒有真實鎖定/螢幕射線,fallback 到玩家前方 6m 當示意
                    return owner.position + owner.forward * 6f;
            }
        }

        private Transform FindChildRecursive(Transform parent, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var result = parent.Find(name);
            if (result != null) return result;
            foreach (Transform child in parent)
            {
                result = FindChildRecursive(child, name);
                if (result != null) return result;
            }
            return null;
        }

        #endregion

        // Manipulators 已提取至 GASEditorManipulators.cs（UnifiedClipDragManipulator、ScrubberManipulator）
    }
}
#endif
