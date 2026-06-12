using System;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 遊戲標籤 - 用於標識能力、效果、狀態等
    /// 使用階層式命名 (例如: "Ability.Attack.Melee", "State.Attacking")
    /// </summary>
    [Serializable]
    public struct GameplayTag : IEquatable<GameplayTag>
    {
        [SerializeField]
        private string _tagName;

        /// <summary>
        /// 標籤的完整名稱 (例如: "Ability.Attack.Melee")
        /// </summary>
        public string TagName => _tagName ?? string.Empty;

        /// <summary>
        /// 標籤是否有效 (非空)
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(_tagName);

        public GameplayTag(string tagName)
        {
            _tagName = tagName ?? string.Empty;
        }

        /// <summary>
        /// 檢查此標籤是否匹配另一個標籤 (完全匹配)
        /// </summary>
        public bool MatchesTag(GameplayTag other)
        {
            if (!IsValid || !other.IsValid) return false;
            return string.Equals(_tagName, other._tagName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 檢查此標籤是否匹配另一個標籤或其父標籤
        /// 例如: "Ability.Attack.Melee" 匹配 "Ability.Attack" 和 "Ability"
        /// </summary>
        public bool MatchesTagExact(GameplayTag other)
        {
            return MatchesTag(other);
        }

        /// <summary>
        /// 檢查此標籤是否是另一個標籤的子標籤
        /// 例如: "Ability.Attack.Melee".IsChildOf("Ability.Attack") = true
        /// </summary>
        public bool IsChildOf(GameplayTag parent)
        {
            if (!IsValid || !parent.IsValid) return false;
            return _tagName.StartsWith(parent._tagName + ".", StringComparison.Ordinal);
        }

        /// <summary>
        /// 檢查此標籤是否匹配另一個標籤 (包含階層匹配)
        /// "Ability.Attack.Melee" 會匹配 "Ability", "Ability.Attack", "Ability.Attack.Melee"
        /// </summary>
        public bool MatchesTagHierarchy(GameplayTag other)
        {
            if (!IsValid || !other.IsValid) return false;
            
            // 完全匹配
            if (string.Equals(_tagName, other._tagName, StringComparison.Ordinal))
                return true;
            
            // 檢查是否為子標籤
            return _tagName.StartsWith(other._tagName + ".", StringComparison.Ordinal);
        }

        /// <summary>
        /// 獲取父標籤
        /// 例如: "Ability.Attack.Melee" 的父標籤是 "Ability.Attack"
        /// </summary>
        public GameplayTag GetParentTag()
        {
            if (!IsValid) return default;
            
            int lastDot = _tagName.LastIndexOf('.');
            if (lastDot <= 0) return default;
            
            return new GameplayTag(_tagName.Substring(0, lastDot));
        }

        /// <summary>
        /// 獲取標籤的階層深度
        /// 例如: "Ability.Attack.Melee" 深度為 3
        /// </summary>
        public int GetDepth()
        {
            if (!IsValid) return 0;
            
            int depth = 1;
            foreach (char c in _tagName)
            {
                if (c == '.') depth++;
            }
            return depth;
        }

        #region Equality & Operators

        public bool Equals(GameplayTag other)
        {
            return string.Equals(_tagName, other._tagName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is GameplayTag other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _tagName?.GetHashCode() ?? 0;
        }

        public static bool operator ==(GameplayTag left, GameplayTag right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GameplayTag left, GameplayTag right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return _tagName ?? "None";
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// 創建一個空的無效標籤
        /// </summary>
        public static GameplayTag None => new(string.Empty);

        /// <summary>
        /// 從字串創建標籤
        /// </summary>
        public static GameplayTag FromString(string tagName)
        {
            return new GameplayTag(tagName);
        }

        #endregion
    }
}
