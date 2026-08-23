using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public sealed class CharacterExpressionImportReport
    {
        public int sourceFiles;
        public int createdExpressions;
        public int overwrittenExpressions;
        public int blinkAssigned;
        public readonly List<string> warnings = new();
        public readonly List<string> errors = new();

        public string Summary()
        {
            return $"파일 {sourceFiles}개, 신규 {createdExpressions}개, "
                + $"덮어씀 {overwrittenExpressions}개, blink 연결 {blinkAssigned}개, "
                + $"경고 {warnings.Count}, 오류 {errors.Count}";
        }
    }

    /// <summary>
    /// 캐릭터 스프라이트 폴더의 png 파일을 CharacterData.expressions에 자동으로 채운다.
    /// key는 파일명(확장자 제외)을 그대로 사용하며, sprite 필드만 덮어쓴다.
    /// "_closed" 파일은 별도 expression을 만들지 않고, 같은 base(및 base_left/base_right)
    /// expression의 blinkSprite에 연결한다.
    /// defaultSprite/overlaySprite/blinkOverlaySprite는 건드리지 않는다.
    /// </summary>
    public static class CharacterExpressionSpriteImporter
    {
        private const string ClosedSuffix = "_closed";
        private static readonly string[] BlinkTargetSuffixes = { string.Empty, "_left", "_right" };

        [MenuItem("Slainte/데이터/캐릭터 표정 스프라이트 폴더 임포트")]
        public static void ImportFromMenu()
        {
            CharacterData character = Selection.activeObject as CharacterData;
            if (character == null)
                return;

            string path = EditorUtility.OpenFolderPanel(
                "표정 스프라이트 폴더 선택",
                Application.dataPath,
                string.Empty);
            if (!string.IsNullOrWhiteSpace(path))
                Import(character, path, showDialog: true);
        }

        [MenuItem("Slainte/데이터/캐릭터 표정 스프라이트 폴더 임포트", true)]
        public static bool ValidateImportFromMenu()
        {
            return Selection.activeObject is CharacterData;
        }

        public static CharacterExpressionImportReport Import(
            CharacterData character,
            string sourceFolder,
            bool showDialog)
        {
            CharacterExpressionImportReport report = new();
            if (character == null)
            {
                report.errors.Add("대상 CharacterData가 지정되지 않았습니다.");
                Finish(report, showDialog);
                return report;
            }

            if (string.IsNullOrWhiteSpace(sourceFolder)
                || !Directory.Exists(sourceFolder))
            {
                report.errors.Add($"스프라이트 폴더를 찾을 수 없습니다: {sourceFolder}");
                Finish(report, showDialog);
                return report;
            }

            if (!TryGetAssetRelativeFolder(sourceFolder, out string relativeFolder))
            {
                report.errors.Add($"프로젝트 Assets 폴더 밖의 경로입니다: {sourceFolder}");
                Finish(report, showDialog);
                return report;
            }

            string[] files = Directory.GetFiles(
                sourceFolder,
                "*.png",
                SearchOption.TopDirectoryOnly);
            report.sourceFiles = files.Length;
            if (files.Length == 0)
            {
                report.warnings.Add("폴더 안에 png 파일이 없습니다.");
                Finish(report, showDialog);
                return report;
            }

            character.expressions ??= new List<CharacterData.ExpressionEntry>();

            List<(string baseKey, string fileName, Sprite sprite)> closedSources = new();
            foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                string relativeAssetPath = relativeFolder + "/" + Path.GetFileName(file);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(relativeAssetPath);
                if (sprite == null)
                {
                    report.warnings.Add($"Sprite를 로드하지 못했습니다: {relativeAssetPath}");
                    continue;
                }

                bool isExactlyClosed = string.Equals(key, "closed", StringComparison.OrdinalIgnoreCase);
                if (!isExactlyClosed && TryGetClosedBaseKey(key, out string baseKey))
                {
                    closedSources.Add((baseKey, key, sprite));
                    continue;
                }

                if (!isExactlyClosed && key.IndexOf("closed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    report.warnings.Add(
                        $"'closed'가 포함되어 있지만 형식을 인식하지 못해 제외합니다: {key}");
                    continue;
                }

                CharacterData.ExpressionEntry entry = FindExpression(character, key);
                if (entry != null)
                {
                    entry.sprite = sprite;
                    report.overwrittenExpressions++;
                }
                else
                {
                    character.expressions.Add(new CharacterData.ExpressionEntry
                    {
                        key = key,
                        sprite = sprite
                    });
                    report.createdExpressions++;
                }
            }

            foreach ((string baseKey, string fileName, Sprite sprite) in closedSources)
            {
                bool matchedAny = false;
                foreach (string suffix in BlinkTargetSuffixes)
                {
                    CharacterData.ExpressionEntry entry = FindExpression(character, baseKey + suffix);
                    if (entry == null)
                        continue;

                    entry.blinkSprite = sprite;
                    matchedAny = true;
                    report.blinkAssigned++;
                }

                if (!matchedAny)
                    report.warnings.Add(
                        $"대응하는 expression을 찾지 못해 blink 연결을 건너뜁니다: "
                        + $"{fileName} (base: {baseKey})");
            }

            EditorUtility.SetDirty(character);
            AssetDatabase.SaveAssets();
            Finish(report, showDialog);
            return report;
        }

        private static bool TryGetClosedBaseKey(string fileNameNoExt, out string baseKey)
        {
            if (fileNameNoExt.EndsWith(ClosedSuffix, StringComparison.OrdinalIgnoreCase)
                && fileNameNoExt.Length > ClosedSuffix.Length)
            {
                baseKey = fileNameNoExt[..^ClosedSuffix.Length];
                return true;
            }

            baseKey = null;
            return false;
        }

        private static CharacterData.ExpressionEntry FindExpression(CharacterData character, string key)
        {
            return character.expressions.FirstOrDefault(
                candidate => candidate != null
                    && string.Equals(candidate.key, key, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetAssetRelativeFolder(string absoluteFolder, out string relativeFolder)
        {
            string projectDataPath = Application.dataPath.Replace('\\', '/');
            string normalized = absoluteFolder.Replace('\\', '/');
            if (!normalized.StartsWith(projectDataPath, StringComparison.OrdinalIgnoreCase))
            {
                relativeFolder = null;
                return false;
            }

            relativeFolder = "Assets" + normalized[projectDataPath.Length..];
            return true;
        }

        private static void Finish(CharacterExpressionImportReport report, bool showDialog)
        {
            string details = string.Join("\n", report.errors.Concat(report.warnings));
            if (report.errors.Count > 0)
                Debug.LogError($"[CharacterExpressionSpriteImporter] {report.Summary()}\n{details}");
            else if (report.warnings.Count > 0)
                Debug.LogWarning($"[CharacterExpressionSpriteImporter] {report.Summary()}\n{details}");
            else
                Debug.Log($"[CharacterExpressionSpriteImporter] PASS: {report.Summary()}");

            if (showDialog)
                EditorUtility.DisplayDialog(
                    "캐릭터 표정 스프라이트 폴더 임포트",
                    report.Summary() + (string.IsNullOrWhiteSpace(details)
                        ? string.Empty
                        : "\n\n" + details),
                    "확인");
        }
    }
}
