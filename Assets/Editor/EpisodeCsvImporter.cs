using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class EpisodeCsvImporter : EditorWindow
{
    private string _csvPath = "";
    private string _outputFolder = "Assets/Data/EpisodeData";

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
        EpisodeData data = ParseCsv(lines);
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

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    private static EpisodeData ParseCsv(string[] lines)
    {
        Dictionary<string, List<string[]>> sections = SplitIntoSections(lines);

        EpisodeData data = ScriptableObject.CreateInstance<EpisodeData>();

        if (!ParseMeta(sections, data)) return null;
        ParseTrigger(sections, data);
        ParseOpeningChars(sections, data);

        Dictionary<string, List<CharacterSlotEntry>> nodeChars       = BuildNodeCharsLookup(sections);
        Dictionary<string, List<EpisodeChoice>>      nodeChoices     = BuildNodeChoicesLookup(sections);
        Dictionary<string, List<NodeFlagBranch>>     nodeBranches    = BuildNodeBranchesLookup(sections);
        Dictionary<string, List<NodeVarBranch>>      nodeVarBranches = BuildNodeVarBranchesLookup(sections);
        ParseNodes(sections, data, nodeChars, nodeChoices, nodeBranches, nodeVarBranches);

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
        data.episodeId    = Field(row, 0);
        data.episodeTitle = Field(row, 1);
        data.firstNodeId  = Field(row, 2);
        return true;
    }

    private static void ParseTrigger(Dictionary<string, List<string[]>> sections, EpisodeData data)
    {
        data.triggerCondition = new EpisodeTriggerCondition();

        if (!sections.TryGetValue("TRIGGER", out var rows) || rows.Count == 0) return;

        string[] row = rows[0];
        data.triggerCondition.minDay                  = int.TryParse(Field(row, 0), out int d) ? d : 0;
        data.triggerCondition.requiredFlags            = SplitList(Field(row, 1));
        data.triggerCondition.blockedFlags             = SplitList(Field(row, 2));
        data.triggerCondition.prerequisiteEpisodeIds   = SplitList(Field(row, 3));
        data.triggerCondition.requiredVars             = ParseVarConditionList(Field(row, 4));
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
                requiredFlag = Field(row, 1),
                nextNodeId   = Field(row, 2)
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

    private static void ParseNodes(
        Dictionary<string, List<string[]>> sections,
        EpisodeData data,
        Dictionary<string, List<CharacterSlotEntry>> nodeChars,
        Dictionary<string, List<EpisodeChoice>> nodeChoices,
        Dictionary<string, List<NodeFlagBranch>> nodeBranches,
        Dictionary<string, List<NodeVarBranch>> nodeVarBranches)
    {
        data.nodes = new List<EpisodeNode>();

        if (!sections.TryGetValue("NODES", out var rows)) return;

        foreach (string[] row in rows)
        {
            string nid = Field(row, 0);
            bool crafting = string.Equals(Field(row, 5), "true", StringComparison.OrdinalIgnoreCase)
                         || Field(row, 5) == "1";

            data.nodes.Add(new EpisodeNode
            {
                nodeId             = nid,
                speakerKey         = Field(row, 1),
                overrideSpeakerName = Field(row, 2),
                text               = Field(row, 3),
                nextNodeId         = Field(row, 4),
                requiresCrafting   = crafting,
                craftingTicketKey  = Field(row, 6),
                nextNodeIdGood     = Field(row, 7),
                nextNodeIdBad      = Field(row, 8),
                characters = nodeChars.TryGetValue(nid, out var chars)
                    ? chars : new List<CharacterSlotEntry>(),
                choices = nodeChoices.TryGetValue(nid, out var choices)
                    ? choices : new List<EpisodeChoice>(),
                flagBranches = nodeBranches.TryGetValue(nid, out var branches)
                    ? branches : new List<NodeFlagBranch>(),
                varBranches = nodeVarBranches.TryGetValue(nid, out var varBr)
                    ? varBr : new List<NodeVarBranch>()
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

    private static List<VarCondition> ParseVarConditionList(string value)
    {
        var result = new List<VarCondition>();
        if (string.IsNullOrWhiteSpace(value)) return result;

        foreach (string item in value.Split('|'))
        {
            string t = item.Trim();
            if (string.IsNullOrEmpty(t)) continue;

            VarCondition vc = TryParseVarCondition(t);
            if (vc != null) result.Add(vc);
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