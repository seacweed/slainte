using System;
using System.Collections.Generic;

[Serializable]
public class NodeFlagBranch
{
    public List<string> requiredAllFlags = new(); // & — ALL must be set
    public List<string> requiredAnyFlags = new(); // | — ANY one must be set
    public string nextNodeId;
}
