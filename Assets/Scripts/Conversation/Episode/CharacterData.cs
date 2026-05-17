using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Character Data", fileName = "CharacterData_")]
public class CharacterData : ScriptableObject
{
    [Serializable]
    public class ExpressionEntry
    {
        public string key;
        public Sprite sprite;
        public Sprite overlaySprite;
    }

    public string key;
    public string displayName;
    public Color nameColor = Color.white;
    public Sprite defaultSprite;
    public Sprite defaultOverlaySprite;

    [Header("Expressions")]
    public List<ExpressionEntry> expressions = new();

    public Sprite GetSprite(string expressionKey)
    {
        if (!string.IsNullOrWhiteSpace(expressionKey))
        {
            for (int i = 0; i < expressions.Count; i++)
            {
                var e = expressions[i];
                if (e != null && string.Equals(e.key, expressionKey, StringComparison.OrdinalIgnoreCase))
                    return e.sprite != null ? e.sprite : defaultSprite;
            }
        }

        return defaultSprite;
    }

    public Sprite GetOverlaySprite(string expressionKey)
    {
        if (!string.IsNullOrWhiteSpace(expressionKey))
        {
            for (int i = 0; i < expressions.Count; i++)
            {
                var e = expressions[i];
                if (e != null && string.Equals(e.key, expressionKey, StringComparison.OrdinalIgnoreCase))
                    return e.overlaySprite != null ? e.overlaySprite : defaultOverlaySprite;
            }
        }

        return defaultOverlaySprite;
    }

}
