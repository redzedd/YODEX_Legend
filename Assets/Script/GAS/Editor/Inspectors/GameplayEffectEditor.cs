#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace GAS.Editor
{
    /// <summary>
    /// GameplayEffect 的自定義 Inspector
    /// 提供更直觀的效果配置界面
    /// </summary>
    [CustomEditor(typeof(GameplayEffect))]
    public class GameplayEffectEditor : UnityEditor.Editor
    {
        private SerializedProperty _effectName;
        private SerializedProperty _effectTag;
        private SerializedProperty _description;
        private SerializedProperty _durationPolicy;
        private SerializedProperty _duration;
        private SerializedProperty _periodicPolicy;
        private SerializedProperty _period;
        private SerializedProperty _stackingPolicy;
        private SerializedProperty _maxStacks;
        private SerializedProperty _stackMagnitudeMultiplier;
        private SerializedProperty _modifiers;
        private SerializedProperty _grantedTags;
        private SerializedProperty _removeTagsOnEnd;
        private SerializedProperty _applicationRequiredTags;
        private SerializedProperty _applicationBlockedTags;
        private SerializedProperty _ongoingRequiredTags;
        private SerializedProperty _cueTags;
        private SerializedProperty _removeEffectsWithTags;

        private ReorderableList _modifiersList;
        private ReorderableList _cueTagsList;

        private bool _showBasicInfo = true;
        private bool _showDuration = true;
        private bool _showStacking = true;
        private bool _showModifiers = true;
        private bool _showTags = true;
        private bool _showCues = true;
        private bool _showRemoval = true;

        private void OnEnable()
        {
            _effectName = serializedObject.FindProperty("EffectName");
            _effectTag = serializedObject.FindProperty("EffectTag");
            _description = serializedObject.FindProperty("Description");
            _durationPolicy = serializedObject.FindProperty("DurationPolicy");
            _duration = serializedObject.FindProperty("Duration");
            _periodicPolicy = serializedObject.FindProperty("PeriodicPolicy");
            _period = serializedObject.FindProperty("Period");
            _stackingPolicy = serializedObject.FindProperty("StackingPolicy");
            _maxStacks = serializedObject.FindProperty("MaxStacks");
            _stackMagnitudeMultiplier = serializedObject.FindProperty("StackMagnitudeMultiplier");
            _modifiers = serializedObject.FindProperty("Modifiers");
            _grantedTags = serializedObject.FindProperty("GrantedTags");
            _removeTagsOnEnd = serializedObject.FindProperty("RemoveTagsOnEnd");
            _applicationRequiredTags = serializedObject.FindProperty("ApplicationRequiredTags");
            _applicationBlockedTags = serializedObject.FindProperty("ApplicationBlockedTags");
            _ongoingRequiredTags = serializedObject.FindProperty("OngoingRequiredTags");
            _cueTags = serializedObject.FindProperty("CueTags");
            _removeEffectsWithTags = serializedObject.FindProperty("RemoveEffectsWithTags");

            SetupModifiersList();
            SetupCueTagsList();
        }

        private void SetupModifiersList()
        {
            _modifiersList = new ReorderableList(serializedObject, _modifiers, true, true, true, true);

            _modifiersList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Attribute Modifiers", EditorStyles.boldLabel);
            };

            _modifiersList.elementHeightCallback = (int index) =>
            {
                if (index >= _modifiers.arraySize) return EditorGUIUtility.singleLineHeight;
                
                var element = _modifiers.GetArrayElementAtIndex(index);
                // 使用 PropertyDrawer 回報的實際高度，並增加上下間距避免擠壓
                float height = EditorGUI.GetPropertyHeight(element, true);
                return height + 12; // 上下各 6px 間距，確保欄位不重疊
            };

            _modifiersList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= _modifiers.arraySize) return;

                var element = _modifiers.GetArrayElementAtIndex(index);
                
                // 上下各留 6px 邊距，不縮減傳給 Drawer 的高度
                float verticalMargin = 6f;
                rect.y += verticalMargin;
                rect.height -= verticalMargin * 2;
                
                // 繪製背景（選中時）
                if (isActive || isFocused)
                {
                    EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), 
                        new Color(0.24f, 0.48f, 0.90f, 0.2f));
                }
                
                // 繪製屬性（Drawer 內部會依各欄位實際高度繪製，不會重疊）
                EditorGUI.PropertyField(rect, element, GUIContent.none, true);
            };

            _modifiersList.onAddCallback = (ReorderableList list) =>
            {
                _modifiers.arraySize++;
                var newElement = _modifiers.GetArrayElementAtIndex(_modifiers.arraySize - 1);
                
                // 設置默認值
                newElement.FindPropertyRelative("AttributeName").stringValue = CombatAttributes.IncomingDamage;
                newElement.FindPropertyRelative("OperationType").enumValueIndex = 0; // Additive
                newElement.FindPropertyRelative("Magnitude").floatValue = 10f;
                newElement.FindPropertyRelative("MagnitudeType").enumValueIndex = 0; // ScalableFloat
                
                // 標記為已修改
                serializedObject.ApplyModifiedProperties();
            };
            
            _modifiersList.onRemoveCallback = (ReorderableList list) =>
            {
                if (EditorUtility.DisplayDialog("確認刪除", 
                    "確定要刪除這個 Modifier 嗎？", "刪除", "取消"))
                {
                    ReorderableList.defaultBehaviours.DoRemoveButton(list);
                    serializedObject.ApplyModifiedProperties();
                }
            };
        }

        private void SetupCueTagsList()
        {
            _cueTagsList = new ReorderableList(serializedObject, _cueTags, true, true, true, true);

            _cueTagsList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Cue Tags");
            };

            _cueTagsList.elementHeight = EditorGUIUtility.singleLineHeight + 4;

            _cueTagsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= _cueTags.arraySize) return;

                var element = _cueTags.GetArrayElementAtIndex(index);
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(rect, element, GUIContent.none);
            };

            _cueTagsList.onAddCallback = (ReorderableList list) =>
            {
                _cueTags.arraySize++;
                var newElement = _cueTags.GetArrayElementAtIndex(_cueTags.arraySize - 1);
                newElement.FindPropertyRelative("_tagName").stringValue = "Cue.";
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 標題
            EditorGUILayout.LabelField("Gameplay Effect", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 效果預覽卡片
            DrawEffectPreview();
            EditorGUILayout.Space(10);

            // 基本資訊
            _showBasicInfo = DrawSection("Basic Info", _showBasicInfo, () =>
            {
                EditorGUILayout.PropertyField(_effectName);
                EditorGUILayout.PropertyField(_effectTag);
                EditorGUILayout.PropertyField(_description);
            });

            // 持續時間設定
            _showDuration = DrawSection("Duration & Periodic", _showDuration, () =>
            {
                EditorGUILayout.PropertyField(_durationPolicy);

                var policy = (DurationPolicy)_durationPolicy.enumValueIndex;
                
                if (policy == DurationPolicy.Duration)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_duration);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(_periodicPolicy);

                var periodicPolicy = (PeriodicPolicy)_periodicPolicy.enumValueIndex;
                if (periodicPolicy != PeriodicPolicy.None)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_period);
                    EditorGUI.indentLevel--;
                }
            });

            // 堆疊設定
            _showStacking = DrawSection("Stacking", _showStacking, () =>
            {
                EditorGUILayout.PropertyField(_stackingPolicy);

                var stackPolicy = (StackingPolicy)_stackingPolicy.enumValueIndex;
                if (stackPolicy != StackingPolicy.None)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_maxStacks);
                    EditorGUILayout.PropertyField(_stackMagnitudeMultiplier);
                    EditorGUI.indentLevel--;
                }
            });

            // 修改器列表
            _showModifiers = DrawSection("Modifiers", _showModifiers, () =>
            {
                // 添加提示訊息
                if (_modifiers.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("沒有 Modifier。點擊 + 按鈕添加一個。", MessageType.Info);
                }
                
                // 顯示摘要資訊
                if (_modifiers.arraySize > 0)
                {
                    EditorGUILayout.LabelField($"共 {_modifiers.arraySize} 個 Modifier(s)", EditorStyles.miniLabel);
                    EditorGUILayout.Space(2);
                }
                
                // 繪製列表
                _modifiersList.DoLayoutList();
            });

            // 標籤設定
            _showTags = DrawSection("Tags", _showTags, () =>
            {
                EditorGUILayout.LabelField("Effect Tags", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_grantedTags, new GUIContent("Granted Tags"));
                EditorGUILayout.PropertyField(_removeTagsOnEnd, new GUIContent("Remove On End"));
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Application Conditions", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_applicationRequiredTags, new GUIContent("Required Tags"));
                EditorGUILayout.PropertyField(_applicationBlockedTags, new GUIContent("Blocked Tags"));
                EditorGUILayout.PropertyField(_ongoingRequiredTags, new GUIContent("Ongoing Required"));
                EditorGUI.indentLevel--;
            });

            // Cues
            _showCues = DrawSection("Gameplay Cues", _showCues, () =>
            {
                _cueTagsList.DoLayoutList();
            });

            // 移除效果
            _showRemoval = DrawSection("Remove Effects", _showRemoval, () =>
            {
                EditorGUILayout.PropertyField(_removeEffectsWithTags);
            });

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawEffectPreview()
        {
            var effect = target as GameplayEffect;
            if (effect == null) return;

            // 預覽卡片背景
            Color bgColor = GetDurationColor(effect.DurationPolicy);
            Rect rect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.DrawTexture(rect, MakeColorTexture(bgColor));

            EditorGUILayout.BeginHorizontal();
            
            // 左側：效果圖標和名稱
            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            EditorGUILayout.LabelField(string.IsNullOrEmpty(effect.EffectName) ? "(Unnamed)" : effect.EffectName, 
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(effect.EffectTag.TagName, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            // 右側：快速資訊
            EditorGUILayout.BeginVertical();
            
            // Duration 資訊
            string durationText = effect.DurationPolicy switch
            {
                DurationPolicy.Instant => "Instant",
                DurationPolicy.Duration => $"Duration: {effect.Duration}s",
                DurationPolicy.Infinite => "Infinite",
                _ => "Unknown"
            };
            EditorGUILayout.LabelField(durationText);

            // Modifier 摘要
            if (effect.Modifiers != null && effect.Modifiers.Count > 0)
            {
                string modSummary = $"{effect.Modifiers.Count} modifier(s)";
                EditorGUILayout.LabelField(modSummary, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private bool DrawSection(string title, bool isExpanded, System.Action drawContent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            isExpanded = EditorGUILayout.Foldout(isExpanded, title, true, EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();

            if (isExpanded)
            {
                EditorGUILayout.Space(2);
                drawContent();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);

            return isExpanded;
        }

        private Color GetDurationColor(DurationPolicy policy)
        {
            return policy switch
            {
                DurationPolicy.Instant => new Color(0.3f, 0.6f, 0.3f, 0.3f),
                DurationPolicy.Duration => new Color(0.3f, 0.5f, 0.7f, 0.3f),
                DurationPolicy.Infinite => new Color(0.6f, 0.4f, 0.6f, 0.3f),
                _ => new Color(0.3f, 0.3f, 0.3f, 0.3f)
            };
        }

        private Texture2D MakeColorTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
#endif
