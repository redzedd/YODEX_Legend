using UnityEngine;

namespace GAS
{
    /// <summary>
    /// GAS 系統初始化器 - 確保系統組件正確初始化
    /// 放置在場景中的 Manager 物件上
    /// </summary>
    public class GASInitializer : MonoBehaviour
    {
        [Header("Required Components")]
        [Tooltip("Gameplay Cue Manager")]
        [SerializeField] private GameplayCueManager _cueManager;

        [Header("Optional References")]
        [Tooltip("標籤庫 (如果不使用 Resources 加載)")]
        [SerializeField] private GameplayTagLibrary _tagLibrary;

        [Header("Settings")]
        [Tooltip("啟用調試日誌")]
        public bool DebugMode = false;

        private static GASInitializer _instance;
        public static GASInitializer Instance => _instance;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                Initialize();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Initialize()
        {
            if (DebugMode)
            {
                Debug.Log("[GAS] Initializing Gameplay Ability System...");
            }

            // 初始化標籤庫
            if (_tagLibrary != null)
            {
                _tagLibrary.Initialize();
                if (DebugMode)
                {
                    Debug.Log("[GAS] Tag Library initialized");
                }
            }

            // 確保 Cue Manager 存在
            if (_cueManager == null)
            {
                _cueManager = FindFirstObjectByType<GameplayCueManager>();
            }

            if (_cueManager == null)
            {
                // 創建 Cue Manager
                var cueManagerObj = new GameObject("GameplayCueManager");
                cueManagerObj.transform.SetParent(transform);
                _cueManager = cueManagerObj.AddComponent<GameplayCueManager>();
                
                if (DebugMode)
                {
                    Debug.Log("[GAS] Created GameplayCueManager");
                }
            }

            if (DebugMode)
            {
                Debug.Log("[GAS] Gameplay Ability System initialized successfully!");
            }
        }

        /// <summary>
        /// 獲取 Cue Manager
        /// </summary>
        public GameplayCueManager GetCueManager()
        {
            return _cueManager;
        }

        /// <summary>
        /// 獲取 Tag Library
        /// </summary>
        public GameplayTagLibrary GetTagLibrary()
        {
            return _tagLibrary;
        }
    }
}
