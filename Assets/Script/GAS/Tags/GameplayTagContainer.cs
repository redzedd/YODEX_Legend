using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 標籤容器 - 管理一組 GameplayTag
    /// 用於能力的條件檢查、狀態追蹤等
    /// </summary>
    [Serializable]
    public class GameplayTagContainer : IEnumerable<GameplayTag>
    {
        [SerializeField]
        private List<GameplayTag> _tags = new();

        /// <summary>
        /// 容器中的標籤數量
        /// </summary>
        public int Count => _tags.Count;

        /// <summary>
        /// 容器是否為空
        /// </summary>
        public bool IsEmpty => _tags.Count == 0;

        /// <summary>
        /// 當標籤被添加時觸發
        /// </summary>
        public event Action<GameplayTag> OnTagAdded;

        /// <summary>
        /// 當標籤被移除時觸發
        /// </summary>
        public event Action<GameplayTag> OnTagRemoved;

        public GameplayTagContainer()
        {
            _tags = new List<GameplayTag>();
        }

        public GameplayTagContainer(params GameplayTag[] tags)
        {
            _tags = new List<GameplayTag>(tags);
        }

        public GameplayTagContainer(IEnumerable<GameplayTag> tags)
        {
            _tags = new List<GameplayTag>(tags);
        }

        #region Add/Remove Operations

        /// <summary>
        /// 添加標籤 (如果不存在)
        /// </summary>
        public bool AddTag(GameplayTag tag)
        {
            if (!tag.IsValid) return false;
            if (HasTagExact(tag)) return false;

            _tags.Add(tag);
            OnTagAdded?.Invoke(tag);
            return true;
        }

        /// <summary>
        /// 添加多個標籤
        /// </summary>
        public void AddTags(GameplayTagContainer other)
        {
            if (other == null) return;
            foreach (var tag in other._tags)
            {
                AddTag(tag);
            }
        }

        /// <summary>
        /// 移除標籤 (完全匹配)
        /// </summary>
        public bool RemoveTag(GameplayTag tag)
        {
            for (int i = _tags.Count - 1; i >= 0; i--)
            {
                if (_tags[i].MatchesTag(tag))
                {
                    _tags.RemoveAt(i);
                    OnTagRemoved?.Invoke(tag);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 移除多個標籤
        /// </summary>
        public void RemoveTags(GameplayTagContainer other)
        {
            if (other == null) return;
            foreach (var tag in other._tags)
            {
                RemoveTag(tag);
            }
        }

        /// <summary>
        /// 清空所有標籤
        /// </summary>
        public void Clear()
        {
            var oldTags = new List<GameplayTag>(_tags);
            _tags.Clear();
            
            foreach (var tag in oldTags)
            {
                OnTagRemoved?.Invoke(tag);
            }
        }

        #endregion

        #region Query Operations

        /// <summary>
        /// 檢查是否包含指定標籤 (完全匹配)
        /// </summary>
        public bool HasTagExact(GameplayTag tag)
        {
            if (!tag.IsValid) return false;
            
            foreach (var t in _tags)
            {
                if (t.MatchesTag(tag)) return true;
            }
            return false;
        }

        /// <summary>
        /// 檢查是否包含指定標籤 (階層匹配)
        /// 例如: 如果容器有 "Ability.Attack.Melee"，查詢 "Ability.Attack" 會返回 true
        /// </summary>
        public bool HasTag(GameplayTag tag)
        {
            if (!tag.IsValid) return false;
            
            foreach (var t in _tags)
            {
                if (t.MatchesTagHierarchy(tag)) return true;
            }
            return false;
        }

        /// <summary>
        /// 檢查是否包含任一指定標籤 (完全匹配)
        /// </summary>
        public bool HasAnyExact(GameplayTagContainer other)
        {
            if (other == null || other.IsEmpty) return false;
            
            foreach (var tag in other._tags)
            {
                if (HasTagExact(tag)) return true;
            }
            return false;
        }

        /// <summary>
        /// 檢查是否包含任一指定標籤 (階層匹配)
        /// </summary>
        public bool HasAny(GameplayTagContainer other)
        {
            if (other == null || other.IsEmpty) return false;
            
            foreach (var tag in other._tags)
            {
                if (HasTag(tag)) return true;
            }
            return false;
        }

        /// <summary>
        /// 檢查是否包含所有指定標籤 (完全匹配)
        /// </summary>
        public bool HasAllExact(GameplayTagContainer other)
        {
            if (other == null || other.IsEmpty) return true;
            
            foreach (var tag in other._tags)
            {
                if (!HasTagExact(tag)) return false;
            }
            return true;
        }

        /// <summary>
        /// 檢查是否包含所有指定標籤 (階層匹配)
        /// </summary>
        public bool HasAll(GameplayTagContainer other)
        {
            if (other == null || other.IsEmpty) return true;
            
            foreach (var tag in other._tags)
            {
                if (!HasTag(tag)) return false;
            }
            return true;
        }

        /// <summary>
        /// 檢查是否不包含任何指定標籤
        /// </summary>
        public bool HasNone(GameplayTagContainer other)
        {
            return !HasAny(other);
        }

        /// <summary>
        /// 獲取所有匹配指定父標籤的子標籤
        /// </summary>
        public List<GameplayTag> GetTagsMatchingParent(GameplayTag parentTag)
        {
            var result = new List<GameplayTag>();
            
            foreach (var tag in _tags)
            {
                if (tag.MatchesTagHierarchy(parentTag))
                {
                    result.Add(tag);
                }
            }
            
            return result;
        }

        #endregion

        #region Utility

        /// <summary>
        /// 複製此容器
        /// </summary>
        public GameplayTagContainer Clone()
        {
            return new GameplayTagContainer(_tags);
        }

        /// <summary>
        /// 獲取所有標籤的陣列
        /// </summary>
        public GameplayTag[] ToArray()
        {
            return _tags.ToArray();
        }

        public IEnumerator<GameplayTag> GetEnumerator()
        {
            return _tags.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            if (_tags.Count == 0) return "[]";
            return "[" + string.Join(", ", _tags) + "]";
        }

        #endregion

        #region Static Factory

        /// <summary>
        /// 創建空容器
        /// </summary>
        public static GameplayTagContainer Empty => new();

        /// <summary>
        /// 從標籤陣列創建容器
        /// </summary>
        public static GameplayTagContainer FromTags(params GameplayTag[] tags)
        {
            return new GameplayTagContainer(tags);
        }

        /// <summary>
        /// 從字串陣列創建容器
        /// </summary>
        public static GameplayTagContainer FromStrings(params string[] tagNames)
        {
            var container = new GameplayTagContainer();
            foreach (var name in tagNames)
            {
                container.AddTag(new GameplayTag(name));
            }
            return container;
        }

        #endregion
    }
}
