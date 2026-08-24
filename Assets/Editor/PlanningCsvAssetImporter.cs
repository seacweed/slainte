using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Slainte.Bartending;
using Slainte.Business;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public sealed class PlanningCsvAssetImporterWindow : EditorWindow
    {
        private string itemCsvPath;
        private string recipeCsvPath;
        private string ingredientCsvPath;

        [MenuItem("Slainte/데이터/기획 CSV 임포트")]
        public static void Open()
        {
            PlanningCsvAssetImporterWindow window = GetWindow<PlanningCsvAssetImporterWindow>("기획 CSV 임포트");
            window.minSize = new Vector2(680f, 190f);
            window.SetDefaultPaths();
        }

        private void OnEnable()
        {
            if (string.IsNullOrWhiteSpace(itemCsvPath))
                SetDefaultPaths();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("슬런챠 기획 CSV → 바텐딩 에셋", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "빈 행과 '임시 비워둠'은 건너뜁니다. 같은 ID를 다시 임포트하면 기존 에셋을 갱신합니다.",
                MessageType.Info);

            DrawPath("아이템 CSV", ref itemCsvPath);
            DrawPath("레시피 CSV", ref recipeCsvPath);
            DrawPath("배합 CSV", ref ingredientCsvPath);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(
                       !File.Exists(itemCsvPath)
                       || !File.Exists(recipeCsvPath)
                       || !File.Exists(ingredientCsvPath)))
            {
                if (GUILayout.Button("임포트 및 에셋 갱신", GUILayout.Height(30f)))
                {
                    PlanningCsvAssetImporter.Import(
                        itemCsvPath,
                        recipeCsvPath,
                        ingredientCsvPath,
                        showDialog: true);
                }
            }
        }

        private void SetDefaultPaths()
        {
            itemCsvPath = PlanningCsvAssetImporter.DefaultItemCsvPath;
            recipeCsvPath = PlanningCsvAssetImporter.DefaultRecipeCsvPath;
            ingredientCsvPath = PlanningCsvAssetImporter.DefaultIngredientCsvPath;
        }

        private static void DrawPath(string label, ref string path)
        {
            EditorGUILayout.BeginHorizontal();
            path = EditorGUILayout.TextField(label, path);
            if (GUILayout.Button("찾기", GUILayout.Width(55f)))
            {
                string selected = EditorUtility.OpenFilePanel(label, GetStartDirectory(path), "csv");
                if (!string.IsNullOrWhiteSpace(selected))
                    path = selected;
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string GetStartDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    return directory;
            }

            return Application.dataPath;
        }
    }

    public static class PlanningCsvAssetImporter
    {
        // Inspector에서 조정한 런타임 에셋을 에디터 시작 시 CSV가 되돌리지 않도록 한다.
        // CSV 반영은 위의 명시적 임포트 메뉴를 통해서만 수행한다.
        private const bool AutomaticAuthoritativeCsvImportEnabled = false;
        private static bool automaticImportAttempted;

        public const string ItemOutputFolder = "Assets/Resources/Items/Planning";
        public const string ShelfOutputFolder = "Assets/Data/LiquorBottle/Planning";
        public const string RecipeOutputFolder = "Assets/Resources/Recipes/Planning";
        public const string VariantOutputFolder = "Assets/Resources/Recipes/Planning/Variants";
        public const string PlanningCsvFolder = "Assets/Editor/Data/Planning";
        public const string LegacyItemFolder = "Assets/Data/Legacy/PlanningItems";
        public const string LegacyBottleFolder = "Assets/Data/Legacy/PlanningBottles";
        public const string LiquorBottleCatalogPath = "Assets/Data/LiquorBottle/Liquor Bottle Catalog.asset";
        public const string LiquorShopCatalogPath = "Assets/Resources/Shop/LiquorShopCatalog.asset";

        public static string DefaultItemCsvPath => ProjectPath(PlanningCsvFolder + "/items.csv");

        public static string DefaultRecipeCsvPath => ProjectPath(PlanningCsvFolder + "/recipes.csv");

        public static string DefaultIngredientCsvPath => ProjectPath(PlanningCsvFolder + "/recipe_ingredients.csv");

        [InitializeOnLoadMethod]
        private static void QueueAuthoritativeCsvImport()
        {
            if (!AutomaticAuthoritativeCsvImportEnabled
                || Application.isBatchMode
                || automaticImportAttempted)
                return;

            EditorApplication.delayCall += TryAutomaticAuthoritativeImport;
        }

        private static void TryAutomaticAuthoritativeImport()
        {
            if (automaticImportAttempted)
                return;
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += TryAutomaticAuthoritativeImport;
                return;
            }

            automaticImportAttempted = true;
            if (!NeedsAuthoritativeCsvImport())
                return;

            try
            {
                Import(
                    DefaultItemCsvPath,
                    DefaultRecipeCsvPath,
                    DefaultIngredientCsvPath,
                    showDialog: false);
                ValidateImportedAssets();
                Debug.Log("[기획 CSV 자동 임포트] 새 기준 CSV를 프로젝트 에셋에 적용했습니다.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static bool NeedsAuthoritativeCsvImport()
        {
            ItemDef slop = AssetDatabase.LoadAssetAtPath<ItemDef>(
                ItemOutputFolder + "/item_1004.asset");
            LiquorBottleDef slopBottle = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(
                ShelfOutputFolder + "/item_1004.asset");
            CocktailRecipeDef burnhamSour = AssetDatabase.LoadAssetAtPath<CocktailRecipeDef>(
                RecipeOutputFolder + "/rec_1001.asset");

            return slop == null
                || slopBottle == null
                || burnhamSour == null
                || !string.Equals(slop.displayName, "슬롭", StringComparison.Ordinal)
                || slop.price != 450
                || slopBottle.price != 450
                || slopBottle.strangeCoinPrice != 5
                || slopBottle.defaultBottleCount != 6
                || burnhamSour.price != 307
                || burnhamSour.strangeCoinPrice != 3
                || burnhamSour.iceRequirement != IceRequirement.Required
                || burnhamSour.requiredIceCount != -1
                || !slop.overrideBottleClickCollider
                || HasBrokenPlanningBottleLinks();
        }

        private static bool HasBrokenPlanningBottleLinks()
        {
            const int firstItemNumber = 1001;
            const int itemCount = 15;
            var expectedBottles = new List<LiquorBottleDef>(itemCount);

            for (int offset = 0; offset < itemCount; offset++)
            {
                string id = $"item_{firstItemNumber + offset}";
                ItemDef item = AssetDatabase.LoadAssetAtPath<ItemDef>(
                    $"{ItemOutputFolder}/{id}.asset");
                LiquorBottleDef bottle = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(
                    $"{ShelfOutputFolder}/{id}.asset");

                if (item == null
                    || bottle == null
                    || bottle.item != item
                    || !string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(bottle.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                expectedBottles.Add(bottle);
            }

            LiquorBottleCatalog shelfCatalog =
                AssetDatabase.LoadAssetAtPath<LiquorBottleCatalog>(LiquorBottleCatalogPath);
            LiquorShopCatalog shopCatalog =
                AssetDatabase.LoadAssetAtPath<LiquorShopCatalog>(LiquorShopCatalogPath);

            return !CatalogMatches(shelfCatalog != null ? shelfCatalog.bottles : null, expectedBottles)
                || !CatalogMatches(shopCatalog != null ? shopCatalog.bottles : null, expectedBottles);
        }

        private static bool CatalogMatches(
            IReadOnlyList<LiquorBottleDef> actual,
            IReadOnlyList<LiquorBottleDef> expected)
        {
            if (actual == null || expected == null || actual.Count != expected.Count)
                return false;

            for (int i = 0; i < expected.Count; i++)
            {
                if (actual[i] != expected[i])
                    return false;
            }

            return true;
        }

        [MenuItem("Slainte/데이터/기준 CSV 바로 임포트")]
        public static void ImportDefaultDownloadFiles()
        {
            Import(
                DefaultItemCsvPath,
                DefaultRecipeCsvPath,
                DefaultIngredientCsvPath,
                showDialog: !Application.isBatchMode);
        }

        public static void RunCommandLineImport()
        {
            try
            {
                ImportDefaultDownloadFiles();
                ValidateImportedAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Slainte/품질 검증/기획 CSV 에셋 검증")]
        public static void ValidateImportedAssets()
        {
            ItemDefCatalog items = ItemDefCatalog.LoadFromResources("Items", null);
            CocktailRecipeCatalog recipes = CocktailRecipeDataLoader.LoadDefault(items);

            Require(recipes.TryGet("rec_1001", out CocktailRecipe burnhamSour),
                "번햄 사워 레시피를 불러오지 못했습니다.");
            Require(burnhamSour.isOrderable && burnhamSour.ingredients.Count == 4,
                "번햄 사워 배합 또는 주문 가능 상태가 잘못되었습니다.");
            Require(burnhamSour.price == 307 && burnhamSour.strangeCoinPrice == 3,
                $"번햄 사워 가격 연결이 잘못되었습니다: {burnhamSour.price}/{burnhamSour.strangeCoinPrice}");
            Require(burnhamSour.iceRequirement == IceRequirement.Required
                    && burnhamSour.requiredIceCount == -1,
                "번햄 사워 얼음 판정이 개수가 아닌 유무 기준이 아닙니다.");
            Require(Mathf.Abs(burnhamSour.expectedAbvPercent - 15f) < 0.01f,
                $"번햄 사워 계산 도수가 15%가 아닙니다: {burnhamSour.expectedAbvPercent:0.##}%");

            int midVariantCount = 0;
            foreach (CocktailRecipe recipe in recipes.Recipes)
            {
                if (recipe != null
                    && !string.IsNullOrWhiteSpace(recipe.baseRecipeId)
                    && recipe.evaluationGrade == CocktailRecipeEvaluationGrade.Mid)
                    midVariantCount++;
            }
            Require(midVariantCount == 73,
                $"숨은 잔·얼음 Mid 판정 레시피가 73개가 아닙니다: {midVariantCount}개");

            GeneratedCocktailOrder order =
                new CocktailOrderGenerator(recipes, null).GenerateRecipeOrder("rec_1001");
            Require(order != null, "번햄 사워 주문을 만들지 못했습니다.");
            CocktailOrderEvaluator evaluator = new CocktailOrderEvaluator(new CocktailEvaluator(recipes));

            CocktailComposition goodComposition = BuildBurnhamSourComposition(items, "rock", true);
            CocktailOrderEvaluationResult goodResult = evaluator.Evaluate(order, goodComposition);
            Require(OrderEvaluationGrader.Resolve(goodResult, null) == OrderEvaluationGrade.Good,
                "정확한 번햄 사워가 Good으로 판정되지 않았습니다.");

            CocktailComposition midComposition = BuildBurnhamSourComposition(items, "martini", true);
            CocktailOrderEvaluationResult midResult = evaluator.Evaluate(order, midComposition);
            Require(midResult.isSuccess
                && midResult.outcome == CocktailOrderEvaluationOutcome.MidGlass,
                "마티니 잔 변형이 MidGlass로 판정되지 않았습니다.");
            Require(OrderEvaluationGrader.Resolve(midResult, null) == OrderEvaluationGrade.Mid,
                "숨은 변형 레시피가 Mid로 판정되지 않았습니다.");

            ItemDef[] planningItems = Resources.LoadAll<ItemDef>("Items/Planning");
            Require(planningItems.Length == 15,
                $"CSV 활성 재료가 15개가 아닙니다: {planningItems.Length}개");
            Require(recipes.OrderableCount == 21,
                $"주문 가능 레시피가 21개가 아닙니다: {recipes.OrderableCount}개");

            foreach (CocktailRecipe recipe in recipes.OrderableRecipes)
            {
                foreach (CocktailRecipeIngredient ingredient in recipe.ingredients)
                {
                    Require(ingredient.item != null,
                        $"레시피 {recipe.id}의 재료 {ingredient.ingredientId}가 ItemDef에 연결되지 않았습니다.");
                }
            }

            LiquorShopCatalog shopCatalog = LiquorShopCatalog.LoadDefault();
            Require(shopCatalog != null && shopCatalog.bottles != null
                && shopCatalog.bottles.Count == 15,
                $"술장/상점 카탈로그가 CSV 재료 15종과 연결되지 않았습니다: {shopCatalog?.bottles?.Count ?? 0}개");
            foreach (LiquorBottleDef bottle in shopCatalog.bottles)
            {
                Require(bottle != null && bottle.item != null,
                    $"술장 재료 {bottle?.id ?? "<null>"}에 제작용 ItemDef가 직접 연결되지 않았습니다.");
                Require(string.Equals(bottle.id, bottle.item.id, StringComparison.OrdinalIgnoreCase),
                    $"술장 재료 ID와 제작용 ItemDef ID가 다릅니다: {bottle.id}/{bottle.item.id}");
                Require(bottle.DefaultAmount > 0f,
                    $"술장 재료 {bottle.id}의 기본 재고가 0입니다.");
                Require(Mathf.Approximately(bottle.unitVolume, bottle.item.capacityMl),
                    $"술장 재료 {bottle.id}의 병 용량과 ItemDef 용량이 다릅니다.");
                ValidatePlanningBottleGeometry(
                    bottle.item,
                    bottle.GetBarSprite(bottle.item.icon));
            }

            int linkedLegacyBottleCount = 0;
            string[] legacyBottleGuids = AssetDatabase.FindAssets(
                "t:LiquorBottleDef",
                new[] { "Assets/Data/LiquorBottle" });
            for (int i = 0; i < legacyBottleGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(legacyBottleGuids[i]);
                if (path.StartsWith(ShelfOutputFolder + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                LiquorBottleDef legacy = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(path);
                if (legacy == null || string.Equals(
                        legacy.id,
                        "lemon_juice",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Require(legacy.item != null,
                    $"구형 술장 에셋 {path}가 새 ItemDef에 연결되지 않았습니다.");
                Require(string.Equals(
                        legacy.InventoryId,
                        legacy.item.id,
                        StringComparison.OrdinalIgnoreCase),
                    $"구형 술장 에셋 {path}의 재고 ID 연결이 잘못되었습니다.");
                LiquorBottleDef canonical = shopCatalog.bottles.FirstOrDefault(
                    bottle => bottle != null && string.Equals(
                        bottle.id,
                        legacy.InventoryId,
                        StringComparison.OrdinalIgnoreCase));
                Require(canonical != null
                    && legacy.defaultBottleCount == canonical.defaultBottleCount
                    && legacy.bottleCount == canonical.bottleCount
                    && Mathf.Approximately(legacy.unitVolume, canonical.unitVolume)
                    && legacy.price == canonical.price
                    && legacy.strangeCoinPrice == canonical.strangeCoinPrice,
                    $"구형 술장 에셋 {path}의 재고/가격이 기준 CSV와 다릅니다.");
                linkedLegacyBottleCount++;
            }
            int expectedLegacyBottleCount = shopCatalog.bottles.Count;
            Require(linkedLegacyBottleCount == expectedLegacyBottleCount,
                $"새 재료에 연결된 구형 술장 에셋 수가 기준 카탈로그와 다릅니다: "
                + $"{linkedLegacyBottleCount}/{expectedLegacyBottleCount}개");
            LiquorBottleDef tropical = shopCatalog.bottles.FirstOrDefault(
                bottle => bottle != null
                    && string.Equals(bottle.id, "item_1001", StringComparison.OrdinalIgnoreCase));
            Require(tropical != null
                && tropical.price == 500
                && tropical.strangeCoinPrice == 5
                && Mathf.Approximately(tropical.DefaultAmount, 3000f)
                && Mathf.Approximately(tropical.MaxAmount, 6000f),
                "열대 주스의 가격/이상한 동전 가격/기본 3병/최대 6병 연결이 잘못되었습니다.");

            Debug.Log("[기획 CSV 에셋 검증] 통과: 재료·술장 15종, 주문 가능 21종, 숨은 잔·얼음 Mid 73종, 가격/재고/도수/얼음/Good/Mid 판정");
        }

        public static PlanningCsvImportReport Import(
            string itemCsvPath,
            string recipeCsvPath,
            string ingredientCsvPath,
            bool showDialog)
        {
            ValidateSourceFile(itemCsvPath, "아이템 CSV");
            ValidateSourceFile(recipeCsvPath, "레시피 CSV");
            ValidateSourceFile(ingredientCsvPath, "배합 CSV");

            EnsureFolder(ItemOutputFolder);
            EnsureFolder(ShelfOutputFolder);
            EnsureFolder(RecipeOutputFolder);
            EnsureFolder(VariantOutputFolder);

            List<CsvRow> itemRows = ReadCsv(itemCsvPath);
            List<CsvRow> recipeRows = ReadCsv(recipeCsvPath);
            PlanningCsvImportReport report = new PlanningCsvImportReport();
            Dictionary<string, List<IngredientImportRow>> ingredientsByRecipe =
                ReadIngredients(ingredientCsvPath, out Dictionary<string, int> iceCountsByRecipe, report);
            ValidateImportRows(itemRows, recipeRows, ingredientsByRecipe, report);
            if (report.errors.Count > 0)
                throw new InvalidDataException(string.Join("\n", report.errors));

            RekeyExistingGeneratedAssets(itemRows, recipeRows, report);
            if (report.errors.Count > 0)
                throw new InvalidDataException(string.Join("\n", report.errors));
            ArchiveStalePlanningItems(itemRows, report);

            Dictionary<string, ItemDef> importedItems = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, LiquorBottleDef> importedBottles = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, CocktailRecipeDef> importedRecipes = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenItemIds = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenRecipeIds = new(StringComparer.OrdinalIgnoreCase);

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < itemRows.Count; i++)
                {
                    CsvRow row = itemRows[i];
                    string id = First(row, "qt", "ID", "id");
                    string displayName = First(row, "Name", "name");
                    if (!IsUsableRow(id, displayName))
                    {
                        report.skippedItemRows++;
                        continue;
                    }

                    if (!seenItemIds.Add(id))
                    {
                        report.errors.Add($"중복 아이템 ID: {id}");
                        continue;
                    }

                    ItemDef item = UpsertItem(row, id, displayName, report);
                    LiquorBottleDef bottle = UpsertShelfDefinition(row, item, id, displayName, report);
                    ApplySpriteColliderGeometry(
                        item,
                        bottle.GetBarSprite(item.icon),
                        report);
                    importedItems[id] = item;
                    importedBottles[id] = bottle;
                    report.importedItems++;
                }

                LinkLegacyShelfDefinitions(importedItems, importedBottles);

                for (int i = 0; i < recipeRows.Count; i++)
                {
                    CsvRow row = recipeRows[i];
                    string id = First(row, "ID", "id");
                    string displayName = First(row, "Name", "name");
                    if (!IsUsableRow(id, displayName))
                    {
                        report.skippedRecipeRows++;
                        continue;
                    }

                    if (!seenRecipeIds.Add(id))
                    {
                        report.errors.Add($"중복 레시피 ID: {id}");
                        continue;
                    }

                    CocktailRecipeDef recipe = UpsertRecipe(
                        row,
                        id,
                        displayName,
                        ingredientsByRecipe,
                        iceCountsByRecipe,
                        importedItems,
                        report);
                    report.importedRecipes++;
                    importedRecipes[id] = recipe;
                    if (recipe.isOrderable)
                        report.orderableRecipes++;
                }

                report.importedVariants = GenerateEvaluationVariants(
                    importedRecipes,
                    out HashSet<string> generatedVariantIds);
                RemoveStaleGeneratedVariants(generatedVariantIds);
                RebuildLiquorCatalogs(itemRows, importedBottles);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = report.ToKoreanSummary();
            if (report.errors.Count > 0)
                Debug.LogError("[기획 CSV 임포트]\n" + summary);
            else
                Debug.Log("[기획 CSV 임포트]\n" + summary);

            for (int i = 0; i < report.warnings.Count; i++)
                Debug.LogWarning("[기획 CSV 임포트] " + report.warnings[i]);

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    report.errors.Count == 0 ? "CSV 임포트 완료" : "CSV 임포트 오류",
                    summary,
                    "확인");
            }

            if (report.errors.Count > 0 && Application.isBatchMode)
                throw new InvalidDataException(string.Join("\n", report.errors));

            return report;
        }

        private static void RekeyExistingGeneratedAssets(
            List<CsvRow> itemRows,
            List<CsvRow> recipeRows,
            PlanningCsvImportReport report)
        {
            Dictionary<string, string> itemIdsByName = BuildIdsByDisplayName(itemRows, "qt");
            Dictionary<string, string> recipeIdsByName = BuildIdsByDisplayName(recipeRows, "ID");
            RekeyFolderByDisplayName<ItemDef>(ItemOutputFolder, itemIdsByName, report);
            RekeyFolderByDisplayName<LiquorBottleDef>(ShelfOutputFolder, itemIdsByName, report);
            RekeyFolderByDisplayName<CocktailRecipeDef>(RecipeOutputFolder, recipeIdsByName, report);
        }

        private static Dictionary<string, string> BuildIdsByDisplayName(
            IEnumerable<CsvRow> rows,
            string idColumn)
        {
            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (CsvRow row in rows)
            {
                string id = First(row, idColumn, "id");
                string displayName = First(row, "Name", "name");
                if (IsUsableRow(id, displayName))
                    result[displayName] = id;
            }
            return result;
        }

        private static void RekeyFolderByDisplayName<T>(
            string folder,
            IReadOnlyDictionary<string, string> idsByDisplayName,
            PlanningCsvImportReport report)
            where T : ScriptableObject
        {
            List<(string temporaryPath, string targetPath)> moves = new();
            foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(
                        Path.GetDirectoryName(path)?.Replace('\\', '/'),
                        folder,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                string displayName = GetDisplayName(asset);
                if (asset == null
                    || string.IsNullOrWhiteSpace(displayName)
                    || !idsByDisplayName.TryGetValue(displayName, out string targetId))
                    continue;

                string targetPath = $"{folder}/{SanitizeFileName(targetId)}.asset";
                if (string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                string temporaryPath = $"{folder}/__csv_rekey_{guid}.asset";
                string error = AssetDatabase.MoveAsset(path, temporaryPath);
                if (!string.IsNullOrEmpty(error))
                {
                    report.errors.Add($"에셋 임시 이동 실패: {path} -> {temporaryPath}: {error}");
                    continue;
                }
                moves.Add((temporaryPath, targetPath));
            }

            foreach ((string temporaryPath, string targetPath) in moves)
            {
                if (AssetDatabase.LoadMainAssetAtPath(targetPath) != null)
                {
                    report.errors.Add($"새 ID 경로가 이미 사용 중입니다: {targetPath}");
                    continue;
                }

                string error = AssetDatabase.MoveAsset(temporaryPath, targetPath);
                if (!string.IsNullOrEmpty(error))
                    report.errors.Add($"에셋 ID 이동 실패: {temporaryPath} -> {targetPath}: {error}");
                else
                    report.rekeyedAssets++;
            }
        }

        private static string GetDisplayName(ScriptableObject asset)
        {
            return asset switch
            {
                ItemDef item => item.displayName,
                LiquorBottleDef bottle => bottle.displayName,
                CocktailRecipeDef recipe => recipe.displayName,
                _ => string.Empty
            };
        }

        private static void ArchiveStalePlanningItems(
            IEnumerable<CsvRow> itemRows,
            PlanningCsvImportReport report)
        {
            HashSet<string> validIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (CsvRow row in itemRows)
            {
                string id = First(row, "qt", "ID", "id");
                if (IsUsableRow(id, First(row, "Name", "name")))
                    validIds.Add(id);
            }

            EnsureFolder(LegacyItemFolder);
            EnsureFolder(LegacyBottleFolder);
            ArchiveStaleFolder<ItemDef>(ItemOutputFolder, LegacyItemFolder, validIds, report);
            ArchiveStaleFolder<LiquorBottleDef>(ShelfOutputFolder, LegacyBottleFolder, validIds, report);
        }

        private static void ArchiveStaleFolder<T>(
            string sourceFolder,
            string legacyFolder,
            HashSet<string> validIds,
            PlanningCsvImportReport report)
            where T : ScriptableObject
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { sourceFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(
                        Path.GetDirectoryName(path)?.Replace('\\', '/'),
                        sourceFolder,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                string id = asset switch
                {
                    ItemDef item => item.id,
                    LiquorBottleDef bottle => bottle.id,
                    _ => string.Empty
                };
                string pathId = Path.GetFileNameWithoutExtension(path);
                if (validIds.Contains(id) || validIds.Contains(pathId))
                    continue;

                string targetPath = $"{legacyFolder}/{Path.GetFileName(path)}";
                if (AssetDatabase.LoadMainAssetAtPath(targetPath) != null)
                    targetPath = $"{legacyFolder}/{Path.GetFileNameWithoutExtension(path)}_{guid}.asset";
                string error = AssetDatabase.MoveAsset(path, targetPath);
                if (!string.IsNullOrEmpty(error))
                    report.errors.Add($"미사용 에셋 보관 실패: {path}: {error}");
                else
                    report.archivedAssets++;
            }
        }

        private static void RemoveStaleGeneratedVariants(HashSet<string> generatedVariantIds)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:CocktailRecipeDef", new[] { VariantOutputFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(
                        Path.GetDirectoryName(path)?.Replace('\\', '/'),
                        VariantOutputFolder,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                CocktailRecipeDef variant = AssetDatabase.LoadAssetAtPath<CocktailRecipeDef>(path);
                if (variant == null || !generatedVariantIds.Contains(variant.id))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        private static void RebuildLiquorCatalogs(
            IEnumerable<CsvRow> itemRows,
            IReadOnlyDictionary<string, LiquorBottleDef> importedBottles)
        {
            List<LiquorBottleDef> ordered = new();
            foreach (CsvRow row in itemRows)
            {
                string id = First(row, "qt", "ID", "id");
                if (importedBottles.TryGetValue(id, out LiquorBottleDef bottle) && bottle != null)
                    ordered.Add(bottle);
            }

            LiquorBottleCatalog shelfCatalog =
                AssetDatabase.LoadAssetAtPath<LiquorBottleCatalog>(LiquorBottleCatalogPath);
            if (shelfCatalog != null)
            {
                shelfCatalog.bottles = new List<LiquorBottleDef>(ordered);
                EditorUtility.SetDirty(shelfCatalog);
            }

            LiquorShopCatalog shopCatalog =
                AssetDatabase.LoadAssetAtPath<LiquorShopCatalog>(LiquorShopCatalogPath);
            if (shopCatalog != null)
            {
                shopCatalog.bottles = new List<LiquorBottleDef>(ordered);
                EditorUtility.SetDirty(shopCatalog);
            }
        }

        private static ItemDef UpsertItem(
            CsvRow row,
            string id,
            string displayName,
            PlanningCsvImportReport report)
        {
            string assetPath = $"{ItemOutputFolder}/{SanitizeFileName(id)}.asset";
            ItemDef item = AssetDatabase.LoadAssetAtPath<ItemDef>(assetPath);
            bool isNew = item == null;
            if (isNew)
            {
                item = ScriptableObject.CreateInstance<ItemDef>();
                CopyExistingVisualSettings(displayName, id, item);
            }

            item.name = id;
            item.id = id;
            item.displayName = displayName;
            item.englishName = First(row, "Name_eng", "name_eng");
            item.type = ItemType.Bottle;
            item.price = ParseInt(First(row, "가격", "Price", "price"));
            item.tasteTag = ParseTaste(First(row, "맛 Flavor", "맛 Flaver", "taste"));
            item.abvPercent = Mathf.Max(0f, ParseFloat(First(row, "ABV", "abv")));
            item.bottleCategory = ParseCategory(First(row, "대분류", "category"));
            item.liquidType = ParseLiquidType(First(row, "소분류", "subcategory"));
            item.capacityMl = Mathf.Max(0f, ParseFloat(First(row, "Size(ml)", "sizeMl"), 700f));
            item.inheritMixedLiquidColor = string.Equals(id, "item_1013", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "item_1014", StringComparison.OrdinalIgnoreCase);
            item.dragMovesObject = true;

            string iconName = First(row, "IconName", "iconName");
            Sprite icon = FindSprite(iconName);
            if (icon != null)
                item.icon = icon;

            string rgba = First(row, "RGBA", "rgba");
            if (TryParseColor(rgba, out Color color))
                item.liquidColor = color;
            else if (string.IsNullOrWhiteSpace(rgba))
                report.emptyRgbaItems++;
            else
                report.warnings.Add($"아이템 {id}({displayName})의 RGBA 값 '{rgba}'을 해석하지 못했습니다.");

            if (isNew)
                AssetDatabase.CreateAsset(item, assetPath);
            else
                EditorUtility.SetDirty(item);

            return item;
        }

        private static LiquorBottleDef UpsertShelfDefinition(
            CsvRow row,
            ItemDef item,
            string id,
            string displayName,
            PlanningCsvImportReport report)
        {
            string assetPath = $"{ShelfOutputFolder}/{SanitizeFileName(id)}.asset";
            LiquorBottleDef definition = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(assetPath);
            bool isNew = definition == null;
            if (isNew)
                definition = ScriptableObject.CreateInstance<LiquorBottleDef>();

            CopyExistingContextVisuals(displayName, id, definition);
            bool preserveExistingContextVisuals = definition.HasContextVisuals;

            definition.name = id;
            definition.id = id;
            definition.displayName = displayName;
            definition.item = item;

            string iconName = First(row, "IconName", "iconName");
            Sprite shelfSprite = preserveExistingContextVisuals
                ? definition.GetShelfSprite()
                : FindSprite(iconName + "_lid");
            if (shelfSprite == null)
                shelfSprite = FindExistingSprite(
                    displayName,
                    id,
                    d => d.shelfLidSprite != null ? d.shelfLidSprite : d.sprite);
            if (shelfSprite != null)
                definition.shelfLidSprite = shelfSprite;

            Sprite shopSprite = preserveExistingContextVisuals
                ? definition.GetShopSprite()
                : FindSprite(iconName + "_blank");
            if (shopSprite == null)
                shopSprite = FindExistingSprite(
                    displayName,
                    id,
                    d => d.shopBlankSprite != null ? d.shopBlankSprite : d.sprite);
            if (shopSprite != null)
                definition.shopBlankSprite = shopSprite;

            Sprite barSprite = preserveExistingContextVisuals
                ? definition.GetBarSprite()
                : FindSprite(iconName);
            if (barSprite == null)
                barSprite = FindExistingSprite(
                    displayName,
                    id,
                    d => d.barSprite != null ? d.barSprite : d.sprite);
            if (barSprite == null && item != null)
                barSprite = item.icon;
            if (barSprite != null)
            {
                definition.barSprite = barSprite;
                if (item != null && item.icon == null)
                {
                    item.icon = barSprite;
                    EditorUtility.SetDirty(item);
                }
            }
            else if (!string.IsNullOrWhiteSpace(iconName) && item != null && item.icon == null)
            {
                report.warnings.Add($"아이템 {id}({displayName})의 스프라이트 '{iconName}'를 찾지 못했습니다.");
            }

            if (shopSprite != null || shelfSprite != null || barSprite != null)
                definition.useContextImages = true;

            definition.subCategory = LocalizedLabel(First(row, "소분류", "subcategory"));
            definition.unitVolume = item != null ? item.capacityMl : 700f;
            definition.category = ResolveLiquorCategory(First(row, "대분류", "category"));
            definition.price = ParseInt(First(row, "가격", "Price", "price"));
            definition.strangeCoinPrice = ParseInt(
                First(row, "가격_이상한 상점", "strangeCoinPrice"));
            definition.defaultBottleCount = Mathf.Max(
                0,
                ParseInt(First(row, "기본 소지 개수", "defaultBottleCount")));
            if (definition.bottleCount <= 0)
                definition.bottleCount = 6;
            definition.bottleCount = Mathf.Max(definition.bottleCount, definition.defaultBottleCount);
            if (definition.category == null)
                report.errors.Add($"아이템 {id}({displayName})의 상점 대분류를 찾지 못했습니다.");

            if (isNew)
                AssetDatabase.CreateAsset(definition, assetPath);
            else
                EditorUtility.SetDirty(definition);

            return definition;
        }

        private static void LinkLegacyShelfDefinitions(
            IReadOnlyDictionary<string, ItemDef> importedItems,
            IReadOnlyDictionary<string, LiquorBottleDef> importedBottles)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:LiquorBottleDef",
                new[] { "Assets/Data/LiquorBottle" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.StartsWith(ShelfOutputFolder + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                LiquorBottleDef legacy = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(path);
                if (legacy == null
                    || !importedItems.TryGetValue(legacy.InventoryId, out ItemDef item)
                    || item == null)
                {
                    continue;
                }

                legacy.item = item;
                if (importedBottles.TryGetValue(legacy.InventoryId, out LiquorBottleDef canonical)
                    && canonical != null)
                {
                    legacy.subCategory = canonical.subCategory;
                    legacy.bottleCount = canonical.bottleCount;
                    legacy.defaultBottleCount = canonical.defaultBottleCount;
                    legacy.unitVolume = canonical.unitVolume;
                    legacy.category = canonical.category;
                    legacy.price = canonical.price;
                    legacy.strangeCoinPrice = canonical.strangeCoinPrice;
                }
                EditorUtility.SetDirty(legacy);
            }
        }

        private static CocktailRecipeDef UpsertRecipe(
            CsvRow row,
            string id,
            string displayName,
            Dictionary<string, List<IngredientImportRow>> ingredientsByRecipe,
            Dictionary<string, int> iceCountsByRecipe,
            Dictionary<string, ItemDef> importedItems,
            PlanningCsvImportReport report)
        {
            string assetPath = $"{RecipeOutputFolder}/{SanitizeFileName(id)}.asset";
            CocktailRecipeDef recipe = AssetDatabase.LoadAssetAtPath<CocktailRecipeDef>(assetPath);
            bool isNew = recipe == null;
            if (isNew)
                recipe = ScriptableObject.CreateInstance<CocktailRecipeDef>();

            recipe.name = id;
            recipe.id = id;
            recipe.displayName = displayName;
            recipe.englishName = First(row, "Name_eng", "name_eng");
            recipe.price = ParseInt(First(row, "가격", "Price", "price"));
            recipe.strangeCoinPrice = ParseInt(
                First(row, "가격_이상한 상점", "strangeCoinPrice"));
            recipe.appearsInRecipeBook = true;
            recipe.baseRecipeId = string.Empty;
            recipe.evaluationGrade = CocktailRecipeEvaluationGrade.Good;
            recipe.toleranceMl = 10f;
            recipe.allowExtraIngredients = false;
            recipe.glassId = ParseGlass(First(row, "잔 Glass", "glass"));
            recipe.iceRequirement = ParseIce(First(row, "얼음 유무 Ice", "ice"));
            recipe.requiredIceCount = recipe.iceRequirement == IceRequirement.None ? 0 : -1;
            recipe.requiredTechnique = ParseTechnique(First(row, "제작 방식 Skill", "technique"));
            recipe.shakeIceRequirement =
                (recipe.requiredTechnique & CocktailTechnique.Shake) != 0
                    ? recipe.iceRequirement
                    : IceRequirement.Any;

            string abv = First(row, "ABV", "abv");
            recipe.abvOverridePercent = string.IsNullOrWhiteSpace(abv) ? -1f : ParseFloat(abv, -1f);

            string description = First(row, "설명", "description");
            if (!string.IsNullOrWhiteSpace(description))
                recipe.description = description;

            ReplaceTags(recipe.ingredientPropertyTags,
                First(row, "재료 속성 1"),
                First(row, "재료 속성 2"),
                First(row, "재료 속성 3"));
            ReplaceTags(recipe.moodTags,
                First(row, "분위기 Mood 1"),
                First(row, "분위기 Mood 2"));
            ReplaceTags(recipe.tasteTags,
                First(row, "맛 Flaver 1", "맛 Flavor 1"),
                First(row, "맛 Flaver 2", "맛 Flavor 2"),
                First(row, "맛 Flaver 3", "맛 Flavor 3"));

            if (ingredientsByRecipe.TryGetValue(id, out List<IngredientImportRow> ingredientRows))
            {
                recipe.ingredients.Clear();
                for (int i = 0; i < ingredientRows.Count; i++)
                {
                    IngredientImportRow ingredient = ingredientRows[i];
                    recipe.ingredients.Add(new CocktailRecipeIngredientDef
                    {
                        itemId = ingredient.itemId,
                        targetMl = ingredient.targetMl,
                        toleranceMl = ingredient.toleranceMl
                    });

                    if (!importedItems.ContainsKey(ingredient.itemId)
                        && !AssetExistsWithItemId(ingredient.itemId))
                    {
                        report.errors.Add($"레시피 {id}가 없는 아이템 ID {ingredient.itemId}를 참조합니다.");
                    }
                }
            }

            float totalMl = recipe.ingredients.Sum(ingredient => Mathf.Max(0f, ingredient.targetMl));
            recipe.minTotalMl = totalMl > 0f ? Mathf.Max(0f, totalMl - recipe.toleranceMl) : 0f;
            recipe.maxTotalMl = totalMl > 0f ? totalMl + recipe.toleranceMl : 0f;
            recipe.isOrderable = recipe.ingredients.Count > 0;
            if (!recipe.isOrderable)
                report.warnings.Add($"레시피 {id}({displayName})는 배합 데이터가 없어 주문 대상에서 제외했습니다.");

            if (isNew)
                AssetDatabase.CreateAsset(recipe, assetPath);
            else
                EditorUtility.SetDirty(recipe);

            return recipe;
        }

        private static int GenerateEvaluationVariants(
            Dictionary<string, CocktailRecipeDef> importedRecipes,
            out HashSet<string> generatedVariantIds)
        {
            generatedVariantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            string[] glasses = { "rock", "highball", "hurricane", "martini" };

            for (int number = 1001; number <= 1007; number++)
            {
                string recipeId = $"rec_{number}";
                if (!importedRecipes.TryGetValue(recipeId, out CocktailRecipeDef source)
                    || !source.isOrderable)
                    continue;

                for (int glassIndex = 0; glassIndex < glasses.Length; glassIndex++)
                {
                    for (int iceIndex = 0; iceIndex < 2; iceIndex++)
                    {
                        IceRequirement ice = iceIndex == 0
                            ? IceRequirement.None
                            : IceRequirement.Required;
                        string glass = glasses[glassIndex];
                        if (string.Equals(source.glassId, glass, StringComparison.OrdinalIgnoreCase)
                            && source.iceRequirement == ice)
                            continue;

                        string iceId = ice == IceRequirement.Required ? "ice" : "no_ice";
                        UpsertVariant(
                            source,
                            $"{source.id}__mid_glass_{glass}_{iceId}",
                            $"잔 {glass}, {(ice == IceRequirement.Required ? "얼음 있음" : "얼음 없음")}",
                            variant =>
                            {
                                variant.glassId = glass;
                                variant.iceRequirement = ice;
                                variant.requiredIceCount = ice == IceRequirement.None ? 0 : -1;
                            },
                            generatedVariantIds);
                        count++;
                    }
                }

            }

            for (int number = 1014; number <= 1021; number++)
            {
                string recipeId = $"rec_{number}";
                if (!importedRecipes.TryGetValue(recipeId, out CocktailRecipeDef source)
                    || !source.isOrderable)
                    continue;

                for (int glassIndex = 0; glassIndex < glasses.Length; glassIndex++)
                {
                    string glass = glasses[glassIndex];
                    if (string.Equals(source.glassId, glass, StringComparison.OrdinalIgnoreCase))
                        continue;

                    UpsertVariant(
                        source,
                        $"{source.id}__mid_glass_{glass}",
                        $"잔 {glass}",
                        variant => variant.glassId = glass,
                        generatedVariantIds);
                    count++;
                }
            }

            return count;
        }

        private static void UpsertVariant(
            CocktailRecipeDef source,
            string id,
            string variationLabel,
            Action<CocktailRecipeDef> modify,
            ISet<string> generatedVariantIds)
        {
            generatedVariantIds?.Add(id);
            string assetPath = $"{VariantOutputFolder}/{SanitizeFileName(id)}.asset";
            CocktailRecipeDef variant = AssetDatabase.LoadAssetAtPath<CocktailRecipeDef>(assetPath);
            bool isNew = variant == null;
            if (isNew)
                variant = ScriptableObject.CreateInstance<CocktailRecipeDef>();

            variant.name = id;
            variant.id = id;
            variant.displayName = $"{source.displayName} (Mid: {variationLabel})";
            variant.englishName = source.englishName;
            variant.price = source.price;
            variant.strangeCoinPrice = source.strangeCoinPrice;
            variant.isOrderable = false;
            variant.appearsInRecipeBook = false;
            variant.baseRecipeId = source.id;
            variant.evaluationGrade = CocktailRecipeEvaluationGrade.Mid;
            variant.toleranceMl = source.toleranceMl;
            variant.allowExtraIngredients = source.allowExtraIngredients;
            variant.glassId = source.glassId;
            variant.iceRequirement = source.iceRequirement;
            variant.requiredIceCount = source.iceRequirement == IceRequirement.None ? 0 : -1;
            variant.shakeIceRequirement = source.shakeIceRequirement;
            variant.requiredTechnique = source.requiredTechnique;
            variant.abvOverridePercent = source.abvOverridePercent;
            variant.ingredientPropertyTags = new List<string>(source.ingredientPropertyTags);
            variant.tasteTags = new List<string>(source.tasteTags);
            variant.moodTags = new List<string>(source.moodTags);
            variant.ingredients = CloneIngredients(source.ingredients);

            modify?.Invoke(variant);
            RecalculateTotalRange(variant);

            if (isNew)
                AssetDatabase.CreateAsset(variant, assetPath);
            else
                EditorUtility.SetDirty(variant);
        }

        private static List<CocktailRecipeIngredientDef> CloneIngredients(
            IEnumerable<CocktailRecipeIngredientDef> source)
        {
            List<CocktailRecipeIngredientDef> result = new();
            if (source == null)
                return result;

            foreach (CocktailRecipeIngredientDef ingredient in source)
            {
                if (ingredient == null)
                    continue;
                result.Add(new CocktailRecipeIngredientDef
                {
                    itemId = ingredient.itemId,
                    targetMl = ingredient.targetMl,
                    toleranceMl = ingredient.toleranceMl
                });
            }

            return result;
        }

        private static void RecalculateTotalRange(CocktailRecipeDef recipe)
        {
            float totalMl = recipe.ingredients.Sum(ingredient =>
                ingredient != null ? Mathf.Max(0f, ingredient.targetMl) : 0f);
            recipe.minTotalMl = totalMl > 0f ? Mathf.Max(0f, totalMl - recipe.toleranceMl) : 0f;
            recipe.maxTotalMl = totalMl > 0f ? totalMl + recipe.toleranceMl : 0f;
        }

        private static Dictionary<string, List<IngredientImportRow>> ReadIngredients(
            string path,
            out Dictionary<string, int> iceCountsByRecipe,
            PlanningCsvImportReport report)
        {
            Dictionary<string, List<IngredientImportRow>> result = new(StringComparer.OrdinalIgnoreCase);
            iceCountsByRecipe = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<CsvRow> rows = ReadCsv(path);
            for (int i = 0; i < rows.Count; i++)
            {
                CsvRow row = rows[i];
                string recipeId = First(row, "recipeId", "RecipeID", "ID");
                if (string.IsNullOrWhiteSpace(recipeId))
                    continue;

                string iceCountText = First(row, "얼음 개수", "iceCount");
                if (!string.IsNullOrWhiteSpace(iceCountText))
                {
                    int iceCount = iceCountText.Equals("X", StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : ParseInt(iceCountText, -1);
                    if (iceCount < 0)
                        report.errors.Add($"레시피 {recipeId}의 얼음 개수 '{iceCountText}'를 해석하지 못했습니다.");
                    else if (!iceCountsByRecipe.TryAdd(recipeId, iceCount))
                        report.errors.Add($"중복 레시피 배합 행: {recipeId}");
                }

                string itemId = First(row, "itemId", "ingredientId", "ItemID");
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    AddIngredientImportRow(
                        result,
                        recipeId,
                        ExtractLeadingId(itemId),
                        First(row, "targetMl", "amountMl"),
                        First(row, "toleranceMl"),
                        report);
                    continue;
                }

                for (int slot = 1; slot <= 4; slot++)
                {
                    string ingredient = First(row, $"재료{slot}");
                    if (string.IsNullOrWhiteSpace(ingredient))
                        continue;

                    AddIngredientImportRow(
                        result,
                        recipeId,
                        ExtractLeadingId(ingredient),
                        First(row, $"재료{slot} 용량"),
                        string.Empty,
                        report);
                }
            }

            return result;
        }

        private static void AddIngredientImportRow(
            Dictionary<string, List<IngredientImportRow>> result,
            string recipeId,
            string itemId,
            string amountText,
            string toleranceText,
            PlanningCsvImportReport report)
        {
            if (string.IsNullOrWhiteSpace(recipeId) || string.IsNullOrWhiteSpace(itemId))
                return;

            float targetMl = ParseFloat(amountText, -1f);
            if (targetMl <= 0f)
            {
                report.errors.Add($"레시피 {recipeId}의 재료 {itemId} 용량 '{amountText}'가 올바르지 않습니다.");
                return;
            }

            if (!result.TryGetValue(recipeId, out List<IngredientImportRow> ingredients))
            {
                ingredients = new List<IngredientImportRow>();
                result[recipeId] = ingredients;
            }

            ingredients.Add(new IngredientImportRow
            {
                itemId = itemId,
                targetMl = targetMl,
                toleranceMl = Mathf.Max(0f, ParseFloat(toleranceText, 10f))
            });
            report.importedIngredientRows++;
        }

        private static string ExtractLeadingId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            int separator = trimmed.IndexOfAny(new[] { ' ', '\t' });
            return separator > 0 ? trimmed.Substring(0, separator) : trimmed;
        }

        private static void ValidateImportRows(
            List<CsvRow> itemRows,
            List<CsvRow> recipeRows,
            Dictionary<string, List<IngredientImportRow>> ingredientsByRecipe,
            PlanningCsvImportReport report)
        {
            HashSet<string> itemIds = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> recipeIds = new(StringComparer.OrdinalIgnoreCase);

            foreach (CsvRow row in itemRows)
            {
                string id = First(row, "qt", "ID", "id");
                string displayName = First(row, "Name", "name");
                if (!IsUsableRow(id, displayName))
                    continue;
                if (!itemIds.Add(id))
                    report.errors.Add($"중복 아이템 ID: {id}");
                ValidateNonNegativePrice(row, id, "가격", report);
                ValidateNonNegativePrice(row, id, "가격_이상한 상점", report);
            }

            foreach (CsvRow row in recipeRows)
            {
                string id = First(row, "ID", "id");
                string displayName = First(row, "Name", "name");
                if (!IsUsableRow(id, displayName))
                    continue;
                if (!recipeIds.Add(id))
                    report.errors.Add($"중복 레시피 ID: {id}");
                ValidateNonNegativePrice(row, id, "가격", report);
                ValidateNonNegativePrice(row, id, "가격_이상한 상점", report);
                if (!ingredientsByRecipe.ContainsKey(id))
                    report.errors.Add($"레시피 {id}({displayName})의 배합 데이터가 없습니다.");
            }

            foreach (KeyValuePair<string, List<IngredientImportRow>> pair in ingredientsByRecipe)
            {
                if (!recipeIds.Contains(pair.Key))
                    report.errors.Add($"배합 데이터가 없는 레시피 ID를 참조합니다: {pair.Key}");
                foreach (IngredientImportRow ingredient in pair.Value)
                {
                    if (!itemIds.Contains(ingredient.itemId))
                        report.errors.Add($"레시피 {pair.Key}가 없는 아이템 ID {ingredient.itemId}를 참조합니다.");
                }
            }
        }

        private static void ValidateNonNegativePrice(
            CsvRow row,
            string id,
            string column,
            PlanningCsvImportReport report)
        {
            string value = First(row, column);
            if (!TryParseInt(value, out int parsed) || parsed < 0)
                report.errors.Add($"{id}의 {column} 값 '{value}'가 0 이상의 정수가 아닙니다.");
        }

        private static CocktailComposition BuildBurnhamSourComposition(
            ItemDefCatalog items,
            string glassId,
            bool hasIce)
        {
            CocktailComposition composition = new CocktailComposition();
            AddItem(composition, items, "item_1002", 15f);
            AddItem(composition, items, "item_1004", 30f);
            AddItem(composition, items, "item_1003", 15f);
            AddItem(composition, items, "item_1011", 30f);
            composition.SetServingStyle(glassId, hasIce ? 3 : 0);
            composition.RecordTechnique(CocktailTechnique.Shake);
            composition.RecordShakenWithIce(hasIce);
            return composition;
        }

        private static void AddItem(
            CocktailComposition composition,
            ItemDefCatalog items,
            string itemId,
            float volumeMl)
        {
            Require(items.TryGet(itemId, out ItemDef item) && item != null,
                $"검증용 아이템 {itemId}를 불러오지 못했습니다.");
            composition.Add(item, volumeMl);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidDataException(message);
        }

        private static List<CsvRow> ReadCsv(string path)
        {
            string text = File.ReadAllText(path);
            return CsvTable.Parse(text.TrimStart('\uFEFF'));
        }

        private static string ProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static void ValidateSourceFile(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException($"{label}를 찾을 수 없습니다.", path);
        }

        private static bool IsUsableRow(string id, string displayName)
        {
            return !string.IsNullOrWhiteSpace(id)
                && !string.IsNullOrWhiteSpace(displayName)
                && !displayName.Contains("임시 비워둠", StringComparison.OrdinalIgnoreCase);
        }

        private static string First(CsvRow row, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string value = row.Get(keys[i]);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static string LocalizedLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            int separator = value.IndexOf('_');
            return (separator >= 0 ? value.Substring(0, separator) : value).Trim();
        }

        private static void ReplaceTags(List<string> target, params string[] values)
        {
            target.Clear();
            for (int i = 0; i < values.Length; i++)
            {
                string tag = LocalizedLabel(values[i]);
                if (!string.IsNullOrWhiteSpace(tag) && !target.Contains(tag))
                    target.Add(tag);
            }
        }

        private static TasteTag ParseTaste(string value)
        {
            string label = LocalizedLabel(value);
            if (label.Contains("새콤달콤")) return TasteTag.SweetSour;
            if (label.Contains("달큰")) return TasteTag.RichSweet;
            if (label.Contains("달콤")) return TasteTag.Sweet;
            if (label.Contains("씁쓸")) return TasteTag.Bitter;
            if (label.Contains("새큼")) return TasteTag.Sour;
            return TasteTag.Neutral;
        }

        private static BottleCategory ParseCategory(string value)
        {
            string label = LocalizedLabel(value);
            if (label.Contains("스피릿")) return BottleCategory.Spirit;
            if (label.Contains("리큐르")) return BottleCategory.Liqueur;
            if (label.Contains("시럽")) return BottleCategory.Syrup;
            if (label.Contains("주스")) return BottleCategory.Juice;
            if (label.Contains("논알콜")) return BottleCategory.NonAlcohol;
            return BottleCategory.Other;
        }

        private static LiquorCategoryDef ResolveLiquorCategory(string value)
        {
            string label = LocalizedLabel(value);
            string categoryId = label.Contains("스피릿") ? "spirit"
                : label.Contains("리큐르") ? "liqueur"
                : label.Contains("시럽") ? "syrup"
                : label.Contains("논알콜") ? "non_alcohol"
                : label.Contains("가루") ? "powder"
                : "etc";

            foreach (string guid in AssetDatabase.FindAssets(
                         "t:LiquorCategoryDef",
                         new[] { "Assets/Data/LiquorCategory" }))
            {
                LiquorCategoryDef category = AssetDatabase.LoadAssetAtPath<LiquorCategoryDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (category != null
                    && string.Equals(category.id, categoryId, StringComparison.OrdinalIgnoreCase))
                    return category;
            }
            return null;
        }

        private static BottleLiquidType ParseLiquidType(string value)
        {
            string label = LocalizedLabel(value);
            if (label.Contains("보드카")) return BottleLiquidType.Vodka;
            if (label.Contains("위스키")) return BottleLiquidType.Whisky;
            if (label.Contains("과일 주스")) return BottleLiquidType.Juice;
            if (label.Contains("시럽")) return BottleLiquidType.Syrup;
            return BottleLiquidType.Other;
        }

        private static CocktailTechnique ParseTechnique(string value)
        {
            string label = LocalizedLabel(value);
            if (label.Contains("셰이") || label.Contains("쉐이")) return CocktailTechnique.Shake;
            if (label.Contains("스터")) return CocktailTechnique.Stir;
            if (label.Contains("빌드")) return CocktailTechnique.Build;
            return CocktailTechnique.None;
        }

        private static IceRequirement ParseIce(string value)
        {
            string normalized = value != null ? value.Trim() : string.Empty;
            if (normalized.Equals("O", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Y", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                return IceRequirement.Required;
            if (normalized.Equals("X", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("N", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                return IceRequirement.None;
            return IceRequirement.Any;
        }

        private static string ParseGlass(string value)
        {
            string label = LocalizedLabel(value);
            if (label.Contains("락")) return "rock";
            if (label.Contains("하이볼")) return "highball";
            if (label.Contains("허리케인")) return "hurricane";
            if (label.Contains("마티니")) return "martini";
            return string.Empty;
        }

        private static int ParseInt(string value, int fallback = 0)
        {
            return TryParseInt(value, out int parsed) ? parsed : fallback;
        }

        private static bool TryParseInt(string value, out int parsed)
        {
            parsed = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            const NumberStyles styles = NumberStyles.Number;
            if (!decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out decimal number)
                && !decimal.TryParse(value, styles, CultureInfo.CurrentCulture, out number))
                return false;
            if (number != decimal.Truncate(number)
                || number < int.MinValue
                || number > int.MaxValue)
                return false;

            parsed = decimal.ToInt32(number);
            return true;
        }

        private static float ParseFloat(string value, float fallback = 0f)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return parsed;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                return parsed;
            return fallback;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            if (!trimmed.StartsWith("#", StringComparison.Ordinal))
                trimmed = "#" + trimmed;
            if (ColorUtility.TryParseHtmlString(trimmed, out color))
                return true;

            string[] components = value.Split(new[] { ',', '|', ';', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (components.Length != 3 && components.Length != 4)
                return false;

            float[] numbers = new float[4] { 0f, 0f, 0f, 1f };
            for (int i = 0; i < components.Length; i++)
            {
                if (!float.TryParse(components[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                    return false;
            }

            bool byteRange = numbers.Take(components.Length).Any(component => component > 1f);
            float scale = byteRange ? 255f : 1f;
            color = new Color(
                Mathf.Clamp01(numbers[0] / scale),
                Mathf.Clamp01(numbers[1] / scale),
                Mathf.Clamp01(numbers[2] / scale),
                Mathf.Clamp01(numbers[3] / scale));
            return true;
        }

        private static Sprite FindSprite(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName))
                return null;

            string[] guids = AssetDatabase.FindAssets($"{iconName} t:Sprite");
            Sprite fallback = null;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                    continue;
                if (string.Equals(sprite.name, iconName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileNameWithoutExtension(path), iconName, StringComparison.OrdinalIgnoreCase))
                    return sprite;
                fallback ??= sprite;
            }

            return fallback;
        }

        private static void ApplySpriteColliderGeometry(
            ItemDef item,
            Sprite barSprite,
            PlanningCsvImportReport report)
        {
            if (item == null || item.overrideBottleGeometry)
                return;

            if (barSprite == null)
            {
                report.warnings.Add($"아이템 {item.id}의 barSprite가 없어 클릭 영역을 계산하지 못했습니다.");
                return;
            }

            if (!BottleSpriteGeometry.TryCalculate(
                    barSprite,
                    out Vector2 centerNormalized,
                    out Vector2 sizeNormalized,
                    out string failure))
            {
                report.warnings.Add($"아이템 {item.id}의 클릭 영역 계산 실패: {failure}");
                return;
            }

            item.overrideBottleClickCollider = true;
            item.colliderCenterNormalized = centerNormalized;
            item.colliderSizeNormalized = sizeNormalized;
            EditorUtility.SetDirty(item);
        }

        private static void ValidatePlanningBottleGeometry(ItemDef item, Sprite barSprite)
        {
            Require(item != null && item.type == ItemType.Bottle,
                $"Planning ItemDef가 병 데이터가 아닙니다: {item?.name ?? "<null>"}");
            Require(barSprite != null,
                $"Planning 병 {item.id}의 barSprite가 없습니다.");
            Require(item.overrideBottleGeometry || item.overrideBottleClickCollider,
                $"Planning 병 {item.id}에 스프라이트별 클릭 geometry가 없습니다.");

            Vector2 center = item.colliderCenterNormalized;
            Vector2 size = item.colliderSizeNormalized;
            Require(size.x > 0f && size.y > 0f && size.x <= 1f && size.y <= 1f,
                $"Planning 병 {item.id}의 클릭 geometry 크기가 잘못되었습니다: {size}");
            Require(center.x - size.x * 0.5f >= -0.0001f
                && center.x + size.x * 0.5f <= 1.0001f
                && center.y - size.y * 0.5f >= -0.0001f
                && center.y + size.y * 0.5f <= 1.0001f,
                $"Planning 병 {item.id}의 클릭 geometry가 스프라이트 범위를 벗어납니다.");

            if (item.overrideBottleLiquidSpawn)
            {
                Vector2 mouth = item.liquidSpawnNormalized;
                Require(mouth.x >= 0f && mouth.x <= 1f
                    && mouth.y >= 0f && mouth.y <= 1f,
                    $"Planning 병 {item.id}의 입구 좌표가 스프라이트 범위를 벗어납니다: {mouth}");
                Require(item.liquidSpawnOutwardPixels >= 0f,
                    $"Planning 병 {item.id}의 입구 바깥쪽 오프셋이 음수입니다: "
                    + item.liquidSpawnOutwardPixels);
            }

            if (item.overrideBottleGeometry)
                return;

            Require(BottleSpriteGeometry.TryCalculate(
                    barSprite,
                    out Vector2 expectedCenter,
                    out Vector2 expectedSize,
                    out string failure),
                $"Planning 병 {item.id}의 스프라이트 geometry를 검증하지 못했습니다: {failure}");
            Require(Vector2.Distance(center, expectedCenter) <= 0.0005f
                && Vector2.Distance(size, expectedSize) <= 0.0005f,
                $"Planning 병 {item.id}의 클릭 geometry가 현재 barSprite 알파 영역과 다릅니다. "
                + $"actual={center}/{size}, expected={expectedCenter}/{expectedSize}");
        }

        private static void CopyExistingVisualSettings(string displayName, string importedId, ItemDef destination)
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDef", new[] { "Assets/Resources/Items" });
            for (int i = 0; i < guids.Length; i++)
            {
                ItemDef source = AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (source == null
                    || string.Equals(source.id, importedId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(source.displayName, displayName, StringComparison.OrdinalIgnoreCase))
                    continue;

                destination.icon = source.icon;
                destination.liquidColor = source.liquidColor;
                destination.density = source.density;
                destination.servingTemperatureC = source.servingTemperatureC;
                destination.overrideBottleGeometry = source.overrideBottleGeometry;
                destination.overrideBottleLiquidSpawn = source.overrideBottleLiquidSpawn;
                destination.overrideBottleClickCollider = source.overrideBottleClickCollider;
                destination.liquidSpawnNormalized = source.liquidSpawnNormalized;
                destination.liquidSpawnOutwardPixels = source.liquidSpawnOutwardPixels;
                destination.colliderCenterNormalized = source.colliderCenterNormalized;
                destination.colliderSizeNormalized = source.colliderSizeNormalized;
                return;
            }
        }

        private static Sprite FindExistingSprite(string displayName, string importedId, Func<LiquorBottleDef, Sprite> selector)
        {
            string[] guids = AssetDatabase.FindAssets("t:LiquorBottleDef", new[] { "Assets/Data/LiquorBottle" });
            for (int i = 0; i < guids.Length; i++)
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (sourcePath.StartsWith(ShelfOutputFolder + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                LiquorBottleDef definition = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(sourcePath);
                if (definition == null
                    || string.Equals(definition.id, importedId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(definition.displayName, displayName, StringComparison.OrdinalIgnoreCase))
                    continue;

                Sprite existing = selector(definition);
                if (existing != null)
                    return existing;
            }

            return null;
        }

        private static void CopyExistingContextVisuals(
            string displayName,
            string importedId,
            LiquorBottleDef destination)
        {
            if (destination == null
                || destination.HasContextVisuals
                || string.IsNullOrWhiteSpace(displayName))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets(
                "t:LiquorBottleDef",
                new[] { "Assets/Data/LiquorBottle" });
            for (int i = 0; i < guids.Length; i++)
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (sourcePath.StartsWith(ShelfOutputFolder + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                LiquorBottleDef source = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(sourcePath);
                if (source == null
                    || !source.HasContextVisuals
                    || string.Equals(source.id, importedId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(source.displayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                destination.useContextImages = source.useContextImages;
                destination.shopBlankSprite = source.shopBlankSprite;
                destination.shelfLidSprite = source.shelfLidSprite;
                destination.barSprite = source.barSprite;
                return;
            }
        }

        private static bool AssetExistsWithItemId(string itemId)
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDef", new[] { "Assets/Resources/Items" });
            for (int i = 0; i < guids.Length; i++)
            {
                ItemDef item = AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (item != null && string.Equals(item.id, itemId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string SanitizeFileName(string value)
        {
            string sanitized = value;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                sanitized = sanitized.Replace(invalid, '_');
            return sanitized;
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private sealed class IngredientImportRow
        {
            public string itemId;
            public float targetMl;
            public float toleranceMl;
        }
    }

    internal static class BottleSpriteGeometry
    {
        private const byte AlphaThreshold = 3;
        private const int PaddingPixels = 2;

        public static bool TryCalculate(
            Sprite sprite,
            out Vector2 centerNormalized,
            out Vector2 sizeNormalized,
            out string failure)
        {
            centerNormalized = new Vector2(0.5f, 0.5f);
            sizeNormalized = Vector2.one;
            failure = string.Empty;
            if (sprite == null)
            {
                failure = "Sprite is null.";
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(sprite);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string sourcePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(sourcePath))
            {
                failure = $"Source image was not found: {assetPath}";
                return false;
            }

            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(sourcePath), false))
                {
                    failure = $"Source image could not be decoded: {assetPath}";
                    return false;
                }

                Rect spriteRect = sprite.rect;
                int rectMinX = Mathf.Clamp(Mathf.FloorToInt(spriteRect.xMin), 0, source.width - 1);
                int rectMinY = Mathf.Clamp(Mathf.FloorToInt(spriteRect.yMin), 0, source.height - 1);
                int rectMaxX = Mathf.Clamp(Mathf.CeilToInt(spriteRect.xMax) - 1, 0, source.width - 1);
                int rectMaxY = Mathf.Clamp(Mathf.CeilToInt(spriteRect.yMax) - 1, 0, source.height - 1);
                if (rectMaxX < rectMinX || rectMaxY < rectMinY)
                {
                    failure = $"Sprite rect is empty: {spriteRect}";
                    return false;
                }

                Color32[] pixels = source.GetPixels32();
                int minX = rectMaxX + 1;
                int minY = rectMaxY + 1;
                int maxX = rectMinX - 1;
                int maxY = rectMinY - 1;
                for (int y = rectMinY; y <= rectMaxY; y++)
                {
                    int row = y * source.width;
                    for (int x = rectMinX; x <= rectMaxX; x++)
                    {
                        if (pixels[row + x].a <= AlphaThreshold)
                            continue;

                        minX = Mathf.Min(minX, x);
                        minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x);
                        maxY = Mathf.Max(maxY, y);
                    }
                }

                if (maxX < minX || maxY < minY)
                {
                    failure = $"Sprite has no visible pixels above alpha {AlphaThreshold}: {assetPath}";
                    return false;
                }

                minX = Mathf.Max(rectMinX, minX - PaddingPixels);
                minY = Mathf.Max(rectMinY, minY - PaddingPixels);
                maxX = Mathf.Min(rectMaxX, maxX + PaddingPixels);
                maxY = Mathf.Min(rectMaxY, maxY + PaddingPixels);

                float normalizedMinX = (minX - spriteRect.xMin) / spriteRect.width;
                float normalizedMinY = (minY - spriteRect.yMin) / spriteRect.height;
                float normalizedMaxX = (maxX + 1f - spriteRect.xMin) / spriteRect.width;
                float normalizedMaxY = (maxY + 1f - spriteRect.yMin) / spriteRect.height;
                centerNormalized = new Vector2(
                    (normalizedMinX + normalizedMaxX) * 0.5f,
                    (normalizedMinY + normalizedMaxY) * 0.5f);
                sizeNormalized = new Vector2(
                    normalizedMaxX - normalizedMinX,
                    normalizedMaxY - normalizedMinY);
                return sizeNormalized.x > 0f && sizeNormalized.y > 0f;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }

    public sealed class PlanningCsvImportReport
    {
        public int importedItems;
        public int importedRecipes;
        public int importedIngredientRows;
        public int orderableRecipes;
        public int importedVariants;
        public int rekeyedAssets;
        public int archivedAssets;
        public int skippedItemRows;
        public int skippedRecipeRows;
        public int emptyRgbaItems;
        public readonly List<string> warnings = new();
        public readonly List<string> errors = new();

        public string ToKoreanSummary()
        {
            return $"아이템 {importedItems}개, 레시피 {importedRecipes}개를 갱신했습니다.\n"
                + $"배합 행: {importedIngredientRows}개\n"
                + $"주문 가능 레시피: {orderableRecipes}개\n"
                + $"숨은 Mid 판정 레시피: {importedVariants}개\n"
                + $"ID 재키: {rekeyedAssets}개 / 보관: {archivedAssets}개\n"
                + $"건너뛴 행: 아이템 {skippedItemRows}개 / 레시피 {skippedRecipeRows}개\n"
                + $"RGBA 미입력 아이템: {emptyRgbaItems}개\n"
                + $"경고 {warnings.Count}개 / 오류 {errors.Count}개";
        }
    }
}
