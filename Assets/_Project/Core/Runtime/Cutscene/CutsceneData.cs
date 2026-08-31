using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Cutscene Data", fileName = "CutsceneData_")]
public class CutsceneData : ScriptableObject
{
    [Serializable]
    public class CutsceneSlide
    {
        public Sprite image;
        [TextArea] public string text;
    }

    public string cutsceneId;
    public List<CutsceneSlide> slides = new();
}
