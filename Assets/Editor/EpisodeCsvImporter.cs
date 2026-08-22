using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class EpisodeCsvImporter : EditorWindow
{
    private string _csvPath = "";
    private string _outputFolder = "Assets/Resources/EpisodeData";

    [MenuItem("Tools/Slainte/Import Episode CSV")]
    public static void Open()
    {
        GetWindow<EpisodeCsvImporter>("Episode CSV Importer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Episode CSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        _csvPath = EditorGUILayout.TextField("CSV Path", _csvPath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFilePanel("Select Episode CSV", Application.dataPath, "csv");
            if (!string.IsNullOrEmpty(path))
                _csvPath = path;
        }
        EditorGUILayout.EndHorizontal();

        _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);

        EditorGUILayout.Space();

        GUI.enabled = !string.IsNullOrEmpty(_csvPath);
        if (GUILayout.Button("Import"))
            TryImport();
        GUI.enabled = true;
    }

    private void TryImport()
    {
        if (!File.Exists(_csvPath))
        {
            EditorUtility.DisplayDialog("Error", "CSV 파일을 찾을 수 없습니다.", "OK");
            return;
        }

        string[] lines = File.ReadAllLines(_csvPath, Encoding.UTF8);
        EpisodeData data = ParseCsv(lines, out Dictionary<string, List<string[]>> sections);
        if (data == null) return;

        if (!AssetDatabase.IsValidFolder(_outputFolder))
        {
            Directory.CreateDirectory(_outputFolder);
            AssetDatabase.Refresh();
        }

        string assetPath = $"{_outputFolder}/EpisodeData_{data.episodeId}.asset";
        EpisodeData existing = AssetDatabase.LoadAssetAtPath<EpisodeData>(assetPath);

        if (existing != null)
        {
            // 이 CSV에 없는 섹션은 인스펙터에서 수동으로 입력한 기존 값을 유지합니다.
            // 섹션이 있으면(빈 섹션 포함) CSV가 해당 필드의 source of truth가 됩니다.
            if (!sections.ContainsKey("BOARD"))
            {
                data.episodeDescription    = existing.episodeDescription;
                data.iconNameBoard         = existing.iconNameBoard;
                data.iconNameArchive       = existing.iconNameArchive;
            }

            if (!sections.ContainsKey("BOARD_CHARS"))
                data.characters = existing.characters;

            if (!sections.ContainsKey("SETTLEMENT_REWARDS"))
                data.settlementRewards = existing.settlementRewards;

            if (!sections.ContainsKey("SELECT_CHARS"))
                RestoreCharacterOverrides(data, existing);

            EditorUtility.CopySerialized(data, existing);
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(data, assetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Success", $"임포트 완료:\n{assetPath}", "OK");
        Debug.Log($"[EpisodeCsvImporter] Imported: {assetPath}");
    }

    // selectConditions는 CSV에서 매번 새로 만들어지므로, 인스펙터에서 직접 채워둔
    // characterOverrides(초상화 변형)는 flag 이름으로 기존 자산에서 찾아 복원한다.
    private static void RestoreCharacterOverrides(EpisodeData data, EpisodeData existing)
    {
        if (data.selectConditions == null || existing.selectConditions == null) return;

        foreach (var entry in data.selectConditions)
        {
            if (string.IsNullOrEmpty(entry.flag)) continue;

            var match = existing.selectConditions.Find(e => e.flag == entry.flag);
            if (match != null) entry.characterOverrides = match.characterOverrides;
        }
    }

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    private static EpisodeData ParseCsv(string[] lines, out Dictionary<string, List<string[]>> sections)
    {
        sections = SplitIntoSections(lines);

        EpisodeData data = ScriptableObject.CreateInstance<EpisodeData>();

        if (!ParseMeta(sections, data)) return null;
        ParseTrigger(sections, data);
        ParsePlayCondition(sections, data);
        ParseSelectCondition(sections, data);
        ParseOpeningChars(sections, data);
        ParseBoard(sections, data);
        ParseBoardChars(sections, data);
        ParseSettlementRewards(sections, data);

        Dictionary<string, List<CharacterSlotEntry>>  nodeChars           = BuildNodeCharsLookup(sections);
        Dictionary<string, List<EpisodeChoice>>       nodeChoices         = BuildNodeChoicesLookup(sections);
        Dictionary<string, List<NodeFlagBranch>>      nodeBranches        = BuildNodeBranchesLookup(sections);
        Dictionary<string, List<NodeVarBranch>>       nodeVarBranches     = BuildNodeVarBranchesLookup(sections);
        Dictionary<string, List<NodeEpisodeBranch>>   nodeEpisodeBranches = BuildNodeEpisodeBranchesLookup(sections);
        Dictionary<string, List<CraftingOutcome>>     nodeCraftingBranches = BuildNodeCraftingBranchesLookup(sections);
        ParseNodes(sections, data, nodeChars, nodeChoices, nodeBranches, nodeVarBranches, nodeEpisodeBranches, nodeCraftingBranches);

        return data;
    }

    private static Dictionary<string, List<string[]>> SplitIntoSections(string[] lines)
    {
        var sections = new Dictionary<string, List<string[]>>();
        string current = null;
        bool headerRead = false;

        foreach (string raw in lines)
        {
            // Strip UTF-8 BOM character that may appear at the start of the file
            string line = raw.Trim().TrimStart('\uFEFF');
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("#"))
            {
                string raw2 = line.Substring(1).Trim();
                current = raw2.Split(',')[0].Trim();
                headerRead = false;
                if (!sections.ContainsKey(current))
                    sections[current] = new List<string[]>();
                continue;
            }

            if (current == null) continue;

            if (!headerRead)
            {
                headerRead = true; // skip header row
                continue;
            }

            sections[current].Add(ParseLine(line));
        }

        return sections;
    }

    private static bool ParseMeta(Dictionary<string, List<string[]>> sections, EpisodeData data)
    {
        if (!sections.TryGetValue("META", out var rows) || rows.Count == 0)
        {
            string found = sections.Count > 0
                ? string.Join(", ", sections.Keys)
                : "(없음)";
            Debug.LogError($"[EpisodeCsvImporter] #META 섹션이 없거나 비어있습니다. 인식된 섹션: {found}");
            return false;
        }

        string[] row = rows[0];
        data.episodeId     = Field(row, 0);
        data.episodeTitle  = Field(row, 1);
        data.firstNodeId   = Field(row, 2);
        data.episodeType   = ParseEpisodeType(Field(row, 3));
        data.mandatorySlot = ParseMandatorySlot(Field(row, 4));
        data.chapterId     = Field(row, 5);
        return true;
    }

    // TRIGGER/PLAY_TRIGGER 공통: 행 하나 = 조건 하나(conditionType,conditionValue,text). 여러 행은 AND로 결합된다.
    private static void ParseConditionEntries(
        Dictionary<string, List<string[]>> sections,
        string sectionName,
        out EpisodeTriggerCondition condition,
        out List<TriggerConditionEntry> entries)
    {
        condition = new EpisodeTriggerCondition();
        entries = new List<TriggerConditionEntry>();

        if (!sections.TryGetValue(sectionName, out var rows)) return;

        foreach (string[] row in rows)
        {
            SelectSingleCondition cond = ParseSingleCondition(Field(row, 0), Field(row, 1));
            if (cond.type == SelectConditionType.None) continue;

            ApplyToCondition(condition, cond);
            entries.Add(new TriggerConditionEntry
            {
                condition = cond,
                conditionText = Field(row, 2)
            });
        }
    }

    // 조건 하나를 평가용 EpisodeTriggerCondition(AND 결합 리스트)에 누적한다.
    private static void ApplyToCondition(EpisodeTriggerCondition target, SelectSingleCondition cond)
    {
        switch (cond.type)
        {
            case SelectConditionType.MinDay:
                target.minDay = cond.minDay;
                break;
            case SelectConditionType.MinMoney:
                target.minMoney = cond.minMoney;
                break;
            case SelectConditionType.RequiredFlag:
                if (!string.IsNullOrEmpty(cond.requiredFlag)) target.requiredFlags.Add(cond.requiredFlag);
                break;
            case SelectConditionType.PrerequisiteEpisode:
                if (!string.IsNullOrEmpty(cond.prerequisiteEpisodeId)) target.prerequisiteEpisodeIds.Add(cond.prerequisiteEpisodeId);
                break;
            case SelectConditionType.RequiredVar:
                if (!string.IsNullOrEmpty(cond.varName))
                    target.requiredVars.Add(new VarCondition { varName = cond.varName, op = cond.varOp, threshold = cond.varThreshold });
                break;
        }
    }

    private static void ParseTrigger(Dictionary<string, List<string[]>> sections, EpisodeData data)
    {
        ParseConditionEntries(sections, "TRIGGER", out data.triggerCondition, out data.triggerConditionEntries);
    }

    private static void ParsePlayCondition(Dictionary<string, List<string[]>> sections, EpisodeData data)
    {
        ParseConditionEntries(sections, "PLAY_TRIGGER", out data.playCondition, out data.playConditionEntries);
    }

    private static void ParseSelectCondition(Dictionary<string, List<string[]>> sections, EpisodeData data)
    {
        data.selectConditions = new List<SelectConditionEntry>();

        if (!sections.TryGetValue("SELECT_TRIGGER", out var rows)) return;

        bool hasSelectChars = sections.ContainsKey("SELECT_CHARS");
        Dictionary<string, List<CharacterDisplay>> selectChars = hasSelectChars
            ? BuildSelectCharsLookup(sections)
            : null;

        foreach (string[] row in rows)
        {
            var entry = new SelectConditionEntry
            {
                condition       = ParseSingleCondition(Field(row, 0), Field(row, 1)),
                flag            = Field(row, 2),
                conditionText   = Field(row, 3),
                revealCondition = ParseSingleCondition(Field(row, 4), Field(row, 5)),
                hiddenText      = Field(row, 6)
            };

            if (hasSelectChars)
            {
                entry.characterOverrides = selectChars.TryGetValue(entry.flag, out var overrides)
                    ? overrides : new List<CharacterDisplay>();
            }

            data.selectConditions.Add(entry);
        }
    }

    private static Dictionary<string, List<CharacterDisplay>> BuildSelectCharsLookup(
        Dictionary<string, List<string[]>> sections)
    {
        var lookup = new Dictionary<string, List<CharacterDisplay>>();

        if (!sections.TryGetValue("SELECT_CHARS", out var rows)) return lookup;

        foreach (string[] row in rows)
        {
            string flag = Field(row, 0);
            if (!lookup.ContainsKey(flag))
                lookup[flag] = new List<CharacterDisplay>();
            lookup[flag].Add(ToCharacterDisplay(row, 1));
        }

        return lookup;
    }

    private static void ParseBoard(Dictionary<string, List<string[]>> sections, EpisodeData data)
    {
        if (!sections.TryGetValue("BOARD", out var rows) || rows.Count == 0) return;

        string[] row = rows[0];
        data.episodeDescription    = Field(row, 0);
        data.iconNameBoard         = Field(row, 1);
        data.iconNameArchive       = Field(row, 2);
    }

    private static void ParseBoardChars(Dictionary<string, List<string[]>> sections, EpisodeData data)
    {
        if (!sections.TryGetValue("BOARD_CHARS", out var rows)) return;

        data.characters = new List<CharacterDisplay>();
        foreach (string[] row in rows)
            data.characters.Add(ToCharacterDisplay(row, 0));
    }

    private static void ParseSettlementRewards(Dictionary<string, List<string[]>> sections, EpisodeData data)
    {
        if (!sections.TryGetValue("SETTLEMENT_REWARDS", out var rows)) return;

        data.settlementRewards = new List<EpisodeSettlementReward>();
        foreach (string[] row in rows)
        {
            data.settlementRewards.Add(new EpisodeSettlementReward
            {
                requiredFlag = Field(row, 0),
                label        = Field(row, 1),
                amount       = int.TryParse(Field(row, 2), out int amount) ? amount : 0
            });
        }
    }

    private static CharacterDisplay ToCharacterDisplay(string[] row, int offset)
    {
        string hiddenStr = Field(row, offset);
        return new CharacterDisplay
        {
            isHidden      = string.Equals(hiddenStr, "true", StringComparison.OrdinalIgnoreCase) || hiddenStr == "1",
            characterName = Field(row, offset + 1)
        };
    }

    // conditionType,conditionValue 두 열로 조건 하나(옵션당 하나)를 만든다.
    private static SelectSingleCondition ParseSingleCondition(string typeStr, string value)
    {
        var cond = new SelectSingleCondition();
        if (!System.Enum.TryParse(typeStr.Trim(), true, out SelectConditionType type)) return cond;
        cond.type = type;

        switch (type)
        {
            case SelectConditionType.MinDay:
                cond.minDay = int.TryParse(value, out int d) ? d : 0;
                break;
            case SelectConditionType.RequiredFlag:
                cond.requiredFlag = value.Trim();
                break;
            case SelectConditionType.PrerequisiteEpisode:
                cond.prerequisiteEpisodeId = value.Trim();
                break;
            case SelectConditionType.RequiredVar:
                VarCondition vc = TryParseVarCondition(value.Trim());
                if (vc != null)
                {
                    cond.varName = vc.varName;
                    cond.varOp = vc.op;
                    cond.varThreshold = vc.threshold;
                }
                break;
            case SelectConditionType.MinMoney:
                cond.minMoney = int.TryParse(value, out int m) ? m : 0;
                break;
        }

        return cond;
    }

    private static void ParseOpeningChars(Dictionary<string, List<string[]>> sections, EpisodeData data)
    {
        data.openingCharacters = new List<CharacterSlotEntry>();

        if (!sections.TryGetValue("OPENING_CHARS", out var rows)) return;

        foreach (string[] row in rows)
            data.openingCharacters.Add(ToSlotEntry(row, 0));
    }

    private static Dictionary<string, List<CharacterSlotEntry>> BuildNodeCharsLookup(
        Dictionary<string, List<string[]>> sections)
    {
        var lookup = new Dictionary<string, List<CharacterSlotEntry>>();

        if (!sections.TryGetValue("NODE_CHARS", out var rows)) return lookup;

        foreach (string[] row in rows)
        {
            string nid = Field(row, 0);
            if (!lookup.ContainsKey(nid))
                lookup[nid] = new List<CharacterSlotEntry>();
            lookup[nid].Add(ToSlotEntry(row, 1));
        }

        return lookup;
    }

    private static Dictionary<string, List<EpisodeChoice>> BuildNodeChoicesLookup(
        Dictionary<string, List<string[]>> sections)
    {
        var lookup = new Dictionary<string, List<EpisodeChoice>>();

        if (!sections.TryGetValue("CHOICES", out var rows)) return lookup;

        foreach (string[] row in rows)
        {
            string nid = Field(row, 0);
            if (!lookup.ContainsKey(nid))
                lookup[nid] = new List<EpisodeChoice>();

            lookup[nid].Add(new EpisodeChoice
            {
                buttonText  = Field(row, 2),
                nextNodeId  = Field(row, 3),
                setFlags    = SplitList(Field(row, 4)),
                clearFlags  = SplitList(Field(row, 5)),
                varChanges  = ParseVarChangeList(Field(row, 6))
            });
        }

        return lookup;
    }

    private static Dictionary<string, List<NodeFlagBranch>> BuildNodeBranchesLookup(
        Dictionary<string, List<string[]>> sections)
    {
        var lookup = new Dictionary<string, List<NodeFlagBranch>>();

        if (!sections.TryGetValue("NODE_BRANCHES", out var rows)) return lookup;

        foreach (string[] row in rows)
        {
            string nid = Field(row, 0);
            if (!lookup.ContainsKey(nid))
                lookup[nid] = new List<NodeFlagBranch>();

            lookup[nid].Add(new NodeFlagBranch
            {
                requiredAllFlags = SplitBy(Field(row, 1), ','),
                requiredAnyFlags = SplitBy(Field(row, 2), ','),
                nextNodeId       = Field(row, 3)
            });
        }

        return lookup;
    }

    private static Dictionary<string, List<NodeVarBranch>> BuildNodeVarBranchesLookup(
        Dictionary<string, List<string[]>> sections)
    {
        var lookup = new Dictionary<string, List<NodeVarBranch>>();

        if (!sections.TryGetValue("NODE_VAR_BRANCHES", out var rows)) return lookup;

        foreach (string[] row in rows)
        {
            string nid = Field(row, 0);
            if (!lookup.ContainsKey(nid))
                lookup[nid] = new List<NodeVarBranch>();

            lookup[nid].Add(new NodeVarBranch
            {
                condition = new VarCondition
                {
                    varName   = Field(row, 1),
                    op        = ParseCompareOp(Field(row, 2)),
                    threshold = int.TryParse(Field(row, 3), out int t) ? t : 0
                },
                nextNodeId = Field(row, 4)
            });
        }

        return lookup;
    }

    private static Dictionary<string, List<CraftingOutcome>> BuildNodeCraftingBranchesLookup(
        Dictionary<string, List<string[]>> sections)
    {
        var lookup = new Dictionary<string, List<CraftingOutcome>>();

        if (!sections.TryGetValue("NODE_CRAFTING_BRANCHES", out var rows)) return lookup;

        foreach (string[] row in rows)
        {
            string nid = Field(row, 0);
            if (!lookup.ContainsKey(nid))
                lookup[nid] = new List<CraftingOutcome>();

            if (!System.Enum.TryParse(Field(row, 1), true, out CraftingJobResult result)) continue;

            lookup[nid].Add(new CraftingOutcome
            {
                result     = result,
                nextNodeId = Field(row, 2),
                flag       = Field(row, 3),
                varChanges = ParseVarChangeList(Field(row, 4))
            });
        }

        return lookup;
    }

    private static Dictionary<string, List<NodeEpisodeBranch>> BuildNodeEpisodeBranchesLookup(
        Dictionary<string, List<string[]>> sections)
    {
        var lookup = new Dictionary<string, List<NodeEpisodeBranch>>();

        if (!sections.TryGetValue("NODE_EPISODE_BRANCHES", out var rows)) return lookup;

        foreach (string[] row in rows)
        {
            string nid = Field(row, 0);
            if (!lookup.ContainsKey(nid))
                lookup[nid] = new List<NodeEpisodeBranch>();

            lookup[nid].Add(new NodeEpisodeBranch
            {
                requiredCompletedEpisodeId = Field(row, 1),
                nextNodeId                 = Field(row, 2)
            });
        }

        return lookup;
    }

    private static void ParseNodes(
        Dictionary<string, List<string[]>> sections,
        EpisodeData data,
        Dictionary<string, List<CharacterSlotEntry>> nodeChars,
        Dictionary<string, List<EpisodeChoice>> nodeChoices,
        Dictionary<string, List<NodeFlagBranch>> nodeBranches,
        Dictionary<string, List<NodeVarBranch>> nodeVarBranches,
        Dictionary<string, List<NodeEpisodeBranch>> nodeEpisodeBranches,
        Dictionary<string, List<CraftingOutcome>> nodeCraftingBranches)
    {
        data.nodes = new List<EpisodeNode>();

        if (!sections.TryGetValue("NODES", out var rows)) return;

        foreach (string[] row in rows)
        {
            string nid = Field(row, 0);
            bool crafting = string.Equals(Field(row, 5), "true", StringComparison.OrdinalIgnoreCase)
                         || Field(row, 5) == "1";
            string craftingRecipeId = Field(row, 15);
            if (crafting && string.IsNullOrWhiteSpace(craftingRecipeId))
            {
                Debug.LogError(
                    $"[EpisodeCsvImporter] Crafting node '{nid}' is missing craftingRecipeId.");
            }

            data.nodes.Add(new EpisodeNode
            {
                nodeId              = nid,
                speakerKey          = Field(row, 1),
                overrideSpeakerName = Field(row, 2),
                text                = Field(row, 3),
                nextNodeId          = Field(row, 4),
                requiresCrafting    = crafting,
                craftingTicketKey   = Field(row, 6),
                bgmCommand          = ParseBgmCommand(Field(row, 7)),
                bgmClipName         = Field(row, 8),
                craftingOutcomes = nodeCraftingBranches.TryGetValue(nid, out var craftBr)
                    ? craftBr : new List<CraftingOutcome>(),
                characters = nodeChars.TryGetValue(nid, out var chars)
                    ? chars : new List<CharacterSlotEntry>(),
                choices = nodeChoices.TryGetValue(nid, out var choices)
                    ? choices : new List<EpisodeChoice>(),
                flagBranches = nodeBranches.TryGetValue(nid, out var branches)
                    ? branches : new List<NodeFlagBranch>(),
                varBranches = nodeVarBranches.TryGetValue(nid, out var varBr)
                    ? varBr : new List<NodeVarBranch>(),
                episodeBranches = nodeEpisodeBranches.TryGetValue(nid, out var epBr)
                    ? epBr : new List<NodeEpisodeBranch>()
            });
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CharacterSlotEntry ToSlotEntry(string[] row, int offset)
    {
        return new CharacterSlotEntry
        {
            characterKey = Field(row, offset),
            expressionKey = Field(row, offset + 1),
            slotIndex = int.TryParse(Field(row, offset + 2), out int s) ? s : -1
        };
    }

    private static string Field(string[] row, int index)
    {
        return index < row.Length ? row[index].Trim() : string.Empty;
    }

    private static List<VarChange> ParseVarChangeList(string value)
    {
        var result = new List<VarChange>();
        if (string.IsNullOrWhiteSpace(value)) return result;

        foreach (string item in value.Split('|'))
        {
            string t = item.Trim();
            if (string.IsNullOrEmpty(t)) continue;

            // Format: varName+5  or  varName-3
            int plusIdx  = t.LastIndexOf('+');
            int minusIdx = t.LastIndexOf('-');
            int splitAt  = -1;
            int sign     = 1;

            if (plusIdx > 0 && plusIdx > minusIdx)  { splitAt = plusIdx;  sign =  1; }
            else if (minusIdx > 0)                   { splitAt = minusIdx; sign = -1; }

            if (splitAt < 0) continue;

            string name  = t.Substring(0, splitAt).Trim();
            string numStr = t.Substring(splitAt + 1).Trim();
            if (!int.TryParse(numStr, out int num)) continue;

            result.Add(new VarChange { varName = name, delta = sign * num });
        }

        return result;
    }

    private static VarCondition TryParseVarCondition(string token)
    {
        // Supported operators (longest first to avoid partial matches)
        string[] ops = { ">=", "<=", "==", ">", "<" };

        foreach (string op in ops)
        {
            int idx = token.IndexOf(op, System.StringComparison.Ordinal);
            if (idx <= 0) continue;

            string name   = token.Substring(0, idx).Trim();
            string numStr = token.Substring(idx + op.Length).Trim();
            if (!int.TryParse(numStr, out int num)) continue;

            return new VarCondition
            {
                varName   = name,
                op        = ParseCompareOp(op),
                threshold = num
            };
        }

        return null;
    }

    private static EpisodeType ParseEpisodeType(string value)
    {
        return System.Enum.TryParse(value.Trim(), true, out EpisodeType result) ? result : EpisodeType.Default;
    }

    private static MandatorySlot ParseMandatorySlot(string value)
    {
        return System.Enum.TryParse(value.Trim(), true, out MandatorySlot result) ? result : MandatorySlot.None;
    }

    private static BgmCommand ParseBgmCommand(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "play" => BgmCommand.Play,
            "stop" => BgmCommand.Stop,
            _      => BgmCommand.None
        };
    }

    private static CompareOp ParseCompareOp(string op)
    {
        return op.Trim() switch
        {
            ">=" => CompareOp.GreaterOrEqual,
            ">"  => CompareOp.Greater,
            "==" => CompareOp.Equal,
            "<"  => CompareOp.Less,
            "<=" => CompareOp.LessOrEqual,
            _    => CompareOp.GreaterOrEqual
        };
    }

    private static List<string> SplitBy(string value, char separator)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return result;
        foreach (string item in value.Split(separator))
        {
            string t = item.Trim();
            if (!string.IsNullOrEmpty(t)) result.Add(t);
        }
        return result;
    }

    private static List<string> SplitList(string value)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return result;

        foreach (string item in value.Split('|'))
        {
            string t = item.Trim();
            if (!string.IsNullOrEmpty(t))
                result.Add(t);
        }

        return result;
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
