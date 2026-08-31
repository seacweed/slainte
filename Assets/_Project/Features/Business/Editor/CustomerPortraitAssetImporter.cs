using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public sealed class CustomerPortraitImportReport
    {
        public int sourceFiles;
        public int characterSets;
        public int linkedCharacters;
        public int blinkCharacters;
        public readonly List<string> warnings = new();
        public readonly List<string> errors = new();

        public string Summary()
        {
            return $"원본 {sourceFiles}개, 캐릭터 세트 {characterSets}개, "
                + $"연결 {linkedCharacters}명, 눈 깜박임 {blinkCharacters}명, "
                + $"경고 {warnings.Count}, 오류 {errors.Count}";
        }
    }

    /// <summary>
    /// 기획자가 전달한 손님 초상화를 드래프트 CharacterData에 연결한다.
    /// 실제 손님 풀 게시와는 무관하며, 이 임포터는 표현 스프라이트만 소유한다.
    /// </summary>
    public static class CustomerPortraitAssetImporter
    {
        private const string DestinationRoot =
            "Assets/Sprites/customers/imported_planning";

        private static readonly Dictionary<string, string> CharacterKeyAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "bigfish", "big_fish" },
                { "valentinoberry", "valentino_and_berry" },
                { "littlepip", "little_pip" },
                { "sabaon", "saba_on" },
                { "laupha", "lau_pha" }
            };

        [MenuItem("Slainte/데이터/손님 이미지 드래프트 임포트")]
        public static void ImportDefaultFromMenu()
        {
            Import(FindDefaultSourceFolder(), showDialog: true);
        }

        [MenuItem("Slainte/데이터/손님 이미지 폴더 선택하여 드래프트 임포트")]
        public static void ImportSelectedFromMenu()
        {
            string path = EditorUtility.OpenFolderPanel(
                "손님 이미지 폴더 선택",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                string.Empty);
            if (!string.IsNullOrWhiteSpace(path))
                Import(path, showDialog: true);
        }

        public static void ImportDefaultFromCommandLine()
        {
            CustomerPortraitImportReport report = Import(
                FindDefaultSourceFolder(),
                showDialog: false);
            if (report.errors.Count > 0)
                throw new InvalidDataException(string.Join("\n", report.errors));
        }

        public static CustomerPortraitImportReport Import(
            string sourceFolder,
            bool showDialog)
        {
            CustomerPortraitImportReport report = new();
            if (string.IsNullOrWhiteSpace(sourceFolder)
                || !Directory.Exists(sourceFolder))
            {
                report.errors.Add($"손님 이미지 폴더를 찾을 수 없습니다: {sourceFolder}");
                Finish(report, showDialog);
                return report;
            }

            List<PortraitSource> sources = ParseSources(sourceFolder, report);
            report.sourceFiles = sources.Count;
            if (report.errors.Count > 0)
            {
                Finish(report, showDialog);
                return report;
            }

            List<PortraitSet> sets = BuildSets(sources, report);
            report.characterSets = sets.Count;
            if (report.errors.Count > 0)
            {
                Finish(report, showDialog);
                return report;
            }

            EnsureAssetFolder(DestinationRoot);
            Dictionary<string, CharacterData> characters = LoadCharactersByKey(report);

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (PortraitSet set in sets)
                    CopySetIntoProject(set, report);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            foreach (PortraitSet set in sets)
            {
                if (!characters.TryGetValue(set.characterKey, out CharacterData character)
                    || character == null)
                {
                    report.errors.Add(
                        $"CharacterData를 찾을 수 없습니다: {set.characterKey}");
                    continue;
                }

                Sprite mid = ImportAndLoadSprite(set.GetDestinationPath(PortraitState.Mid));
                Sprite good = ImportAndLoadSprite(set.GetDestinationPath(PortraitState.Good));
                Sprite bad = ImportAndLoadSprite(set.GetDestinationPath(PortraitState.Bad));
                Sprite closed = set.closed != null
                    ? ImportAndLoadSprite(set.GetDestinationPath(PortraitState.Closed))
                    : null;

                if (mid == null || good == null || bad == null
                    || (set.closed != null && closed == null))
                {
                    report.errors.Add($"Sprite 로드에 실패했습니다: {set.characterKey}");
                    continue;
                }

                character.defaultSprite = mid;
                // 닫힌 눈은 mid 전용이다. 기본값에 넣으면 good/bad에도 잘못 적용된다.
                character.defaultBlinkSprite = null;

                CharacterData.ExpressionEntry midEntry =
                    FindOrCreateExpression(character, "mid");
                midEntry.sprite = mid;
                midEntry.blinkSprite = closed;

                FindOrCreateExpression(character, "good").sprite = good;
                FindOrCreateExpression(character, "bad").sprite = bad;

                EditorUtility.SetDirty(character);
                report.linkedCharacters++;
                if (closed != null)
                    report.blinkCharacters++;
            }

            AssetDatabase.SaveAssets();
            ValidateLinks(sets, characters, report);
            Finish(report, showDialog);
            return report;
        }

        private static string FindDefaultSourceFolder()
        {
            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            if (!Directory.Exists(downloads))
                return string.Empty;

            DirectoryInfo package = new DirectoryInfo(downloads)
                .GetDirectories("손님-*")
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .FirstOrDefault(directory =>
                    Directory.Exists(Path.Combine(directory.FullName, "손님")));
            return package == null
                ? string.Empty
                : Path.Combine(package.FullName, "손님");
        }

        private static List<PortraitSource> ParseSources(
            string sourceFolder,
            CustomerPortraitImportReport report)
        {
            List<PortraitSource> sources = new();
            foreach (string path in Directory.GetFiles(
                sourceFolder,
                "*.png",
                SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!TryParseFileName(fileName, out string characterKey,
                    out PortraitState state))
                {
                    report.warnings.Add($"파일명 형식을 인식하지 못해 제외합니다: {fileName}");
                    continue;
                }

                sources.Add(new PortraitSource
                {
                    sourcePath = path,
                    sourceFileName = Path.GetFileName(path),
                    characterKey = characterKey,
                    state = state
                });
            }

            if (sources.Count == 0)
                report.errors.Add("인식 가능한 PNG가 없습니다.");
            return sources;
        }

        private static bool TryParseFileName(
            string fileName,
            out string characterKey,
            out PortraitState state)
        {
            characterKey = string.Empty;
            state = PortraitState.Mid;
            string normalized = fileName.Trim().ToLowerInvariant();

            string stem;
            if (normalized.EndsWith("_mid_closed", StringComparison.Ordinal))
            {
                state = PortraitState.Closed;
                stem = normalized[..^"_mid_closed".Length];
            }
            else if (normalized.EndsWith("_closed", StringComparison.Ordinal))
            {
                state = PortraitState.Closed;
                stem = normalized[..^"_closed".Length];
            }
            else if (normalized.EndsWith("_good", StringComparison.Ordinal))
            {
                state = PortraitState.Good;
                stem = normalized[..^"_good".Length];
            }
            else if (normalized.EndsWith("_bad", StringComparison.Ordinal))
            {
                state = PortraitState.Bad;
                stem = normalized[..^"_bad".Length];
            }
            else if (normalized.EndsWith("_mid", StringComparison.Ordinal))
            {
                state = PortraitState.Mid;
                stem = normalized[..^"_mid".Length];
            }
            else
            {
                return false;
            }

            int firstUnderscore = stem.IndexOf('_');
            if (firstUnderscore >= 0
                && int.TryParse(stem[..firstUnderscore], out _))
                stem = stem[(firstUnderscore + 1)..];

            // 핏 파일은 good/bad가 pit_mid_good 형태로 전달되었다.
            if (stem.EndsWith("_mid", StringComparison.Ordinal))
                stem = stem[..^"_mid".Length];

            if (int.TryParse(stem, out int numericIdentity))
                characterKey = $"f{numericIdentity:00}";
            else if (!CharacterKeyAliases.TryGetValue(stem, out characterKey))
                characterKey = stem.Replace('-', '_');

            return !string.IsNullOrWhiteSpace(characterKey);
        }

        private static List<PortraitSet> BuildSets(
            IReadOnlyList<PortraitSource> sources,
            CustomerPortraitImportReport report)
        {
            List<PortraitSet> sets = new();
            foreach (IGrouping<string, PortraitSource> group in sources.GroupBy(
                source => source.characterKey,
                StringComparer.OrdinalIgnoreCase))
            {
                PortraitSet set = new(group.Key);
                foreach (PortraitSource source in group)
                {
                    if (!set.TryAssign(source))
                        report.errors.Add(
                            $"동일 상태 이미지가 중복되었습니다: "
                            + $"{group.Key}/{source.state} ({source.sourceFileName})");
                }

                if (set.mid == null || set.good == null || set.bad == null)
                {
                    report.errors.Add(
                        $"필수 mid/good/bad 이미지가 누락되었습니다: {group.Key}");
                }
                sets.Add(set);
            }
            return sets.OrderBy(set => set.characterKey).ToList();
        }

        private static void CopySetIntoProject(
            PortraitSet set,
            CustomerPortraitImportReport report)
        {
            string characterFolder = DestinationRoot + "/" + set.characterKey;
            EnsureAssetFolder(characterFolder);
            foreach (PortraitSource source in set.AllSources())
            {
                string destination = set.GetDestinationPath(source.state);
                try
                {
                    string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                    if (string.IsNullOrWhiteSpace(projectRoot))
                        throw new DirectoryNotFoundException("Unity 프로젝트 루트를 찾을 수 없습니다.");
                    File.Copy(
                        source.sourcePath,
                        Path.Combine(projectRoot, destination),
                        true);
                }
                catch (Exception exception)
                {
                    report.errors.Add(
                        $"이미지 복사 실패: {source.sourceFileName} ({exception.Message})");
                }
            }
        }

        private static Sprite ImportAndLoadSprite(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            }
            if (importer == null)
                return null;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static CharacterData.ExpressionEntry FindOrCreateExpression(
            CharacterData character,
            string key)
        {
            character.expressions ??= new List<CharacterData.ExpressionEntry>();
            CharacterData.ExpressionEntry entry = character.expressions.FirstOrDefault(
                candidate => candidate != null
                    && string.Equals(candidate.key, key, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
                return entry;

            entry = new CharacterData.ExpressionEntry { key = key };
            character.expressions.Add(entry);
            return entry;
        }

        private static Dictionary<string, CharacterData> LoadCharactersByKey(
            CustomerPortraitImportReport report)
        {
            Dictionary<string, CharacterData> result =
                new(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:CharacterData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CharacterData character = AssetDatabase.LoadAssetAtPath<CharacterData>(path);
                if (character == null || string.IsNullOrWhiteSpace(character.key))
                    continue;
                if (!result.TryAdd(character.key, character))
                    report.warnings.Add(
                        $"중복 CharacterData key가 있어 먼저 발견된 자산을 사용합니다: "
                        + character.key);
            }
            return result;
        }

        private static void ValidateLinks(
            IReadOnlyList<PortraitSet> sets,
            IReadOnlyDictionary<string, CharacterData> characters,
            CustomerPortraitImportReport report)
        {
            foreach (PortraitSet set in sets)
            {
                if (!characters.TryGetValue(set.characterKey, out CharacterData character)
                    || character == null)
                    continue;

                CharacterData.ExpressionEntry mid = FindExpression(character, "mid");
                CharacterData.ExpressionEntry good = FindExpression(character, "good");
                CharacterData.ExpressionEntry bad = FindExpression(character, "bad");
                if (mid?.sprite == null || good?.sprite == null || bad?.sprite == null)
                    report.errors.Add($"표정 연결 검증 실패: {set.characterKey}");
                if (character.defaultSprite != mid?.sprite)
                    report.errors.Add($"기본 표정이 mid가 아닙니다: {set.characterKey}");
                if (set.closed != null && mid?.blinkSprite == null)
                    report.errors.Add($"mid 눈 깜박임 연결 검증 실패: {set.characterKey}");
                if (set.closed == null && mid?.blinkSprite != null)
                    report.errors.Add($"원본에 없는 눈 깜박임이 연결되었습니다: {set.characterKey}");
                if (character.defaultBlinkSprite != null)
                    report.errors.Add(
                        $"기본 눈 깜박임은 비어 있어야 합니다: {set.characterKey}");

                List<Sprite> sprites = new() { mid?.sprite, good?.sprite, bad?.sprite };
                if (mid?.blinkSprite != null)
                    sprites.Add(mid.blinkSprite);
                sprites.RemoveAll(sprite => sprite == null);
                if (sprites.Select(sprite => sprite.rect.size).Distinct().Count() > 1)
                    report.errors.Add($"표정 이미지 크기가 서로 다릅니다: {set.characterKey}");
            }
        }

        private static CharacterData.ExpressionEntry FindExpression(
            CharacterData character,
            string key)
        {
            return character.expressions?.FirstOrDefault(entry => entry != null
                && string.Equals(entry.key, key, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void Finish(CustomerPortraitImportReport report, bool showDialog)
        {
            string details = string.Join("\n", report.errors.Concat(report.warnings));
            if (report.errors.Count > 0)
                Debug.LogError($"[CustomerPortraitImporter] {report.Summary()}\n{details}");
            else if (report.warnings.Count > 0)
                Debug.LogWarning($"[CustomerPortraitImporter] {report.Summary()}\n{details}");
            else
                Debug.Log($"[CustomerPortraitImporter] PASS: {report.Summary()}");

            if (showDialog)
                EditorUtility.DisplayDialog(
                    "손님 이미지 드래프트 임포트",
                    report.Summary() + (string.IsNullOrWhiteSpace(details)
                        ? string.Empty
                        : "\n\n" + details),
                    "확인");
        }

        private enum PortraitState
        {
            Mid,
            Good,
            Bad,
            Closed
        }

        private sealed class PortraitSource
        {
            public string sourcePath;
            public string sourceFileName;
            public string characterKey;
            public PortraitState state;
        }

        private sealed class PortraitSet
        {
            public readonly string characterKey;
            public PortraitSource mid;
            public PortraitSource good;
            public PortraitSource bad;
            public PortraitSource closed;

            public PortraitSet(string characterKey)
            {
                this.characterKey = characterKey;
            }

            public bool TryAssign(PortraitSource source)
            {
                switch (source.state)
                {
                    case PortraitState.Mid:
                        if (mid != null) return false;
                        mid = source;
                        return true;
                    case PortraitState.Good:
                        if (good != null) return false;
                        good = source;
                        return true;
                    case PortraitState.Bad:
                        if (bad != null) return false;
                        bad = source;
                        return true;
                    case PortraitState.Closed:
                        if (closed != null) return false;
                        closed = source;
                        return true;
                    default:
                        return false;
                }
            }

            public IEnumerable<PortraitSource> AllSources()
            {
                if (mid != null) yield return mid;
                if (good != null) yield return good;
                if (bad != null) yield return bad;
                if (closed != null) yield return closed;
            }

            public string GetDestinationPath(PortraitState state)
            {
                string suffix = state switch
                {
                    PortraitState.Mid => "mid",
                    PortraitState.Good => "good",
                    PortraitState.Bad => "bad",
                    PortraitState.Closed => "mid_closed",
                    _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
                };
                return $"{DestinationRoot}/{characterKey}/{characterKey}_{suffix}.png";
            }
        }
    }
}
