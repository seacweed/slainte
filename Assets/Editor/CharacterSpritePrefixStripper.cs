using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public sealed class SpritePrefixStripReport
    {
        public int renamed;
        public int skipped;
        public readonly System.Collections.Generic.List<string> warnings = new();

        public string Summary()
        {
            return $"이름 변경 {renamed}개, 건너뜀 {skipped}개, 경고 {warnings.Count}";
        }
    }

    /// <summary>
    /// 선택한 폴더 안 스프라이트 파일명에서 "폴더명_" 접두어(대소문자 무관)를 제거한다.
    /// AssetDatabase.RenameAsset을 사용해 GUID와 기존 참조를 그대로 보존한다.
    /// </summary>
    public static class CharacterSpritePrefixStripper
    {
        [MenuItem("Slainte/데이터/캐릭터 스프라이트 접두어 제거")]
        public static void StripPrefixFromMenu()
        {
            string[] folderPaths = Selection.objects
                .Select(AssetDatabase.GetAssetPath)
                .Where(AssetDatabase.IsValidFolder)
                .ToArray();
            if (folderPaths.Length == 0)
                return;

            SpritePrefixStripReport report = new();
            foreach (string folderPath in folderPaths)
                StripPrefixInFolder(folderPath, report);

            Finish(report);
        }

        [MenuItem("Slainte/데이터/캐릭터 스프라이트 접두어 제거", true)]
        public static bool ValidateStripPrefixFromMenu()
        {
            return Selection.objects.Any(
                obj => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(obj)));
        }

        private static void StripPrefixInFolder(string folderPath, SpritePrefixStripReport report)
        {
            string folderName = Path.GetFileName(folderPath.TrimEnd('/'));
            string prefix = folderName + "_";

            string[] assetGuids = AssetDatabase.FindAssets("t:Sprite", new[] { folderPath });
            foreach (string guid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(assetPath)?.Replace('\\', '/') != folderPath)
                    continue; // 하위 폴더 제외, 바로 안의 파일만 대상

                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    report.skipped++;
                    continue;
                }

                string newName = fileName[prefix.Length..];
                if (string.IsNullOrWhiteSpace(newName))
                {
                    report.warnings.Add($"접두어를 제거하면 이름이 비어 건너뜀: {assetPath}");
                    report.skipped++;
                    continue;
                }

                string error = AssetDatabase.RenameAsset(assetPath, newName);
                if (!string.IsNullOrEmpty(error))
                {
                    report.warnings.Add($"이름 변경 실패: {assetPath} ({error})");
                    continue;
                }

                report.renamed++;
            }
        }

        private static void Finish(SpritePrefixStripReport report)
        {
            string details = string.Join("\n", report.warnings);
            if (report.warnings.Count > 0)
                Debug.LogWarning($"[CharacterSpritePrefixStripper] {report.Summary()}\n{details}");
            else
                Debug.Log($"[CharacterSpritePrefixStripper] PASS: {report.Summary()}");

            EditorUtility.DisplayDialog(
                "캐릭터 스프라이트 접두어 제거",
                report.Summary() + (string.IsNullOrWhiteSpace(details) ? string.Empty : "\n\n" + details),
                "확인");
        }
    }
}
