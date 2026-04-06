using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class EpisodeCSVImporter : EditorWindow
{
    private const string csvPath = "Assets/RestScene/Episode/Episodes.csv";
    private const string savePath = "Assets/RestScene/Episode/";

    [MenuItem("Episode/Import CSV")]
    public static void ImportCSV()
    {
        if (!File.Exists(csvPath))
        {
            Debug.LogError($"[Episode Importer] CSV를 찾을 수 없습니다: {csvPath}");
            return;
        }

        string[] lines = File.ReadAllLines(csvPath);
        if (lines.Length <= 1) return; // Only header

        int createdCount = 0;
        int updatedCount = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // 따옴표 내의 쉼표는 무시하고 자르기용 정규식
            string[] rawCols = Regex.Split(line, ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");
            string[] cols = new string[9]; // 9 columns now (adding Required_Episodes)
            for (int c = 0; c < Mathf.Min(rawCols.Length, 9); c++)
            {
                cols[c] = rawCols[c].Trim('\"', ' ');
            }

            string id = cols[0];
            string name = cols[1];
            string nameEng = cols[2];
            string categoryStr = cols[3];
            string charsStr = cols[4];
            string iconBoard = cols[5];
            string iconArch = cols[6];
            string desc = cols[7];
            string requiredEpsStr = cols[8];

            // 파일명에 특수문자 없게 처리 (ID 기반 생성)
            string assetPath = $"{savePath}Episode_{id}.asset";

            EpisodeData asset = AssetDatabase.LoadAssetAtPath<EpisodeData>(assetPath);
            bool isNew = false;
            
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<EpisodeData>();
                isNew = true;
            }

            // 기본 정보
            asset.episodeID = id;
            asset.episodeName = name;
            asset.episodeNameEng = nameEng;
            asset.episodeDescription = desc;
            asset.iconNameBoard = iconBoard;
            asset.iconNameArchive = iconArch;

            // Category (Enum 매핑)
            if (System.Enum.TryParse(categoryStr, true, out EpisodeCategory cat))
            {
                asset.category = cat;
            }

            // 등장인물 목록 파싱
            if (!string.IsNullOrEmpty(charsStr))
            {
                string[] charNames = charsStr.Split(',');
                asset.characters = new List<EpisodeCharacter>();
                foreach (var cn in charNames)
                {
                    string safeName = cn.Trim();
                    if (!string.IsNullOrEmpty(safeName))
                    {
                        asset.characters.Add(new EpisodeCharacter { characterName = safeName });
                    }
                }
            }
            else
            {
                asset.characters = new List<EpisodeCharacter>();
            }

            // 조건 리스트 (초기화)
            if (asset.conditions == null) asset.conditions = new List<EpisodeCondition>();

            // 선행 조건 에피소드 파싱
            asset.prerequisiteEpisodeIDs = new List<string>();
            if (!string.IsNullOrEmpty(requiredEpsStr))
            {
                string[] reqIds = requiredEpsStr.Split(',');
                foreach (var r in reqIds)
                {
                    string safeId = r.Trim();
                    if (!string.IsNullOrEmpty(safeId))
                        asset.prerequisiteEpisodeIDs.Add(safeId);
                }
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, assetPath);
                createdCount++;
            }
            else
            {
                EditorUtility.SetDirty(asset);
                updatedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Episode Importer] 성공! 생성됨: {createdCount} / 갱신됨: {updatedCount}");
    }
}
