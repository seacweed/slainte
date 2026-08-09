using System;
using System.Collections.Generic;

[Serializable]
public class CraftingOutcome
{
    public CraftingJobResult result;
    public string nextNodeId;
    public string flag;
    public List<VarChange> varChanges = new();
}
