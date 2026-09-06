// 에피소드 시작 조건, 손님 등장 조건, 영업 필수 액션 조건 등 GameProgress 기반 등장/발동 조건을
// 판정하는 단일 공용 로직(EpisodeManager.CanStart, BusinessSequencePlanner 등에서 재사용).
// 조건은 minDay → requiredFlags → blockedFlags → prerequisiteEpisodeIds → requiredVars →
// requiredCustomerAppearances 순서로 검사하며 모두 AND로 결합, 하나라도 어긋나면 즉시 false.
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

        // condition 자체가 없으면(설정 안 함) 무조건 통과 — "항상 등장 가능" 대상에 쓰인다.
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

        if (condition.requiredCustomerAppearances != null)
        {
            for (int i = 0; i < condition.requiredCustomerAppearances.Count; i++)
            {
                CustomerAppearanceCondition appearance = condition.requiredCustomerAppearances[i];
                if (appearance != null
                    && progress.GetCustomerAppearance(appearance.characterId) < appearance.count)
                    return false;
            }
        }

        return true;
    }
}
