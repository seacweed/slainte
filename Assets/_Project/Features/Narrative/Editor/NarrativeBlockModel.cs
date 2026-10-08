using System;
using System.Collections.Generic;
using System.Linq;

namespace NarrativeFlow.Editor
{
    public enum BlockTerminalKind
    {
        Branches, // 마지막 이벤트가 대사(또는 이벤트 없음) — 포트 = 사용자가 정한 조건 라벨 목록
        Choice,   // 마지막 이벤트가 선택지 — 포트 = 선택지 버튼
        Crafting  // 마지막 이벤트가 제조 — 포트 = 제조 결과 6종(CraftingJobResultPorts.Order)
    }

    // 그래프 블록(EpisodeNodeSO) 하나의 해석 규칙을 한 곳에 모은 클래스. 컴파일러·임포터·노드 뷰·검증이
    // 모두 이 규칙을 공유해야 "에디터에서 보이는 것 = 컴파일 결과"가 보장된다.
    // - 실행 순서: Events 리스트 순서 그대로(카드의 줄 순서).
    // - 선택지/제조 이벤트는 블록을 끝내므로 그 뒤 이벤트는 실행되지 않는다.
    public static class NarrativeBlockModel
    {
        public const string NextLabel = "Next";

        public static bool IsRuntimeEvent(EpisodeEvent ev) =>
            ev != null
            && (ev.Type == EpisodeEventType.Dialogue
                || ev.Type == EpisodeEventType.Choice
                || ev.Type == EpisodeEventType.BusinessStart);

        public static bool EndsBlock(EpisodeEvent ev) =>
            ev.Type == EpisodeEventType.Choice
            || ev.Type == EpisodeEventType.BusinessStart;

        // 새로 만들 수 있는 이벤트 종류(나머지는 예전 데이터 호환용 레거시).
        public static readonly EpisodeEventType[] EditableTypes =
        {
            EpisodeEventType.Dialogue,
            EpisodeEventType.Choice,
            EpisodeEventType.BusinessStart
        };

        // 실행될 이벤트 순서. unreachable에는 블록을 끝내는 이벤트 뒤에 있어 실행되지 않는 이벤트가 담긴다.
        public static List<EpisodeEvent> GetExecutionOrder(
            EpisodeNodeSO block,
            List<EpisodeEvent> unreachable = null)
        {
            List<EpisodeEvent> ordered = new();
            List<EpisodeEvent> events = block?.Events;
            if (events == null)
                return ordered;

            bool ended = false;
            foreach (EpisodeEvent ev in events)
            {
                if (ended)
                {
                    unreachable?.Add(ev);
                    continue;
                }
                ordered.Add(ev);
                ended = EndsBlock(ev);
            }
            return ordered;
        }

        // 예전 시퀀스 에디터는 이벤트 순서를 연결선(StartEventGuid/NextEventGuids)으로 저장했다. 그 순서대로
        // Events 리스트를 한 번 재정렬하고 연결 정보를 비운다(연결선에서 빠진 이벤트는 뒤에 붙음). 바뀌었으면 true.
        public static bool MigrateLegacyOrder(EpisodeNodeSO block)
        {
            List<EpisodeEvent> events = block?.Events;
            if (events == null
                || (string.IsNullOrEmpty(block.StartEventGuid)
                    && !events.Any(e => e.NextEventGuids != null && e.NextEventGuids.Count > 0)))
                return false;

            List<EpisodeEvent> ordered = new();
            if (events.Any(e => e.NextEventGuids != null && e.NextEventGuids.Count > 0))
            {
                Dictionary<string, EpisodeEvent> byGuid = events
                    .Where(e => !string.IsNullOrEmpty(e.Guid))
                    .GroupBy(e => e.Guid)
                    .ToDictionary(g => g.Key, g => g.First());
                HashSet<string> targeted = new(events.Where(e => e.NextEventGuids != null).SelectMany(e => e.NextEventGuids));
                EpisodeEvent current = !string.IsNullOrEmpty(block.StartEventGuid) && byGuid.TryGetValue(block.StartEventGuid, out var start)
                    ? start
                    : events.FirstOrDefault(e => !targeted.Contains(e.Guid)) ?? events[0];
                HashSet<string> visited = new();
                while (current != null && visited.Add(current.Guid))
                {
                    ordered.Add(current);
                    if (current.NextEventGuids == null || current.NextEventGuids.Count == 0)
                        break;
                    byGuid.TryGetValue(current.NextEventGuids[0], out current);
                }
            }

            ordered.AddRange(events.Where(e => !ordered.Contains(e)));
            block.Events = ordered;
            block.StartEventGuid = string.Empty;
            foreach (EpisodeEvent ev in ordered)
                ev.NextEventGuids?.Clear();
            return true;
        }

        public static EpisodeEvent GetTerminalEvent(EpisodeNodeSO block)
        {
            List<EpisodeEvent> order = GetExecutionOrder(block);
            return order.Count > 0 ? order[order.Count - 1] : null;
        }

        public static BlockTerminalKind GetTerminalKind(EpisodeNodeSO block)
        {
            EpisodeEvent terminal = GetTerminalEvent(block);
            return terminal?.Type switch
            {
                EpisodeEventType.Choice => BlockTerminalKind.Choice,
                EpisodeEventType.BusinessStart => BlockTerminalKind.Crafting,
                _ => BlockTerminalKind.Branches
            };
        }

        // 블록 출력 포트 라벨(포트 인덱스 = 리스트 인덱스). 선택지/제조 블록은 이벤트 내용에서 자동으로 결정된다.
        public static List<string> GetPortLabels(EpisodeNodeSO block)
        {
            EpisodeEvent terminal = GetTerminalEvent(block);
            if (terminal?.Type == EpisodeEventType.Choice)
            {
                return terminal.Choices
                    .Select((c, i) => string.IsNullOrWhiteSpace(c.ButtonText) ? $"Choice {i}" : c.ButtonText)
                    .ToList();
            }

            if (terminal?.Type == EpisodeEventType.BusinessStart)
                return CraftingJobResultPorts.Order.Select(CraftingJobResultPorts.Label).ToList();

            return block?.OutgoingBranches ?? new List<string>();
        }

        // 선택지/제조 블록의 OutgoingBranches를 자동 결정된 포트 라벨과 맞춘다. 바뀌었으면 true.
        // 저장된 라벨을 실제 포트와 맞춰 두는 이유: 나중에 마지막 이벤트를 지워 대사로 끝나는 블록이 되어도
        // 포트 수와 기존 엣지의 포트 인덱스가 어긋나지 않게 하기 위해서다.
        public static bool SyncDerivedPorts(EpisodeNodeSO block)
        {
            if (block == null || GetTerminalKind(block) == BlockTerminalKind.Branches)
                return false;

            List<string> labels = GetPortLabels(block);
            if (block.OutgoingBranches != null && block.OutgoingBranches.SequenceEqual(labels))
                return false;

            block.OutgoingBranches = labels;
            return true;
        }
    }

    public enum BranchLabelKind
    {
        Next,
        RequiredFlag,
        BlockedFlag,
        Variable,
        Episode,
        Invalid
    }

    // 분기 포트 라벨 문법. 기존 CSV 값 형식을 그대로 쓴다(새 접두사 없음):
    //   Next / Default / 빈칸      → 조건 없는 기본 다음 노드
    //   flag == true              → 플래그가 켜져 있으면 (a&b == true: 모두 켜짐)   = RequiredFlag
    //   flag == false             → 플래그가 꺼져 있으면 (a&b == false: 모두 꺼짐)  = BlockedFlag
    //   var >= 5                  → 수치 변수 분기 (>= > == < <=)                 = RequiredVar
    //   그 외 공백 없는 순수 텍스트 → 그 에피소드 완료 시 분기                       = PrerequisiteEpisode
    // 포트 순서가 곧 런타임 판정 순서(NodeBranch 목록 순서)다.
    public readonly struct BranchLabel
    {
        private static readonly string[] Operators = { ">=", "<=", "==", ">", "<" };

        public BranchLabelKind Kind { get; }
        public IReadOnlyList<string> Flags { get; }
        public string Name { get; }
        public CompareOp Op { get; }
        public int Threshold { get; }
        public string Error { get; }

        private BranchLabel(
            BranchLabelKind kind,
            IReadOnlyList<string> flags = null,
            string name = null,
            CompareOp op = CompareOp.GreaterOrEqual,
            int threshold = 0,
            string error = null)
        {
            Kind = kind;
            Flags = flags ?? Array.Empty<string>();
            Name = name;
            Op = op;
            Threshold = threshold;
            Error = error;
        }

        public static BranchLabel Parse(string label)
        {
            string text = label?.Trim() ?? string.Empty;
            if (text.Length == 0
                || string.Equals(text, NarrativeBlockModel.NextLabel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase))
                return new BranchLabel(BranchLabelKind.Next);

            foreach (string op in Operators)
            {
                int index = text.IndexOf(op, StringComparison.Ordinal);
                if (index < 0)
                    continue;

                string left = text.Substring(0, index).Trim();
                string right = text.Substring(index + op.Length).Trim();
                if (left.Length == 0)
                    return Invalid($"조건 왼쪽이 비었습니다: {text}");

                bool isTrue = string.Equals(right, "true", StringComparison.OrdinalIgnoreCase);
                if (isTrue || string.Equals(right, "false", StringComparison.OrdinalIgnoreCase))
                {
                    if (op != "==")
                        return Invalid($"플래그 조건은 '== true' / '== false'만 쓸 수 있습니다: {text}");
                    return ParseFlags(left, text, isTrue);
                }

                if (!int.TryParse(right, out int threshold))
                    return Invalid($"비교값이 정수가 아닙니다: {text}");
                if (left.IndexOfAny(new[] { '&', '|', ' ' }) >= 0)
                    return Invalid($"변수 이름에 공백이나 &,|를 쓸 수 없습니다: {text}");

                return new BranchLabel(
                    BranchLabelKind.Variable,
                    name: left,
                    op: EpisodeCsvCodec.ParseCompareOp(op),
                    threshold: threshold);
            }

            if (text.IndexOfAny(new[] { ' ', '&', '|', '=' }) >= 0)
                return Invalid($"해석할 수 없는 분기 라벨입니다: {text}");

            return new BranchLabel(BranchLabelKind.Episode, name: text);
        }

        private static BranchLabel ParseFlags(string left, string text, bool required)
        {
            if (left.Contains('|'))
                return Invalid($"'하나라도 켜짐(|)' 분기는 지원하지 않습니다. 플래그마다 포트를 따로 만드세요: {text}");

            List<string> flags = left
                .Split('&')
                .Select(f => f.Trim())
                .Where(f => f.Length > 0)
                .ToList();
            if (flags.Count == 0 || flags.Any(f => f.Contains(' ')))
                return Invalid($"플래그 이름이 올바르지 않습니다: {text}");

            return new BranchLabel(required ? BranchLabelKind.RequiredFlag : BranchLabelKind.BlockedFlag, flags);
        }

        public bool IsFlag => Kind == BranchLabelKind.RequiredFlag || Kind == BranchLabelKind.BlockedFlag;

        // 조건 라벨 → 런타임 분기. Next·Invalid는 분기가 아니므로 null.
        public NodeBranch ToNodeBranch(string target)
        {
            NodeBranch branch = new() { nextNodeId = target };
            switch (Kind)
            {
                case BranchLabelKind.RequiredFlag:
                    branch.conditionType = NodeBranchConditionType.RequiredFlag;
                    branch.flags = Flags.ToList();
                    return branch;
                case BranchLabelKind.BlockedFlag:
                    branch.conditionType = NodeBranchConditionType.BlockedFlag;
                    branch.flags = Flags.ToList();
                    return branch;
                case BranchLabelKind.Variable:
                    branch.conditionType = NodeBranchConditionType.RequiredVar;
                    branch.varCondition = new VarCondition { varName = Name, op = Op, threshold = Threshold };
                    return branch;
                case BranchLabelKind.Episode:
                    branch.conditionType = NodeBranchConditionType.PrerequisiteEpisode;
                    branch.episodeId = Name;
                    return branch;
                default:
                    return null;
            }
        }

        private static BranchLabel Invalid(string error) => new(BranchLabelKind.Invalid, error: error);

        // 런타임 분기 → 포트 라벨(ToNodeBranch의 역변환).
        public static string Format(NodeBranch branch) => branch.conditionType switch
        {
            NodeBranchConditionType.RequiredFlag => $"{string.Join("&", branch.flags ?? new List<string>())} == true",
            NodeBranchConditionType.BlockedFlag => $"{string.Join("&", branch.flags ?? new List<string>())} == false",
            NodeBranchConditionType.RequiredVar =>
                $"{branch.varCondition.varName} {EpisodeCsvCodec.CompareOpToString(branch.varCondition.op)} {branch.varCondition.threshold}",
            NodeBranchConditionType.PrerequisiteEpisode => branch.episodeId,
            _ => NarrativeBlockModel.NextLabel
        };
    }
}
