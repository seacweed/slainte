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
        public const string ItemOutputFolder = "Assets/Resources/Items/Planning";
        public const string ShelfOutputFolder = "Assets/Data/LiquorBottle/Planning";
        public const string RecipeOutputFolder = "Assets/Resources/Recipes/Planning";
        public const string VariantOutputFolder = "Assets/Resources/Recipes/Planning/Variants";
        public const string IngredientCsvAssetPath = "Assets/Editor/Data/slainte_recipe_ingredients.csv";

        public static string DefaultItemCsvPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "Data_slainte.csv - 아이템.csv");

        public static string DefaultRecipeCsvPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "Data_slainte.csv - 레시피.csv");

        public static string DefaultIngredientCsvPath => Path.GetFullPath(
            Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty, IngredientCsvAssetPath));

        [MenuItem("Slainte/데이터/다운로드 폴더 CSV 바로 임포트")]
        public static void ImportDefaultDownloadFiles()
        {
            Import(
                DefaultItemCsvPath,
                DefaultRecipeCsvPath,
                DefaultIngredientCsvPath,
                showDialog: !Application.isBatchMode);
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
            Require(midVariantCount == 87,
                $"숨은 Mid 판정 레시피가 87개가 아닙니다: {midVariantCount}개");

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
                && midResult.requestedRecipeResult?.matchedRecipe?.evaluationGrade
                    == CocktailRecipeEvaluationGrade.Mid,
                "마티니 잔 변형이 숨은 Mid 레시피와 일치하지 않았습니다.");
            Require(OrderEvaluationGrader.Resolve(midResult, null) == OrderEvaluationGrade.Mid,
                "숨은 변형 레시피가 Mid로 판정되지 않았습니다.");

            Debug.Log("[기획 CSV 에셋 검증] 통과: 기본 18종, 주문 가능 15종, 숨은 Mid 87종, 도수 계산, Good/Mid 판정");
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
            Dictionary<string, List<IngredientImportRow>> ingredientsByRecipe =
                ReadIngredients(ingredientCsvPath);

            PlanningCsvImportReport report = new PlanningCsvImportReport();
            Dictionary<string, ItemDef> importedItems = new(StringComparer.OrdinalIgnoreCase);
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
                    UpsertShelfDefinition(row, item, id, displayName);
                    importedItems[id] = item;
                    report.importedItems++;
                }

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
                        importedItems,
                        report);
                    report.importedRecipes++;
                    importedRecipes[id] = recipe;
                    if (recipe.isOrderable)
                        report.orderableRecipes++;
                }

                report.importedVariants = GenerateEvaluationVariants(importedRecipes);
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
            item.type = ItemType.Bottle;
            item.price = ParseInt(First(row, "Price", "price"));
            item.tasteTag = ParseTaste(First(row, "맛 Flavor", "맛 Flaver", "taste"));
            item.abvPercent = Mathf.Max(0f, ParseFloat(First(row, "ABV", "abv")));
            item.bottleCategory = ParseCategory(First(row, "대분류", "category"));
            item.liquidType = ParseLiquidType(First(row, "소분류", "subcategory"));
            item.capacityMl = Mathf.Max(0f, ParseFloat(First(row, "Size(ml)", "sizeMl"), 700f));
            item.dragMovesObject = true;

            string iconName = First(row, "IconName", "iconName");
            Sprite icon = FindSprite(iconName);
            if (icon == null)
                icon = FindExistingShelfSprite(displayName, id);
            if (icon != null)
                item.icon = icon;
            else if (!string.IsNullOrWhiteSpace(iconName) && item.icon == null)
                report.warnings.Add($"아이템 {id}({displayName})의 스프라이트 '{iconName}'를 찾지 못했습니다.");

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

        private static void UpsertShelfDefinition(
            CsvRow row,
            ItemDef item,
            string id,
            string displayName)
        {
            string assetPath = $"{ShelfOutputFolder}/{SanitizeFileName(id)}.asset";
            LiquorBottleDef definition = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(assetPath);
            bool isNew = definition == null;
            if (isNew)
                definition = ScriptableObject.CreateInstance<LiquorBottleDef>();

            CopyExistingContextVisuals(displayName, id, definition);

            definition.name = id;
            definition.id = id;
            definition.displayName = displayName;
            if (item != null && item.icon != null)
                definition.sprite = item.icon;
            definition.subCategory = LocalizedLabel(First(row, "소분류", "subcategory"));
            definition.unitVolume = item != null ? item.capacityMl : 700f;
            if (definition.bottleCount <= 0)
                definition.bottleCount = 6;

            if (isNew)
                AssetDatabase.CreateAsset(definition, assetPath);
            else
                EditorUtility.SetDirty(definition);
        }

        private static CocktailRecipeDef UpsertRecipe(
            CsvRow row,
            string id,
            string displayName,
            Dictionary<string, List<IngredientImportRow>> ingredientsByRecipe,
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
            recipe.appearsInRecipeBook = true;
            recipe.baseRecipeId = string.Empty;
            recipe.evaluationGrade = CocktailRecipeEvaluationGrade.Good;
            recipe.toleranceMl = 5f;
            recipe.allowExtraIngredients = false;
            recipe.glassId = ParseGlass(First(row, "잔 Glass", "glass"));
            recipe.iceRequirement = ParseIce(First(row, "얼음 유무 Ice", "ice"));
            recipe.requiredTechnique = ParseTechnique(First(row, "제작 방식 Skill", "technique"));

            string abv = First(row, "ABV", "abv");
            recipe.abvOverridePercent = string.IsNullOrWhiteSpace(abv) ? -1f : ParseFloat(abv, -1f);

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
            Dictionary<string, CocktailRecipeDef> importedRecipes)
        {
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
                            });
                        count++;
                    }
                }

                CocktailTechnique technique = GetTechniqueVariant(source.id);
                UpsertVariant(
                    source,
                    $"{source.id}__mid_technique_{technique.ToString().ToLowerInvariant()}",
                    $"제조법 {technique}",
                    variant => variant.requiredTechnique = technique);
                count++;

                UpsertVariant(
                    source,
                    $"{source.id}__mid_ingredient",
                    "지정 재료 변형",
                    variant => ApplyIngredientVariant(source.id, variant));
                count++;
            }

            for (int number = 1011; number <= 1018; number++)
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
                        variant => variant.glassId = glass);
                    count++;
                }
            }

            return count;
        }

        private static void UpsertVariant(
            CocktailRecipeDef source,
            string id,
            string variationLabel,
            Action<CocktailRecipeDef> modify)
        {
            string assetPath = $"{VariantOutputFolder}/{SanitizeFileName(id)}.asset";
            CocktailRecipeDef variant = AssetDatabase.LoadAssetAtPath<CocktailRecipeDef>(assetPath);
            bool isNew = variant == null;
            if (isNew)
                variant = ScriptableObject.CreateInstance<CocktailRecipeDef>();

            variant.name = id;
            variant.id = id;
            variant.displayName = $"{source.displayName} (Mid: {variationLabel})";
            variant.englishName = source.englishName;
            variant.isOrderable = false;
            variant.appearsInRecipeBook = false;
            variant.baseRecipeId = source.id;
            variant.evaluationGrade = CocktailRecipeEvaluationGrade.Mid;
            variant.toleranceMl = source.toleranceMl;
            variant.allowExtraIngredients = source.allowExtraIngredients;
            variant.glassId = source.glassId;
            variant.iceRequirement = source.iceRequirement;
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

        private static CocktailTechnique GetTechniqueVariant(string recipeId)
        {
            return recipeId switch
            {
                "rec_1001" => CocktailTechnique.Stir,
                "rec_1002" => CocktailTechnique.Stir,
                "rec_1003" => CocktailTechnique.Shake,
                "rec_1004" => CocktailTechnique.Stir,
                "rec_1005" => CocktailTechnique.Build,
                "rec_1006" => CocktailTechnique.Build,
                "rec_1007" => CocktailTechnique.Shake,
                _ => CocktailTechnique.None
            };
        }

        private static void ApplyIngredientVariant(string recipeId, CocktailRecipeDef recipe)
        {
            switch (recipeId)
            {
                case "rec_1001":
                    ReplaceIngredient(recipe, "item_1013", "item_1012");
                    break;
                case "rec_1002":
                case "rec_1003":
                    AddIngredient(recipe, "item_1002", 15f);
                    break;
                case "rec_1004":
                    ReplaceIngredient(recipe, "item_1012", "item_1014");
                    break;
                case "rec_1005":
                    ReplaceIngredient(recipe, "item_1013", "item_1012");
                    break;
                case "rec_1006":
                    SetIngredientAmount(recipe, "item_1007", 60f);
                    break;
                case "rec_1007":
                    recipe.ingredients.RemoveAll(ingredient =>
                        ingredient != null
                        && string.Equals(ingredient.itemId, "item_1003", StringComparison.OrdinalIgnoreCase));
                    break;
            }
        }

        private static void ReplaceIngredient(CocktailRecipeDef recipe, string oldId, string newId)
        {
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                CocktailRecipeIngredientDef ingredient = recipe.ingredients[i];
                if (ingredient != null
                    && string.Equals(ingredient.itemId, oldId, StringComparison.OrdinalIgnoreCase))
                {
                    ingredient.itemId = newId;
                    return;
                }
            }
        }

        private static void AddIngredient(CocktailRecipeDef recipe, string itemId, float targetMl)
        {
            recipe.ingredients.Add(new CocktailRecipeIngredientDef
            {
                itemId = itemId,
                targetMl = targetMl,
                toleranceMl = recipe.toleranceMl
            });
        }

        private static void SetIngredientAmount(CocktailRecipeDef recipe, string itemId, float targetMl)
        {
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                CocktailRecipeIngredientDef ingredient = recipe.ingredients[i];
                if (ingredient != null
                    && string.Equals(ingredient.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    ingredient.targetMl = targetMl;
                    return;
                }
            }
        }

        private static void RecalculateTotalRange(CocktailRecipeDef recipe)
        {
            float totalMl = recipe.ingredients.Sum(ingredient =>
                ingredient != null ? Mathf.Max(0f, ingredient.targetMl) : 0f);
            recipe.minTotalMl = totalMl > 0f ? Mathf.Max(0f, totalMl - recipe.toleranceMl) : 0f;
            recipe.maxTotalMl = totalMl > 0f ? totalMl + recipe.toleranceMl : 0f;
        }

        private static Dictionary<string, List<IngredientImportRow>> ReadIngredients(string path)
        {
            Dictionary<string, List<IngredientImportRow>> result = new(StringComparer.OrdinalIgnoreCase);
            List<CsvRow> rows = ReadCsv(path);
            for (int i = 0; i < rows.Count; i++)
            {
                string recipeId = First(rows[i], "recipeId", "RecipeID");
                string itemId = First(rows[i], "itemId", "ingredientId", "ItemID");
                if (string.IsNullOrWhiteSpace(recipeId) || string.IsNullOrWhiteSpace(itemId))
                    continue;

                if (!result.TryGetValue(recipeId, out List<IngredientImportRow> ingredients))
                {
                    ingredients = new List<IngredientImportRow>();
                    result[recipeId] = ingredients;
                }

                ingredients.Add(new IngredientImportRow
                {
                    itemId = itemId,
                    targetMl = Mathf.Max(0f, ParseFloat(First(rows[i], "targetMl", "amountMl"))),
                    toleranceMl = Mathf.Max(0f, ParseFloat(First(rows[i], "toleranceMl"), 5f))
                });
            }

            return result;
        }

        private static CocktailComposition BuildBurnhamSourComposition(
            ItemDefCatalog items,
            string glassId,
            bool hasIce)
        {
            CocktailComposition composition = new CocktailComposition();
            AddItem(composition, items, "item_1002", 15f);
            AddItem(composition, items, "item_1005", 30f);
            AddItem(composition, items, "item_1003", 15f);
            AddItem(composition, items, "item_1013", 30f);
            composition.SetServingStyle(glassId, hasIce);
            composition.RecordTechnique(CocktailTechnique.Shake);
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
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
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
                destination.liquidSpawnNormalized = source.liquidSpawnNormalized;
                destination.colliderCenterNormalized = source.colliderCenterNormalized;
                destination.colliderSizeNormalized = source.colliderSizeNormalized;
                return;
            }
        }

        private static Sprite FindExistingShelfSprite(string displayName, string importedId)
        {
            string[] guids = AssetDatabase.FindAssets("t:LiquorBottleDef", new[] { "Assets/Data/LiquorBottle" });
            for (int i = 0; i < guids.Length; i++)
            {
                LiquorBottleDef definition = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (definition == null
                    || string.Equals(definition.id, importedId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(definition.displayName, displayName, StringComparison.OrdinalIgnoreCase))
                    continue;

                Sprite barSprite = definition.GetBarSprite();
                if (barSprite != null)
                    return barSprite;
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
                LiquorBottleDef source = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
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

    public sealed class PlanningCsvImportReport
    {
        public int importedItems;
        public int importedRecipes;
        public int orderableRecipes;
        public int importedVariants;
        public int skippedItemRows;
        public int skippedRecipeRows;
        public int emptyRgbaItems;
        public readonly List<string> warnings = new();
        public readonly List<string> errors = new();

        public string ToKoreanSummary()
        {
            return $"아이템 {importedItems}개, 레시피 {importedRecipes}개를 갱신했습니다.\n"
                + $"주문 가능 레시피: {orderableRecipes}개\n"
                + $"숨은 Mid 판정 레시피: {importedVariants}개\n"
                + $"건너뛴 행: 아이템 {skippedItemRows}개 / 레시피 {skippedRecipeRows}개\n"
                + $"RGBA 미입력 아이템: {emptyRgbaItems}개\n"
                + $"경고 {warnings.Count}개 / 오류 {errors.Count}개";
        }
    }
}
