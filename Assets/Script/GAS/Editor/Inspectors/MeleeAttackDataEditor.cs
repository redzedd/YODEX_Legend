#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace GAS.Editor
{
    /// <summary>
    /// MeleeAttackData 自訂 Inspector
    /// 提供動畫預覽、時間軸簡化視圖、連招流程圖和 Timeline Editor 快速開啟
    /// </summary>
    [CustomEditor(typeof(MeleeAttackData))]
    public class MeleeAttackDataEditor : UnityEditor.Editor
    {
        private bool _showAnimation = true;
        private bool _showTiming = true;
        private bool _showCombo = true;
        private bool _showHitWindows = true;
        private bool _showTimeline = true;
        private bool _showMovement = true;

        // 時間軸視圖的動畫總長度
        private float _clipLength = 1f;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var attackData = (MeleeAttackData)target;

            // 更新 clip 長度
            if (attackData.Clip != null && attackData.Clip.Clip != null)
            {
                _clipLength = attackData.Clip.Clip.length;
            }

            // Timeline Editor 快速開啟按鈕
            DrawOpenTimelineButton();

            // 「以此攻擊為基礎建連擊下一段」快捷按鈕 — 開啟 AttackCreationWizard 並預填所有數值
            EditorGUILayout.Space(2);
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
            if (GUILayout.Button(new GUIContent(
                    "➕ 以此攻擊為基礎建連擊下一段",
                    "開啟 Create Attack wizard,placement 自動設為連擊延伸並預填所有數值。\n你只需改名與動畫即可一鍵建出下一段。"),
                GUILayout.Height(24)))
            {
                GAS.Editor.AttackCreation.AttackCreationWizard.OpenAsFollowUp(attackData);
            }
            GUI.backgroundColor = prevBg;

            EditorGUILayout.Space(4);

            // 動畫預覽
            _showAnimation = DrawSection("Animation", _showAnimation, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Clip"));
                if (attackData.Clip != null && attackData.Clip.Clip != null)
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

            // Hit Windows
            _showHitWindows = DrawSection("Hit Windows", _showHitWindows, () =>
            {
                DrawHitWindowsBar(attackData);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HitWindows"), true);
            });

            // Timeline Events
            _showTimeline = DrawSection("Timeline Events", _showTimeline, () =>
            {
                DrawTimelineEventsBar(attackData);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("TimelineEvents"), true);
            });

            // Movement Config
            _showMovement = DrawSection("Movement Config", _showMovement, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("MovementConfig"), true);
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
                // 嘗試開啟 GASAttackDataEditorWindow
                var windowType = System.Type.GetType("GAS.Editor.GASAttackDataEditorWindow, Assembly-CSharp-Editor");
                if (windowType == null)
                {
                    // 嘗試其他可能的 assembly
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

        private void DrawTimingFields(MeleeAttackData data)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AllowInputTime"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ComboResetTime"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AllowCancelTime"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SheatheCancelTime"));
        }

        /// <summary>
        /// 繪製時間軸簡化 bar（顯示 AllowInput/Cancel/Reset 時間點）
        /// </summary>
        private void DrawTimingBar(MeleeAttackData data)
        {
            if (_clipLength <= 0) return;

            Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(30));
            barRect.x += EditorGUI.indentLevel * 15;
            barRect.width -= EditorGUI.indentLevel * 15;

            // 背景
            EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f));

            // 動畫範圍
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width, barRect.height),
                new Color(0.25f, 0.25f, 0.25f));

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

            // 垂直線
            EditorGUI.DrawRect(new Rect(x - 1, barRect.y, 2, barRect.height), color);

            // 標籤
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
        /// 繪製連招流程圖（支援跨類型 Combo）
        /// </summary>
        private void DrawComboGraph(MeleeAttackData data)
        {
            if (data.NextCombos == null || data.NextCombos.Count == 0)
            {
                EditorGUILayout.LabelField("No combo links defined.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 當前攻擊
            var currentName = data.name;
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            EditorGUILayout.LabelField($"[ {currentName} ]", headerStyle);

            foreach (var combo in data.NextCombos)
            {
                if (combo.NextAttack == null) continue;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                // 輸入類型顏色
                var inputColor = combo.InputType switch
                {
                    MeleeInputType.LightAttack => new Color(0.4f, 0.7f, 1f),
                    MeleeInputType.HeavyAttack => new Color(1f, 0.5f, 0.3f),
                    MeleeInputType.Special => new Color(0.8f, 0.4f, 0.9f),
                    MeleeInputType.RangedAttack => new Color(0.3f, 0.9f, 0.6f),
                    _ => Color.gray
                };

                // 顯示攻擊類型標記 [M] = Melee, [R] = Ranged
                string typeTag = combo.NextAttack is MeleeAttackData ? "[M]" : "[R]";

                var originalColor = GUI.color;
                GUI.color = inputColor;
                EditorGUILayout.LabelField(
                    $"--[ {combo.InputType} ]--> {typeTag} {combo.NextAttack.name}",
                    EditorStyles.miniLabel, GUILayout.Width(280));
                GUI.color = originalColor;

                // 快速選取
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
        /// 繪製 Hit Windows 簡化 bar
        /// </summary>
        private void DrawHitWindowsBar(MeleeAttackData data)
        {
            if (data.HitWindows == null || data.HitWindows.Count == 0 || _clipLength <= 0) return;

            Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(20));
            barRect.x += EditorGUI.indentLevel * 15;
            barRect.width -= EditorGUI.indentLevel * 15;

            EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f));

            for (int i = 0; i < data.HitWindows.Count; i++)
            {
                var hw = data.HitWindows[i];
                float startNorm = Mathf.Clamp01(hw.StartTime / _clipLength);
                float endNorm = Mathf.Clamp01(hw.EndTime / _clipLength);

                float x = barRect.x + barRect.width * startNorm;
                float w = barRect.width * (endNorm - startNorm);

                var hitColor = new Color(0.9f, 0.3f, 0.2f, 0.7f);
                EditorGUI.DrawRect(new Rect(x, barRect.y + 2, w, barRect.height - 4), hitColor);

                // 標籤
                var miniStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white },
                    fontSize = 9
                };
                EditorGUI.LabelField(new Rect(x, barRect.y, w, barRect.height),
                    $"Hit{i + 1}", miniStyle);
            }
        }

        /// <summary>
        /// 繪製 Timeline Events 簡化 bar
        /// </summary>
        private void DrawTimelineEventsBar(MeleeAttackData data)
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

                // 菱形標記
                var evtColor = new Color(0.3f, 0.7f, 0.9f, 0.9f);
                EditorGUI.DrawRect(new Rect(x - 3, barRect.y + 2, 6, barRect.height - 4), evtColor);

                // 名稱
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
