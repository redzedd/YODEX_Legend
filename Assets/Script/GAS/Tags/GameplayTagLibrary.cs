using System;
using System.Collections.Generic;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 標籤庫 - ScriptableObject，用於定義和管理所有遊戲標籤
    /// 在編輯器中配置，運行時作為標籤的來源
    /// </summary>
    [CreateAssetMenu(fileName = "GameplayTagLibrary", menuName = "GAS/Tag Library")]
    public class GameplayTagLibrary : ScriptableObject
    {
        [Serializable]
        public class TagDefinition
        {
            [Tooltip("標籤名稱 (例如: Ability.Attack.Melee)")]
            public string TagName;
            
            [Tooltip("標籤描述")]
            [TextArea(1, 3)]
            public string Description;
        }

        [Header("Tag Definitions")]
        [SerializeField]
        private List<TagDefinition> _tagDefinitions = new();

        // 運行時快取
        private Dictionary<string, GameplayTag> _tagCache;
        private bool _isInitialized;

        /// <summary>
        /// 所有已定義的標籤
        /// </summary>
        public IReadOnlyList<TagDefinition> TagDefinitions => _tagDefinitions;

        private void OnEnable()
        {
            Initialize();
        }

        /// <summary>
        /// 初始化標籤快取
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;
            
            _tagCache = new Dictionary<string, GameplayTag>();
            
            foreach (var def in _tagDefinitions)
            {
                if (!string.IsNullOrEmpty(def.TagName))
                {
                    _tagCache[def.TagName] = new GameplayTag(def.TagName);
                }
            }
            
            _isInitialized = true;
        }

        /// <summary>
        /// 根據名稱獲取標籤
        /// </summary>
        public GameplayTag GetTag(string tagName)
        {
            if (!_isInitialized) Initialize();
            
            if (_tagCache.TryGetValue(tagName, out var tag))
            {
                return tag;
            }
            
            // 如果標籤不在定義中，創建一個新的 (但會打印警告)
            Debug.LogWarning($"[GameplayTagLibrary] Tag '{tagName}' not found in library. Creating dynamic tag.");
            return new GameplayTag(tagName);
        }

        /// <summary>
        /// 檢查標籤是否已定義
        /// </summary>
        public bool IsTagDefined(string tagName)
        {
            if (!_isInitialized) Initialize();
            return _tagCache.ContainsKey(tagName);
        }

        /// <summary>
        /// 獲取所有已定義的標籤名稱
        /// </summary>
        public IEnumerable<string> GetAllTagNames()
        {
            if (!_isInitialized) Initialize();
            return _tagCache.Keys;
        }

        /// <summary>
        /// 獲取指定前綴的所有標籤
        /// </summary>
        public List<GameplayTag> GetTagsWithPrefix(string prefix)
        {
            if (!_isInitialized) Initialize();
            
            var result = new List<GameplayTag>();
            foreach (var kvp in _tagCache)
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    result.Add(kvp.Value);
                }
            }
            return result;
        }

        #region Editor Utilities

#if UNITY_EDITOR
        /// <summary>
        /// 添加新標籤定義 (僅編輯器)
        /// </summary>
        public void AddTagDefinition(string tagName, string description = "")
        {
            if (string.IsNullOrEmpty(tagName)) return;
            
            // 檢查是否已存在
            foreach (var def in _tagDefinitions)
            {
                if (def.TagName == tagName) return;
            }
            
            _tagDefinitions.Add(new TagDefinition
            {
                TagName = tagName,
                Description = description
            });
            
            // 重新初始化快取
            _isInitialized = false;
            Initialize();
            
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// 移除標籤定義 (僅編輯器)
        /// </summary>
        public bool RemoveTagDefinition(string tagName)
        {
            for (int i = _tagDefinitions.Count - 1; i >= 0; i--)
            {
                if (_tagDefinitions[i].TagName == tagName)
                {
                    _tagDefinitions.RemoveAt(i);
                    _isInitialized = false;
                    Initialize();
                    UnityEditor.EditorUtility.SetDirty(this);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 排序標籤定義 (僅編輯器)
        /// </summary>
        public void SortTagDefinitions()
        {
            _tagDefinitions.Sort((a, b) => string.Compare(a.TagName, b.TagName, StringComparison.Ordinal));
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        #endregion

        #region Singleton Access

        private static GameplayTagLibrary _instance;

        /// <summary>
        /// 全局標籤庫實例 (需要在 Resources 資料夾中放置)
        /// </summary>
        public static GameplayTagLibrary Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<GameplayTagLibrary>("GameplayTagLibrary");
                    
                    if (_instance == null)
                    {
                        Debug.LogWarning("[GameplayTagLibrary] No GameplayTagLibrary found in Resources folder. " +
                                       "Create one via Assets > Create > GAS > Tag Library and place it in a Resources folder.");
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 靜態方法：根據名稱獲取標籤
        /// </summary>
        public static GameplayTag RequestTag(string tagName)
        {
            if (Instance != null)
            {
                return Instance.GetTag(tagName);
            }
            
            // 如果沒有標籤庫，直接創建標籤
            return new GameplayTag(tagName);
        }

        #endregion
    }
}
