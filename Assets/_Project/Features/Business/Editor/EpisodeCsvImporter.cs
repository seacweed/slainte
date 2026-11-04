using System.Collections.Generic;
using System.IO;
using System.Text;
using Slainte.Content;
using Slainte.EditorTools;
using UnityEditor;
using UnityEngine;

// 기획자가 작성한 에피소드 CSV를 EpisodeData 에셋으로 임포트하는 창. 포맷 해석은 EpisodeCsvCodec이 맡고,
// 이 클래스는 파일 선택·에셋 저장만 담당한다. 이미 같은 episodeId의 에셋이 있으면 덮어쓰지 않고
// EditorUtility.CopySerialized로 병합하되, CSV에 SETTLEMENT_REWARDS 섹션이 아예 없으면 인스펙터에서
// 수기로 채운 기존 보상 값을 그대로 보존한다 — 섹션이 있으면 비어 있어도 CSV가 그 필드의 기준이 된다.
public class EpisodeCsvImporter : EditorWindow
{
    private string _csvPath = "";
    private string _outputFolder =
        ProjectResourcePaths.AssetRoot + ProjectResourcePaths.NarrativeEpisodes;

    [MenuItem("Tools/Slainte/Import Episode CSV")]
    public static void Open()
    {
        GetWindow<EpisodeCsvImporter>("Episode CSV Importer");
    }

    [MenuItem("Tools/Slainte/Import All Episode CSVs")]
    public static void ImportAllSourceCsvs()
    {
        string folder = NarrativeAssetPaths.EpisodeSourceRoot;
        if (!Directory.Exists(folder))
        {
            EditorUtility.DisplayDialog("Error", $"에피소드 CSV 폴더를 찾을 수 없습니다:\n{folder}", "OK");
            return;
        }

        string outputFolder = ProjectResourcePaths.AssetRoot + ProjectResourcePaths.NarrativeEpisodes;
        int imported = 0;
        int warnings = 0;
        List<string> failures = new();
        foreach (string path in Directory.GetFiles(folder, "*.csv"))
        {
            EpisodeData asset = ImportFile(path, outputFolder, out EpisodeCsvCodec.ReadResult result);
            warnings += result?.Warnings.Count ?? 0;
            if (asset != null)
                imported++;
            else
                failures.Add($"{Path.GetFileName(path)}: {result?.Error}");
        }

        AssetDatabase.SaveAssets();
        string summary = $"임포트 {imported}개, 경고 {warnings}개(Console 참고)";
        if (failures.Count > 0)
            summary += "\n실패:\n" + string.Join("\n", failures);
        EditorUtility.DisplayDialog("Import All Episode CSVs", summary, "OK");
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

        EpisodeData asset = ImportFile(_csvPath, _outputFolder, out EpisodeCsvCodec.ReadResult result);
        AssetDatabase.SaveAssets();
        if (asset == null)
        {
            EditorUtility.DisplayDialog("Error", result?.Error ?? "임포트에 실패했습니다.", "OK");
            return;
        }

        string message = $"임포트 완료:\n{AssetDatabase.GetAssetPath(asset)}";
        if (result.Warnings.Count > 0)
            message += $"\n\n경고 {result.Warnings.Count}개:\n" + string.Join("\n", result.Warnings);
        EditorUtility.DisplayDialog("Success", message, "OK");
    }

    // CSV 파일 하나를 읽어 에셋으로 저장한다. 실패하면 null을 돌려주고 result.Error에 이유를 남긴다.
    public static EpisodeData ImportFile(
        string csvPath,
        string outputFolder,
        out EpisodeCsvCodec.ReadResult result)
    {
        result = EpisodeCsvCodec.Read(File.ReadAllText(csvPath, Encoding.UTF8));
        string fileName = Path.GetFileName(csvPath);
        foreach (string warning in result.Warnings)
            Debug.LogWarning($"[EpisodeCsvImporter] {fileName}: {warning}");
        if (!result.Succeeded)
        {
            Debug.LogError($"[EpisodeCsvImporter] {fileName}: {result.Error}");
            return null;
        }

        return SaveAsset(result, outputFolder);
    }

    // 읽은 데이터를 기존 에셋에 병합(없으면 생성)한다. 그래프 에디터의 CSV 동기화도 같은 저장 규칙을 쓴다.
    public static EpisodeData SaveAsset(EpisodeCsvCodec.ReadResult result, string outputFolder)
    {
        EpisodeData data = result.Data;
        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
            AssetDatabase.Refresh();
        }

        string assetName = $"EpisodeData_{data.episodeId}";
        data.name = assetName;
        string assetPath = $"{outputFolder}/{assetName}.asset";
        EpisodeData existing = AssetDatabase.LoadAssetAtPath<EpisodeData>(assetPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(data, assetPath);
            Debug.Log($"[EpisodeCsvImporter] Created: {assetPath}");
            return data;
        }

        if (!result.Sections.Contains(EpisodeCsvCodec.SettlementRewardsSection))
            data.settlementRewards = existing.settlementRewards;

        EditorUtility.CopySerialized(data, existing);
        existing.name = assetName;
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(data);
        Debug.Log($"[EpisodeCsvImporter] Updated: {assetPath}");
        return existing;
    }
}
