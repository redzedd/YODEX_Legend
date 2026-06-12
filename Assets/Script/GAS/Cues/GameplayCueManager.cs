using System.Collections.Generic;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// Gameplay Cue 管理器 - 全局管理所有 Cue 的執行
    /// 場景中需要一個此組件的實例
    /// </summary>
    public class GameplayCueManager : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("註冊的 Cue 列表")]
        [SerializeField] private List<GameplayCue> _registeredCues = new();

        [Header("Debug")]
        [Tooltip("啟用調試日誌")]
        public bool DebugMode = false;

        // Cue 查找表
        private Dictionary<string, GameplayCue> _cueMap = new();

        // 活躍的持續 Cue
        private readonly List<GameplayCueHandler> _activeCues = new();

        // 單例
        private static GameplayCueManager _instance;
        public static GameplayCueManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<GameplayCueManager>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            InitializeCueMap();
        }

        private void Update()
        {
            // 更新所有活躍的持續 Cue
            for (int i = _activeCues.Count - 1; i >= 0; i--)
            {
                var handler = _activeCues[i];
                handler.Tick(Time.deltaTime);

                if (!handler.IsActive)
                {
                    _activeCues.RemoveAt(i);
                }
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// 初始化 Cue 查找表
        /// </summary>
        private void InitializeCueMap()
        {
            _cueMap.Clear();

            foreach (var cue in _registeredCues)
            {
                if (cue != null && cue.CueTag.IsValid)
                {
                    _cueMap[cue.CueTag.TagName] = cue;
                }
            }

            if (DebugMode)
            {
                Debug.Log($"[GameplayCueManager] Initialized with {_cueMap.Count} cues");
            }
        }

        /// <summary>
        /// 註冊 Cue
        /// </summary>
        public void RegisterCue(GameplayCue cue)
        {
            if (cue == null || !cue.CueTag.IsValid) return;

            _cueMap[cue.CueTag.TagName] = cue;
            
            if (!_registeredCues.Contains(cue))
            {
                _registeredCues.Add(cue);
            }
        }

        /// <summary>
        /// 執行一次性 Cue
        /// </summary>
        public void ExecuteCue(GameplayTag cueTag, GameplayCueParameters parameters)
        {
            var cue = FindCue(cueTag);
            if (cue == null)
            {
                if (DebugMode)
                {
                    Debug.LogWarning($"[GameplayCueManager] Cue not found: {cueTag}");
                }
                return;
            }

            if (DebugMode)
            {
                Debug.Log($"[GameplayCueManager] Executing cue: {cueTag}");
            }

            cue.OnExecute(parameters);
        }

        /// <summary>
        /// 啟動持續 Cue
        /// </summary>
        public GameplayCueHandler ActivateCue(GameplayTag cueTag, GameplayCueParameters parameters)
        {
            var cue = FindCue(cueTag);
            if (cue == null)
            {
                if (DebugMode)
                {
                    Debug.LogWarning($"[GameplayCueManager] Cue not found: {cueTag}");
                }
                return null;
            }

            var handler = new GameplayCueHandler(cue, parameters);
            handler.Activate();
            
            // [FIX] 從 VFXCue 獲取生成的實例
            if (cue is VFXCue vfxCue && vfxCue.LastSpawnedInstance != null)
            {
                handler.SpawnedObject = vfxCue.LastSpawnedInstance;
                vfxCue.LastSpawnedInstance = null; // 清除引用，避免混淆
            }
            
            _activeCues.Add(handler);

            if (DebugMode)
            {
                Debug.Log($"[GameplayCueManager] Activated cue: {cueTag}, SpawnedObject: {handler.SpawnedObject?.name ?? "null"}");
            }

            return handler;
        }

        /// <summary>
        /// 停用持續 Cue
        /// </summary>
        public void DeactivateCue(GameplayCueHandler handler)
        {
            if (handler == null) return;

            handler.Deactivate();
            _activeCues.Remove(handler);

            if (DebugMode)
            {
                Debug.Log($"[GameplayCueManager] Deactivated cue: {handler.CueDef.CueTag}");
            }
        }

        /// <summary>
        /// 停用所有指定標籤的 Cue
        /// </summary>
        public void DeactivateAllCuesWithTag(GameplayTag cueTag)
        {
            for (int i = _activeCues.Count - 1; i >= 0; i--)
            {
                var handler = _activeCues[i];
                if (handler.CueDef.CueTag.MatchesTagHierarchy(cueTag))
                {
                    handler.Deactivate();
                    _activeCues.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 查找 Cue
        /// </summary>
        private GameplayCue FindCue(GameplayTag cueTag)
        {
            if (!cueTag.IsValid) return null;

            // 完全匹配
            if (_cueMap.TryGetValue(cueTag.TagName, out var cue))
            {
                return cue;
            }

            // 階層匹配 (找最接近的父 Cue)
            foreach (var kvp in _cueMap)
            {
                if (cueTag.MatchesTagHierarchy(kvp.Value.CueTag))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// 清除所有活躍 Cue
        /// </summary>
        public void ClearAllActiveCues()
        {
            foreach (var handler in _activeCues)
            {
                handler.Deactivate();
            }
            _activeCues.Clear();
        }

#if UNITY_EDITOR
        [ContextMenu("Reload Cues")]
        private void EditorReloadCues()
        {
            InitializeCueMap();
        }
#endif
    }
}
