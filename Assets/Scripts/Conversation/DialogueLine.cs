using System;
using UnityEngine;

[Serializable]
public class DialogueLine
{
    public string speakerName;
    [TextArea(2, 6)]
    public string text;
    public Color nameColor = Color.white;
}
