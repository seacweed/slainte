public static class ProgressConditionEvaluator
{
    public static bool IsMet(
        EpisodeTriggerCondition condition,
        GameProgress progress,
        int maxDay = 0)
    {
        if (progress == null)
            return false;

        if (maxDay > 0 && progress.CurrentDay > maxDay)
            return false;

        if (condition == null)
            return true;

        if (progress.CurrentDay < condition.minDay)
            return false;

        if (condition.requiredFlags != null)
        {
            for (int i = 0; i < condition.requiredFlags.Count; i++)
            {
                string flag = condition.requiredFlags[i];
                if (!string.IsNullOrWhiteSpace(flag) && !progress.HasFlag(flag))
                    return false;
            }
        }

        if (condition.blockedFlags != null)
        {
            for (int i = 0; i < condition.blockedFlags.Count; i++)
            {
                string flag = condition.blockedFlags[i];
                if (!string.IsNullOrWhiteSpace(flag) && progress.HasFlag(flag))
                    return false;
            }
        }

        if (condition.prerequisiteEpisodeIds != null)
        {
            for (int i = 0; i < condition.prerequisiteEpisodeIds.Count; i++)
            {
                string episodeId = condition.prerequisiteEpisodeIds[i];
                if (!string.IsNullOrWhiteSpace(episodeId)
                    && !progress.IsEpisodeCompleted(episodeId))
                    return false;
            }
        }

        if (condition.requiredVars != null)
        {
            for (int i = 0; i < condition.requiredVars.Count; i++)
            {
                VarCondition variable = condition.requiredVars[i];
                if (variable != null
                    && !variable.Evaluate(progress.GetAffinity(variable.varName)))
                    return false;
            }
        }

        return true;
    }
}
