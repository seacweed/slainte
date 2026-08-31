using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Recipe Book/Taste Mood Palette", fileName = "TasteMoodPalette")]
public class TasteMoodTagPaletteDef : ScriptableObject
{
    [Serializable]
    public class TagColorEntry
    {
        public string tag;
        public Color backgroundColor = Color.white;
        public Color textColor = Color.black;
    }

    public List<TagColorEntry> tasteEntries = new();
    public List<TagColorEntry> moodEntries = new();

    public bool TryGetTasteColor(string tag, out Color backgroundColor, out Color textColor)
    {
        return TryGetColor(tasteEntries, tag, out backgroundColor, out textColor);
    }

    public bool TryGetMoodColor(string tag, out Color backgroundColor, out Color textColor)
    {
        return TryGetColor(moodEntries, tag, out backgroundColor, out textColor);
    }

    private static bool TryGetColor(
        List<TagColorEntry> entries,
        string tag,
        out Color backgroundColor,
        out Color textColor)
    {
        if (entries != null && !string.IsNullOrWhiteSpace(tag))
        {
            for (int i = 0; i < entries.Count; i++)
            {
                TagColorEntry entry = entries[i];
                if (entry != null && string.Equals(entry.tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    backgroundColor = entry.backgroundColor;
                    textColor = entry.textColor;
                    return true;
                }
            }
        }

        backgroundColor = Color.white;
        textColor = Color.black;
        return false;
    }
}
