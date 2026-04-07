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
                    Debug.LogWarning("[EpisodeManager] 씬에 EpisodeManager가 존재하지 않습니다! (CoreScene 로드 전일 수 있습니다)");
                }
                else if (!_instance.isInitialized)
                {
                    // Awake보다 먼저 호출당했을 경우 즉시 초기화
                    _instance.LoadAllEpisodes();
                    _instance.isInitialized = true;
                }
            }
            return _instance;
        }
    }

    private Dictionary<string, EpisodeProgressData> progressDict = new Dictionary<string, EpisodeProgressData>();
    private List<EpisodeData> allEpisodes = new List<EpisodeData>();

    private bool isInitialized = false;

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
            return;
        }

        if (!isInitialized)
        {
            LoadAllEpisodes();
            isInitialized = true;
        }
    }

    private void LoadAllEpisodes()
    {
        allEpisodes.Clear();
        allEpisodes.AddRange(Resources.LoadAll<EpisodeData>("EpisodeData"));
        Debug.Log($"[EpisodeManager] 총 {allEpisodes.Count}개의 에피소드 데이터 로드 완료.");
    }

    // 선행조건을 통과하여 상황판에 현재 노출 가능한 모든 에피소드를 반환합니다.
    public List<EpisodeData> GetAvailableEpisodes()
    {
        List<EpisodeData> available = new List<EpisodeData>();
        foreach(var ep in allEpisodes)
        {
            if (IsAvailableToStart(ep))
            {
                available.Add(ep);
            }
        }
        return available;
    }

    public void LoadProgress(List<EpisodeProgressData> list)
    {
        progressDict.Clear();
        if (list != null)
        {
            foreach (var p in list)
            {
                progressDict[p.episodeID] = p;
            }
        }
    }

    public List<EpisodeProgressData> SaveProgress()
    {
        return new List<EpisodeProgressData>(progressDict.Values);
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
        // 1. 이미 완료된 에피소드인지 검사 (수락 후 도중에 껐다면 다시 할 수 있도록 isCleared만 체크)
        EpisodeProgressData currentProgress = GetProgress(data);
        if (currentProgress.isCleared)
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

    public string CurrentPlayingEpisodeID { get; private set; }

    // 시작 버튼을 눌렀을 때 호출됨
    public void StartEpisode(string episodeID)
    {
        if (progressDict.TryGetValue(episodeID, out var progress))
        {
            progress.isStarted = true;
            CurrentPlayingEpisodeID = episodeID;
            Debug.Log($"[EpisodeManager] 에피소드 시작됨: {episodeID}");
            
            // 상태가 변경되었으니 디스크에 즉시 자동 저장
            if (DataManager.Instance != null) DataManager.Instance.Save();
            
            // 실제 게임 씬 및 상태 변경
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ChangeState(GameState.Episode);
            }
        }
    }

    // 에피소드가 성공적으로 끝났을 때 호출됨
    public void ClearEpisode(string episodeID)
    {
        if (progressDict.TryGetValue(episodeID, out var progress))
        {
            progress.isCleared = true;
            Debug.Log($"[EpisodeManager] 에피소드 클리어: {episodeID}");
            
            // 깼으니 디스크에 즉시 자동 저장
            if (DataManager.Instance != null) DataManager.Instance.Save();
        }
    }
}
