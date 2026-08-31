using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Character Database", fileName = "CharacterDatabase")]
public class CharacterDatabase : ScriptableObject
{
    public List<CharacterData> characters = new();

    public CharacterData FindByKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        for (int i = 0; i < characters.Count; i++)
        {
            var c = characters[i];
            if (c != null && string.Equals(c.key, key, StringComparison.OrdinalIgnoreCase))
                return c;
        }

        return null;
    }
}