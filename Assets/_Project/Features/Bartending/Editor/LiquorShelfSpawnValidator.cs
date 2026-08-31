using System;
using System.Collections.Generic;
using Slainte.Bartending;
using Slainte.Business;
using Slainte.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class LiquorShelfSpawnValidator
{
    private const string BusinessScenePath =
        "Assets/_Project/Scenes/Production/BusinessScene.unity";
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
            Resources.Load<BusinessOrderFlowSettings>("Business/BusinessOrderFlowSettings");
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
            Resources.Load<BusinessOrderFlowSettings>("Business/BusinessOrderFlowSettings");
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
                    Finish(true,
                        "BusinessScene shelf click spawned one bottle, rejected a duplicate, "
                        + "and applied the barSprite-specific click collider.");
                    break;
            }
        }
        catch (Exception exception)
        {
            Finish(false, exception.ToString());
        }
    }

    private static BottleController ValidateShelfClick(
        BusinessBartendingBootstrap bartending,
        out LiquorBottleDef definition)
    {
        LiquorBottleSlotUI slot = FindUsableSlot();
        Require(slot != null, "No unlocked shelf bottle with a matching ItemDef was found.");
        definition = GetSlotDefinition(slot);
        Require(definition != null && definition.item != null,
            "The usable shelf slot has no bottle definition.");

        int before = bartending.SessionBottleCount;
        HashSet<int> existingBottleIds = new HashSet<int>();
        foreach (BottleController existing in UnityEngine.Object.FindObjectsByType<BottleController>(
                     FindObjectsSortMode.None))
        {
            existingBottleIds.Add(existing.GetInstanceID());
        }

        PointerEventData click = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left
        };

        slot.OnPointerClick(click);
        Require(bartending.SessionBottleCount == before + 1,
            $"Shelf click did not add exactly one bottle: {before} -> {bartending.SessionBottleCount}.");

        BottleController created = null;
        foreach (BottleController candidate in UnityEngine.Object.FindObjectsByType<BottleController>(
                     FindObjectsSortMode.None))
        {
            if (!existingBottleIds.Contains(candidate.GetInstanceID())
                && candidate.BottleData == definition.item)
            {
                created = candidate;
                break;
            }
        }
        Require(created != null,
            $"Shelf click created no runtime BottleController for {definition.id}.");

        slot.OnPointerClick(click);
        Require(bartending.SessionBottleCount == before + 1,
            "A duplicate shelf click added the same bottle twice.");
        return created;
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

    private static LiquorBottleDef GetSlotDefinition(LiquorBottleSlotUI slot)
    {
        SerializedProperty definitionProperty =
            new SerializedObject(slot).FindProperty("def");
        return definitionProperty?.objectReferenceValue as LiquorBottleDef;
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

    private static LiquorBottleSlotUI FindUsableSlot()
    {
        foreach (LiquorBottleSlotUI slot in Resources.FindObjectsOfTypeAll<LiquorBottleSlotUI>())
        {
            if (slot == null
                || EditorUtility.IsPersistent(slot)
                || slot.gameObject.scene != SceneManager.GetActiveScene())
            {
                continue;
            }

            LiquorBottleDef definition = GetSlotDefinition(slot);
            if (definition == null
                || string.IsNullOrWhiteSpace(definition.id)
                || definition.item == null
                || !string.Equals(
                    definition.InventoryId,
                    definition.id,
                    StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(definition.unlockFlagKey)
                    && !GameProgress.Instance.HasFlag(definition.unlockFlagKey))
                || definition.item.type != ItemType.Bottle
                || GameProgress.Instance.EnsureBottleAmount(
                    definition.InventoryId,
                    definition.DefaultAmount) <= 0f)
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
