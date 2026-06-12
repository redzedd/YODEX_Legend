using UnityEditor;
using UnityEngine;

namespace Enemy.AttackSystem.EditorTools
{
    /// <summary>
    /// EnemyAttackProfile 的 Custom Inspector
    /// 沿用 Unity 預設 Inspector 繪製所有 [SerializeField] 欄位 (除了 [HideInInspector] 的 _vfxEvents)
    /// 額外加一個「開啟攻擊時間軸編輯器」按鈕,點開後跳出 EnemyAttackProfileTimelineWindow 編輯 VFX 事件
    /// </summary>
    [CustomEditor(typeof(EnemyAttackProfile))]
    public class EnemyAttackProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("時間軸特效事件", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "VFX Events 由獨立時間軸視窗管理 — 點下方按鈕開啟。\n" +
                "時間軸內可看到 招架窗 / 命中窗 / 每個 VFX Event 的時間點,並選中編輯。",
                MessageType.Info);

            if (GUILayout.Button("開啟攻擊時間軸編輯器", GUILayout.Height(30)))
            {
                EnemyAttackProfileTimelineWindow.OpenForProfile((EnemyAttackProfile)target);
            }
        }
    }
}
