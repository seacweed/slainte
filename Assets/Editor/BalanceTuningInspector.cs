using System;
using System.Collections.Generic;
using System.Linq;
using Slainte.Bartending;
using Slainte.Business;
using Slainte.TV;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    [CustomEditor(typeof(BusinessOrderFlowSettings))]
    public sealed class BalanceTuningInspector : UnityEditor.Editor
    {
        private const string BusinessSettingsPath =
            "Assets/Resources/Business/BusinessOrderFlowSettings.asset";
        private const string TvDatabasePath =
            "Assets/Resources/TV/TVBroadcastDatabase.asset";
        private const string ShopCatalogPath =
            "Assets/Resources/Shop/LiquorShopCatalog.asset";
        private const string RecipeFolder = "Assets/Resources/Recipes/Planning";
        private const string UpgradeFolder = "Assets/Data/UpgradeData";
        private const string GuidePath = "docs/gameplay/balance-tuning-guide.md";

        private readonly Dictionary<int, bool> expandedAssets = new();
        private readonly List<CocktailRecipeDef> recipes = new();
        private readonly List<LiquorBottleDef> ingredients = new();
        private readonly List<CustomerVisitData> customerVisits = new();
        private readonly List<UpgradeDef> upgrades = new();

        private TVBroadcastDatabase tvDatabase;
        private LiquorShopCatalog shopCatalog;
        private CustomerVisitDatabase customerDatabase;
        private bool quickEditorFoldout = true;
        private bool recipesFoldout = true;
        private bool ingredientsFoldout = true;
        private bool tvFoldout;
        private bool customersFoldout;
        private bool upgradesFoldout;
        private string recipeSearch = string.Empty;
        private string ingredientSearch = string.Empty;
        private string customerSearch = string.Empty;

        [MenuItem("Slainte/데이터/밸런스 설정 열기", priority = 50)]
        public static void OpenBalanceSettings()
        {
            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(BusinessSettingsPath);
            if (settings == null)
            {
                EditorUtility.DisplayDialog(
                    "밸런스 설정",
                    $"영업 설정 에셋을 찾지 못했습니다.\n{BusinessSettingsPath}",
                    "확인");
                return;
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        private void OnEnable()
        {
            RefreshAssetLists();
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "이 에셋의 기존 영업 설정은 아래 기본 Inspector에서 그대로 수정합니다. "
                + "빠른 편집 영역은 별도 데이터를 만들지 않고 현재 프로젝트가 사용하는 원본 에셋을 직접 편집합니다.",
                MessageType.Info);

            DrawDefaultInspector();
            EditorGUILayout.Space(10f);
            DrawQuickEditor();
        }

        private void DrawQuickEditor()
        {
            quickEditorFoldout = EditorGUILayout.Foldout(
                quickEditorFoldout,
                "기획 밸런스 빠른 편집",
                true,
                EditorStyles.foldoutHeader);
            if (!quickEditorFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "에디터 시작 시 CSV 자동 복원은 꺼져 있습니다. 다만 기획 CSV 임포트 메뉴를 직접 실행하면 "
                    + "레시피·재료 Inspector 값이 덮어써질 수 있습니다.",
                    MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("목록 새로고침"))
                        RefreshAssetLists();
                    if (GUILayout.Button("설명서 선택"))
                        SelectGuide();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("밸런스 유효성 검사"))
                        ValidateBalanceData();
                    if (GUILayout.Button("영업 구조 검증"))
                        EditorApplication.ExecuteMenuItem("Slainte/품질 검증/시간 기반 영업 검증");
                    if (GUILayout.Button("TV 구조 검증"))
                        EditorApplication.ExecuteMenuItem("Slainte/TV/Validate TV System");
                }
            }

            BusinessOrderFlowSettings settings = (BusinessOrderFlowSettings)target;
            DrawRewardPreview(settings);
            DrawRecipes(settings);
            DrawIngredients();
            DrawTvDatabase();
            DrawCustomerVisits();
            DrawUpgrades();
        }

        private static void DrawRewardPreview(BusinessOrderFlowSettings settings)
        {
            if (settings == null)
                return;

            const int examplePrice = 1000;
            float tvTipMultiplier = BalanceInspectorUtility.GetTipBroadcastMultiplier();
            int normalTip = Mathf.RoundToInt(examplePrice * settings.satisfiedTipRate);
            int tvTip = Mathf.RoundToInt(
                examplePrice * settings.satisfiedTipRate * tvTipMultiplier);
            int badPenalty = Mathf.RoundToInt(examplePrice * settings.badPenaltyRate);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("현재 보상 계산 예시", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "정가 1,000 G / Good",
                    $"일반 {examplePrice + normalTip:N0} G · TV 팁 방송 {examplePrice + tvTip:N0} G");
                EditorGUILayout.LabelField(
                    "정가 1,000 G / Bad",
                    $"{examplePrice - badPenalty:N0} G (페널티 {badPenalty:N0} G)");
                EditorGUILayout.HelpBox(
                    "현재 런타임은 레시피 가격이 있으면 Good/Mid/Bad 모두 정가를 기본 수익으로 사용합니다. "
                    + "good/mid/badRecipePriceMultiplier는 현재 계산에 연결되어 있지 않습니다.",
                    MessageType.Warning);
            }
        }

        private void DrawRecipes(BusinessOrderFlowSettings settings)
        {
            recipesFoldout = EditorGUILayout.Foldout(
                recipesFoldout,
                $"칵테일 ({recipes.Count})",
                true,
                EditorStyles.foldoutHeader);
            if (!recipesFoldout)
                return;

            recipeSearch = EditorGUILayout.TextField("검색", recipeSearch);
            int visibleCount = 0;
            for (int i = 0; i < recipes.Count; i++)
            {
                CocktailRecipeDef recipe = recipes[i];
                if (recipe == null || !Matches(recipeSearch, recipe.id, recipe.displayName, recipe.englishName))
                    continue;

                visibleCount++;
                DrawRecipe(recipe, settings);
            }

            if (visibleCount == 0)
                EditorGUILayout.HelpBox("검색 조건에 맞는 칵테일이 없습니다.", MessageType.Info);
        }

        private void DrawRecipe(CocktailRecipeDef recipe, BusinessOrderFlowSettings settings)
        {
            int instanceId = recipe.GetInstanceID();
            bool expanded = GetExpanded(instanceId);
            SerializedObject recipeObject = new(recipe);
            recipeObject.Update();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool nextExpanded = EditorGUILayout.Foldout(
                        expanded,
                        $"{recipe.id} · {recipe.displayName}",
                        true);
                    SetExpanded(instanceId, nextExpanded);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("선택", GUILayout.Width(46f)))
                        SelectAsset(recipe);
                }

                DrawProperty(recipeObject, "price", "일반 판매가");
                DrawProperty(recipeObject, "strangeCoinPrice", "이상한 동전 판매가");

                float totalMl = BalanceInspectorUtility.GetTotalRecipeMl(recipe);
                float materialCost = BalanceInspectorUtility.GetRecipeMaterialCost(recipe, ingredients, false);
                int normalTip = Mathf.RoundToInt(recipe.price * Mathf.Clamp01(settings.satisfiedTipRate));
                float tvMultiplier = BalanceInspectorUtility.GetTipBroadcastMultiplier(tvDatabase);
                int tvTip = Mathf.RoundToInt(
                    recipe.price * Mathf.Clamp01(settings.satisfiedTipRate) * tvMultiplier);
                EditorGUILayout.LabelField(
                    "계산 미리보기",
                    $"총 {totalMl:0.##} ml · 재료 원가 약 {materialCost:0.##} G · "
                    + $"Good {recipe.price + normalTip:N0} G · TV Good {recipe.price + tvTip:N0} G");

                if (GetExpanded(instanceId))
                {
                    EditorGUILayout.Space(2f);
                    DrawProperty(recipeObject, "isOrderable", "주문 가능");
                    DrawProperty(recipeObject, "appearsInRecipeBook", "레시피북 노출");
                    DrawProperty(recipeObject, "glassId", "잔 ID");
                    DrawProperty(recipeObject, "iceRequirement", "얼음 조건");
                    DrawProperty(recipeObject, "shakeIceRequirement", "셰이커 얼음 조건");
                    DrawProperty(recipeObject, "requiredTechnique", "제조 방식");
                    DrawProperty(recipeObject, "toleranceMl", "기본 허용 오차(ml)");
                    DrawProperty(recipeObject, "minTotalMl", "최소 총용량(ml)");
                    DrawProperty(recipeObject, "maxTotalMl", "최대 총용량(ml)");
                    DrawProperty(recipeObject, "allowExtraIngredients", "추가 재료 허용");
                    DrawProperty(recipeObject, "abvOverridePercent", "도수 덮어쓰기(%)");
                    DrawProperty(recipeObject, "ingredientPropertyTags", "재료 속성 태그", true);
                    DrawProperty(recipeObject, "tasteTags", "맛 태그", true);
                    DrawProperty(recipeObject, "moodTags", "분위기 태그", true);
                    DrawProperty(recipeObject, "ingredients", "배합", true);
                }
            }

            if (recipeObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(recipe);
        }

        private void DrawIngredients()
        {
            ingredientsFoldout = EditorGUILayout.Foldout(
                ingredientsFoldout,
                $"재료 ({ingredients.Count})",
                true,
                EditorStyles.foldoutHeader);
            if (!ingredientsFoldout)
                return;

            ingredientSearch = EditorGUILayout.TextField("검색", ingredientSearch);
            int visibleCount = 0;
            for (int i = 0; i < ingredients.Count; i++)
            {
                LiquorBottleDef ingredient = ingredients[i];
                if (ingredient == null
                    || !Matches(
                        ingredientSearch,
                        ingredient.id,
                        ingredient.displayName,
                        ingredient.item != null ? ingredient.item.englishName : string.Empty))
                {
                    continue;
                }

                visibleCount++;
                DrawIngredient(ingredient);
            }

            if (visibleCount == 0)
                EditorGUILayout.HelpBox("검색 조건에 맞는 재료가 없습니다.", MessageType.Info);
        }

        private void DrawIngredient(LiquorBottleDef ingredient)
        {
            int instanceId = ingredient.GetInstanceID();
            bool expanded = GetExpanded(instanceId);
            SerializedObject bottleObject = new(ingredient);
            bottleObject.Update();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool nextExpanded = EditorGUILayout.Foldout(
                        expanded,
                        $"{ingredient.InventoryId} · {ingredient.displayName}",
                        true);
                    SetExpanded(instanceId, nextExpanded);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("선택", GUILayout.Width(46f)))
                        SelectAsset(ingredient);
                }

                DrawProperty(bottleObject, "price", "일반 구매가");
                DrawProperty(bottleObject, "strangeCoinPrice", "이상한 동전 구매가");
                DrawProperty(bottleObject, "unitVolume", "병 용량(ml)");
                DrawProperty(bottleObject, "defaultBottleCount", "기본 소지 병 수");
                DrawProperty(bottleObject, "bottleCount", "최대 보관 병 수");

                float unitCost = ingredient.unitVolume > 0f
                    ? ingredient.price / ingredient.unitVolume
                    : 0f;
                EditorGUILayout.LabelField(
                    "계산 미리보기",
                    $"1 ml당 {unitCost:0.###} G · 기본 {ingredient.DefaultAmount:0.##} ml · 최대 {ingredient.MaxAmount:0.##} ml");

                if (GetExpanded(instanceId))
                {
                    DrawProperty(bottleObject, "unlockFlagKey", "해금 플래그");
                    DrawProperty(bottleObject, "subCategory", "소분류");
                    DrawProperty(bottleObject, "category", "상점 카테고리");
                    DrawLinkedItem(ingredient);
                }
            }

            if (bottleObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(ingredient);
        }

        private static void DrawLinkedItem(LiquorBottleDef ingredient)
        {
            ItemDef item = ingredient.item;
            if (item == null)
            {
                EditorGUILayout.HelpBox("연결된 제조용 ItemDef가 없습니다.", MessageType.Error);
                return;
            }

            SerializedObject itemObject = new(item);
            itemObject.Update();
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("연결된 제조 데이터", EditorStyles.boldLabel);
            DrawProperty(itemObject, "abvPercent", "ABV(%)");
            DrawProperty(itemObject, "tasteTag", "맛");
            DrawProperty(itemObject, "bottleCategory", "대분류");
            DrawProperty(itemObject, "liquidType", "액체 종류");
            DrawProperty(itemObject, "servingTemperatureC", "제공 온도(℃)");
            DrawProperty(itemObject, "liquidColor", "액체 색상");

            bool priceMismatch = item.price != ingredient.price;
            bool volumeMismatch = !Mathf.Approximately(item.capacityMl, ingredient.unitVolume);
            if (priceMismatch || volumeMismatch)
            {
                EditorGUILayout.HelpBox(
                    "상점 데이터와 제조 데이터의 가격 또는 병 용량이 다릅니다. "
                    + "현재 상점 구매에는 LiquorBottleDef 값이 사용됩니다.",
                    MessageType.Warning);
                if (GUILayout.Button("상점 가격·용량을 제조 데이터에 맞추기"))
                {
                    Undo.RecordObject(item, "재료 가격·용량 동기화");
                    item.price = ingredient.price;
                    item.capacityMl = ingredient.unitVolume;
                    EditorUtility.SetDirty(item);
                    itemObject.Update();
                }
            }

            if (itemObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(item);
        }

        private void DrawTvDatabase()
        {
            int count = tvDatabase != null && tvDatabase.broadcasts != null
                ? tvDatabase.broadcasts.Count
                : 0;
            tvFoldout = EditorGUILayout.Foldout(
                tvFoldout,
                $"TV 방송 ({count})",
                true,
                EditorStyles.foldoutHeader);
            if (!tvFoldout)
                return;

            if (tvDatabase == null)
            {
                EditorGUILayout.HelpBox("기본 TV 방송 데이터베이스를 찾지 못했습니다.", MessageType.Error);
                return;
            }

            SerializedObject tvObject = new(tvDatabase);
            tvObject.Update();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField("원본 에셋", tvDatabase, typeof(TVBroadcastDatabase), false);
                    if (GUILayout.Button("선택", GUILayout.Width(46f)))
                        SelectAsset(tvDatabase);
                }

                DrawProperty(tvObject, "broadcasts", "방송 목록", true);
                DrawTvProbabilitySummary(tvDatabase);
            }

            if (tvObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(tvDatabase);
        }

        internal static void DrawTvProbabilitySummary(TVBroadcastDatabase database)
        {
            float totalWeight = database.broadcasts?
                .Where(entry => entry != null && entry.weight > 0f)
                .Sum(entry => entry.weight) ?? 0f;
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(
                "추첨 확률 미리보기",
                totalWeight > 0f ? $"유효 가중치 합 {totalWeight:0.##}" : "유효 가중치가 없습니다.");

            if (totalWeight <= 0f || database.broadcasts == null)
                return;

            for (int i = 0; i < database.broadcasts.Count; i++)
            {
                TVBroadcastEntry entry = database.broadcasts[i];
                if (entry == null)
                    continue;

                float probability = Mathf.Max(0f, entry.weight) / totalWeight * 100f;
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(entry.title) ? entry.id : entry.title,
                    $"{probability:0.##}% · 배율 {entry.effectMultiplier:0.##}x");
            }
        }

        private void DrawCustomerVisits()
        {
            customersFoldout = EditorGUILayout.Foldout(
                customersFoldout,
                $"손님 등장·주문 가중치 ({customerVisits.Count})",
                true,
                EditorStyles.foldoutHeader);
            if (!customersFoldout)
                return;

            EditorGUILayout.HelpBox(
                "손님 드래프트를 CSV에서 다시 임포트하고 게시하면 아래 값이 바뀔 수 있습니다.",
                MessageType.Warning);
            customerSearch = EditorGUILayout.TextField("검색", customerSearch);
            int visibleCount = 0;
            for (int i = 0; i < customerVisits.Count; i++)
            {
                CustomerVisitData visit = customerVisits[i];
                if (visit == null
                    || !Matches(
                        customerSearch,
                        visit.sourceCustomerId,
                        visit.visitKey,
                        visit.customerAttributeKey))
                {
                    continue;
                }

                visibleCount++;
                DrawCustomerVisit(visit);
            }

            if (visibleCount == 0)
                EditorGUILayout.HelpBox("검색 조건에 맞는 손님이 없습니다.", MessageType.Info);
        }

        private void DrawCustomerVisit(CustomerVisitData visit)
        {
            int instanceId = visit.GetInstanceID();
            bool expanded = GetExpanded(instanceId);
            SerializedObject visitObject = new(visit);
            visitObject.Update();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool nextExpanded = EditorGUILayout.Foldout(
                        expanded,
                        $"{visit.sourceCustomerId} · {visit.visitKey}",
                        true);
                    SetExpanded(instanceId, nextExpanded);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("선택", GUILayout.Width(46f)))
                        SelectAsset(visit);
                }

                DrawProperty(visitObject, "weight", "손님 선택 가중치");
                DrawProperty(visitObject, "initiallyAvailable", "시작 시 등장 가능");
                DrawProperty(visitObject, "maxDay", "최대 등장 날짜");
                if (GetExpanded(instanceId))
                {
                    DrawProperty(visitObject, "tags", "태그", true);
                    DrawProperty(visitObject, "condition", "등장 조건", true);
                    DrawProperty(visitObject, "availabilityTransitions", "활성 상태 전환", true);
                    DrawProperty(visitObject, "orders", "주문 후보와 가중치", true);
                }
            }

            if (visitObject.ApplyModifiedProperties())
                EditorUtility.SetDirty(visit);
        }

        private void DrawUpgrades()
        {
            upgradesFoldout = EditorGUILayout.Foldout(
                upgradesFoldout,
                $"업그레이드 가격 ({upgrades.Count})",
                true,
                EditorStyles.foldoutHeader);
            if (!upgradesFoldout)
                return;

            EditorGUILayout.HelpBox(
                "현재 업그레이드는 구매 가격과 레벨 저장은 작동하지만 설명에 적힌 게임플레이 효과 연결은 확인되지 않았습니다.",
                MessageType.Warning);
            for (int i = 0; i < upgrades.Count; i++)
            {
                UpgradeDef upgrade = upgrades[i];
                if (upgrade == null)
                    continue;

                SerializedObject upgradeObject = new(upgrade);
                upgradeObject.Update();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            $"{upgrade.id} · {upgrade.displayName}",
                            EditorStyles.boldLabel);
                        if (GUILayout.Button("선택", GUILayout.Width(46f)))
                            SelectAsset(upgrade);
                    }
                    DrawProperty(upgradeObject, "pricesPerLevel", "단계별 가격", true);
                }

                if (upgradeObject.ApplyModifiedProperties())
                    EditorUtility.SetDirty(upgrade);
            }
        }

        private void RefreshAssetLists()
        {
            recipes.Clear();
            ingredients.Clear();
            customerVisits.Clear();
            upgrades.Clear();

            tvDatabase = AssetDatabase.LoadAssetAtPath<TVBroadcastDatabase>(TvDatabasePath);
            shopCatalog = AssetDatabase.LoadAssetAtPath<LiquorShopCatalog>(ShopCatalogPath);
            BusinessOrderFlowSettings settings = target as BusinessOrderFlowSettings;
            customerDatabase = settings != null && settings.customerVisitDatabase != null
                ? settings.customerVisitDatabase
                : CustomerVisitDatabase.LoadDefault();

            string[] recipeGuids = AssetDatabase.FindAssets(
                "t:CocktailRecipeDef",
                new[] { RecipeFolder });
            for (int i = 0; i < recipeGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(recipeGuids[i]);
                CocktailRecipeDef recipe = AssetDatabase.LoadAssetAtPath<CocktailRecipeDef>(path);
                if (recipe == null
                    || !string.IsNullOrWhiteSpace(recipe.baseRecipeId)
                    || !recipe.appearsInRecipeBook)
                {
                    continue;
                }
                recipes.Add(recipe);
            }
            recipes.Sort((left, right) => string.Compare(left.id, right.id, StringComparison.OrdinalIgnoreCase));

            if (shopCatalog != null && shopCatalog.bottles != null)
            {
                HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < shopCatalog.bottles.Count; i++)
                {
                    LiquorBottleDef ingredient = shopCatalog.bottles[i];
                    if (ingredient == null || !seenIds.Add(ingredient.InventoryId))
                        continue;
                    ingredients.Add(ingredient);
                }
                ingredients.Sort((left, right) => string.Compare(
                    left.InventoryId,
                    right.InventoryId,
                    StringComparison.OrdinalIgnoreCase));
            }

            if (customerDatabase != null && customerDatabase.visits != null)
            {
                customerVisits.AddRange(customerDatabase.visits.Where(visit => visit != null));
                customerVisits.Sort((left, right) => string.Compare(
                    left.sourceCustomerId,
                    right.sourceCustomerId,
                    StringComparison.OrdinalIgnoreCase));
            }

            string[] upgradeGuids = AssetDatabase.FindAssets("t:UpgradeDef", new[] { UpgradeFolder });
            for (int i = 0; i < upgradeGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(upgradeGuids[i]);
                UpgradeDef upgrade = AssetDatabase.LoadAssetAtPath<UpgradeDef>(path);
                if (upgrade != null)
                    upgrades.Add(upgrade);
            }
            upgrades.Sort((left, right) => string.Compare(left.id, right.id, StringComparison.OrdinalIgnoreCase));

            Repaint();
        }

        private void ValidateBalanceData()
        {
            List<string> errors = new();
            BusinessOrderFlowSettings settings = target as BusinessOrderFlowSettings;
            if (settings == null)
                errors.Add("영업 설정 에셋이 없습니다.");
            else
            {
                if (settings.shiftDurationSeconds < 1f)
                    errors.Add("영업시간은 1초 이상이어야 합니다.");
                if (settings.badPenaltyRate < 0f
                    || settings.bigFishGoodBonusRate < 0f
                    || settings.bigFishFailurePenaltyRate < 0f)
                {
                    errors.Add("판매 보너스·페널티 비율은 음수일 수 없습니다.");
                }
            }

            HashSet<string> recipeIds = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> ingredientIds = new(
                ingredients
                    .Where(ingredient => ingredient != null)
                    .Select(ingredient => ingredient.InventoryId),
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < recipes.Count; i++)
            {
                CocktailRecipeDef recipe = recipes[i];
                if (recipe == null)
                    continue;
                if (string.IsNullOrWhiteSpace(recipe.id) || !recipeIds.Add(recipe.id))
                    errors.Add($"레시피 ID가 비어 있거나 중복됩니다: {recipe.name}");
                if (recipe.price < 0 || recipe.strangeCoinPrice < 0)
                    errors.Add($"레시피 가격이 음수입니다: {recipe.id}");
                if (recipe.minTotalMl < 0f || recipe.maxTotalMl < recipe.minTotalMl)
                    errors.Add($"레시피 총용량 범위가 유효하지 않습니다: {recipe.id}");
                if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                {
                    errors.Add($"레시피 배합이 비어 있습니다: {recipe.id}");
                    continue;
                }

                for (int ingredientIndex = 0;
                     ingredientIndex < recipe.ingredients.Count;
                     ingredientIndex++)
                {
                    CocktailRecipeIngredientDef row = recipe.ingredients[ingredientIndex];
                    if (row == null
                        || string.IsNullOrWhiteSpace(row.itemId)
                        || !ingredientIds.Contains(row.itemId))
                    {
                        errors.Add($"레시피가 알 수 없는 재료를 참조합니다: {recipe.id}");
                    }
                    else if (row.targetMl <= 0f)
                    {
                        errors.Add($"레시피 재료 용량은 0보다 커야 합니다: {recipe.id}/{row.itemId}");
                    }
                }
            }

            for (int i = 0; i < ingredients.Count; i++)
            {
                LiquorBottleDef ingredient = ingredients[i];
                if (ingredient == null)
                    continue;
                if (ingredient.price < 0 || ingredient.strangeCoinPrice < 0)
                    errors.Add($"재료 가격이 음수입니다: {ingredient.InventoryId}");
                if (ingredient.unitVolume <= 0f)
                    errors.Add($"재료 병 용량은 0보다 커야 합니다: {ingredient.InventoryId}");
                if (ingredient.bottleCount < 0
                    || ingredient.defaultBottleCount < 0
                    || ingredient.defaultBottleCount > ingredient.bottleCount)
                {
                    errors.Add($"재료 기본·최대 병 수가 유효하지 않습니다: {ingredient.InventoryId}");
                }
                if (ingredient.item == null)
                    errors.Add($"재료에 제조용 ItemDef가 없습니다: {ingredient.InventoryId}");
            }

            if (tvDatabase == null || tvDatabase.broadcasts == null || tvDatabase.broadcasts.Count == 0)
            {
                errors.Add("TV 방송 목록이 비어 있습니다.");
            }
            else
            {
                HashSet<string> broadcastIds = new(StringComparer.OrdinalIgnoreCase);
                float totalWeight = 0f;
                for (int i = 0; i < tvDatabase.broadcasts.Count; i++)
                {
                    TVBroadcastEntry entry = tvDatabase.broadcasts[i];
                    if (entry == null
                        || string.IsNullOrWhiteSpace(entry.id)
                        || !broadcastIds.Add(entry.id))
                    {
                        errors.Add("TV 방송 ID가 비어 있거나 중복됩니다.");
                        continue;
                    }
                    if (entry.weight < 0f || entry.effectMultiplier < 0f)
                        errors.Add($"TV 가중치·배율이 음수입니다: {entry.id}");
                    totalWeight += Mathf.Max(0f, entry.weight);
                }
                if (totalWeight <= 0f)
                    errors.Add("TV 방송 유효 가중치 합이 0입니다.");
            }

            for (int i = 0; i < customerVisits.Count; i++)
            {
                CustomerVisitData visit = customerVisits[i];
                if (visit != null && visit.weight < 0f)
                    errors.Add($"손님 등장 가중치가 음수입니다: {visit.visitKey}");
            }

            for (int i = 0; i < upgrades.Count; i++)
            {
                UpgradeDef upgrade = upgrades[i];
                if (upgrade?.pricesPerLevel != null
                    && upgrade.pricesPerLevel.Any(price => price < 0))
                {
                    errors.Add($"업그레이드 가격이 음수입니다: {upgrade.id}");
                }
            }

            if (errors.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "밸런스 유효성 검사",
                    $"검사를 통과했습니다.\n칵테일 {recipes.Count}개 / 재료 {ingredients.Count}개 / "
                    + $"손님 {customerVisits.Count}개 / 업그레이드 {upgrades.Count}개",
                    "확인");
                return;
            }

            string message = string.Join("\n", errors.Take(20).Select(error => "• " + error));
            if (errors.Count > 20)
                message += $"\n… 외 {errors.Count - 20}건";
            EditorUtility.DisplayDialog("밸런스 유효성 검사", message, "확인");
        }

        private bool GetExpanded(int instanceId)
        {
            return expandedAssets.TryGetValue(instanceId, out bool expanded) && expanded;
        }

        private void SetExpanded(int instanceId, bool expanded)
        {
            expandedAssets[instanceId] = expanded;
        }

        private static bool Matches(string search, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            string term = search.Trim();
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])
                    && values[i].IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void DrawProperty(
            SerializedObject serialized,
            string propertyName,
            string label,
            bool includeChildren = false)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label), includeChildren);
        }

        private static void SelectAsset(UnityEngine.Object asset)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static void SelectGuide()
        {
            UnityEngine.Object guide = AssetDatabase.LoadMainAssetAtPath(GuidePath);
            if (guide == null)
            {
                EditorUtility.DisplayDialog(
                    "밸런스 설명서",
                    $"아직 설명서 파일을 찾지 못했습니다.\n{GuidePath}",
                    "확인");
                return;
            }
            SelectAsset(guide);
        }
    }

    [CustomEditor(typeof(CocktailRecipeDef))]
    public sealed class CocktailRecipeBalancePreviewInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            CocktailRecipeDef recipe = (CocktailRecipeDef)target;
            BusinessOrderFlowSettings settings = BusinessOrderFlowSettings.LoadDefault();
            IReadOnlyList<LiquorBottleDef> ingredients = BalanceInspectorUtility.LoadShopIngredients();
            float totalMl = BalanceInspectorUtility.GetTotalRecipeMl(recipe);
            float cost = BalanceInspectorUtility.GetRecipeMaterialCost(recipe, ingredients, false);
            float tipMultiplier = BalanceInspectorUtility.GetTipBroadcastMultiplier();
            float tipRate = settings != null ? Mathf.Clamp01(settings.satisfiedTipRate) : 0f;
            int normalTip = Mathf.RoundToInt(recipe.price * tipRate);
            int tvTip = Mathf.RoundToInt(recipe.price * tipRate * tipMultiplier);

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("밸런스 계산 미리보기", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("총 제조 용량", $"{totalMl:0.##} ml");
                EditorGUILayout.LabelField("예상 재료 원가", $"{cost:0.##} G");
                EditorGUILayout.LabelField("Good 총수익", $"{recipe.price + normalTip:N0} G");
                EditorGUILayout.LabelField("TV 팁 방송 Good 총수익", $"{recipe.price + tvTip:N0} G");
                EditorGUILayout.HelpBox(
                    "이 에셋은 기획 CSV 임포트 시 덮어써질 수 있습니다.",
                    MessageType.Warning);
            }
        }
    }

    [CustomEditor(typeof(LiquorBottleDef))]
    public sealed class LiquorBottleBalancePreviewInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            LiquorBottleDef ingredient = (LiquorBottleDef)target;
            float unitCost = ingredient.unitVolume > 0f
                ? ingredient.price / ingredient.unitVolume
                : 0f;

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("밸런스 계산 미리보기", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("1 ml당 원가", $"{unitCost:0.###} G");
                EditorGUILayout.LabelField("기본 재고", $"{ingredient.DefaultAmount:0.##} ml");
                EditorGUILayout.LabelField("최대 재고", $"{ingredient.MaxAmount:0.##} ml");

                if (ingredient.item != null
                    && (ingredient.item.price != ingredient.price
                        || !Mathf.Approximately(ingredient.item.capacityMl, ingredient.unitVolume)))
                {
                    EditorGUILayout.HelpBox(
                        "연결된 ItemDef와 가격 또는 용량이 다릅니다. 상점 구매에는 이 LiquorBottleDef 값이 사용됩니다.",
                        MessageType.Warning);
                }
            }
        }
    }

    [CustomEditor(typeof(TVBroadcastDatabase))]
    public sealed class TvBroadcastBalancePreviewInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            TVBroadcastDatabase database = (TVBroadcastDatabase)target;
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("밸런스 계산 미리보기", EditorStyles.boldLabel);
                BalanceTuningInspector.DrawTvProbabilitySummary(database);
            }
        }
    }

    [CustomEditor(typeof(UpgradeDef))]
    public sealed class UpgradeBalanceStatusInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox(
                "현재 구매 가격과 레벨 저장은 작동하지만, 설명에 적힌 업그레이드 효과를 읽는 런타임 코드는 확인되지 않았습니다.",
                MessageType.Warning);
        }
    }

    internal static class BalanceInspectorUtility
    {
        private const string TvDatabasePath =
            "Assets/Resources/TV/TVBroadcastDatabase.asset";
        private const string ShopCatalogPath =
            "Assets/Resources/Shop/LiquorShopCatalog.asset";

        public static float GetTotalRecipeMl(CocktailRecipeDef recipe)
        {
            if (recipe == null || recipe.ingredients == null)
                return 0f;

            float total = 0f;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                CocktailRecipeIngredientDef ingredient = recipe.ingredients[i];
                if (ingredient != null)
                    total += Mathf.Max(0f, ingredient.targetMl);
            }
            return total;
        }

        public static float GetRecipeMaterialCost(
            CocktailRecipeDef recipe,
            IReadOnlyList<LiquorBottleDef> ingredients,
            bool strangeCoin)
        {
            if (recipe == null || recipe.ingredients == null || ingredients == null)
                return 0f;

            Dictionary<string, LiquorBottleDef> byId = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ingredients.Count; i++)
            {
                LiquorBottleDef bottle = ingredients[i];
                if (bottle != null && !string.IsNullOrWhiteSpace(bottle.InventoryId))
                    byId[bottle.InventoryId] = bottle;
            }

            float total = 0f;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                CocktailRecipeIngredientDef recipeIngredient = recipe.ingredients[i];
                if (recipeIngredient == null
                    || string.IsNullOrWhiteSpace(recipeIngredient.itemId)
                    || !byId.TryGetValue(recipeIngredient.itemId, out LiquorBottleDef bottle)
                    || bottle.unitVolume <= 0f)
                {
                    continue;
                }

                int price = strangeCoin ? bottle.strangeCoinPrice : bottle.price;
                total += Mathf.Max(0f, recipeIngredient.targetMl) * price / bottle.unitVolume;
            }
            return total;
        }

        public static float GetTipBroadcastMultiplier(TVBroadcastDatabase database = null)
        {
            database ??= AssetDatabase.LoadAssetAtPath<TVBroadcastDatabase>(TvDatabasePath);
            if (database?.broadcasts == null)
                return 1f;

            TVBroadcastEntry entry = database.broadcasts.FirstOrDefault(candidate =>
                candidate != null && candidate.effectType == TVBroadcastEffectType.BoostTips);
            return entry != null ? Mathf.Max(0f, entry.effectMultiplier) : 1f;
        }

        public static IReadOnlyList<LiquorBottleDef> LoadShopIngredients()
        {
            LiquorShopCatalog catalog =
                AssetDatabase.LoadAssetAtPath<LiquorShopCatalog>(ShopCatalogPath);
            if (catalog?.bottles != null)
                return catalog.bottles;
            return Array.Empty<LiquorBottleDef>();
        }
    }
}
