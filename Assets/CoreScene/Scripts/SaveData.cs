using System;
using System.Collections.Generic;

[Serializable] // JSON 변환을 위해 필수
public class SaveData
{
    public string gameVersion = "1.0.0";
    public string currentEpisodeID = "Ep_Start";
    public int dayCount = 1;
    
    // 호감도 및 플래그 저장용 Dictionary (Yarn Spinner 변수와 연동됨)
    // Note: Dictionary는 기본적으로 JsonUtility로 직렬화되지 않으므로 추가 처리가 필요할 수 있습니다.
    public Dictionary<string, float> variables = new Dictionary<string, float>();
    public List<string> unlockedStoryFlags = new List<string>();

    public List<EpisodeProgressData> episodeProgressList = new List<EpisodeProgressData>();
}