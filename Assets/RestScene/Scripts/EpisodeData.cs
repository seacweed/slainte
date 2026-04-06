using UnityEngine;
using System.Collections.Generic;

public enum EpisodeCategory
{
    encounter_episode,
    episode
}

[System.Serializable]
public struct EpisodeCondition
{
    public string conditionText; 
}

[System.Serializable]
public struct EpisodeCharacter
{
    public string characterName;  // NPC 명 (선택/출력용)
    public Sprite portraitSprite; // 초상화 이미지 (아직 지정 안되면 null)
}

[CreateAssetMenu(fileName = "NewEpisode", menuName = "Episode/EpisodeData")]
public class EpisodeData : ScriptableObject
{
    [Header("System")]
    public string episodeID; // [핵심] 식별용 고유 ID (예: "EP_01_MissingDrink")
    public EpisodeCategory category;
    
    [Tooltip("이 에피소드를 해금하기 위해 먼저 클리어해야 하는 에피소드들의 ID")]
    public List<string> prerequisiteEpisodeIDs;

    [Header("Basic Info")]
    public string episodeName;
    public string episodeNameEng;
    [TextArea(3, 5)] 
    public string episodeDescription;

    [Header("Icons")]
    public string iconNameBoard;
    public string iconNameArchive;
    
    [Header("Details")]
    public List<EpisodeCondition> conditions; 
    public List<EpisodeCharacter> characters; 
}

[System.Serializable]
public class EpisodeProgressData
{
    public string episodeID;
    public bool isStarted; // 수락 여부
    public bool isCleared; // 완료 여부
    
    public bool[] conditionUnlocks; // 각 조건 달성 여부
    public bool[] characterMeets;   // 각 인물 조우 여부
}