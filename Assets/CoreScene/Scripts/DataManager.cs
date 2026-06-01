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
        Load();
    }

    [ContextMenu("Save Game")]
    public void Save()
    {
        GameProgress gp = GameProgress.Instance;
        if (gp != null)
        {
            CurrentData.dayCount            = gp.CurrentDay;
            CurrentData.flags               = gp.GetFlagList();
            CurrentData.completedEpisodeIds = gp.GetCompletedList();
            CurrentData.affinityKeys        = gp.GetAffinityKeys();
            CurrentData.affinityValues      = gp.GetAffinityValues();
            CurrentData.boardSlotKeys       = gp.GetBoardSlotKeys();
            CurrentData.boardSlotValues     = gp.GetBoardSlotValues();
        }

        string json = JsonUtility.ToJson(CurrentData, true);
        File.WriteAllText(savePath, json);
        Debug.Log($"[DataManager] Saved: {savePath}");
    }

    [ContextMenu("Delete Save File")]
    public void DeleteSaveFile()
    {
        string path = string.IsNullOrEmpty(savePath)
            ? Path.Combine(Application.persistentDataPath, "autosave.json")
            : savePath;

        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log("[DataManager] Save file deleted.");
        }
        else
        {
            Debug.Log("[DataManager] No save file to delete.");
        }

        CurrentData = new SaveData();
    }

    public void Load()
    {
        if (File.Exists(savePath))
        {
            string json = File.ReadAllText(savePath);
            CurrentData = JsonUtility.FromJson<SaveData>(json);
            Debug.Log("[DataManager] Save data loaded.");
        }
        else
        {
            Debug.Log("[DataManager] No save file found. Using new data.");
        }

        GameProgress.Instance?.LoadFrom(CurrentData);
    }
}
