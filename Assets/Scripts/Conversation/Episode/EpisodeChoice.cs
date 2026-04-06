using System;
using System.Collections.Generic;

[Serializable]
public class EpisodeChoice
{
    public string buttonText;
    public string nextNodeId;

    public List<string>    setFlags   = new();
    public List<string>    clearFlags = new();
    public List<VarChange> varChanges = new();
}