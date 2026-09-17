using System;
using System.Collections.Generic;
using Slainte.Bartending;
using Slainte.Business;
using Slainte.Content;
using Slainte.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LiquorShelfSpawnValidator
{
    private const string BusinessScenePath = ProjectScenePaths.Business;
    private const string RunningKey = "Slainte.LiquorShelfSpawnValidator.Running";
    private const string ManualToolCabinetKey =
        "Slainte.ToolCabinetManualPlaytest.Running";
    private const string ManualToolCabinetReadyKey =
        "Slainte.ToolCabinetManualPlaytest.Ready";
    private const string ManualToolCabinetAutoStartKey =
        "Slainte.ToolCabinetManualPlaytest.PreviousAutoStart";
    private static int phase;
    private static int phaseFrames;
    private static double phaseStartedAt;
    private static BottleController spawnedBottle;
    private static LiquorBottleDef spawnedDefinition;

    [MenuItem("Slainte/Bartending/Validate Business Shelf Spawn")]
    public static void RunFromMenu()
    {
        Begin(false);
    }

    [MenuItem("Slainte/Bartending/Prepare Tool Cabinet Manual Playtest")]
    public static void PrepareToolCabinetManualPlaytest()
    {
        if (EditorApplication.isPlaying)
        {
            PrepareManualPlaytestOnUpdate();
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(BusinessScenePath, OpenSceneMode.Single);
        GameObject isolationHost = new GameObject("__ToolCabinetManualPlaytestIsolation");
        SceneManager.MoveGameObjectToScene(isolationHost, scene);
        PlaytestProgressIsolation.Attach(isolationHost);
        BusinessOrderFlowSettings flowSettings =
            Resources.Load<BusinessOrderFlowSettings>(
                ProjectResourcePaths.BusinessOrderFlowSettings);
        if (flowSettings != null)
        {
            SessionState.SetBool(ManualToolCabinetAutoStartKey, flowSettings.autoStart);
            flowSettings.autoStart = false;
        }
        SessionState.SetBool(ManualToolCabinetKey, true);
        SessionState.SetBool(ManualToolCabinetReadyKey, false);
        EditorApplication.update -= PrepareManualPlaytestOnUpdate;
        EditorApplication.update += PrepareManualPlaytestOnUpdate;
        EditorApplication.isPlaying = true;
    }

    public static void RunFromCommandLine()
    {
        Begin(true);
    }

    private static void Begin(bool commandLine)
    {
        Scene scene = EditorSceneManager.OpenScene(BusinessScenePath, OpenSceneMode.Single);
        GameObject isolationHost = new GameObject("__LiquorShelfValidationIsolation");
        SceneManager.MoveGameObjectToScene(isolationHost, scene);
        PlaytestProgressIsolation.Attach(isolationHost);

        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(RunningKey + ".CommandLine", commandLine);
        phase = 0;
        phaseFrames = 0;
        spawnedBottle = null;
        spawnedDefinition = null;
        phaseStartedAt = EditorApplication.timeSinceStartup;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(ManualToolCabinetKey, false))
        {
            EditorApplication.update -= PrepareManualPlaytestOnUpdate;
            EditorApplication.update += PrepareManualPlaytestOnUpdate;
        }

        if (!SessionState.GetBool(RunningKey, false))
            return;

        EditorApplication.update -= ValidateOnUpdate;
        EditorApplication.update += ValidateOnUpdate;
        phaseStartedAt = EditorApplication.timeSinceStartup;
    }

    private static void PrepareManualPlaytestOnUpdate()
    {
        if (!EditorApplication.isPlaying)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.update -= PrepareManualPlaytestOnUpdate;
                RestoreBusinessAutoStartForManualPlaytest();
                SessionState.EraseBool(ManualToolCabinetKey);
                SessionState.EraseBool(ManualToolCabinetReadyKey);
            }

            return;
        }

        GameModeManager modeManager =
            UnityEngine.Object.FindFirstObjectByType<GameModeManager>();
        BusinessBartendingBootstrap bartending =
            UnityEngine.Object.FindFirstObjectByType<BusinessBartendingBootstrap>();
        if (modeManager == null || bartending == null)
            return;

        BusinessShiftController[] shifts =
            UnityEngine.Object.FindObjectsByType<BusinessShiftController>(
                FindObjectsSortMode.None);
        for (int i = 0; i < shifts.Length; i++)
            shifts[i].enabled = false;

        if (modeManager.CurrentMode != GameMode.CraftingMode)
        {
            modeManager.RequestModeChange(GameMode.CraftingMode);
            return;
        }

        if (!bartending.IsSessionReady)
            return;

        if (SessionState.GetBool(ManualToolCabinetReadyKey, false))
            return;

        SessionState.SetBool(ManualToolCabinetReadyKey, true);
        Debug.Log(
            "[ToolCabinetManualPlaytest] READY: progress writes are isolated and CraftingMode is active.");
    }

    private static void RestoreBusinessAutoStartForManualPlaytest()
    {
        BusinessOrderFlowSettings flowSettings =
            Resources.Load<BusinessOrderFlowSettings>(
                ProjectResourcePaths.BusinessOrderFlowSettings);
        if (flowSettings != null
            && SessionState.GetBool(ManualToolCabinetAutoStartKey, false))
        {
            flowSettings.autoStart = true;
        }

        SessionState.EraseBool(ManualToolCabinetAutoStartKey);
    }

    private static void ValidateOnUpdate()
    {
        if (!EditorApplication.isPlaying)
            return;

        if (EditorApplication.timeSinceStartup - phaseStartedAt > 30d)
        {
            Finish(false, "Timed out waiting for the BusinessScene shelf validation.");
            return;
        }

        try
        {
            phaseFrames++;
            GameModeManager modeManager = UnityEngine.Object.FindFirstObjectByType<GameModeManager>();
            BusinessBartendingBootstrap bartending =
                UnityEngine.Object.FindFirstObjectByType<BusinessBartendingBootstrap>();

            switch (phase)
            {
                case 0:
                    if (phaseFrames < 5 || modeManager == null || bartending == null)
                        return;

                    Require(DataManager.AreDiskWritesSuppressed,
                        "Playtest progress isolation did not suppress disk writes.");
                    modeManager.RequestModeChange(GameMode.CraftingMode);
                    phase = 1;
                    phaseFrames = 0;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 1:
                    if (modeManager == null
                        || bartending == null
                        || modeManager.CurrentMode != GameMode.CraftingMode
                        || !bartending.IsSessionReady)
                    {
                        return;
                    }

                    spawnedBottle = ValidateShelfClick(bartending, out spawnedDefinition);
                    phase = 2;
                    phaseFrames = 0;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 2:
                    if (phaseFrames < 2)
                        return;

                    ValidateSpawnedBottleGeometry(spawnedBottle, spawnedDefinition);
                    ConsumeFromSpawnedBottle(spawnedBottle, spawnedDefinition);
                    modeManager.RequestModeChange(GameMode.OrderMode);
                    phase = 3;
                    phaseFrames = 0;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 3:
                    if (bartending == null || bartending.IsSessionReady || phaseFrames < 2)
                        return;

                    ValidateAutomaticReturn(bartending, spawnedDefinition);
                    Finish(true,
                        "Ingredient stock spawned the opened bottle first, allowed duplicates, "
                        + "stopped at zero stock, merged remaining volume back on session end, "
                        + "and applied the barSprite-specific click collider.");
                    break;
            }
        }
        catch (Exception exception)
        {
            Finish(false, exception.ToString());
        }
    }

    private const float PartialBottleMl = 100f;
    private const float ConsumedMl = 50f;
    private static float expectedTotalAfterConsume;

    // 재고를 "가득 찬 병 1개 + 따 둔 병(100ml)"으로 맞춘 뒤, 같은 재료를 연속으로 꺼내
    // 따 둔 병 우선·중복 허용·재고 0에서 거부를 확인한다. 반환된 병은 기하 검증에 쓰인다.
    private static BottleController ValidateShelfClick(
        BusinessBartendingBootstrap bartending,
        out LiquorBottleDef definition)
    {
        IngredientSlotUI slot = FindUsableSlot();
        Require(slot != null, "No unlocked ingredient slot with a matching ItemDef was found.");
        definition = slot.Definition;
        Require(definition != null && definition.item != null,
            "The usable ingredient slot has no bottle definition.");

        float capacity = Mathf.Max(1f, definition.item.capacityMl);
        float partial = Mathf.Min(PartialBottleMl, capacity * 0.5f);
        GameProgress.Instance.SetBottleAmount(definition.InventoryId, capacity + partial);
        Require(Mathf.Abs(bartending.GetShelfAmount(definition) - (capacity + partial)) <= 0.01f,
            "Shelf amount does not match the prepared inventory total.");

        int before = bartending.SessionBottleCount;
        BottleController opened = PlaceAndFind(bartending, definition);
        Require(bartending.SessionBottleCount == before + 1,
            $"The first take-out did not add exactly one bottle: {before} -> {bartending.SessionBottleCount}.");
        Require(Mathf.Abs(opened.CurrentCapacity - partial) <= 0.01f,
            $"The opened bottle was not taken out first: capacity={opened.CurrentCapacity}, expected={partial}.");

        BottleController full = PlaceAndFind(bartending, definition);
        Require(bartending.SessionBottleCount == before + 2,
            "Taking out the same ingredient twice was rejected.");
        Require(Mathf.Abs(full.CurrentCapacity - capacity) <= 0.01f,
            $"The second bottle was not full: capacity={full.CurrentCapacity}, expected={capacity}.");
        Require(bartending.GetShelfAmount(definition) <= 0.01f,
            "Shelf amount did not reach zero after taking out every bottle.");

        Require(!bartending.TryPlaceBottleFromShelf(definition, out _),
            "An out-of-stock ingredient still produced a bottle.");
        Require(bartending.SessionBottleCount == before + 2,
            "An out-of-stock take-out changed the session bottle count.");

        expectedTotalAfterConsume = capacity + partial - ConsumedMl;
        return full;
    }

    private static BottleController PlaceAndFind(
        BusinessBartendingBootstrap bartending,
        LiquorBottleDef definition)
    {
        HashSet<int> existingBottleIds = new HashSet<int>();
        foreach (BottleController existing in UnityEngine.Object.FindObjectsByType<BottleController>(
                     FindObjectsSortMode.None))
        {
            existingBottleIds.Add(existing.GetInstanceID());
        }

        Require(bartending.TryPlaceBottleFromShelf(definition, out string failure),
            $"Taking out {definition.id} failed: {failure}");

        foreach (BottleController candidate in UnityEngine.Object.FindObjectsByType<BottleController>(
                     FindObjectsSortMode.None))
        {
            if (!existingBottleIds.Contains(candidate.GetInstanceID())
                && candidate.BottleData == definition.item)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No runtime BottleController was created for {definition.id}.");
    }

    // 따른 것과 같은 경로(CapacityChanged)로 잔량을 줄여 총량이 소비량만큼만 줄어드는지 확인한다.
    private static void ConsumeFromSpawnedBottle(BottleController bottle, LiquorBottleDef definition)
    {
        bottle.SetCurrentCapacity(bottle.CurrentCapacity - ConsumedMl, notify: true);
        float total = GameProgress.Instance.GetBottleAmount(definition.InventoryId, -1f);
        Require(Mathf.Abs(total - expectedTotalAfterConsume) <= 0.01f,
            $"Pouring did not reduce the inventory total by the consumed volume: total={total}, "
            + $"expected={expectedTotalAfterConsume}.");
    }

    // 제조 세션이 끝나면 슬롯에 남은 병의 잔량이 술장 재고로 합쳐져야 한다.
    private static void ValidateAutomaticReturn(
        BusinessBartendingBootstrap bartending,
        LiquorBottleDef definition)
    {
        Require(bartending.SessionBottleCount == 0,
            "Bottles are still registered after the crafting session ended.");
        Require(Mathf.Abs(bartending.GetShelfAmount(definition) - expectedTotalAfterConsume) <= 0.01f,
            $"Remaining bottle volume was not merged back into the shelf: "
            + $"shelf={bartending.GetShelfAmount(definition)}, expected={expectedTotalAfterConsume}.");
    }

    private static void ValidateSpawnedBottleGeometry(
        BottleController bottle,
        LiquorBottleDef definition)
    {
        Require(bottle != null && bottle.gameObject.activeInHierarchy,
            "The spawned shelf bottle is no longer active.");
        Require(bottle.GetComponent<BartendingItemOrder>() != null,
            "The spawned shelf bottle did not initialize its click ordering component.");

        SpriteRenderer renderer = bottle.GetComponent<SpriteRenderer>();
        BoxCollider2D collider = bottle.GetComponent<BoxCollider2D>();
        Require(renderer != null && renderer.enabled && renderer.sprite != null,
            "The spawned shelf bottle has no active barSprite renderer.");
        Require(collider != null && collider.enabled,
            "The spawned shelf bottle has no active BoxCollider2D.");
        Require(definition != null && definition.item == bottle.BottleData,
            "The spawned shelf bottle is not linked to the clicked shelf ItemDef.");
        Require(definition.GetBarSprite(bottle.BottleData.icon) == renderer.sprite,
            "The spawned shelf bottle is not using the definition's barSprite.");
        ValidateSpriteSize(bottle.gameObject, renderer, "shelf bottle", 0.7f);
        Require(BottleSpriteGeometry.TryCalculate(
                renderer.sprite,
                out Vector2 expectedCenterNormalized,
                out Vector2 expectedSizeNormalized,
                out string failure),
            "The spawned bottle barSprite geometry could not be calculated: " + failure);

        Bounds spriteBounds = renderer.sprite.bounds;
        Vector2 expectedOffset = new Vector2(
            Mathf.Lerp(spriteBounds.min.x, spriteBounds.max.x, expectedCenterNormalized.x),
            Mathf.Lerp(spriteBounds.min.y, spriteBounds.max.y, expectedCenterNormalized.y));
        Vector2 expectedSize = new Vector2(
            spriteBounds.size.x * expectedSizeNormalized.x,
            spriteBounds.size.y * expectedSizeNormalized.y);
        Require(Vector2.Distance(collider.offset, expectedOffset) <= 0.0005f,
            $"Spawned bottle collider offset does not match its barSprite: "
            + $"actual={collider.offset}, expected={expectedOffset}");
        Require(Vector2.Distance(collider.size, expectedSize) <= 0.0005f,
            $"Spawned bottle collider size does not match its barSprite: "
            + $"actual={collider.size}, expected={expectedSize}");

        ItemDef item = bottle.BottleData;
        if (item.overrideBottleGeometry || item.overrideBottleLiquidSpawn)
        {
            Require(bottle.liquidSpawnPoint != null,
                "The spawned bottle has no liquid spawn point transform.");
            Vector2 mouthNormalized = new Vector2(
                Mathf.Clamp01(item.liquidSpawnNormalized.x),
                Mathf.Clamp01(item.liquidSpawnNormalized.y));
            if (renderer.flipX)
                mouthNormalized.x = 1f - mouthNormalized.x;
            if (renderer.flipY)
                mouthNormalized.y = 1f - mouthNormalized.y;

            Vector3 expectedLocalMouth = new Vector3(
                Mathf.Lerp(spriteBounds.min.x, spriteBounds.max.x, mouthNormalized.x),
                Mathf.Lerp(spriteBounds.min.y, spriteBounds.max.y, mouthNormalized.y),
                0f);
            if (item.overrideBottleLiquidSpawn)
            {
                float direction = renderer.flipY ? -1f : 1f;
                expectedLocalMouth.y += direction
                    * Mathf.Max(0f, item.liquidSpawnOutwardPixels)
                    / Mathf.Max(1f, renderer.sprite.pixelsPerUnit);
            }

            Vector3 expectedWorldMouth = renderer.transform.TransformPoint(expectedLocalMouth);
            Require(Vector2.Distance(
                    bottle.liquidSpawnPoint.position,
                    expectedWorldMouth) <= 0.0005f,
                $"Spawned bottle liquid point does not match its configured mouth: "
                + $"actual={bottle.liquidSpawnPoint.position}, expected={expectedWorldMouth}");
        }

        Physics2D.SyncTransforms();
        Vector2[] localSamples =
        {
            collider.offset,
            collider.offset + new Vector2(collider.size.x * 0.4f, 0f),
            collider.offset - new Vector2(collider.size.x * 0.4f, 0f),
            collider.offset + new Vector2(0f, collider.size.y * 0.4f),
            collider.offset - new Vector2(0f, collider.size.y * 0.4f)
        };
        for (int i = 0; i < localSamples.Length; i++)
        {
            Vector3 worldSample = bottle.transform.TransformPoint(localSamples[i]);
            Require(collider.OverlapPoint(worldSample),
                $"Spawned bottle click collider missed representative point {i}: {worldSample}");
        }
    }

    private static void ValidateSpriteSize(
        GameObject itemObject,
        SpriteRenderer renderer,
        string label,
        float scaleMultiplier)
    {
        Require(itemObject != null && renderer != null && renderer.sprite != null,
            $"{label} has no reference sprite renderer.");
        Require(BartendingViewport.TryConvertActiveCanvasPixelsToWorld(
                renderer.sprite.rect.size * scaleMultiplier,
                out Vector2 expectedWorldSize),
            $"Could not convert the source pixels for {label}.");

        Vector2 actualWorldSize = renderer.bounds.size;
        Require(Vector2.Distance(actualWorldSize, expectedWorldSize) <= 0.002f,
            $"{label} has the wrong source-relative size: actual={actualWorldSize}, "
            + $"expected={expectedWorldSize}, sprite={renderer.sprite.rect.size}.");
    }

    private static IngredientSlotUI FindUsableSlot()
    {
        foreach (IngredientSlotUI slot in Resources.FindObjectsOfTypeAll<IngredientSlotUI>())
        {
            if (slot == null
                || EditorUtility.IsPersistent(slot)
                || slot.gameObject.scene != SceneManager.GetActiveScene())
            {
                continue;
            }

            LiquorBottleDef definition = slot.Definition;
            if (definition == null
                || string.IsNullOrWhiteSpace(definition.id)
                || definition.item == null
                || definition.item.type != ItemType.Bottle)
            {
                continue;
            }

            return slot;
        }

        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Finish(bool success, string message)
    {
        EditorApplication.update -= ValidateOnUpdate;
        SessionState.EraseBool(RunningKey);
        bool commandLine = SessionState.GetBool(RunningKey + ".CommandLine", false);
        SessionState.EraseBool(RunningKey + ".CommandLine");
        spawnedBottle = null;
        spawnedDefinition = null;

        if (success)
            Debug.Log("[LiquorShelfSpawnValidator] PASS: " + message);
        else
            Debug.LogError("[LiquorShelfSpawnValidator] FAIL: " + message);

        if (commandLine)
            EditorApplication.Exit(success ? 0 : 1);
        else
            EditorApplication.isPlaying = false;
    }
}
