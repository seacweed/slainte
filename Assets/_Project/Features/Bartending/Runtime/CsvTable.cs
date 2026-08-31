using System.Collections.Generic;
using System.Text;

namespace Slainte.Bartending
{
    public sealed class CsvRow
    {
        private readonly Dictionary<string, string> values;

        public CsvRow(Dictionary<string, string> values)
        {
            this.values = values;
        }

        public string Get(string key, string fallback = "")
        {
            return values.TryGetValue(key, out string value) ? value : fallback;
        }
    }

    public static class CsvTable
    {
        public static List<CsvRow> Parse(string text)
        {
            List<string[]> lines = ParseLines(text);
            List<CsvRow> rows = new();
            if (lines.Count == 0)
                return rows;

            string[] headers = lines[0];
            for (int i = 1; i < lines.Count; i++)
            {
                string[] fields = lines[i];
                if (fields.Length == 0)
                    continue;

                Dictionary<string, string> values = new();
                for (int j = 0; j < headers.Length; j++)
                {
                    string key = headers[j].Trim();
                    if (string.IsNullOrEmpty(key))
                        continue;

                    values[key] = j < fields.Length ? fields[j].Trim() : string.Empty;
                }

                rows.Add(new CsvRow(values));
            }

            return rows;
        }

        private static List<string[]> ParseLines(string text)
        {
            List<string[]> lines = new();
            List<string> fields = new();
            StringBuilder current = new();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
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
                else if ((c == '\n' || c == '\r') && !inQuotes)
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;

                    AddLine(lines, fields, current);
                }
                else
                {
                    current.Append(c);
                }
            }

            AddLine(lines, fields, current);
            return lines;
        }

        private static void AddLine(List<string[]> lines, List<string> fields, StringBuilder current)
        {
            fields.Add(current.ToString());
            current.Clear();

            bool hasContent = false;
            for (int i = 0; i < fields.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(fields[i]))
                {
                    hasContent = true;
                    break;
                }
            }

            if (hasContent && !fields[0].TrimStart().StartsWith("#"))
                lines.Add(fields.ToArray());

            fields.Clear();
        }
    }
}
