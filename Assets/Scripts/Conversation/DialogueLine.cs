using System;
using UnityEngine;

[Serializable]
public class DialogueLine
{
    public string speakerName;   // 화면에 표시될 이름 (예: "유카리")
    [TextArea(2, 6)]
    public string text;          // 대사 본문 (줄바꿈 가능)
}
