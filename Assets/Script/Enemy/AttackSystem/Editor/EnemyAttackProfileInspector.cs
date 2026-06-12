using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Enemy.AttackSystem.EditorTools
{
    /// <summary>
    /// EnemyAttackProfile 的標準 Inspector — 在最上方加一顆大按鈕直通時間軸編輯器，
    /// 並支援雙擊 .asset 直接開窗載入該招式。
    /// 其餘欄位走 DrawDefaultInspector，跟原本一樣（VFX 清單因為 [HideInInspector] 不會出現在這裡）。
    /// </summary>
    [CustomEditor(typeof(EnemyAttackProfile))]
    public class EnemyAttackProfileInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EnemyAttackProfile profile = (EnemyAttackProfile)target;

            EditorGUILayout.Space(6);

            GUIStyle bigButton = new GUIStyle(GUI.skin.button);
            bigButton.fontSize = 13;
            bigButton.fontStyle = FontStyle.Bold;
            if (GUILayout.Button("開啟攻擊時間軸編輯器", bigButton, GUILayout.Height(38)))
            {
                EnemyAttackProfileTimelineWindow.OpenForProfile(profile);
            }

            int vfxCount = profile.VfxEvents?.Count ?? 0;
            EditorGUILayout.LabelField($"目前 VFX 事件數：{vfxCount}（透過時間軸視窗編輯）", EditorStyles.miniLabel);

            EditorGUILayout.Space(8);
            DrawDefaultInspector();
        }

        // 雙擊 EnemyAttackProfile.asset 時 Unity 會呼叫此方法 — 攔截為「直接開時間軸視窗」
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceID, int line)
        {
            Object obj = EditorUtility.InstanceIDToObject(instanceID);
            EnemyAttackProfile profile = obj as EnemyAttackProfile;
            if (profile == null)
            {
                return false;
            }
            EnemyAttackProfileTimelineWindow.OpenForProfile(profile);
            return true;
        }
    }
}
