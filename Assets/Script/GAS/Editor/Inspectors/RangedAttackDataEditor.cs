#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace GAS.Editor
{
    /// <summary>
    /// RangedAttackData 自訂 Inspector
    /// 提供動畫預覽、時間軸簡化視圖、連招流程圖、位移視覺化
    /// 支援 Scene 中互動調整開火位置
    /// </summary>
    [CustomEditor(typeof(RangedAttackData))]
    public class RangedAttackDataEditor : UnityEditor.Editor
    {
        private bool _showAnimation = true;
        private bool _showTiming = true;
        private bool _showCombo = true;
        private bool _showTimeline = true;
        private bool _showDamage = true;
        private bool _showVFX = true;
        private bool _showAiming = true;
        private bool _showDirectionSolver = true;
        private bool _showMovement = true;
        private bool _showAttackMovement = true;
        private bool _showSpawn = true;
        private bool _showProjectile = true;
        private bool _showMultiShot = true;

        private float _clipLength = 1f;

        /// <summary>
        /// 取得 GASAttackDataEditorWindow 的當前時間軸時間（靜態共享）
        /// </summary>
        private static float GetTimelineCurrentTime()
        {
            var windowType = System.Type.GetType("GAS.Editor.GASAttackDataEditorWindow, Assembly-CSharp-Editor");
            if (windowType == null)
            {
                var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    windowType = asm.GetType("GAS.Editor.GASAttackDataEditorWindow");
                    if (windowType != null) break;
                }
            }
            if (windowType == null) return -1f;
            var field = windowType.GetField("_currentTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return -1f;
            var window = EditorWindow.GetWindow(windowType, false, "GAS Attack Editor", false);
            if (window == null) return -1f;
            return (float)field.GetValue(window);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += DrawSceneHandles;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawSceneHandles;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var attackData = (RangedAttackData)target;

            // 更新 clip 長度
            if (attackData.FireAnimation != null && attackData.FireAnimation.Clip != null)
            {
                _clipLength = attackData.FireAnimation.Clip.length;
            }

            // Timeline Editor 快速開啟按鈕
            DrawOpenTimelineButton();

            // 類型標識
            EditorGUILayout.Space(2);
            var typeRect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.DrawRect(typeRect, new Color(0.15f, 0.3f, 0.5f, 0.4f));
            var typeStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.4f, 0.8f, 1f) }
            };
            EditorGUI.LabelField(typeRect, "Ranged Attack Data", typeStyle);

            EditorGUILayout.Space(4);

            // 攻擊類型
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AttackType"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Charge"));

            EditorGUILayout.Space(4);

            // 動畫
            _showAnimation = DrawSection("Animation", _showAnimation, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("FireAnimation"));
                if (attackData.Charge != ChargeMode.None)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("ChargeStartAnimation"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("ChargeLoopAnimation"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("ChargeFireAnimation"));
                }
                if (attackData.FireAnimation != null && attackData.FireAnimation.Clip != null)
                {
                    EditorGUILayout.LabelField("Duration",
                        $"{_clipLength:F3}s ({(_clipLength * 30):F0} frames @ 30fps)",
                        EditorStyles.miniLabel);
                }
            });

            // 時間軸視覺化
            _showTiming = DrawSection("Timing", _showTiming, () =>
            {
                DrawTimingFields(attackData);
                EditorGUILayout.Space(4);
                DrawTimingBar(attackData);
            });

            // 連招流程圖
            _showCombo = DrawSection("Combo Links", _showCombo, () =>
            {
                DrawComboGraph(attackData);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("NextCombos"), true);
            });

            // Timeline Events
            _showTimeline = DrawSection("Timeline Events", _showTimeline, () =>
            {
                DrawTimelineEventsBar(attackData);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("TimelineEvents"), true);
            });

            // 傷害
            _showDamage = DrawSection("Damage", _showDamage, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("BaseDamage"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ChargeMultiplier"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HitEffect"));
            });

            // 投射物 / AoE
            _showProjectile = DrawSection("Projectile / AoE", _showProjectile, () =>
            {
                if (attackData.AttackType == RangedAttackType.Projectile)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("ProjectileConfig"), true);
                }
                else if (attackData.AttackType == RangedAttackType.AoETargeted ||
                         attackData.AttackType == RangedAttackType.AoEAtTarget)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("AoEPrefab"));
                    EditorGUILayout.HelpBox("AoE Prefab 必須掛有 AoEBehaviour — 範圍/Tick/特效全在 prefab 上設定", MessageType.Info);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("AoEOriginMode"));
                    if (attackData.AoEOriginMode == AoEOriginMode.PlayerForward)
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("AoEForwardDistance"));
                    }
                }
            });

            // VFX/SFX
            _showVFX = DrawSection("VFX / SFX Cues", _showVFX, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("FireCueTag"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ChargeCueTag"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HitCueTag"));
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Direct Hit FX (不需要 Cue 系統)", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HitVFXPrefab"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HitSFX"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HitVFXLifetime"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("AttachHitVFXToSurface"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HitVFXScale"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HitVFXScaleAllChildren"));
            });

            // 瞄準
            _showAiming = DrawSection("Aiming", _showAiming, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("EnableAimCamera"));
                if (attackData.EnableAimCamera)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("AimCameraOffset"));
                }
            });

            // 方向解算（Pitch Clamp 等 Solver 設定）
            _showDirectionSolver = DrawSection("Direction Solver", _showDirectionSolver, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ApplyPitchClamp"));
                if (attackData.ApplyPitchClamp)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("MaxPitchDown"));
                    EditorGUILayout.HelpBox(
                        "MaxPitchDown 限制下射角度的下限。\n0.8 ≈ 最多 53° 向下；1.0 ≈ 不夾。\n僅在敵人位於玩家正下方深處時會觸發。",
                        MessageType.Info);
                }
            });

            // 移動設定
            _showMovement = DrawSection("Movement", _showMovement, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("LockMovementDuringFire"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoFaceTarget"));
                if (attackData.AutoFaceTarget)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoFaceRange"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoFaceDuration"));
                }
            });

            // 攻擊位移（多段）
            _showAttackMovement = DrawSection("Attack Movement", _showAttackMovement, () =>
            {
                DrawMovementBars(attackData);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("AttackMovements"), true);
            });

            // 生成點
            _showSpawn = DrawSection("Spawn Point", _showSpawn, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("SpawnSocketName"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("SpawnOffset"));
                EditorGUILayout.HelpBox("可在 Scene 視窗中直接拖動橘色球體調整開火位置", MessageType.Info);
            });

            // 多發射擊 — 每發可獨立覆寫 FireTime/SpawnOffset/SpawnSocket/Damage/HitEffect
            _showMultiShot = DrawSection("Multi-Shot (Fire Events)", _showMultiShot, () =>
            {
                EditorGUILayout.HelpBox("留空時使用上方 Spawn Point + Damage + HitEffect 的單發設定。\n每發的 SpawnSocketNameOverride 留空會 fallback 到預設 SpawnSocketName。", MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("FireEvents"), true);
            });

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawOpenTimelineButton()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                fixedHeight = 28
            };

            if (GUILayout.Button("Open Timeline Editor", btnStyle, GUILayout.Width(200)))
            {
                var windowType = System.Type.GetType("GAS.Editor.GASAttackDataEditorWindow, Assembly-CSharp-Editor");
                if (windowType == null)
                {
                    var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in assemblies)
                    {
                        windowType = asm.GetType("GAS.Editor.GASAttackDataEditorWindow");
                        if (windowType != null) break;
                    }
                }

                if (windowType != null)
                {
                    EditorWindow.GetWindow(windowType, false, "GAS Attack Data Editor");
                }
                else
                {
                    EditorUtility.DisplayDialog("Not Found",
                        "GASAttackDataEditorWindow not found. Use Window > GAS menu.", "OK");
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 繪製時間欄位，每個欄位旁附帶「定位到時間軸」按鈕
        /// </summary>
        private void DrawTimingFields(RangedAttackData data)
        {
            DrawTimingFieldWithLocator("FireTime");

            if (data.Charge != ChargeMode.None)
            {
                DrawTimingFieldWithLocator("MinChargeTime");
                DrawTimingFieldWithLocator("MaxChargeTime");
            }

            DrawTimingFieldWithLocator("AllowInputTime");
            DrawTimingFieldWithLocator("ComboResetTime");
            DrawTimingFieldWithLocator("AllowCancelTime");
            DrawTimingFieldWithLocator("SheatheCancelTime");
        }

        /// <summary>
        /// 繪製帶有「定位」按鈕的時間欄位
        /// 按下按鈕會將該欄位設定為 Timeline Editor 的當前時間
        /// </summary>
        private void DrawTimingFieldWithLocator(string propertyName)
        {
            var prop = serializedObject.FindProperty(propertyName);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(prop);

            var btnStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 9,
                fixedWidth = 20,
                fixedHeight = 18
            };

            if (GUILayout.Button("\u25C9", btnStyle))
            {
                float timelineTime = GetTimelineCurrentTime();
                if (timelineTime >= 0f)
                {
                    Undo.RecordObject(target, $"Set {propertyName} to Timeline Time");
                    prop.floatValue = timelineTime;
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                }
                else
                {
                    EditorUtility.DisplayDialog("Timeline Editor",
                        "請先開啟 Timeline Editor 並設定時間軸位置", "OK");
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 繪製時間軸簡化 bar（含位移區段）
        /// </summary>
        private void DrawTimingBar(RangedAttackData data)
        {
            if (_clipLength <= 0) return;

            Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(30));
            barRect.x += EditorGUI.indentLevel * 15;
            barRect.width -= EditorGUI.indentLevel * 15;

            EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width, barRect.height),
                new Color(0.2f, 0.3f, 0.4f));

            // 繪製位移區段（紫色半透明條）
            if (data.AttackMovements != null)
            {
                for (int i = 0; i < data.AttackMovements.Count; i++)
                {
                    var moveCfg = data.AttackMovements[i];
                    if (!moveCfg.Enabled) continue;

                    float startNorm = Mathf.Clamp01(moveCfg.StartTime / _clipLength);
                    float endNorm = Mathf.Clamp01((moveCfg.StartTime + moveCfg.Duration) / _clipLength);
                    float x = barRect.x + barRect.width * startNorm;
                    float w = barRect.width * (endNorm - startNorm);

                    var moveColor = new Color(0.6f, 0.3f, 0.9f, 0.5f);
                    EditorGUI.DrawRect(new Rect(x, barRect.y + 2, w, barRect.height - 4), moveColor);

                    // 標記方向
                    string dirLabel = moveCfg.Distance >= 0 ? "\u2192" : "\u2190";
                    var moveLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Color.white },
                        fontSize = 9
                    };
                    EditorGUI.LabelField(new Rect(x, barRect.y, w, barRect.height),
                        $"Move{i + 1} {dirLabel}", moveLabelStyle);
                }
            }

            // FireTime 標記 (橘)
            DrawTimeMarker(barRect, data.FireTime / _clipLength,
                new Color(1f, 0.6f, 0f), "Fire");

            // AllowInputTime 標記 (綠)
            DrawTimeMarker(barRect, data.AllowInputTime / _clipLength,
                new Color(0.2f, 0.8f, 0.2f), "Input");

            // AllowCancelTime 標記 (黃)
            DrawTimeMarker(barRect, data.AllowCancelTime / _clipLength,
                new Color(0.9f, 0.9f, 0.2f), "Cancel");

            // ComboResetTime 標記 (紅)
            DrawTimeMarker(barRect, data.ComboResetTime / _clipLength,
                new Color(0.9f, 0.3f, 0.2f), "Reset");

            // SheatheCancelTime 標記 (紫)
            if (data.SheatheCancelTime >= 0f)
            {
                DrawTimeMarker(barRect, data.SheatheCancelTime / _clipLength,
                    new Color(0.6f, 0.4f, 1f), "Sheathe");
            }

            // 時間刻度
            var miniStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.gray }
            };
            EditorGUI.LabelField(
                new Rect(barRect.x, barRect.yMax, 40, 15),
                "0s", miniStyle);
            miniStyle.alignment = TextAnchor.UpperRight;
            EditorGUI.LabelField(
                new Rect(barRect.xMax - 40, barRect.yMax, 40, 15),
                $"{_clipLength:F2}s", miniStyle);

            GUILayout.Space(14);
        }

        private void DrawTimeMarker(Rect barRect, float normalizedTime, Color color, string label)
        {
            float t = Mathf.Clamp01(normalizedTime);
            float x = barRect.x + barRect.width * t;

            EditorGUI.DrawRect(new Rect(x - 1, barRect.y, 2, barRect.height), color);

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.LowerCenter,
                normal = { textColor = color },
                fontSize = 9
            };
            EditorGUI.LabelField(
                new Rect(x - 20, barRect.y - 12, 40, 14), label, labelStyle);
        }

        /// <summary>
        /// 繪製位移區段 bar（獨立 bar，在 Attack Movement section 中顯示）
        /// </summary>
        private void DrawMovementBars(RangedAttackData data)
        {
            if (data.AttackMovements == null || data.AttackMovements.Count == 0 || _clipLength <= 0)
            {
                EditorGUILayout.LabelField("No attack movements defined.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(24));
            barRect.x += EditorGUI.indentLevel * 15;
            barRect.width -= EditorGUI.indentLevel * 15;

            EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f));

            for (int i = 0; i < data.AttackMovements.Count; i++)
            {
                var moveCfg = data.AttackMovements[i];
                float startNorm = Mathf.Clamp01(moveCfg.StartTime / _clipLength);
                float endNorm = Mathf.Clamp01((moveCfg.StartTime + moveCfg.Duration) / _clipLength);
                float x = barRect.x + barRect.width * startNorm;
                float w = Mathf.Max(barRect.width * (endNorm - startNorm), 4f);

                Color moveColor = moveCfg.Enabled
                    ? new Color(0.6f, 0.3f, 0.9f, 0.7f)
                    : new Color(0.4f, 0.4f, 0.4f, 0.4f);
                EditorGUI.DrawRect(new Rect(x, barRect.y + 2, w, barRect.height - 4), moveColor);

                string dirLabel = moveCfg.Distance >= 0 ? $"+{moveCfg.Distance:F1}" : $"{moveCfg.Distance:F1}";
                var miniStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white },
                    fontSize = 9
                };
                EditorGUI.LabelField(new Rect(x, barRect.y, w, barRect.height),
                    $"M{i + 1} {dirLabel}m", miniStyle);
            }
        }

        /// <summary>
        /// 繪製連招流程圖（支援跨類型）
        /// </summary>
        private void DrawComboGraph(RangedAttackData data)
        {
            if (data.NextCombos == null || data.NextCombos.Count == 0)
            {
                EditorGUILayout.LabelField("No combo links defined.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var currentName = data.name;
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            EditorGUILayout.LabelField($"[ {currentName} ]", headerStyle);

            foreach (var combo in data.NextCombos)
            {
                if (combo.NextAttack == null) continue;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                var inputColor = combo.InputType switch
                {
                    MeleeInputType.LightAttack => new Color(0.4f, 0.7f, 1f),
                    MeleeInputType.HeavyAttack => new Color(1f, 0.5f, 0.3f),
                    MeleeInputType.Special => new Color(0.8f, 0.4f, 0.9f),
                    MeleeInputType.RangedAttack => new Color(0.3f, 0.9f, 0.6f),
                    _ => Color.gray
                };

                // 顯示攻擊類型標記
                string typeTag = combo.NextAttack is MeleeAttackData ? "[M]" : "[R]";

                var originalColor = GUI.color;
                GUI.color = inputColor;
                EditorGUILayout.LabelField(
                    $"--[ {combo.InputType} ]--> {typeTag} {combo.NextAttack.name}",
                    EditorStyles.miniLabel, GUILayout.Width(280));
                GUI.color = originalColor;

                if (GUILayout.Button("Go", EditorStyles.miniButton, GUILayout.Width(30)))
                {
                    Selection.activeObject = combo.NextAttack;
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 繪製 Timeline Events 簡化 bar
        /// </summary>
        private void DrawTimelineEventsBar(RangedAttackData data)
        {
            if (data.TimelineEvents == null || data.TimelineEvents.Count == 0 || _clipLength <= 0)
                return;

            Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(20));
            barRect.x += EditorGUI.indentLevel * 15;
            barRect.width -= EditorGUI.indentLevel * 15;

            EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f));

            foreach (var evt in data.TimelineEvents)
            {
                float tNorm = Mathf.Clamp01(evt.TriggerTime / _clipLength);
                float x = barRect.x + barRect.width * tNorm;

                var evtColor = new Color(0.3f, 0.7f, 0.9f, 0.9f);
                EditorGUI.DrawRect(new Rect(x - 3, barRect.y + 2, 6, barRect.height - 4), evtColor);

                if (!string.IsNullOrEmpty(evt.Name))
                {
                    var miniStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.LowerCenter,
                        normal = { textColor = evtColor },
                        fontSize = 8
                    };
                    EditorGUI.LabelField(
                        new Rect(x - 25, barRect.y - 12, 50, 14),
                        evt.Name, miniStyle);
                }
            }
        }

        #region Scene GUI

        /// <summary>
        /// 在 Scene 視窗中繪製可互動的開火位置把手
        /// </summary>
        private void DrawSceneHandles(SceneView sceneView)
        {
            var attackData = target as RangedAttackData;
            if (attackData == null) return;

            // 尋找場景中的玩家角色作為參考點
            var previewTarget = FindPreviewTarget();
            if (previewTarget == null) return;

            Transform socket = FindChildRecursive(previewTarget, attackData.SpawnSocketName);
            if (socket == null) socket = previewTarget;

            Vector3 spawnPos = socket.TransformPoint(attackData.SpawnOffset);
            Vector3 fireDir = previewTarget.forward;
            float handleSize = HandleUtility.GetHandleSize(spawnPos) * 0.1f;

            // 繪製生成點（橘色球）
            using (new Handles.DrawingScope(new Color(1f, 0.6f, 0f)))
            {
                Handles.SphereHandleCap(0, spawnPos, Quaternion.identity, handleSize * 2, EventType.Repaint);
                Handles.ArrowHandleCap(0, spawnPos, Quaternion.LookRotation(fireDir), handleSize * 5, EventType.Repaint);
            }

            Handles.Label(spawnPos + Vector3.up * 0.3f, "Spawn Point",
                new GUIStyle("WhiteLabel") { fontSize = 10, normal = { textColor = new Color(1f, 0.6f, 0f) } });

            // 可互動的位置把手
            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPos = Handles.PositionHandle(spawnPos, socket.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(attackData, "Move Spawn Point");
                attackData.SpawnOffset = socket.InverseTransformPoint(newWorldPos);
                EditorUtility.SetDirty(attackData);
            }

            // 繪製每個 FireEvent 的生成點（如果有多發設定）
            if (attackData.FireEvents != null && attackData.FireEvents.Count > 0)
            {
                for (int i = 0; i < attackData.FireEvents.Count; i++)
                {
                    var evt = attackData.FireEvents[i];
                    // 每發可獨立指定 socket;留空時 fallback 到預設 socket
                    Transform evtSocket = socket;
                    if (!string.IsNullOrEmpty(evt.SpawnSocketNameOverride))
                    {
                        Transform overrideSocket = FindChildRecursive(previewTarget, evt.SpawnSocketNameOverride);
                        if (overrideSocket != null) evtSocket = overrideSocket;
                    }
                    Vector3 evtPos = evtSocket.TransformPoint(evt.SpawnOffset);
                    float evtHandleSize = HandleUtility.GetHandleSize(evtPos) * 0.08f;

                    using (new Handles.DrawingScope(new Color(1f, 0.8f, 0.3f)))
                    {
                        Handles.SphereHandleCap(0, evtPos, Quaternion.identity, evtHandleSize * 2, EventType.Repaint);
                    }

                    Handles.Label(evtPos + Vector3.up * 0.2f, $"Fire {i + 1}",
                        new GUIStyle("WhiteLabel") { fontSize = 9, normal = { textColor = new Color(1f, 0.8f, 0.3f) } });

                    EditorGUI.BeginChangeCheck();
                    Vector3 newEvtPos = Handles.PositionHandle(evtPos, evtSocket.rotation);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(attackData, $"Move Fire Event {i + 1}");
                        evt.SpawnOffset = evtSocket.InverseTransformPoint(newEvtPos);
                        EditorUtility.SetDirty(attackData);
                    }
                }
            }
        }

        /// <summary>
        /// 在場景中尋找預覽用角色
        /// </summary>
        private Transform FindPreviewTarget()
        {
            var player = Object.FindAnyObjectByType<AbilitySystemComponent>();
            return player != null ? player.transform : null;
        }

        /// <summary>
        /// 遞迴搜尋子物件
        /// </summary>
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

        // === Helper ===

        private bool DrawSection(string title, bool isExpanded, System.Action drawContent)
        {
            var headerRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.foldoutHeader);
            var headerColor = new Color(0.22f, 0.22f, 0.22f, 0.6f);
            EditorGUI.DrawRect(headerRect, headerColor);

            isExpanded = EditorGUI.Foldout(headerRect, isExpanded, " " + title,
                true, EditorStyles.foldoutHeader);

            if (isExpanded)
            {
                EditorGUI.indentLevel++;
                drawContent();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }

            return isExpanded;
        }
    }
}
#endif
