using System;
using System.Collections.Generic;
using System.Reflection;
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
            ValidateLiquidPoolIsolation();
            ValidateVesselLiquidTransfer();
            ValidateBottleGeometryProfile();
            ValidateOrderEvaluation(spirit);
            ValidateCsvData();
            ValidateBusinessBottleData();
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

    private static void ValidateBottleGeometryProfile()
    {
        GameObject bottleObject = new GameObject("검증용 병");
        GameObject spawnObject = new GameObject("검증용 액체 스폰 위치");
        Texture2D texture = new Texture2D(10, 20, TextureFormat.RGBA32, false);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            10f);
        ItemDef bottleItem = ScriptableObject.CreateInstance<ItemDef>();

        try
        {
            spawnObject.transform.SetParent(bottleObject.transform, false);
            spawnObject.transform.localPosition = new Vector3(-3f, -3f, 0f);

            bottleObject.AddComponent<SpriteRenderer>();
            BoxCollider2D boxCollider = bottleObject.AddComponent<BoxCollider2D>();
            boxCollider.offset = new Vector2(0.2f, 0.3f);
            boxCollider.size = new Vector2(0.4f, 0.6f);
            BottleController bottle = bottleObject.AddComponent<BottleController>();
            bottle.liquidSpawnPoint = spawnObject.transform;
            bottleItem.type = ItemType.Bottle;
            bottleItem.icon = sprite;
            bottleItem.capacityMl = 100f;
            bottle.Init(bottleItem);

            AssertApproximately(-3f, spawnObject.transform.localPosition.x,
                "형상 프로필이 없는 병의 입구 X가 변경되었습니다.");
            AssertApproximately(-3f, spawnObject.transform.localPosition.y,
                "형상 프로필이 없는 병의 입구 Y가 변경되었습니다.");
            AssertApproximately(0.2f, boxCollider.offset.x,
                "형상 프로필이 없는 병의 콜라이더 오프셋이 변경되었습니다.");
            AssertApproximately(0.4f, boxCollider.size.x,
                "형상 프로필이 없는 병의 콜라이더 크기가 변경되었습니다.");

            bottleItem.overrideBottleGeometry = true;
            bottleItem.liquidSpawnNormalized = new Vector2(0.25f, 0.9f);
            bottleItem.colliderCenterNormalized = new Vector2(0.4f, 0.45f);
            bottleItem.colliderSizeNormalized = new Vector2(0.8f, 0.7f);
            bottle.Init(bottleItem);

            Bounds bounds = sprite.bounds;
            AssertApproximately(Mathf.Lerp(bounds.min.x, bounds.max.x, 0.25f),
                spawnObject.transform.localPosition.x,
                "형상 프로필의 병 입구 X가 적용되지 않았습니다.");
            AssertApproximately(Mathf.Lerp(bounds.min.y, bounds.max.y, 0.9f),
                spawnObject.transform.localPosition.y,
                "형상 프로필의 병 입구 Y가 적용되지 않았습니다.");
            AssertApproximately(Mathf.Lerp(bounds.min.x, bounds.max.x, 0.4f),
                boxCollider.offset.x,
                "형상 프로필의 콜라이더 X가 적용되지 않았습니다.");
            AssertApproximately(bounds.size.y * 0.7f, boxCollider.size.y,
                "형상 프로필의 콜라이더 높이가 적용되지 않았습니다.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(bottleObject);
            UnityEngine.Object.DestroyImmediate(bottleItem);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
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
        CocktailRecipeCatalog recipes = CocktailRecipeDataLoader.LoadDefault(items);
        Assert(recipes.TryGet("whiskey_neat", out CocktailRecipe whiskey),
            "recipes.csv에서 whiskey_neat 레시피를 불러오지 못했습니다.");
        Assert(whiskey.glassId == "rock"
            && whiskey.iceRequirement == IceRequirement.None
            && whiskey.requiredTechnique == CocktailTechnique.Build,
            "recipes.csv에서 잔·얼음·제조법 조건을 불러오지 못했습니다.");
        Assert(recipes.TryGet("rec_1001", out CocktailRecipe burnhamSour)
            && burnhamSour.isOrderable
            && burnhamSour.ingredients.Count == 4,
            "기획 CSV에서 가져온 번햄 사워 레시피 에셋이 올바르지 않습니다.");

        CocktailOrderTemplateCatalog templates =
            CocktailOrderCsvLoader.LoadTemplatesFromStreamingAssets();
        Assert(templates.Count >= 3, "주문 문장을 불러오지 못했습니다.");
    }

    private static void ValidateBusinessBottleData()
    {
        BusinessBartendingSettings settings =
            Resources.Load<BusinessBartendingSettings>("Bartending/BusinessBartendingSettings");
        Assert(settings != null && settings.bottlePrefab != null,
            "영업 바텐딩 공통 병 프리팹이 설정되지 않았습니다.");
        Assert(settings.bottlePrefab.GetComponent<BottleController>() != null,
            "영업 바텐딩 공통 병 프리팹에 BottleController가 없습니다.");

        ItemDefCatalog items = ItemDefCatalog.LoadFromResources("Items", null);
        List<string> missingItemIds = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:LiquorBottleDef");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            LiquorBottleDef shelfBottle = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(path);
            if (shelfBottle == null || string.IsNullOrWhiteSpace(shelfBottle.id))
                continue;

            if (!items.TryGet(shelfBottle.id, out ItemDef item) || item == null)
            {
                missingItemIds.Add(shelfBottle.id);
                continue;
            }

            Assert(item.type == ItemType.Bottle,
                $"선반 액체 '{shelfBottle.id}'의 ItemDef 종류가 Bottle이 아닙니다.");
            Assert(item.capacityMl > 0f,
                $"선반 액체 '{shelfBottle.id}'의 ItemDef 병 용량이 0 이하입니다.");
            AssertApproximately(shelfBottle.unitVolume, item.capacityMl,
                $"선반 액체 '{shelfBottle.id}'의 병 용량이 ItemDef와 다릅니다.");
            Assert(item.overrideBottleGeometry,
                $"선반 액체 '{shelfBottle.id}'에 병 형상 프로필이 없습니다.");
        }

        if (missingItemIds.Count > 0)
        {
            missingItemIds.Sort(StringComparer.OrdinalIgnoreCase);
            Debug.LogWarning(
                "[바텐딩 시스템 검증] 아직 ItemDef이 준비되지 않은 선반 액체: "
                + string.Join(", ", missingItemIds));
        }
    }

    private static void ValidateLiquidPoolIsolation()
    {
        GameObject particlePrefab = new GameObject("검증용 액체 입자");
        GameObject poolObject = new GameObject("검증용 액체 풀");
        ItemDef firstLiquid = ScriptableObject.CreateInstance<ItemDef>();
        ItemDef secondLiquid = ScriptableObject.CreateInstance<ItemDef>();

        try
        {
            particlePrefab.SetActive(false);
            particlePrefab.AddComponent<LiquidParticleData>();
            particlePrefab.AddComponent<Rigidbody2D>();

            poolObject.SetActive(false);
            LiquidPool pool = poolObject.AddComponent<LiquidPool>();
            pool.particlePrefab = particlePrefab;
            pool.poolSize = 1;

            firstLiquid.id = "qa_first_liquid";
            firstLiquid.type = ItemType.Bottle;
            secondLiquid.id = "qa_second_liquid";
            secondLiquid.type = ItemType.Bottle;

            GameObject firstParticle = pool.GetParticle(Vector3.zero, firstLiquid, 1f);
            Assert(firstParticle != null,
                "첫 번째 액체가 초기 풀에서 생성되지 않았습니다.");

            GameObject secondParticle = pool.GetParticle(Vector3.right, secondLiquid, 1f);
            Assert(secondParticle != null,
                "첫 번째 액체가 초기 풀을 사용한 뒤 두 번째 액체가 생성되지 않았습니다.");
            Assert(pool.TotalParticleCount == 2 && pool.ActiveParticleCount == 2,
                "초기 풀이 소진되었을 때 액체 풀이 동적으로 확장되지 않았습니다.");

            LiquidParticleData secondData = secondParticle.GetComponent<LiquidParticleData>();
            AssertApproximately(1f, secondData.payload.GetVolume(secondLiquid),
                "동적으로 생성된 입자에 두 번째 액체 정보가 적용되지 않았습니다.");
            AssertApproximately(0f, secondData.payload.GetVolume(firstLiquid),
                "두 번째 액체 입자에 첫 번째 액체 정보가 남아 있습니다.");

            Rigidbody2D secondBody = secondParticle.GetComponent<Rigidbody2D>();
            secondData.hasBeenCollected = true;
            secondBody.linearVelocity = new Vector2(3f, -2f);
            secondBody.angularVelocity = 15f;
            Assert(pool.ReturnParticle(secondParticle),
                "사용한 입자가 풀로 반환되지 않았습니다.");
            Assert(!pool.ReturnParticle(secondParticle),
                "같은 입자가 풀에 중복 반환되었습니다.");

            GameObject reusedParticle = pool.GetParticle(Vector3.up, firstLiquid, 0.5f);
            Assert(reusedParticle == secondParticle,
                "반환된 액체 입자가 재사용되지 않았습니다.");

            LiquidParticleData reusedData = reusedParticle.GetComponent<LiquidParticleData>();
            AssertApproximately(0.5f, reusedData.payload.GetVolume(firstLiquid),
                "재사용 입자에 새 액체 정보가 적용되지 않았습니다.");
            AssertApproximately(0f, reusedData.payload.GetVolume(secondLiquid),
                "재사용 입자에 이전 액체 정보가 남아 있습니다.");
            Assert(!reusedData.hasBeenCollected,
                "재사용 입자의 수집 상태가 초기화되지 않았습니다.");
            Assert(reusedParticle.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero,
                "재사용 입자의 이동 속도가 초기화되지 않았습니다.");
            AssertApproximately(0f, reusedParticle.GetComponent<Rigidbody2D>().angularVelocity,
                "재사용 입자의 회전 속도가 초기화되지 않았습니다.");

            Assert(pool.ReturnParticle(firstParticle),
                "첫 번째 검증 입자가 풀로 반환되지 않았습니다.");
            Assert(pool.ReturnParticle(reusedParticle),
                "재사용 검증 입자가 풀로 반환되지 않았습니다.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(poolObject);
            UnityEngine.Object.DestroyImmediate(particlePrefab);
            UnityEngine.Object.DestroyImmediate(firstLiquid);
            UnityEngine.Object.DestroyImmediate(secondLiquid);
        }
    }

    private static void ValidateVesselLiquidTransfer()
    {
        GameObject sourceObject = new GameObject("검증용 원본 용기");
        GameObject targetObject = new GameObject("검증용 대상 용기");
        GameObject particleObject = new GameObject("검증용 이동 액체");
        VesselLiquidTracker sourceTracker = null;
        VesselLiquidTracker targetTracker = null;

        try
        {
            BoxCollider2D sourceTrigger = sourceObject.AddComponent<BoxCollider2D>();
            sourceTrigger.isTrigger = true;
            sourceTrigger.size = Vector2.one * 2f;
            sourceTracker = sourceObject.AddComponent<VesselLiquidTracker>();

            targetObject.transform.position = Vector3.right * 4f;
            BoxCollider2D targetTrigger = targetObject.AddComponent<BoxCollider2D>();
            targetTrigger.isTrigger = true;
            targetTrigger.size = Vector2.one * 2f;
            targetTracker = targetObject.AddComponent<VesselLiquidTracker>();

            CircleCollider2D particleCollider = particleObject.AddComponent<CircleCollider2D>();
            LiquidParticleData particle = particleObject.AddComponent<LiquidParticleData>();

            InvokeNonPublic(sourceTracker, "Awake");
            InvokeNonPublic(targetTracker, "Awake");
            InvokeNonPublic(sourceTracker, "OnEnable");
            InvokeNonPublic(targetTracker, "OnEnable");

            Physics2D.SyncTransforms();
            InvokeNonPublic(sourceTracker, "OnTriggerEnter2D", particleCollider);
            Assert(particle.VesselOwner == sourceTracker,
                "액체 입자가 처음 들어간 용기의 소유권을 얻지 못했습니다.");

            particleObject.transform.position = targetObject.transform.position;
            Physics2D.SyncTransforms();
            InvokeNonPublic(sourceTracker, "OnTriggerExit2D", particleCollider);
            Assert(particle.VesselOwner == null,
                "액체 입자가 원래 용기를 빠져나온 뒤 소유권이 해제되지 않았습니다.");

            InvokeNonPublic(targetTracker, "OnTriggerEnter2D", particleCollider);
            Assert(particle.VesselOwner == targetTracker,
                "액체 입자가 새 용기로 소유권을 이전하지 못했습니다.");

            // Verify the reverse direction even when the new vessel's enter callback
            // arrives before the previous vessel's exit callback.
            particleObject.transform.position = sourceObject.transform.position;
            Physics2D.SyncTransforms();
            InvokeNonPublic(sourceTracker, "OnTriggerEnter2D", particleCollider);
            Assert(particle.VesselOwner == sourceTracker,
                "콜백 순서가 바뀌었을 때 액체 입자가 원래 방향으로 재이전되지 못했습니다.");
        }
        finally
        {
            if (targetTracker != null)
                InvokeNonPublic(targetTracker, "OnDisable");
            if (sourceTracker != null)
                InvokeNonPublic(sourceTracker, "OnDisable");

            UnityEngine.Object.DestroyImmediate(particleObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
            UnityEngine.Object.DestroyImmediate(sourceObject);
        }
    }

    private static void InvokeNonPublic(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target?.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(method != null, $"검증 메서드 '{methodName}'를 찾지 못했습니다.");
        method.Invoke(target, arguments);
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
