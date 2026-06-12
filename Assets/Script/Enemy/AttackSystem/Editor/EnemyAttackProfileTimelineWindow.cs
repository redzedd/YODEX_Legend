using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Enemy.AttackSystem.EditorTools
{
    /// <summary>
    /// 攻擊招式時間軸編輯器 — Timeline 風格視窗，視覺化編輯 EnemyAttackProfile 的 VFX 事件。
    /// 上方：當前 Profile 與動畫資訊。中間：時間軸（標尺 + 招架軌 + 命中軌 + VFX 軌）。下方：選中事件的設定面板。
    /// 透過 SerializedObject 操作支援 Undo / Save Asset。
    /// </summary>
    public class EnemyAttackProfileTimelineWindow : EditorWindow
    {
        // ─── 視窗開啟 ─────────────────────────────────────────────────
        [MenuItem("YODEX/敵人/攻擊招式時間軸編輯器")]
        public static void OpenWindow()
        {
            EnsureWindow();
        }

        // 從 Inspector 按鈕 / 雙擊 .asset 進入：開窗並指定編輯目標
        public static void OpenForProfile(EnemyAttackProfile profile)
        {
            EnemyAttackProfileTimelineWindow window = EnsureWindow();
            if (profile != null && window._profile != profile)
            {
                window._profile = profile;
                window.ClearSelection();
                window._serializedProfile = null; // 下個 OnGUI 會自動重建
                window.Repaint();
            }
            window.Focus();
        }

        private void ClearSelection()
        {
            _selectionKind = SelectionKind.None;
            _selectedEventIndex = -1;
            ResyncPreviewIfActive();
        }

        private void SelectVfxEvent(int idx)
        {
            _selectionKind = SelectionKind.VfxEvent;
            _selectedEventIndex = idx;
            ResyncPreviewIfActive();
        }

        // hitboxIndex：-1 = 主 Hitbox，0+ = ExtraHitboxes[idx]
        private void SelectHitbox(int hitboxIndex = -1)
        {
            _selectionKind = SelectionKind.Hitbox;
            _selectedEventIndex = -1;
            _selectedHitboxIndex = hitboxIndex;
            ResyncPreviewIfActive();
        }

        private bool IsExtraHitboxSelected(int idx)
        {
            return _selectionKind == SelectionKind.Hitbox && _selectedHitboxIndex == idx;
        }

        private bool IsPrimaryHitboxSelected()
        {
            return _selectionKind == SelectionKind.Hitbox && _selectedHitboxIndex == -1;
        }

        private void ResyncPreviewIfActive()
        {
            if (_previewActive)
            {
                SyncPreviewVfx(_previewTime);
                SceneView.RepaintAll();
            }
        }

        private bool IsVfxSelected(int idx)
        {
            return _selectionKind == SelectionKind.VfxEvent && _selectedEventIndex == idx;
        }

        private static EnemyAttackProfileTimelineWindow EnsureWindow()
        {
            EnemyAttackProfileTimelineWindow window = GetWindow<EnemyAttackProfileTimelineWindow>();
            window.titleContent = new GUIContent("攻擊時間軸");
            window.minSize = new Vector2(640, 540);
            window.Show();
            return window;
        }

        // ─── 狀態 ────────────────────────────────────────────────────
        // [SerializeField] 讓視窗記住跨 domain reload 編輯中的 Profile 與選中事件
        [SerializeField] private EnemyAttackProfile _profile;
        [SerializeField] private int _selectedEventIndex = -1;
        // Hitbox 選取索引：-1 = 主 Hitbox，0+ = ExtraHitboxes[idx]
        [SerializeField] private int _selectedHitboxIndex = -1;
        [SerializeField] private SelectionKind _selectionKind = SelectionKind.None;

        // VFX marker 自動分軌：同時間或太靠近的 marker 會被排到下方 lane 避免重疊
        private int[] _vfxLaneCache;
        private int _vfxLaneCount = 1;

        private SerializedObject _serializedProfile;
        private Vector2 _bottomPanelScroll;

        private enum SelectionKind
        {
            None,
            VfxEvent,
            Hitbox,
        }

        // ─── 預覽狀態（Edit Mode AnimationMode + VFX 即時生成） ───────
        private bool _previewActive;
        private MonoBehaviour _previewTargetController;
        private GameObject _previewAnimatorRoot;
        private float _previewTime;
        // 預覽生成的 VFX 實體 — 每筆對應一個 VfxEvents[index]
        private readonly List<PreviewVfxInstance> _previewInstances = new List<PreviewVfxInstance>();

        private struct PreviewVfxInstance
        {
            public int EventIndex;
            public GameObject Instance;
            public ParticleSystem[] Particles;
        }

        // ─── 版面常數 ────────────────────────────────────────────────
        private const float TIMELINE_LEFT_PADDING = 60f;
        private const float TIMELINE_RIGHT_PADDING = 20f;
        private const float TRACK_HEIGHT = 26f;
        private const float TRACK_SPACING = 6f;
        private const float RULER_HEIGHT = 22f;
        private const float MARKER_SIZE = 14f;
        private const float MARKER_LABEL_HEIGHT = 12f;
        // 同軌多 lane 間的垂直間隔
        private const float LANE_OFFSET = 18f;
        // 命中軌邊緣抓取拖曳的容差（像素）— 點在邊緣 N 像素內視為拖曳 HitStart/HitEnd
        private const float HIT_EDGE_GRAB_TOLERANCE = 6f;
        // 拖曳時間吸附粒度（秒）
        private const float TIME_SNAP = 0.01f;

        // ─── 顏色 ────────────────────────────────────────────────────
        private static readonly Color RULER_LINE_COLOR = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color PARRY_FLASH_COLOR = new Color(1f, 0.85f, 0.15f, 0.85f);
        private static readonly Color PARRY_BUFFER_COLOR = new Color(1f, 0.85f, 0.15f, 0.4f);
        private static readonly Color HIT_COLOR = new Color(0.95f, 0.25f, 0.25f, 0.85f);
        private static readonly Color HIT_COLOR_SELECTED = new Color(1f, 0.55f, 0.55f, 0.95f);
        // 額外 Hitbox 用較橙的色帶，避免跟主 Hitbox 的純紅混淆
        private static readonly Color EXTRA_HIT_COLOR = new Color(0.95f, 0.55f, 0.2f, 0.85f);
        private static readonly Color EXTRA_HIT_COLOR_SELECTED = new Color(1f, 0.75f, 0.45f, 0.95f);
        private static readonly Color VFX_MARKER_COLOR = new Color(0.3f, 0.75f, 1f, 1f);
        private static readonly Color VFX_MARKER_SELECTED_COLOR = new Color(0.55f, 0.95f, 1f, 1f);
        private static readonly Color VFX_MARKER_OUTLINE_COLOR = Color.white;
        private static readonly Color TRACK_BG = new Color(0.18f, 0.18f, 0.18f, 1f);
        private static readonly Color SEPARATOR_COLOR = new Color(0.4f, 0.4f, 0.4f, 1f);
        private static readonly Color BLOCK_LABEL_COLOR = new Color(0.1f, 0.1f, 0.1f, 1f);
        private static readonly Color PLAYHEAD_COLOR = new Color(0.2f, 1f, 0.6f, 0.95f);
        private static readonly Color PREVIEW_BAR_ACTIVE_BG = new Color(0.2f, 0.5f, 0.3f, 0.5f);

        // ─── 生命週期 ─────────────────────────────────────────────────
        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            StopPreview();
        }

        // Play Mode 切換時務必清掉預覽 — 否則 AnimationMode 會卡住、預覽 VFX 殘留進 Play 場景
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                StopPreview();
            }
        }

        private void OnGUI()
        {
            EnsureSerializedProfile();
            _serializedProfile?.Update();

            DrawTopBar();
            DrawSeparator();
            if (_profile == null)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.HelpBox("請拖入 EnemyAttackProfile 資產到上方「編輯中招式」欄位開始編輯", MessageType.Info);
                return;
            }

            DrawPreviewBar();
            DrawSeparator();
            DrawTimeline();
            EditorGUILayout.Space(6);
            DrawSeparator();
            DrawToolbar();
            DrawSeparator();
            DrawSelectedEventPanel();

            bool propertiesChanged = _serializedProfile != null && _serializedProfile.ApplyModifiedProperties();
            // 編輯中改動屬性（marker 拖曳、欄位輸入...）後若預覽開啟，重新同步 VFX 讓設計師即時看見變化
            if (propertiesChanged && _previewActive)
            {
                SyncPreviewVfx(_previewTime);
                SceneView.RepaintAll();
            }
        }

        // ─── 序列化物件管理 ───────────────────────────────────────────
        private void EnsureSerializedProfile()
        {
            if (_profile == null)
            {
                _serializedProfile = null;
                ClearSelection();
                return;
            }
            if (_serializedProfile == null || _serializedProfile.targetObject != _profile)
            {
                _serializedProfile = new SerializedObject(_profile);
                ClearSelection();
            }
        }

        // ─── 頂部 Profile 選擇 ────────────────────────────────────────
        private void DrawTopBar()
        {
            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            EnemyAttackProfile newProfile = (EnemyAttackProfile)EditorGUILayout.ObjectField(
                "編輯中招式", _profile, typeof(EnemyAttackProfile), false);
            if (EditorGUI.EndChangeCheck())
            {
                _profile = newProfile;
                EnsureSerializedProfile();
            }
            if (_profile != null)
            {
                AnimationClip clip = _profile.AnimationClip;
                string info = clip != null
                    ? $"動畫片段：{clip.name}    長度：{_profile.Duration:F2}s    招架窗：{_profile.ParryWindowDuration:F2}s    命中窗：{_profile.HitStart:F2}s ~ {_profile.HitEnd:F2}s"
                    : "動畫片段：（未指定）— 請先在 Inspector 設定 AnimationClip";
                EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(4);
        }

        private void DrawSeparator()
        {
            Rect r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, SEPARATOR_COLOR);
        }

        // ─── 時間軸繪製 ───────────────────────────────────────────────
        private void DrawTimeline()
        {
            float duration = _profile.Duration;
            if (duration <= 0f)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.HelpBox("動畫片段未指定或長度為 0 — 無法繪製時間軸。請先在 Inspector 設定 AnimationClip", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(10);

            float barLeft = TIMELINE_LEFT_PADDING;
            float barRight = position.width - TIMELINE_RIGHT_PADDING;
            float barWidth = barRight - barLeft;
            if (barWidth < 100f)
            {
                EditorGUILayout.HelpBox("視窗太窄，請拉寬", MessageType.Warning);
                return;
            }

            // 先算 lane 數量（影響 VFX 軌高度與總高）
            RecomputeVfxLanes(barWidth, duration);
            float vfxTrackHeight = TRACK_HEIGHT + Mathf.Max(0, _vfxLaneCount - 1) * LANE_OFFSET;

            float totalHeight = MARKER_LABEL_HEIGHT + 4f + RULER_HEIGHT
                + (TRACK_HEIGHT + TRACK_SPACING) * 2
                + vfxTrackHeight + TRACK_SPACING + 20f;
            Rect timelineArea = GUILayoutUtility.GetRect(0, totalHeight, GUILayout.ExpandWidth(true));

            // 1. 標尺（預覽開啟時也是 playhead 拖曳區）
            float rulerY = timelineArea.y + MARKER_LABEL_HEIGHT + 4f;
            Rect rulerRect = new Rect(barLeft, rulerY, barWidth, RULER_HEIGHT);
            DrawRuler(rulerRect, duration);
            if (_previewActive)
            {
                HandlePlayheadDrag(rulerRect, duration);
            }

            float trackY = rulerRect.yMax + 4f;

            // 2. 招架軌
            Rect parryTrack = new Rect(barLeft, trackY, barWidth, TRACK_HEIGHT);
            DrawTrackBackground(parryTrack, "招架");
            DrawTimeRange(parryTrack, 0f, _profile.ParryFlashDuration, duration, PARRY_FLASH_COLOR, "黃光");
            DrawTimeRange(parryTrack, _profile.ParryFlashDuration, _profile.ParryWindowDuration, duration, PARRY_BUFFER_COLOR, "緩衝");
            trackY += TRACK_HEIGHT + TRACK_SPACING;

            // 3. 命中軌 — 主 Hitbox + 額外 Hitboxes 都顯示在這軌
            Rect hitTrack = new Rect(barLeft, trackY, barWidth, TRACK_HEIGHT);
            DrawTrackBackground(hitTrack, "命中");
            // 主 Hitbox
            DrawTimeRange(hitTrack, _profile.HitStart, _profile.HitEnd, duration,
                IsPrimaryHitboxSelected() ? HIT_COLOR_SELECTED : HIT_COLOR, "Hit 0");
            // 額外 Hitboxes — 用不同色帶區分
            if (_profile.ExtraHitboxes != null)
            {
                for (int i = 0; i < _profile.ExtraHitboxes.Count; i++)
                {
                    EnemyAttackHitboxData hb = _profile.ExtraHitboxes[i];
                    if (hb == null) continue;
                    Color extraColor = IsExtraHitboxSelected(i) ? EXTRA_HIT_COLOR_SELECTED : EXTRA_HIT_COLOR;
                    DrawTimeRange(hitTrack, hb.HitStart, hb.HitEnd, duration, extraColor, $"Hit {i + 1}");
                }
            }
            HandleHitTrackInteraction(hitTrack, duration);
            trackY += TRACK_HEIGHT + TRACK_SPACING;

            // 4. VFX 軌（含 marker 選取與拖曳互動）— 用 lane-based 排版
            Rect vfxTrack = new Rect(barLeft, trackY, barWidth, vfxTrackHeight);
            DrawTrackBackground(vfxTrack, "特效");
            HandleVfxTrackInteraction(vfxTrack, duration);
            DrawVfxMarkers(vfxTrack, duration);

            // 5. Playhead（垂直綠線跨越所有軌道，預覽開啟時才畫）
            if (_previewActive)
            {
                float playheadT = Mathf.Clamp01(_previewTime / duration);
                float playheadX = barLeft + playheadT * barWidth;
                float lineTop = rulerRect.y;
                float lineBottom = vfxTrack.yMax;
                EditorGUI.DrawRect(new Rect(playheadX - 1f, lineTop, 2f, lineBottom - lineTop), PLAYHEAD_COLOR);
            }
        }

        // ─── Lane 計算 ───────────────────────────────────────────────
        // 將同時間（或近到 marker 重疊）的 VFX 事件排到下一個 lane，避免擠在一起
        // 演算法：依時間排序，每個 marker 找第一個「跟 lane 上前一個 marker 間距夠」的 lane
        private void RecomputeVfxLanes(float trackWidth, float duration)
        {
            if (_profile == null || _profile.VfxEvents == null || duration <= 0f)
            {
                _vfxLaneCache = null;
                _vfxLaneCount = 1;
                return;
            }
            int n = _profile.VfxEvents.Count;
            if (_vfxLaneCache == null || _vfxLaneCache.Length < n)
            {
                _vfxLaneCache = new int[Mathf.Max(n, 8)];
            }
            if (n == 0)
            {
                _vfxLaneCount = 1;
                return;
            }

            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) =>
            {
                EnemyAttackVfxEvent ea = _profile.VfxEvents[a];
                EnemyAttackVfxEvent eb = _profile.VfxEvents[b];
                float ta = ea != null ? ea.Time : 0f;
                float tb = eb != null ? eb.Time : 0f;
                return ta.CompareTo(tb);
            });

            const int MAX_LANES = 8;
            float[] laneMaxX = new float[MAX_LANES];
            for (int i = 0; i < MAX_LANES; i++) laneMaxX[i] = float.NegativeInfinity;
            float minSpacing = MARKER_SIZE + 4f;
            int maxLaneUsed = 0;

            for (int k = 0; k < order.Length; k++)
            {
                int idx = order[k];
                EnemyAttackVfxEvent evt = _profile.VfxEvents[idx];
                if (evt == null)
                {
                    _vfxLaneCache[idx] = 0;
                    continue;
                }
                float x = (evt.Time / duration) * trackWidth;
                int lane = 0;
                while (lane < MAX_LANES && x < laneMaxX[lane] + minSpacing) lane++;
                if (lane >= MAX_LANES) lane = MAX_LANES - 1;
                _vfxLaneCache[idx] = lane;
                laneMaxX[lane] = x;
                if (lane > maxLaneUsed) maxLaneUsed = lane;
            }
            _vfxLaneCount = maxLaneUsed + 1;
        }

        private float GetVfxMarkerY(Rect track, int lane)
        {
            // Lane 0 在最頂，每多一 lane 往下加 LANE_OFFSET
            float topPadding = (TRACK_HEIGHT - MARKER_SIZE) * 0.5f;
            return track.y + topPadding + lane * LANE_OFFSET;
        }

        private void DrawRuler(Rect rect, float duration)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), RULER_LINE_COLOR);

            int tickCount = 5;
            GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel);
            labelStyle.alignment = TextAnchor.UpperCenter;
            for (int i = 0; i <= tickCount; i++)
            {
                float t = (float)i / tickCount;
                float x = rect.x + t * rect.width;
                float tickTime = t * duration;
                EditorGUI.DrawRect(new Rect(x, rect.y + rect.height * 0.5f, 1, rect.height * 0.5f), RULER_LINE_COLOR);
                Rect labelRect = new Rect(x - 30f, rect.y, 60f, 16f);
                GUI.Label(labelRect, $"{tickTime:F2}s", labelStyle);
            }
        }

        private void DrawTrackBackground(Rect rect, string label)
        {
            EditorGUI.DrawRect(rect, TRACK_BG);
            Rect labelRect = new Rect(rect.x - (TIMELINE_LEFT_PADDING - 8f), rect.y, TIMELINE_LEFT_PADDING - 12f, rect.height);
            GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel);
            labelStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(labelRect, label, labelStyle);
        }

        private void DrawTimeRange(Rect track, float startTime, float endTime, float duration, Color color, string label)
        {
            if (endTime <= startTime)
            {
                return;
            }
            float xStart = track.x + Mathf.Clamp01(startTime / duration) * track.width;
            float xEnd = track.x + Mathf.Clamp01(endTime / duration) * track.width;
            Rect blockRect = new Rect(xStart, track.y + 2, xEnd - xStart, track.height - 4);
            EditorGUI.DrawRect(blockRect, color);
            if (blockRect.width >= 24f)
            {
                GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel);
                labelStyle.alignment = TextAnchor.MiddleCenter;
                labelStyle.normal.textColor = BLOCK_LABEL_COLOR;
                GUI.Label(blockRect, label, labelStyle);
            }
        }

        // ─── VFX Marker 互動 ─────────────────────────────────────────
        // 處理順序：先處理每個 marker 的滑鼠事件（按下選取、拖曳改時間、放開結束），
        // 最後 fallback 處理「點空白處取消選取」。e.Use() 會把 e.type 設為 Used，自然跳過後續判定。
        private void HandleVfxTrackInteraction(Rect track, float duration)
        {
            if (_profile.VfxEvents == null)
            {
                return;
            }
            Event e = Event.current;
            SerializedProperty list = _serializedProfile.FindProperty("_vfxEvents");

            for (int i = 0; i < _profile.VfxEvents.Count; i++)
            {
                EnemyAttackVfxEvent evt = _profile.VfxEvents[i];
                if (evt == null)
                {
                    continue;
                }
                float t = Mathf.Clamp(evt.Time / duration, 0f, 1f);
                float x = track.x + t * track.width;
                int lane = (_vfxLaneCache != null && i < _vfxLaneCache.Length) ? _vfxLaneCache[i] : 0;
                float markerY = GetVfxMarkerY(track, lane);
                // 點擊熱區僅鎖在該 marker 的 lane 範圍內，避免兩個 lane 的 marker 滑鼠互搶
                Rect hitRect = new Rect(x - MARKER_SIZE, markerY - 2f, MARKER_SIZE * 2f, MARKER_SIZE + 4f);

                EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.SlideArrow);

                int controlId = GUIUtility.GetControlID(FocusType.Passive);
                EventType type = e.GetTypeForControl(controlId);
                switch (type)
                {
                    case EventType.MouseDown:
                        if (e.button == 0 && hitRect.Contains(e.mousePosition))
                        {
                            GUIUtility.hotControl = controlId;
                            SelectVfxEvent(i);
                            GUI.FocusControl(null);
                            e.Use();
                            Repaint();
                        }
                        break;
                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == controlId)
                        {
                            float newTime = ((e.mousePosition.x - track.x) / track.width) * duration;
                            newTime = Mathf.Round(newTime / TIME_SNAP) * TIME_SNAP;
                            newTime = Mathf.Clamp(newTime, 0f, duration);
                            list.GetArrayElementAtIndex(i).FindPropertyRelative("_time").floatValue = newTime;
                            e.Use();
                            Repaint();
                        }
                        break;
                    case EventType.MouseUp:
                        if (GUIUtility.hotControl == controlId)
                        {
                            GUIUtility.hotControl = 0;
                            e.Use();
                        }
                        break;
                }
            }

            // 點軌道空白處取消選取（e.Use 過的事件 type 已變 Used，不會誤觸）
            if (e.type == EventType.MouseDown && e.button == 0 && track.Contains(e.mousePosition))
            {
                ClearSelection();
                GUI.FocusControl(null);
                e.Use();
                Repaint();
            }
        }

        private void DrawVfxMarkers(Rect track, float duration)
        {
            if (_profile.VfxEvents == null)
            {
                return;
            }
            GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < _profile.VfxEvents.Count; i++)
            {
                EnemyAttackVfxEvent evt = _profile.VfxEvents[i];
                if (evt == null)
                {
                    continue;
                }
                float t = Mathf.Clamp(evt.Time / duration, 0f, 1f);
                float x = track.x + t * track.width;
                int lane = (_vfxLaneCache != null && i < _vfxLaneCache.Length) ? _vfxLaneCache[i] : 0;
                float markerY = GetVfxMarkerY(track, lane);
                Rect markerRect = new Rect(
                    x - MARKER_SIZE * 0.5f,
                    markerY,
                    MARKER_SIZE,
                    MARKER_SIZE);

                bool isSelected = IsVfxSelected(i);
                Color markerColor = isSelected ? VFX_MARKER_SELECTED_COLOR : VFX_MARKER_COLOR;
                EditorGUI.DrawRect(markerRect, markerColor);
                if (isSelected)
                {
                    DrawRectOutline(markerRect, VFX_MARKER_OUTLINE_COLOR, 2);
                }

                // 標籤只在 lane 0 marker 顯示在軌道上方，避免多 lane 標籤互相覆蓋；其他 lane 的 marker 只顯示色塊
                if (lane == 0)
                {
                    Rect labelRect = new Rect(x - 60f, track.y - MARKER_LABEL_HEIGHT - 2f, 120f, MARKER_LABEL_HEIGHT);
                    GUI.Label(labelRect, evt.Label, labelStyle);
                }
            }
        }

        // ─── 命中軌互動：每個 hitbox（主 + 額外）各自能點選 / 拖邊緣 ──
        private void HandleHitTrackInteraction(Rect track, float duration)
        {
            if (duration <= 0f) return;
            Event e = Event.current;

            // -1 = 主 Hitbox；0+ = ExtraHitboxes[idx]
            // 倒序處理：額外 hitbox 排後面所以畫在上面，先檢查上層的點選優先
            int extraCount = _profile.ExtraHitboxes != null ? _profile.ExtraHitboxes.Count : 0;
            for (int idx = extraCount - 1; idx >= -1; idx--)
            {
                float startTime, endTime;
                if (idx == -1)
                {
                    startTime = _profile.HitStart;
                    endTime = _profile.HitEnd;
                }
                else
                {
                    EnemyAttackHitboxData hb = _profile.ExtraHitboxes[idx];
                    if (hb == null) continue;
                    startTime = hb.HitStart;
                    endTime = hb.HitEnd;
                }
                if (HandleSingleHitboxInteraction(track, duration, idx, startTime, endTime, e))
                {
                    return;
                }
            }
        }

        // 處理單一 hitbox（idx = -1 = 主、0+ = 額外）的點選與邊緣拖曳
        // 回傳 true 表示本次事件已被吃掉，呼叫者應該停止迴圈
        private bool HandleSingleHitboxInteraction(Rect track, float duration, int hitboxIdx, float startTime, float endTime, Event e)
        {
            float xStart = track.x + Mathf.Clamp01(startTime / duration) * track.width;
            float xEnd = track.x + Mathf.Clamp01(endTime / duration) * track.width;
            Rect blockRect = new Rect(xStart, track.y, Mathf.Max(2f, xEnd - xStart), track.height);
            Rect leftEdge = new Rect(xStart - HIT_EDGE_GRAB_TOLERANCE, track.y, HIT_EDGE_GRAB_TOLERANCE * 2f, track.height);
            Rect rightEdge = new Rect(xEnd - HIT_EDGE_GRAB_TOLERANCE, track.y, HIT_EDGE_GRAB_TOLERANCE * 2f, track.height);

            EditorGUIUtility.AddCursorRect(leftEdge, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(rightEdge, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(blockRect, MouseCursor.Link);

            int leftId = GUIUtility.GetControlID(FocusType.Passive);
            int rightId = GUIUtility.GetControlID(FocusType.Passive);

            // 左邊緣 → HitStart
            switch (e.GetTypeForControl(leftId))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && leftEdge.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = leftId;
                        SelectHitbox(hitboxIdx);
                        GUI.FocusControl(null);
                        e.Use();
                        Repaint();
                        return true;
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == leftId)
                    {
                        float newTime = SnapTime(((e.mousePosition.x - track.x) / track.width) * duration, duration);
                        newTime = Mathf.Min(newTime, endTime);
                        WriteHitboxStartTime(hitboxIdx, newTime);
                        e.Use();
                        Repaint();
                        return true;
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == leftId)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        return true;
                    }
                    break;
            }

            // 右邊緣 → HitEnd
            switch (e.GetTypeForControl(rightId))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rightEdge.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = rightId;
                        SelectHitbox(hitboxIdx);
                        GUI.FocusControl(null);
                        e.Use();
                        Repaint();
                        return true;
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == rightId)
                    {
                        float newTime = SnapTime(((e.mousePosition.x - track.x) / track.width) * duration, duration);
                        newTime = Mathf.Max(newTime, startTime);
                        WriteHitboxEndTime(hitboxIdx, newTime);
                        e.Use();
                        Repaint();
                        return true;
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == rightId)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        return true;
                    }
                    break;
            }

            // 點區塊本體（非邊緣）— 選中該 hitbox
            if (e.type == EventType.MouseDown && e.button == 0 && blockRect.Contains(e.mousePosition))
            {
                SelectHitbox(hitboxIdx);
                GUI.FocusControl(null);
                e.Use();
                Repaint();
                return true;
            }
            return false;
        }

        private void WriteHitboxStartTime(int hitboxIdx, float t)
        {
            if (hitboxIdx == -1)
            {
                _serializedProfile.FindProperty("_hitStart").floatValue = t;
            }
            else
            {
                SerializedProperty list = _serializedProfile.FindProperty("_extraHitboxes");
                if (hitboxIdx >= list.arraySize) return;
                list.GetArrayElementAtIndex(hitboxIdx).FindPropertyRelative("HitStart").floatValue = t;
            }
        }

        private void WriteHitboxEndTime(int hitboxIdx, float t)
        {
            if (hitboxIdx == -1)
            {
                _serializedProfile.FindProperty("_hitEnd").floatValue = t;
            }
            else
            {
                SerializedProperty list = _serializedProfile.FindProperty("_extraHitboxes");
                if (hitboxIdx >= list.arraySize) return;
                list.GetArrayElementAtIndex(hitboxIdx).FindPropertyRelative("HitEnd").floatValue = t;
            }
        }

        private static float SnapTime(float t, float duration)
        {
            t = Mathf.Round(t / TIME_SNAP) * TIME_SNAP;
            return Mathf.Clamp(t, 0f, duration);
        }

        private static void DrawRectOutline(Rect rect, Color color, int thickness)
        {
            Rect outline = new Rect(rect.x - thickness, rect.y - thickness, rect.width + thickness * 2f, rect.height + thickness * 2f);
            EditorGUI.DrawRect(new Rect(outline.x, outline.y, outline.width, thickness), color);
            EditorGUI.DrawRect(new Rect(outline.x, outline.yMax - thickness, outline.width, thickness), color);
            EditorGUI.DrawRect(new Rect(outline.x, outline.y, thickness, outline.height), color);
            EditorGUI.DrawRect(new Rect(outline.xMax - thickness, outline.y, thickness, outline.height), color);
        }

        // ─── 工具列（新增 / 刪除）─────────────────────────────────────
        private void DrawToolbar()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 新增 VFX 事件", GUILayout.Width(130), GUILayout.Height(22)))
                {
                    AddVfxEvent();
                }
                using (new EditorGUI.DisabledScope(_selectionKind != SelectionKind.VfxEvent || _selectedEventIndex < 0))
                {
                    if (GUILayout.Button("－ 刪除選中 VFX", GUILayout.Width(130), GUILayout.Height(22)))
                    {
                        DeleteSelectedVfxEvent();
                    }
                }
                GUILayout.Space(12);
                if (GUILayout.Button("＋ 新增 Hitbox", GUILayout.Width(120), GUILayout.Height(22)))
                {
                    AddExtraHitbox();
                }
                // 主 Hitbox 不可刪 — 只能刪額外（_selectedHitboxIndex >= 0）
                using (new EditorGUI.DisabledScope(_selectionKind != SelectionKind.Hitbox || _selectedHitboxIndex < 0))
                {
                    if (GUILayout.Button("－ 刪除選中 Hitbox", GUILayout.Width(140), GUILayout.Height(22)))
                    {
                        DeleteSelectedExtraHitbox();
                    }
                }
                GUILayout.FlexibleSpace();
                int hbCount = 1 + (_profile.ExtraHitboxes?.Count ?? 0);
                GUILayout.Label($"事件：{_profile.VfxEvents?.Count ?? 0}　Hitbox：{hbCount}", EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(4);
        }

        private void AddExtraHitbox()
        {
            SerializedProperty list = _serializedProfile.FindProperty("_extraHitboxes");
            int idx = list.arraySize;
            list.arraySize++;
            SerializedProperty newElem = list.GetArrayElementAtIndex(idx);
            // 強制設預設值（避免 Unity 把上一筆內容複製過來）
            newElem.FindPropertyRelative("Label").stringValue = $"Hitbox {idx + 1}";
            // 預設時間放在主 Hitbox 之後，方便連段（不重疊）
            float defaultStart = Mathf.Clamp(_profile.HitEnd + 0.1f, 0f, _profile.Duration);
            float defaultEnd = Mathf.Min(defaultStart + 0.2f, _profile.Duration);
            newElem.FindPropertyRelative("HitStart").floatValue = defaultStart;
            newElem.FindPropertyRelative("HitEnd").floatValue = defaultEnd;
            newElem.FindPropertyRelative("Bone").stringValue = _profile.HitboxBone;
            newElem.FindPropertyRelative("Offset").vector3Value = _profile.HitboxOffset;
            newElem.FindPropertyRelative("Rotation").vector3Value = _profile.HitboxRotation;
            newElem.FindPropertyRelative("Size").vector3Value = _profile.HitboxSize;
            newElem.FindPropertyRelative("LayerMask").intValue = _profile.HitboxLayerMask;
            _serializedProfile.ApplyModifiedProperties();
            SelectHitbox(idx);
            GUI.FocusControl(null);
        }

        private void DeleteSelectedExtraHitbox()
        {
            if (_selectionKind != SelectionKind.Hitbox || _selectedHitboxIndex < 0) return;
            SerializedProperty list = _serializedProfile.FindProperty("_extraHitboxes");
            if (_selectedHitboxIndex >= list.arraySize) return;
            list.DeleteArrayElementAtIndex(_selectedHitboxIndex);
            _serializedProfile.ApplyModifiedProperties();
            // 刪除後跳回主 Hitbox 選取狀態
            SelectHitbox(-1);
            GUI.FocusControl(null);
        }

        private void AddVfxEvent()
        {
            SerializedProperty list = _serializedProfile.FindProperty("_vfxEvents");
            int idx = list.arraySize;
            list.arraySize++;
            SerializedProperty newElement = list.GetArrayElementAtIndex(idx);
            // 強制設預設值（避免 Unity 把上一筆 copy 過來）
            newElement.FindPropertyRelative("_label").stringValue = $"新特效 {idx + 1}";
            float defaultTime = Mathf.Min(_profile.HitStart, _profile.Duration * 0.5f);
            newElement.FindPropertyRelative("_time").floatValue = Mathf.Round(defaultTime / TIME_SNAP) * TIME_SNAP;
            newElement.FindPropertyRelative("_vfxPrefab").objectReferenceValue = null;
            newElement.FindPropertyRelative("_boneName").stringValue = "";
            newElement.FindPropertyRelative("_positionOffset").vector3Value = Vector3.zero;
            newElement.FindPropertyRelative("_rotationOffset").vector3Value = Vector3.zero;
            newElement.FindPropertyRelative("_scaleMultiplier").vector3Value = Vector3.one;
            newElement.FindPropertyRelative("_scaleAllChildren").boolValue = true;
            newElement.FindPropertyRelative("_attachToBone").boolValue = true;
            newElement.FindPropertyRelative("_lifetime").floatValue = 2f;
            SelectVfxEvent(idx);
            GUI.FocusControl(null);
        }

        private void DeleteSelectedVfxEvent()
        {
            if (_selectionKind != SelectionKind.VfxEvent) return;
            SerializedProperty list = _serializedProfile.FindProperty("_vfxEvents");
            if (_selectedEventIndex < 0 || _selectedEventIndex >= list.arraySize)
            {
                return;
            }
            list.DeleteArrayElementAtIndex(_selectedEventIndex);
            if (list.arraySize == 0)
            {
                ClearSelection();
            }
            else if (_selectedEventIndex >= list.arraySize)
            {
                SelectVfxEvent(list.arraySize - 1);
            }
            GUI.FocusControl(null);
        }

        // ─── 選中項設定面板（分流到 VFX 或 Hitbox）────────────────────
        private void DrawSelectedEventPanel()
        {
            EditorGUILayout.Space(6);
            _bottomPanelScroll = EditorGUILayout.BeginScrollView(_bottomPanelScroll);

            switch (_selectionKind)
            {
                case SelectionKind.VfxEvent:
                    DrawVfxEventPanel();
                    break;
                case SelectionKind.Hitbox:
                    DrawHitboxPanel();
                    break;
                default:
                    EditorGUILayout.HelpBox("點擊特效軌上的圓點選中 VFX 事件；點命中軌（紅色 Hit 區塊）選中 Hitbox。命中軌左右邊緣可拖曳調整 HitStart / HitEnd", MessageType.Info);
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawVfxEventPanel()
        {
            SerializedProperty list = _serializedProfile.FindProperty("_vfxEvents");
            if (_selectedEventIndex < 0 || _selectedEventIndex >= list.arraySize)
            {
                ClearSelection();
                EditorGUILayout.HelpBox("選中事件已被刪除", MessageType.Warning);
                return;
            }
            SerializedProperty elem = list.GetArrayElementAtIndex(_selectedEventIndex);
            EditorGUILayout.LabelField($"VFX 事件 #{_selectedEventIndex + 1} 設定", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("_label"), new GUIContent("顯示名稱"));

            SerializedProperty timeProp = elem.FindPropertyRelative("_time");
            EditorGUI.BeginChangeCheck();
            float newTime = EditorGUILayout.FloatField(new GUIContent("觸發時間 (秒)"), timeProp.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                timeProp.floatValue = Mathf.Clamp(newTime, 0f, _profile.Duration);
            }

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("_vfxPrefab"), new GUIContent("VFX Prefab"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("_boneName"), new GUIContent("綁定骨骼"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("_positionOffset"), new GUIContent("位置偏移"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("_rotationOffset"), new GUIContent("旋轉偏移 (Euler)"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("_scaleMultiplier"), new GUIContent("縮放倍率"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("_scaleAllChildren"), new GUIContent("縮放所有子特效"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("_attachToBone"), new GUIContent("跟隨骨骼"));

            SerializedProperty lifetimeProp = elem.FindPropertyRelative("_lifetime");
            EditorGUI.BeginChangeCheck();
            float newLifetime = EditorGUILayout.FloatField(new GUIContent("自動銷毀秒數 (0 = 不主動銷毀)"), lifetimeProp.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                lifetimeProp.floatValue = Mathf.Max(0f, newLifetime);
            }
        }

        private void DrawHitboxPanel()
        {
            if (_selectedHitboxIndex == -1)
            {
                DrawPrimaryHitboxPanel();
            }
            else
            {
                DrawExtraHitboxPanel(_selectedHitboxIndex);
            }
        }

        private void DrawPrimaryHitboxPanel()
        {
            EditorGUILayout.LabelField("主 Hitbox (Hit 0)", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            SerializedProperty hitStartProp = _serializedProfile.FindProperty("_hitStart");
            SerializedProperty hitEndProp = _serializedProfile.FindProperty("_hitEnd");
            EditorGUI.BeginChangeCheck();
            float newStart = EditorGUILayout.FloatField(new GUIContent("HitStart (秒)"), hitStartProp.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                hitStartProp.floatValue = Mathf.Clamp(newStart, 0f, _profile.HitEnd);
            }
            EditorGUI.BeginChangeCheck();
            float newEnd = EditorGUILayout.FloatField(new GUIContent("HitEnd (秒)"), hitEndProp.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                hitEndProp.floatValue = Mathf.Clamp(newEnd, _profile.HitStart, _profile.Duration);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_serializedProfile.FindProperty("_hitboxBone"), new GUIContent("綁定骨骼"));
            EditorGUILayout.PropertyField(_serializedProfile.FindProperty("_hitboxOffset"), new GUIContent("位置偏移 (local)"));
            EditorGUILayout.PropertyField(_serializedProfile.FindProperty("_hitboxRotation"), new GUIContent("旋轉 (Euler, local)"));
            EditorGUILayout.PropertyField(_serializedProfile.FindProperty("_hitboxSize"), new GUIContent("大小 (X/Y/Z 全長)"));
            EditorGUILayout.PropertyField(_serializedProfile.FindProperty("_hitboxLayerMask"), new GUIContent("命中 Layer"));

            if (_previewActive)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("Scene 視窗中拖動 Move / Rotate / Scale 工具控制此 Hitbox", MessageType.Info);
            }
        }

        private void DrawExtraHitboxPanel(int idx)
        {
            SerializedProperty list = _serializedProfile.FindProperty("_extraHitboxes");
            if (idx < 0 || idx >= list.arraySize)
            {
                ClearSelection();
                EditorGUILayout.HelpBox("選中 Hitbox 已被刪除", MessageType.Warning);
                return;
            }
            SerializedProperty elem = list.GetArrayElementAtIndex(idx);
            EditorGUILayout.LabelField($"額外 Hitbox (Hit {idx + 1})", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("Label"), new GUIContent("名稱"));
            SerializedProperty hitStartProp = elem.FindPropertyRelative("HitStart");
            SerializedProperty hitEndProp = elem.FindPropertyRelative("HitEnd");
            EditorGUI.BeginChangeCheck();
            float newStart = EditorGUILayout.FloatField(new GUIContent("HitStart (秒)"), hitStartProp.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                hitStartProp.floatValue = Mathf.Clamp(newStart, 0f, hitEndProp.floatValue);
            }
            EditorGUI.BeginChangeCheck();
            float newEnd = EditorGUILayout.FloatField(new GUIContent("HitEnd (秒)"), hitEndProp.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                hitEndProp.floatValue = Mathf.Clamp(newEnd, hitStartProp.floatValue, _profile.Duration);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("Bone"), new GUIContent("綁定骨骼"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("Offset"), new GUIContent("位置偏移 (local)"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("Rotation"), new GUIContent("旋轉 (Euler, local)"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("Size"), new GUIContent("大小 (X/Y/Z 全長)"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("LayerMask"), new GUIContent("命中 Layer"));

            if (_previewActive)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("Scene 視窗中拖動 Move / Rotate / Scale 工具控制此 Hitbox", MessageType.Info);
            }
        }

        // ─── 預覽列 ──────────────────────────────────────────────────
        private void DrawPreviewBar()
        {
            EditorGUILayout.Space(4);
            Rect barRect = EditorGUILayout.BeginHorizontal();
            if (_previewActive)
            {
                EditorGUI.DrawRect(barRect, PREVIEW_BAR_ACTIVE_BG);
            }

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontStyle = FontStyle.Bold;
            string buttonText = _previewActive ? "■ 結束預覽" : "▶ 開啟預覽";
            if (GUILayout.Button(buttonText, buttonStyle, GUILayout.Width(110), GUILayout.Height(22)))
            {
                if (_previewActive)
                {
                    StopPreview();
                }
                else
                {
                    StartPreview();
                }
            }

            if (_previewActive)
            {
                // 目標敵人欄位（唯讀顯示，按 Ping 可定位 Hierarchy）
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(_previewTargetController, typeof(MonoBehaviour), true, GUILayout.Width(180));
                }
                GUILayout.Label($"時間：{_previewTime:F2} / {_profile.Duration:F2}s", EditorStyles.miniLabel, GUILayout.Width(140));
                GUILayout.Label("（拖動上方標尺改變時間）", EditorStyles.miniLabel);
            }
            else
            {
                int candidateCount = CountCandidateEnemies();
                string status = candidateCount > 0
                    ? $"場景中找到 {candidateCount} 個使用此招式的敵人 — 按開啟預覽"
                    : "場景中沒有使用此招式的敵人 — 請拖一個敵人 prefab 進場景、把此 Profile 加入其攻擊清單";
                GUILayout.Label(status, EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        // ─── Playhead 拖曳（在標尺上） ────────────────────────────────
        private void HandlePlayheadDrag(Rect rulerRect, float duration)
        {
            Event e = Event.current;
            EditorGUIUtility.AddCursorRect(rulerRect, MouseCursor.SlideArrow);
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rulerRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        SetPlayheadFromMouse(e.mousePosition.x, rulerRect, duration);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        SetPlayheadFromMouse(e.mousePosition.x, rulerRect, duration);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        private void SetPlayheadFromMouse(float mouseX, Rect rulerRect, float duration)
        {
            float t = Mathf.Clamp01((mouseX - rulerRect.x) / rulerRect.width);
            ApplyPreviewTime(t * duration);
            Repaint();
        }

        // ─── 預覽核心：找敵人 / 啟動 / 停止 / 套用時間 ──────────────────
        private int CountCandidateEnemies()
        {
            MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is IAttackProfileHost host && UsesProfile(host, _profile))
                {
                    count++;
                }
            }
            return count;
        }

        private MonoBehaviour FindFirstCandidateEnemy()
        {
            MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is IAttackProfileHost host && UsesProfile(host, _profile))
                {
                    return all[i];
                }
            }
            return null;
        }

        private static bool UsesProfile(IAttackProfileHost host, EnemyAttackProfile profile)
        {
            if (host == null || profile == null || host.AttackProfiles == null)
            {
                return false;
            }
            for (int i = 0; i < host.AttackProfiles.Count; i++)
            {
                if (host.AttackProfiles[i] == profile)
                {
                    return true;
                }
            }
            return false;
        }

        private void StartPreview()
        {
            if (_profile == null || _profile.AnimationClip == null)
            {
                EditorUtility.DisplayDialog("無法開啟預覽", "此 Profile 沒有指定 AnimationClip，無法預覽動畫", "確定");
                return;
            }
            MonoBehaviour target = FindFirstCandidateEnemy();
            if (target == null)
            {
                EditorUtility.DisplayDialog("無法開啟預覽",
                    "場景中沒有實作 IAttackProfileHost 的物件使用此 Profile\n\n請將敵人/Boss prefab 拖進場景,並在 Inspector 的攻擊招式清單中加入此 Profile",
                    "確定");
                return;
            }
            Animator anim = target.GetComponentInChildren<Animator>(true);
            if (anim == null)
            {
                EditorUtility.DisplayDialog("無法開啟預覽", "找不到敵人的 Animator 元件", "確定");
                return;
            }
            _previewTargetController = target;
            _previewAnimatorRoot = anim.gameObject;
            _previewTime = 0f;
            _previewActive = true;
            AnimationMode.StartAnimationMode();
            ApplyPreviewTime(_previewTime);
        }

        private void StopPreview()
        {
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }
            ClearPreviewVfx();
            _previewActive = false;
            _previewTargetController = null;
            _previewAnimatorRoot = null;
            SceneView.RepaintAll();
        }

        // 套用指定時間：對 Animator 採樣動畫姿勢 + 同步 VFX 實體與粒子模擬時間
        private void ApplyPreviewTime(float t)
        {
            if (!_previewActive || _profile == null)
            {
                return;
            }
            _previewTime = Mathf.Clamp(t, 0f, _profile.Duration);
            if (_previewAnimatorRoot != null && _profile.AnimationClip != null && AnimationMode.InAnimationMode())
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(_previewAnimatorRoot, _profile.AnimationClip, _previewTime);
                AnimationMode.EndSampling();
            }
            SyncPreviewVfx(_previewTime);
            SceneView.RepaintAll();
        }

        // 增量同步：把預覽 VFX 實體跟當前時間軸狀態對齊
        // 顯示判斷：
        //   • playhead 已超過 event.Time → 顯示（依 age 模擬粒子）
        //   • 該 event 是「目前選中的」 → 強制顯示（age = max(0, t - event.Time)）
        //     讓設計師擺位置時不用先把 playhead 拖過去
        //   • 否則 → 不顯示
        private void SyncPreviewVfx(float t)
        {
            if (_profile == null || _profile.VfxEvents == null)
            {
                ClearPreviewVfx();
                return;
            }

            for (int i = _previewInstances.Count - 1; i >= 0; i--)
            {
                PreviewVfxInstance inst = _previewInstances[i];
                bool shouldKeep = inst.EventIndex >= 0 && inst.EventIndex < _profile.VfxEvents.Count;
                if (shouldKeep)
                {
                    EnemyAttackVfxEvent evt = _profile.VfxEvents[inst.EventIndex];
                    bool selected = IsVfxSelected(inst.EventIndex);
                    if (evt == null || evt.VfxPrefab == null || (evt.Time > t && !selected))
                    {
                        shouldKeep = false;
                    }
                }
                if (!shouldKeep)
                {
                    if (inst.Instance != null)
                    {
                        DestroyImmediate(inst.Instance);
                    }
                    _previewInstances.RemoveAt(i);
                }
            }

            for (int i = 0; i < _profile.VfxEvents.Count; i++)
            {
                EnemyAttackVfxEvent evt = _profile.VfxEvents[i];
                if (evt == null || evt.VfxPrefab == null) continue;
                bool selected = IsVfxSelected(i);
                if (evt.Time > t && !selected) continue;

                int existingIdx = FindPreviewInstanceIndex(i);
                if (existingIdx < 0)
                {
                    SpawnPreviewVfx(i, evt);
                    existingIdx = _previewInstances.Count - 1;
                }
                PreviewVfxInstance inst = _previewInstances[existingIdx];
                UpdatePreviewInstanceTransform(inst.Instance, evt);
                float age = Mathf.Max(0f, t - evt.Time);
                if (inst.Particles != null)
                {
                    for (int p = 0; p < inst.Particles.Length; p++)
                    {
                        ParticleSystem ps = inst.Particles[p];
                        if (ps != null)
                        {
                            ps.Simulate(age, true, true);
                        }
                    }
                }
            }
        }

        private int FindPreviewInstanceIndex(int eventIndex)
        {
            for (int i = 0; i < _previewInstances.Count; i++)
            {
                if (_previewInstances[i].EventIndex == eventIndex)
                {
                    return i;
                }
            }
            return -1;
        }

        // 生成預覽用 VFX 實體。hideFlags = DontSave 確保預覽物件不會被存進 scene
        // 預覽永遠 parent 到骨骼（即使 AttachToBone = false），方便編輯期跟隨動畫姿勢
        private void SpawnPreviewVfx(int eventIndex, EnemyAttackVfxEvent evt)
        {
            GameObject go = Instantiate(evt.VfxPrefab);
            go.hideFlags = HideFlags.DontSave;
            go.name = $"[Preview] {evt.Label}";
            PreviewVfxInstance inst = new PreviewVfxInstance
            {
                EventIndex = eventIndex,
                Instance = go,
                Particles = go.GetComponentsInChildren<ParticleSystem>(true)
            };
            _previewInstances.Add(inst);
            UpdatePreviewInstanceTransform(go, evt);
        }

        // 把實體放到正確位置 / 旋轉 / 縮放，並套用粒子縮放模式 — 跟 Runtime SpawnVfx (AttachToBone=true 分支) 邏輯一致
        // 用 local transform：parent scale 自動透過 hierarchy 套用到 world 位置/旋轉/大小，符合「父物件放大特效跟著放大」需求
        private void UpdatePreviewInstanceTransform(GameObject go, EnemyAttackVfxEvent evt)
        {
            if (go == null)
            {
                return;
            }
            Transform bone = ResolvePreviewBone(evt.BoneName);
            if (bone == null)
            {
                bone = _previewAnimatorRoot != null ? _previewAnimatorRoot.transform : null;
            }
            if (bone == null)
            {
                return;
            }
            if (go.transform.parent != bone)
            {
                go.transform.SetParent(bone, false);
            }
            go.transform.localPosition = evt.PositionOffset;
            go.transform.localRotation = Quaternion.Euler(evt.RotationOffset);
            Vector3 prefabScale = evt.VfxPrefab != null ? evt.VfxPrefab.transform.localScale : Vector3.one;
            go.transform.localScale = Vector3.Scale(prefabScale, evt.ScaleMultiplier);
            ApplyParticleScalingMode(go, evt.ScaleAllChildren);
        }

        // 跟 Runtime EnemyAttackExecutor 的同名 method 邏輯一致
        private static void ApplyParticleScalingMode(GameObject root, bool useHierarchy)
        {
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystemScalingMode mode = useHierarchy ? ParticleSystemScalingMode.Hierarchy : ParticleSystemScalingMode.Local;
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null)
                {
                    continue;
                }
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = mode;
            }
        }

        private void ClearPreviewVfx()
        {
            for (int i = 0; i < _previewInstances.Count; i++)
            {
                if (_previewInstances[i].Instance != null)
                {
                    DestroyImmediate(_previewInstances[i].Instance);
                }
            }
            _previewInstances.Clear();
        }

        private Transform ResolvePreviewBone(string boneName)
        {
            if (_previewAnimatorRoot == null)
            {
                return null;
            }
            if (string.IsNullOrEmpty(boneName))
            {
                return _previewAnimatorRoot.transform;
            }
            return FindChildRecursive(_previewAnimatorRoot.transform, boneName);
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name)
            {
                return parent;
            }
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        // ─── Scene 視窗：依當前選取（VFX / Hitbox）顯示對應 Move/Rotate/Scale handle ──
        private void OnSceneGUI(SceneView sv)
        {
            if (!_previewActive || _profile == null || _serializedProfile == null)
            {
                return;
            }

            DrawHitboxGizmo();
            DrawProjectileGizmo();

            switch (_selectionKind)
            {
                case SelectionKind.VfxEvent:
                    DrawVfxEventHandles();
                    break;
                case SelectionKind.Hitbox:
                    DrawHitboxHandles();
                    break;
            }
        }

        // 遠程招：在發射點畫子彈射出方向箭頭。Forward 模式附旋轉 gizmo，拖動即時回寫 _projectileForwardAngles
        private void DrawProjectileGizmo()
        {
            if (_profile == null || !_profile.IsRanged) return;
            Transform bone = ResolvePreviewBone(_profile.ProjectileSpawnBone);
            if (bone == null)
            {
                bone = _previewAnimatorRoot != null ? _previewAnimatorRoot.transform : null;
            }
            if (bone == null) return;

            Vector3 spawnPos = bone.TransformPoint(_profile.ProjectileSpawnOffset);
            float handleSize = HandleUtility.GetHandleSize(spawnPos);

            if (_profile.RangedAimMode == RangedAimMode.Forward)
            {
                Quaternion worldRot = bone.rotation * Quaternion.Euler(_profile.ProjectileForwardAngles);

                Handles.color = new Color(0.3f, 0.85f, 1f, 1f);
                DrawAimArrow(spawnPos, worldRot * Vector3.forward, handleSize);
                Handles.SphereHandleCap(0, spawnPos, Quaternion.identity, handleSize * 0.12f, EventType.Repaint);
                Handles.Label(spawnPos + Vector3.up * (handleSize * 0.3f), $"子彈方向 (Forward)\n角度偏移 {_profile.ProjectileForwardAngles}");

                EditorGUI.BeginChangeCheck();
                Quaternion newWorldRot = Handles.RotationHandle(worldRot, spawnPos);
                if (EditorGUI.EndChangeCheck())
                {
                    Quaternion newLocalRot = Quaternion.Inverse(bone.rotation) * newWorldRot;
                    SerializedProperty prop = _serializedProfile.FindProperty("_projectileForwardAngles");
                    if (prop != null)
                    {
                        prop.vector3Value = newLocalRot.eulerAngles;
                        _serializedProfile.ApplyModifiedProperties();
                        Repaint();
                    }
                }
            }
            else
            {
                // TowardPlayer / Homing：實際方向執行時朝玩家算，預覽用骨骼 forward 當參考（淡色）
                Handles.color = new Color(0.3f, 0.85f, 1f, 0.5f);
                DrawAimArrow(spawnPos, bone.forward, handleSize);
                Handles.SphereHandleCap(0, spawnPos, Quaternion.identity, handleSize * 0.12f, EventType.Repaint);
                Handles.Label(spawnPos + Vector3.up * (handleSize * 0.3f), $"子彈發射點\n({_profile.RangedAimMode}：執行時朝玩家)");
            }
        }

        private static void DrawAimArrow(Vector3 origin, Vector3 dir, float handleSize)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            float length = handleSize * 1.6f;
            Vector3 tip = origin + dir * length;
            Handles.DrawLine(origin, tip, 3f);
            Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(dir), length * 0.18f, EventType.Repaint);
        }

        // VFX 事件選中時：拉 Move/Rotate/Scale 改 PositionOffset / RotationOffset / ScaleMultiplier
        private void DrawVfxEventHandles()
        {
            if (_selectedEventIndex < 0 || _profile.VfxEvents == null || _selectedEventIndex >= _profile.VfxEvents.Count)
            {
                return;
            }
            EnemyAttackVfxEvent evt = _profile.VfxEvents[_selectedEventIndex];
            if (evt == null) return;
            Transform bone = ResolvePreviewBone(evt.BoneName);
            if (bone == null)
            {
                bone = _previewAnimatorRoot != null ? _previewAnimatorRoot.transform : null;
            }
            if (bone == null) return;

            Vector3 worldPos = bone.TransformPoint(evt.PositionOffset);
            Quaternion worldRot = bone.rotation * Quaternion.Euler(evt.RotationOffset);
            Vector3 prefabScale = evt.VfxPrefab != null ? evt.VfxPrefab.transform.localScale : Vector3.one;
            Vector3 scaleBase = Vector3.Scale(bone.lossyScale, prefabScale);
            Vector3 visualScale = Vector3.Scale(scaleBase, evt.ScaleMultiplier);

            string info = $"{evt.Label}\nPos:{evt.PositionOffset}  Rot:{evt.RotationOffset}\nScale×{evt.ScaleMultiplier}";
            Handles.color = new Color(0.3f, 0.85f, 1f, 1f);
            Handles.Label(worldPos + Vector3.up * 0.15f, info);

            SerializedProperty list = _serializedProfile.FindProperty("_vfxEvents");
            SerializedProperty elem = list.GetArrayElementAtIndex(_selectedEventIndex);

            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPos = worldPos;
            Quaternion newWorldRot = worldRot;
            Vector3 newScale = visualScale;

            switch (Tools.current)
            {
                case Tool.Move: newWorldPos = Handles.PositionHandle(worldPos, worldRot); break;
                case Tool.Rotate: newWorldRot = Handles.RotationHandle(worldRot, worldPos); break;
                case Tool.Scale: newScale = Handles.ScaleHandle(visualScale, worldPos, worldRot, HandleUtility.GetHandleSize(worldPos)); break;
                case Tool.Transform:
                    newWorldPos = Handles.PositionHandle(worldPos, worldRot);
                    newWorldRot = Handles.RotationHandle(newWorldRot, newWorldPos);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                if (newWorldPos != worldPos)
                {
                    Vector3 newOffset = bone.InverseTransformPoint(newWorldPos);
                    elem.FindPropertyRelative("_positionOffset").vector3Value = newOffset;
                }
                if (newWorldRot != worldRot)
                {
                    Quaternion newLocalRot = Quaternion.Inverse(bone.rotation) * newWorldRot;
                    elem.FindPropertyRelative("_rotationOffset").vector3Value = newLocalRot.eulerAngles;
                }
                if (newScale != visualScale)
                {
                    Vector3 newMult = new Vector3(
                        Mathf.Abs(scaleBase.x) > 0.0001f ? newScale.x / scaleBase.x : evt.ScaleMultiplier.x,
                        Mathf.Abs(scaleBase.y) > 0.0001f ? newScale.y / scaleBase.y : evt.ScaleMultiplier.y,
                        Mathf.Abs(scaleBase.z) > 0.0001f ? newScale.z / scaleBase.z : evt.ScaleMultiplier.z);
                    elem.FindPropertyRelative("_scaleMultiplier").vector3Value = newMult;
                }
                _serializedProfile.ApplyModifiedProperties();
                SyncPreviewVfx(_previewTime);
                Repaint();
            }
        }

        // Hitbox 選中時：拉 Move/Rotate/Scale 改對應 Offset / Rotation / Size
        // 依 _selectedHitboxIndex 決定操作的是主 Hitbox 還是某個額外 Hitbox
        private void DrawHitboxHandles()
        {
            string boneName;
            Vector3 offset, rotation, size;
            SerializedProperty offsetProp, rotationProp, sizeProp;

            if (_selectedHitboxIndex == -1)
            {
                boneName = _profile.HitboxBone;
                offset = _profile.HitboxOffset;
                rotation = _profile.HitboxRotation;
                size = _profile.HitboxSize;
                offsetProp = _serializedProfile.FindProperty("_hitboxOffset");
                rotationProp = _serializedProfile.FindProperty("_hitboxRotation");
                sizeProp = _serializedProfile.FindProperty("_hitboxSize");
            }
            else
            {
                SerializedProperty list = _serializedProfile.FindProperty("_extraHitboxes");
                if (_selectedHitboxIndex >= list.arraySize) return;
                SerializedProperty elem = list.GetArrayElementAtIndex(_selectedHitboxIndex);
                EnemyAttackHitboxData hb = _profile.ExtraHitboxes[_selectedHitboxIndex];
                if (hb == null) return;
                boneName = hb.Bone;
                offset = hb.Offset;
                rotation = hb.Rotation;
                size = hb.Size;
                offsetProp = elem.FindPropertyRelative("Offset");
                rotationProp = elem.FindPropertyRelative("Rotation");
                sizeProp = elem.FindPropertyRelative("Size");
            }

            Transform bone = ResolvePreviewBone(boneName);
            if (bone == null)
            {
                bone = _previewAnimatorRoot != null ? _previewAnimatorRoot.transform : null;
            }
            if (bone == null) return;

            Vector3 worldPos = bone.TransformPoint(offset);
            Quaternion worldRot = bone.rotation * Quaternion.Euler(rotation);
            Vector3 worldSize = Vector3.Scale(size, bone.lossyScale);

            string labelText = _selectedHitboxIndex == -1
                ? $"Hit 0 (主)\nOffset:{offset}  Rot:{rotation}\nSize:{size}"
                : $"Hit {_selectedHitboxIndex + 1}\nOffset:{offset}  Rot:{rotation}\nSize:{size}";
            Handles.color = new Color(1f, 0.55f, 0.55f, 1f);
            Handles.Label(worldPos + Vector3.up * 0.15f, labelText);

            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPos = worldPos;
            Quaternion newWorldRot = worldRot;
            Vector3 newWorldSize = worldSize;

            switch (Tools.current)
            {
                case Tool.Move: newWorldPos = Handles.PositionHandle(worldPos, worldRot); break;
                case Tool.Rotate: newWorldRot = Handles.RotationHandle(worldRot, worldPos); break;
                case Tool.Scale: newWorldSize = Handles.ScaleHandle(worldSize, worldPos, worldRot, HandleUtility.GetHandleSize(worldPos)); break;
                case Tool.Transform:
                    newWorldPos = Handles.PositionHandle(worldPos, worldRot);
                    newWorldRot = Handles.RotationHandle(newWorldRot, newWorldPos);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                if (newWorldPos != worldPos)
                {
                    offsetProp.vector3Value = bone.InverseTransformPoint(newWorldPos);
                }
                if (newWorldRot != worldRot)
                {
                    Quaternion newLocalRot = Quaternion.Inverse(bone.rotation) * newWorldRot;
                    rotationProp.vector3Value = newLocalRot.eulerAngles;
                }
                if (newWorldSize != worldSize)
                {
                    Vector3 lossy = bone.lossyScale;
                    Vector3 newLocalSize = new Vector3(
                        Mathf.Abs(lossy.x) > 0.0001f ? newWorldSize.x / lossy.x : size.x,
                        Mathf.Abs(lossy.y) > 0.0001f ? newWorldSize.y / lossy.y : size.y,
                        Mathf.Abs(lossy.z) > 0.0001f ? newWorldSize.z / lossy.z : size.z);
                    sizeProp.vector3Value = newLocalSize;
                }
                _serializedProfile.ApplyModifiedProperties();
                Repaint();
            }
        }

        // 預覽進行中持續顯示所有 Hitbox wireframe（主 + 額外）— 各自時間窗內紅、窗外橘
        // 主 Hitbox 使用純紅；額外 Hitbox 使用橙紅，方便視覺上區分
        private void DrawHitboxGizmo()
        {
            DrawSingleHitboxGizmo(_profile.HitboxBone, _profile.HitboxOffset, _profile.HitboxRotation,
                _profile.HitboxSize, _profile.HitStart, _profile.HitEnd,
                "Hit 0", true);

            if (_profile.ExtraHitboxes != null)
            {
                for (int i = 0; i < _profile.ExtraHitboxes.Count; i++)
                {
                    EnemyAttackHitboxData hb = _profile.ExtraHitboxes[i];
                    if (hb == null) continue;
                    string label = string.IsNullOrEmpty(hb.Label) ? $"Hit {i + 1}" : $"{hb.Label} (Hit {i + 1})";
                    DrawSingleHitboxGizmo(hb.Bone, hb.Offset, hb.Rotation, hb.Size,
                        hb.HitStart, hb.HitEnd, label, false);
                }
            }
        }

        private void DrawSingleHitboxGizmo(string boneName, Vector3 offset, Vector3 rotation, Vector3 size,
            float hitStart, float hitEnd, string label, bool isPrimary)
        {
            Transform bone = ResolvePreviewBone(boneName);
            if (bone == null)
            {
                bone = _previewAnimatorRoot != null ? _previewAnimatorRoot.transform : null;
            }
            if (bone == null) return;

            bool isHitActive = _previewTime >= hitStart && _previewTime <= hitEnd;
            Color outlineColor;
            if (isPrimary)
            {
                outlineColor = isHitActive
                    ? new Color(1f, 0.2f, 0.2f, 1f)
                    : new Color(1f, 0.55f, 0.1f, 0.75f);
            }
            else
            {
                outlineColor = isHitActive
                    ? new Color(1f, 0.5f, 0.2f, 1f)
                    : new Color(1f, 0.75f, 0.4f, 0.65f);
            }

            Vector3 center = bone.TransformPoint(offset);
            Vector3 worldSize = Vector3.Scale(size, bone.lossyScale);
            Quaternion gizmoRot = bone.rotation * Quaternion.Euler(rotation);

            Matrix4x4 prev = Handles.matrix;
            Handles.matrix = Matrix4x4.TRS(center, gizmoRot, Vector3.one);
            Handles.color = outlineColor;
            Handles.DrawWireCube(Vector3.zero, worldSize);
            Handles.matrix = prev;

            GUIStyle labelStyle = new GUIStyle();
            labelStyle.normal.textColor = outlineColor;
            labelStyle.fontStyle = FontStyle.Bold;
            string prefix = isHitActive ? "★ " : "";
            Handles.Label(center + Vector3.up * (worldSize.y * 0.5f + 0.15f), prefix + label, labelStyle);
        }
    }
}
