using System;

[Serializable]
public class NodeVarBranch
{
    public VarCondition condition = new();
    public string       nextNodeId;
}
