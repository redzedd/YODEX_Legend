using UnityEngine;
using System.Collections.Generic;

namespace GAS.UI
{
    /// <summary>
    /// Buff 圖標容器 UI — 管理 Buff 圖標的顯示與排序
    /// RemoveById 改為先播退場動畫再 Destroy
    /// </summary>
    public class BuffBarUI : MonoBehaviour
    {
        public static BuffBarUI Instance { get; private set; }

        [Header("Layout")]
        public Transform content;
        public BuffIconView buffIconPrefab;

        private readonly Dictionary<int, BuffIconView> _active = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void ShowOrUpdate(BuffDefinition def, int level)
        {
            if (!def || !content || !buffIconPrefab) return;
            if (_active.TryGetValue(def.buffId, out BuffIconView view))
            {
                view.Bind(def, level);
            }
            else
            {
                BuffIconView v = Instantiate(buffIconPrefab, content);
                v.Bind(def, level);
                _active[def.buffId] = v;
                ResortByBuffId();
            }
        }

        /// <summary>
        /// 移除 Buff 圖標 — 先播退場動畫再銷毀
        /// </summary>
        public void RemoveById(int buffId)
        {
            if (!_active.TryGetValue(buffId, out BuffIconView view) || view == null) return;
            _active.Remove(buffId);
            view.PlayRemoveAnimation(() =>
            {
                if (view != null) Destroy(view.gameObject);
            });
        }

        public void ResortByBuffId()
        {
            var list = new List<KeyValuePair<int, BuffIconView>>(_active);
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Value) list[i].Value.transform.SetSiblingIndex(i);
            }
        }
    }
}
