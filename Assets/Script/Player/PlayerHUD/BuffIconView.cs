// BuffIconView.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuffIconView : MonoBehaviour
{
    public Image iconImage;
    public TMP_Text levelText;

    private BuffDefinition _def;
    private int _level;

    public void Bind(BuffDefinition def, int level)
    {
        _def = def;
        _level = Mathf.Clamp(level, 1, 3);

        var tier = _def.GetTier(_level);
        if (iconImage) iconImage.sprite = tier.icon;
        if (levelText) levelText.text = _level > 1 ? $"Lv.{_level}" : "";
    }
}