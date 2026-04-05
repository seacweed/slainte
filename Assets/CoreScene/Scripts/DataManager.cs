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
        string json = JsonUtility.ToJson(CurrentData, true); // true는 가독성 좋게 포맷팅
        File.WriteAllText(savePath, json);
        Debug.Log($"[DataManager] 자동 저장 완료: {savePath}");
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
            // 💡 새롭게 추가하는 부분: 첫 실행 시 파일이 없을 때 띄워줄 로그
            Debug.Log("[DataManager] 저장된 세이브 파일이 없습니다. 새 데이터를 사용합니다.");
        }
    }
}