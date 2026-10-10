using System;
using UnityEngine;

[Serializable]
public class DialogueLine
{
    [Tooltip("CharacterData key of the speaker. Optional: when empty, the speaker is matched by display name.")]
    public string speakerKey;
    public string speakerName;
    [TextArea(2, 6)]
    public string text;
    public Color nameColor = Color.white;
}
