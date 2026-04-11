using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
    public string gameVersion = "1.0.0";
    public int dayCount = 1;
    public List<string> flags = new();
    public List<string> completedEpisodeIds = new();
    public List<string> varKeys = new();
    public List<int> varValues = new();
}
