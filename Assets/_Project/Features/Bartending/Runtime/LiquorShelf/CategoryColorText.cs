using System.Collections.Generic;
using UnityEngine;

// Wraps every occurrence of a registered LiquorCategoryDef.displayName inside free-form
// text with a TMP <color> tag using that category's unique color. Only text explicitly
// passed through Highlight() is affected — this is not a global text hook.
public static class CategoryColorText
{
    private static readonly List<LiquorCategoryDef> _categories = new();

    public static void Register(IEnumerable<LiquorCategoryDef> categories)
    {
        _categories.Clear();
        foreach (var category in categories)
        {
            if (category != null && !string.IsNullOrEmpty(category.displayName))
                _categories.Add(category);
        }
    }

    public static string Highlight(string text)
    {
        if (string.IsNullOrEmpty(text) || _categories.Count == 0) return text;

        foreach (var category in _categories)
        {
            string colorHex = ColorUtility.ToHtmlStringRGB(category.color);
            text = text.Replace(category.displayName, $"<color=#{colorHex}>{category.displayName}</color>");
        }

        return text;
    }
}
