using System;
using System.Collections.Generic;

// 노드 조건 분기의 조건 종류. CSV #NODE_BRANCHES의 conditionType 값이며 #TRIGGER와 같은 이름을 쓴다.
public enum NodeBranchConditionType
{
    RequiredFlag,       // flags가 모두 켜져 있으면
    BlockedFlag,        // flags가 모두 꺼져 있으면
    RequiredVar,        // 변수 비교(varCondition)
    PrerequisiteEpisode // episodeId 에피소드를 완료했으면
}

// 노드의 조건 분기 하나. 노드의 branches 목록을 위에서부터 판정해 처음 만족하는 분기로 가고,
// 하나도 맞지 않으면 노드의 nextNodeId로 간다 — 목록 순서(= CSV 줄 순서 = 그래프 포트 순서)가 우선순위다.
[Serializable]
public class NodeBranch
{
    public NodeBranchConditionType conditionType;
    public List<string> flags = new();
    public VarCondition varCondition = new();
    public string episodeId;
    public string nextNodeId;

    public bool IsSatisfied(GameProgress progress)
    {
        if (progress == null)
            return false;

        switch (conditionType)
        {
            case NodeBranchConditionType.RequiredFlag:
            case NodeBranchConditionType.BlockedFlag:
                // 플래그가 하나도 없는 분기는 항상 참이 되면 뒤 분기를 모두 가리므로 거짓으로 본다.
                if (flags == null || flags.Count == 0)
                    return false;
                bool wantSet = conditionType == NodeBranchConditionType.RequiredFlag;
                for (int i = 0; i < flags.Count; i++)
                    if (progress.HasFlag(flags[i]) != wantSet)
                        return false;
                return true;
            case NodeBranchConditionType.RequiredVar:
                return varCondition != null
                    && !string.IsNullOrWhiteSpace(varCondition.varName)
                    && varCondition.Evaluate(progress.GetAffinity(varCondition.varName));
            case NodeBranchConditionType.PrerequisiteEpisode:
                return !string.IsNullOrWhiteSpace(episodeId) && progress.IsEpisodeCompleted(episodeId);
            default:
                return false;
        }
    }
}
