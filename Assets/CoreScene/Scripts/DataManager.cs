using UnityEngine;
using System.IO;

public class DataManager : MonoSingleton<DataManager>
{
    public SaveData CurrentData { get; private set; } = new SaveData();
    private string savePath;

    protected override void Awake()
    {
        base.Awake();
        savePath = Path.Combine(Application.persistentDataPath, "autosave.json");
        Load(); // 게임 시작 시 기존 데이터가 있다면 로드
    }

    [ContextMenu("Save Game")] // 에디터에서 테스트 가능하도록 설정
    public void Save()
    {
        // 1. 세이브 직전에 게임 내(EpisodeManager)의 최신 진행도를 수거해옵니다.
        if (EpisodeManager.Instance != null && CurrentData != null)
        {
            CurrentData.episodeProgressList = EpisodeManager.Instance.SaveProgress();
        }

        string json = JsonUtility.ToJson(CurrentData, true); // true는 가독성 좋게 포맷팅
        File.WriteAllText(savePath, json);
        Debug.Log($"[DataManager] 자동 저장 완료: {savePath}");
    }

    [ContextMenu("🗑 Delete Save File (초기화)")]
    public void DeleteSaveFile()
    {
        // savePath는 Awake에서만 세팅되므로, 에디터 모드에서도 직접 계산해서 씁니다.
        string path = string.IsNullOrEmpty(savePath)
            ? Path.Combine(Application.persistentDataPath, "autosave.json")
            : savePath;

        Debug.Log($"[DataManager] 세이브 파일 경로: {path}");

        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log("[DataManager] 세이브 파일 삭제 완료! 다시 플레이하면 처음부터 시작됩니다.");
        }
        else
        {
            Debug.Log("[DataManager] 삭제할 세이브 파일이 없습니다. (이미 없거나 한 번도 저장된 적이 없습니다)");
        }

        // 메모리 내 데이터도 즉시 초기화
        CurrentData = new SaveData();
        if (EpisodeManager.Instance != null)
        {
            EpisodeManager.Instance.LoadProgress(null);
        }
    }

    public void Load()
    {
        if (File.Exists(savePath))
        {
            string json = File.ReadAllText(savePath);
            CurrentData = JsonUtility.FromJson<SaveData>(json);
            Debug.Log("[DataManager] 기존 세이브 데이터 로드 완료.");
        }
        else
        {
            Debug.Log("[DataManager] 저장된 세이브 파일이 없습니다. 새 데이터를 사용합니다.");
        }

        // 2. 디스크에서 불러온 진행도 리스트를 게임 내(EpisodeManager)로 넘겨줍니다.
        if (EpisodeManager.Instance != null && CurrentData != null && CurrentData.episodeProgressList != null)
        {
            EpisodeManager.Instance.LoadProgress(CurrentData.episodeProgressList);
        }
    }
}