using UnityEngine;
using System.Collections.Generic;

public class BuffBarUI : MonoBehaviour
{
    public static BuffBarUI Instance { get; private set; }

    [Header("Layout")]
    public Transform content;
    public BuffIconView buffIconPrefab;

    // key = buffId
    private readonly Dictionary<int, BuffIconView> _active = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void ShowOrUpdate(BuffDefinition def, int level)
    {
        if (!def || !content || !buffIconPrefab) return;

        if (_active.TryGetValue(def.buffId, out var view))
        {
            view.Bind(def, level);
        }
        else
        {
            var v = Instantiate(buffIconPrefab, content);
            v.Bind(def, level);
            _active[def.buffId] = v;
            ResortByBuffId();
        }
    }

    public void RemoveById(int buffId)
    {
        if (_active.TryGetValue(buffId, out var view) && view)
        {
            _active.Remove(buffId);
            Destroy(view.gameObject);
        }
    }

    public void ResortByBuffId()
    {
        var list = new List<KeyValuePair<int, BuffIconView>>(_active);
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (int i = 0; i < list.Count; i++)
            if (list[i].Value) list[i].Value.transform.SetSiblingIndex(i);
    }
}
