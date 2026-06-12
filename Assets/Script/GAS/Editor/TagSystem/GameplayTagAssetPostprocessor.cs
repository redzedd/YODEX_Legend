#if UNITY_EDITOR
using UnityEditor;

namespace GAS.Editor.TagSystem
{
    /// <summary>
    /// 監聽 GameplayTagLibrary.asset 變動 → 自動觸發 GameplayTagCodeGenerator。
    /// 設計師存檔 Library 即自動更新 GameplayTags.generated.cs,完全免按按鈕。
    /// </summary>
    public class GameplayTagAssetPostprocessor : AssetPostprocessor
    {
        private const string LIBRARY_PATH = "Assets/Resources/GameplayTagLibrary.asset";

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool libraryTouched = false;
            foreach (string path in importedAssets)
            {
                if (path == LIBRARY_PATH)
                {
                    libraryTouched = true;
                    break;
                }
            }
            if (!libraryTouched)
            {
                return;
            }
            GameplayTagLibrary library = AssetDatabase.LoadAssetAtPath<GameplayTagLibrary>(LIBRARY_PATH);
            if (library == null)
            {
                return;
            }
            // 靜默重新生成 — 出錯時 Generator 自己會 Debug.LogError;成功時不彈窗
            GameplayTagCodeGenerator.RegenerateSilent(library);
            // 清 Drawer cache,讓 Inspector 紅字與下拉選單即時反映新的 Tag 集合
            GAS.Editor.GameplayTagDrawer.ClearCache();
        }
    }
}
#endif
