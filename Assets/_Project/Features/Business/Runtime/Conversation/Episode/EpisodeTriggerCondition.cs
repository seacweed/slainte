using System;
using System.Collections.Generic;

[Serializable]
public class EpisodeTriggerCondition
{
    public int minDay = 0;
    public int minMoney = 0;

    public List<string>       requiredFlags          = new();
    public List<string>       blockedFlags           = new();
    public List<string>       prerequisiteEpisodeIds = new();
    public List<VarCondition> requiredVars           = new();
    public List<CustomerAppearanceCondition> requiredCustomerAppearances = new();
}