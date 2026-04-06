using System.Collections.Generic;
using UnityEngine;

public class EpisodeManager : MonoBehaviour
{
    private static EpisodeManager _instance;
    public static EpisodeManager Instance 
    { 
        get 
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<EpisodeManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("EpisodeManager (Auto-Generated)");
                    _instance = go.AddComponent<EpisodeManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    private Dictionary<string, EpisodeProgressData> progressDict = new Dictionary<string, EpisodeProgressData>();

    private void Awake()
    {
        if (_instance == null) 
        { 
            _instance = this; 
            DontDestroyOnLoad(gameObject); 
        }
        else if (_instance != this)
        { 
            Destroy(gameObject); 
        }
    }

    // 데이터 조회 (없으면 초기화해서 반환)
    public EpisodeProgressData GetProgress(EpisodeData data)
    {
        if (!progressDict.ContainsKey(data.episodeID))
        {
            progressDict[data.episodeID] = new EpisodeProgressData
            {
                episodeID = data.episodeID,
                isStarted = false,
                isCleared = false,
                conditionUnlocks = new bool[data.conditions.Count],
                characterMeets = new bool[data.characters.Count]
            };
        }
        return progressDict[data.episodeID];
    }

    // 선행 조건 및 현재 진행 상태를 고려하여 상황판에 띄울지 결정합니다.
    public bool IsAvailableToStart(EpisodeData data)
    {
        // 1. 이미 시작했거나 완료된 에피소드인지 검사 (중복 방지 및 안 보이게 처리)
        EpisodeProgressData currentProgress = GetProgress(data);
        if (currentProgress.isStarted || currentProgress.isCleared)
        {
            return false;
        }

        // 2. 선행 에피소드들이 모두 클리어 상태인지 검사
        if (data.prerequisiteEpisodeIDs != null)
        {
            foreach (string reqId in data.prerequisiteEpisodeIDs)
            {
                if (progressDict.TryGetValue(reqId, out EpisodeProgressData reqProgress))
                {
                    if (!reqProgress.isCleared) return false;
                }
                else
                {
                    // 진행도(dict)에 전혀 기록되지 않은 에피소드면 아직 깬 적이 없다는 뜻
                    return false;
                }
            }
        }

        return true;
    }

    // 시작 버튼을 눌렀을 때 호출됨
    public void StartEpisode(string episodeID)
    {
        if (progressDict.TryGetValue(episodeID, out var progress))
        {
            progress.isStarted = true;
            Debug.Log($"[EpisodeManager] 에피소드 시작됨: {episodeID}");
        }
    }
}
