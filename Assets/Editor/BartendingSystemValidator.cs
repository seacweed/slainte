using System;
using Slainte.Bartending;
using UnityEditor;
using UnityEngine;

public static class BartendingSystemValidator
{
    [MenuItem("Slainte/품질 검증/바텐딩 시스템 검증")]
    public static void Run()
    {
        ItemDef spirit = ScriptableObject.CreateInstance<ItemDef>();
        spirit.id = "qa_spirit";
        spirit.displayName = "검증용 증류주";
        spirit.liquidColor = new Color(0.25f, 0.5f, 0.75f, 0.2f);
        spirit.servingTemperatureC = 80f;

        try
        {
            ValidateLiquidPayload(spirit);
            ValidateOrderEvaluation(spirit);
            ValidateCsvData();
            ValidateSteamSetup();
            Debug.Log("[바텐딩 시스템 검증] 통과");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(spirit);
        }
    }

    private static void ValidateLiquidPayload(ItemDef spirit)
    {
        LiquidPayload hot = new LiquidPayload();
        hot.SetSingle(spirit, 1f);
        AssertApproximately(80f, hot.temperatureC, "ItemDef의 온도가 액체 입자에 적용되지 않았습니다.");
        AssertApproximately(1f, hot.EvaluateColor().a, "액체 입자가 완전 불투명하게 표시되지 않습니다.");

        ItemDef coldItem = ScriptableObject.CreateInstance<ItemDef>();
        coldItem.servingTemperatureC = 20f;
        try
        {
            LiquidPayload cold = new LiquidPayload();
            cold.SetSingle(coldItem, 1f);
            LiquidPayload.MixPair(hot, cold, 1f);
            AssertApproximately(50f, hot.temperatureC, "혼합 온도가 부피 가중 평균으로 계산되지 않았습니다.");
            AssertApproximately(50f, cold.temperatureC, "두 입자 사이에서 온도가 대칭적으로 전달되지 않았습니다.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(coldItem);
        }
    }

    private static void ValidateOrderEvaluation(ItemDef spirit)
    {
        CocktailRecipe recipe = new CocktailRecipe
        {
            id = "qa_recipe",
            displayName = "검증용 레시피",
            minTotalMl = 45f,
            maxTotalMl = 55f,
            toleranceMl = 2f,
            glassId = "rock",
            iceRequirement = IceRequirement.None,
            requiredTechnique = CocktailTechnique.Build
        };
        recipe.ingredients.Add(new CocktailRecipeIngredient
        {
            ingredientId = spirit.id,
            item = spirit,
            targetMl = 50f,
            toleranceMl = 2f
        });

        CocktailRecipeCatalog catalog = new CocktailRecipeCatalog();
        catalog.Add(recipe);
        CocktailEvaluator evaluator = new CocktailEvaluator(catalog);

        CocktailComposition correct = new CocktailComposition();
        correct.Add(spirit, 50f);
        correct.SetServingStyle("rock", false);
        CocktailEvaluationResult good = evaluator.EvaluateRecipe(recipe.id, correct);
        Assert(good.isSuccess, "정확한 레시피와 제출 조건이 좋음으로 판정되지 않았습니다.");

        CocktailComposition wrongGlass = new CocktailComposition();
        wrongGlass.Add(spirit, 50f);
        wrongGlass.SetServingStyle("highball", false);
        CocktailEvaluationResult mid = evaluator.EvaluateRecipe(recipe.id, wrongGlass);
        Assert(!mid.isSuccess && !mid.glassValid && mid.score >= 0.45f,
            "잔 종류가 틀린 결과가 보통 판정 범위로 처리되지 않았습니다.");

        GeneratedCocktailOrder episodeOrder = new GeneratedCocktailOrder
        {
            orderType = CocktailOrderType.EpisodeOrder,
            requestedRecipeId = recipe.id,
            requestedRecipe = recipe
        };
        CocktailOrderEvaluationResult episodeResult =
            new CocktailOrderEvaluator(evaluator).Evaluate(episodeOrder, correct);
        Assert(episodeResult.isSuccess, "에피소드 주문이 공용 레시피 판정기를 사용하지 않습니다.");
    }

    private static void ValidateSteamSetup()
    {
        GameObject glass = new GameObject("김 연출 검증용 잔");
        try
        {
            BoxCollider2D trigger = glass.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            VesselLiquidTracker tracker = glass.AddComponent<VesselLiquidTracker>();
            GlassSteamEmitter emitter = glass.AddComponent<GlassSteamEmitter>();
            emitter.Initialize(tracker, 1f, 1f, 55f, 48f, 9f);
            Assert(glass.transform.Find("연기 입자") != null,
                "잔 위에 김 입자 시스템이 생성되지 않았습니다.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(glass);
        }
    }

    private static void ValidateCsvData()
    {
        ItemDefCatalog items = ItemDefCatalog.LoadFromResources("Items", null);
        CocktailRecipeCatalog recipes = CocktailRecipeCsvLoader.LoadFromStreamingAssets(items);
        Assert(recipes.TryGet("whiskey_neat", out CocktailRecipe whiskey),
            "recipes.csv에서 whiskey_neat 레시피를 불러오지 못했습니다.");
        Assert(whiskey.glassId == "rock"
            && whiskey.iceRequirement == IceRequirement.None
            && whiskey.requiredTechnique == CocktailTechnique.Build,
            "recipes.csv에서 잔·얼음·제조법 조건을 불러오지 못했습니다.");

        CocktailOrderTemplateCatalog templates =
            CocktailOrderCsvLoader.LoadTemplatesFromStreamingAssets();
        Assert(templates.Count >= 3, "주문 문장을 불러오지 못했습니다.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertApproximately(float expected, float actual, string message)
    {
        if (!Mathf.Approximately(expected, actual))
            throw new InvalidOperationException($"{message} 예상값: {expected}, 실제값: {actual}.");
    }
}
