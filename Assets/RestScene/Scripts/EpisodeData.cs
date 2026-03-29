using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct EpisodeCondition
{
    public string conditionText; 
    public bool isUnlocked;      
}

[System.Serializable]
public struct EpisodeCharacter
{
    public Sprite portraitSprite; 
    public bool hasMet;          
}

[CreateAssetMenu(fileName = "NewEpisode", menuName = "Episode/EpisodeData")]
public class EpisodeData : ScriptableObject
{
    [Header("Basic Info")]
    public string episodeName;
    [TextArea(3, 5)] 
    public string episodeDescription;
    
    [Header("Details")]
    public List<EpisodeCondition> conditions; 
    public List<EpisodeCharacter> characters; 
}