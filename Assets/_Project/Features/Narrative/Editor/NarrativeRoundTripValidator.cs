using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Slainte.EditorTools;
using UnityEditor;
using UnityEngine;

namespace NarrativeFlow.Editor
{
    // CSV와 그래프 에디터를 병행 사용해도 데이터가 바뀌지 않는지 확인하는 검증기. 모든 원본 CSV에 대해
    // CSV → EpisodeData(A) → 그래프 → EpisodeData(B)를 메모리에서만 수행하고 A와 B가 같은 CSV로 쓰이는지 비교한다.
    // 에셋·파일은 건드리지 않는다.
    public static class NarrativeRoundTripValidator
    {
        [MenuItem("Narrative/Validate CSV ⇄ Graph Round Trip")]
        public static void ValidateFromMenu()
        {
            bool passed = Run(out string report);
            Debug.Log("[NarrativeRoundTrip]\n" + report);
            EditorUtility.DisplayDialog(
                "CSV ⇄ Graph Round Trip",
                passed ? "모든 원본 CSV가 그래프 왕복 후에도 동일합니다." : "차이가 있습니다. Console을 확인하세요.\n\n" + Truncate(report, 900),
                "확인");
        }

        public static void RunBatchValidation()
        {
            if (!Run(out string report))
                throw new InvalidOperationException(report);
            Debug.Log("[NarrativeRoundTrip] PASS\n" + report);
        }

        public static bool Run(out string report)
        {
            StringBuilder sb = new();
            bool passed = true;
            foreach (string path in Directory.GetFiles(NarrativeAssetPaths.EpisodeSourceRoot, "*.csv"))
            {
                bool ok = ValidateFile(path, out string line);
                passed &= ok;
                sb.AppendLine(line);
            }
            report = sb.ToString();
            return passed;
        }

        private static bool ValidateFile(string path, out string line)
        {
            string name = Path.GetFileName(path);
            EpisodeCsvCodec.ReadResult read = EpisodeCsvCodec.Read(File.ReadAllText(path, Encoding.UTF8));
            if (!read.Succeeded)
            {
                line = $"FAIL {name}: {read.Error}";
                return false;
            }

            EpisodeData original = read.Data;
            EpisodeDataImporter.BuildResult built = EpisodeDataImporter.Build(original);
            CompileReport compile = new();
            EpisodeData roundTripped = EpisodeDataCompiler.Build(built.Graph, compile);
            try
            {
                string expected = Canonical(original);
                string actual = Canonical(roundTripped);
                if (compile.HasErrors)
                {
                    line = $"FAIL {original.episodeId}: 컴파일 오류 {string.Join(" / ", compile.Errors)}";
                    return false;
                }

                if (expected == actual)
                {
                    line = $"OK   {original.episodeId} (블록 {built.Graph.Nodes.Count}, 노드 {original.nodes.Count})";
                    return true;
                }

                line = $"DIFF {original.episodeId}: {FirstDifference(expected, actual)}";
                return false;
            }
            finally
            {
                foreach (NodeDataSO node in built.Graph.Nodes)
                    UnityEngine.Object.DestroyImmediate(node);
                UnityEngine.Object.DestroyImmediate(built.Graph);
                UnityEngine.Object.DestroyImmediate(original);
                UnityEngine.Object.DestroyImmediate(roundTripped);
            }
        }

        // 실행 결과가 같은 표현 차이를 없앤 비교용 CSV: 노드 ID 순 정렬, 중복 ID는 런타임처럼 첫 노드만,
        // 제조 결과는 결과 순, 플래그 하나짜리 OR 조건은 AND와 동일하게 취급.
        private static string Canonical(EpisodeData data)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            data.nodes = data.nodes
                .Where(n => n != null && seen.Add(n.nodeId))
                .OrderBy(n => n.nodeId, StringComparer.Ordinal)
                .ToList();
            foreach (EpisodeNode node in data.nodes)
            {
                // 제조 노드가 아니면 런타임이 제조 필드를 읽지 않으므로(CSV에 남은 흔적) 비교에서 제외한다.
                if (!node.requiresCrafting)
                {
                    node.craftingPaymentEnabled = false;
                    node.craftingPaymentCurrency = Slainte.Economy.GameCurrency.Money;
                    node.craftingPaymentMultiplier = 1f;
                    node.craftingOrderType = Slainte.Bartending.CocktailOrderType.EpisodeOrder;
                    node.craftingOrderTarget = string.Empty;
                    node.craftingTicketKey = string.Empty;
                }
                node.craftingOutcomes = node.craftingOutcomes.OrderBy(o => o.result).ToList();
            }
            return EpisodeCsvCodec.Write(data);
        }

        private static string FirstDifference(string expected, string actual)
        {
            string[] a = expected.Split('\n');
            string[] b = actual.Split('\n');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                if (a[i] != b[i])
                    return $"줄 {i + 1}\n    CSV  : {a[i].Trim()}\n    그래프: {b[i].Trim()}";
            }
            return $"줄 수 차이 {a.Length} / {b.Length}";
        }

        private static string Truncate(string text, int max) =>
            text.Length <= max ? text : text.Substring(0, max) + "...";
    }
}
