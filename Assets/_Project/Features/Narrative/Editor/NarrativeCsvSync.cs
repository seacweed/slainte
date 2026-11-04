using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Slainte.EditorTools;
using UnityEditor;
using UnityEngine;

namespace NarrativeFlow.Editor
{
    // 그래프 ⇄ 원본 CSV(Content/Source/Episodes) 동기화. 그래프를 컴파일하면 원본 CSV를 직접 덮어쓰고,
    // CSV에서 그래프를 다시 만들 수도 있다. 두 쪽을 병행 편집하므로 마지막으로 읽거나 쓴 시점의 CSV 해시를
    // 그래프에 기록해 두고, 그 사이 CSV가 바깥(스프레드시트 등)에서 바뀌었으면 덮어쓰기 전에 묻는다.
    public static class NarrativeCsvSync
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public enum ConflictChoice
        {
            Overwrite,
            Cancel,
            ReloadFromCsv
        }

        // META의 episodeId로 원본 CSV를 찾는다(파일명은 스프레드시트 내보내기 이름이라 규칙이 없으므로).
        public static string FindSourceCsv(string episodeId)
        {
            if (string.IsNullOrWhiteSpace(episodeId) || !Directory.Exists(NarrativeAssetPaths.EpisodeSourceRoot))
                return null;

            foreach (string path in Directory.GetFiles(NarrativeAssetPaths.EpisodeSourceRoot, "*.csv"))
            {
                EpisodeCsvCodec.ReadResult result = EpisodeCsvCodec.Read(File.ReadAllText(path, Encoding.UTF8));
                string id = result.Data != null ? result.Data.episodeId : null;
                if (result.Data != null)
                    UnityEngine.Object.DestroyImmediate(result.Data);
                if (string.Equals(id, episodeId, StringComparison.OrdinalIgnoreCase))
                    return path.Replace('\\', '/');
            }

            return null;
        }

        public static string ResolveSourceCsvPath(NarrativeGraphSO graph, string episodeId)
        {
            if (!string.IsNullOrWhiteSpace(graph.SourceCsvPath) && File.Exists(graph.SourceCsvPath))
                return graph.SourceCsvPath;

            return FindSourceCsv(episodeId)
                ?? $"{NarrativeAssetPaths.EpisodeSourceRoot}EpisodeData_{episodeId}.csv";
        }

        public static string ComputeHash(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return string.Empty;

            using MD5 md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(File.ReadAllBytes(path));
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        // 원본 CSV가 그래프와 마지막으로 동기화된 이후 바뀌었는지(또는 동기화 기록이 없는지).
        public static bool HasExternalChanges(NarrativeGraphSO graph, string path)
        {
            if (!File.Exists(path))
                return false;
            return string.IsNullOrEmpty(graph.LastSyncedCsvHash)
                || !string.Equals(graph.LastSyncedCsvHash, ComputeHash(path), StringComparison.Ordinal);
        }

        public static ConflictChoice AskConflict(string path)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "원본 CSV 변경 감지",
                $"원본 CSV가 그래프와 마지막으로 동기화된 이후 바뀌었거나, 아직 동기화한 적이 없습니다.\n\n{path}\n\n"
                + "덮어쓰면 CSV에서 한 수정이 사라집니다.",
                "그래프로 덮어쓰기",
                "취소",
                "CSV에서 그래프 다시 만들기");
            return choice switch
            {
                0 => ConflictChoice.Overwrite,
                2 => ConflictChoice.ReloadFromCsv,
                _ => ConflictChoice.Cancel
            };
        }

        public static void WriteSourceCsv(NarrativeGraphSO graph, string path, EpisodeData data)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, EpisodeCsvCodec.Write(data), Utf8NoBom);
            AssetDatabase.ImportAsset(path);
            MarkSynced(graph, path);
            Debug.Log($"[NarrativeCsvSync] 원본 CSV에 기록했습니다: {path}");
        }

        public static void MarkSynced(NarrativeGraphSO graph, string path)
        {
            Undo.RecordObject(graph, "Sync CSV");
            graph.SourceCsvPath = path;
            graph.LastSyncedCsvHash = ComputeHash(path);
            EditorUtility.SetDirty(graph);
        }

        // CSV → EpisodeData 에셋 → 그래프. 기존 그래프가 있으면 노드 위치를 유지한 채 내용을 교체한다.
        public static NarrativeGraphSO ImportCsvToGraph(string csvPath)
        {
            EpisodeData asset = EpisodeCsvImporter.ImportFile(
                csvPath,
                Slainte.Content.ProjectResourcePaths.AssetRoot + Slainte.Content.ProjectResourcePaths.NarrativeEpisodes,
                out EpisodeCsvCodec.ReadResult result);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("CSV 가져오기 실패", result?.Error ?? "CSV를 읽지 못했습니다.", "확인");
                return null;
            }

            AssetDatabase.SaveAssets();
            NarrativeGraphSO graph = EpisodeDataImporter.Import(asset);
            if (graph != null)
            {
                MarkSynced(graph, csvPath.Replace('\\', '/'));
                AssetDatabase.SaveAssets();
            }
            return graph;
        }

        [MenuItem("Narrative/Import CSV to Graph...")]
        public static void ImportCsvToGraphFromMenu()
        {
            string path = EditorUtility.OpenFilePanel(
                "Import Episode CSV to Graph",
                NarrativeAssetPaths.EpisodeSourceRoot,
                "csv");
            if (string.IsNullOrEmpty(path))
                return;

            NarrativeGraphSO graph = ImportCsvToGraph(ToProjectRelative(path));
            if (graph != null)
            {
                Selection.activeObject = graph;
                EditorGUIUtility.PingObject(graph);
            }
        }

        public static string ToProjectRelative(string path)
        {
            string normalized = path.Replace('\\', '/');
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
            return normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(projectRoot.Length)
                : normalized;
        }
    }
}
