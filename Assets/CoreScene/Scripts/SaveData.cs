using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
    public string gameVersion = "1.0.0";
    public int dayCount = 1;
    public List<string> flags = new();
    public List<string> completedEpisodeIds = new();
    public List<string> affinityKeys   = new();
    public List<int>    affinityValues = new();
    public List<string> boardSlotKeys   = new();
    public List<int>    boardSlotValues = new();
    public List<string> bottleAmountKeys   = new();
    public List<float>  bottleAmountValues = new();
    public int money;
    public int reputation;
    public BusinessDaySnapshot businessDay = new();
}

[Serializable]
public sealed class BusinessSequenceEntrySnapshot
{
    public string entryType;
    public string entryId;
    public string contentId;

    public BusinessSequenceEntrySnapshot Clone()
    {
        return new BusinessSequenceEntrySnapshot
        {
            entryType = entryType,
            entryId = entryId,
            contentId = contentId
        };
    }
}

[Serializable]
public sealed class BusinessDaySnapshot
{
    public int day;
    public int seed;
    public int currentIndex;
    public bool isCompleted;
    public List<BusinessSequenceEntrySnapshot> entries = new();

    public bool HasEntries => entries != null && entries.Count > 0;

    public BusinessDaySnapshot Clone()
    {
        BusinessDaySnapshot clone = new BusinessDaySnapshot
        {
            day = day,
            seed = seed,
            currentIndex = currentIndex,
            isCompleted = isCompleted,
            entries = new List<BusinessSequenceEntrySnapshot>()
        };

        if (entries == null)
            return clone;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null)
                clone.entries.Add(entries[i].Clone());
        }

        return clone;
    }
}
