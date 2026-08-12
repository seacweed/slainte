using System;
using Slainte.Bartending;
using Slainte.Business;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class LiquorShelfSpawnValidator
{
    private const string BusinessScenePath = "Assets/BusinessScene.unity";
    private const string RunningKey = "Slainte.LiquorShelfSpawnValidator.Running";
    private static int phase;
    private static int phaseFrames;
    private static double phaseStartedAt;

    [MenuItem("Slainte/Bartending/Validate Business Shelf Spawn")]
    public static void RunFromMenu()
    {
        Begin(false);
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
        phaseStartedAt = EditorApplication.timeSinceStartup;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        EditorApplication.update -= ValidateOnUpdate;
        EditorApplication.update += ValidateOnUpdate;
        phaseStartedAt = EditorApplication.timeSinceStartup;
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
                        || bartending.CurrentTargetTracker == null)
                    {
                        return;
                    }

                    ValidateShelfClick(bartending);
                    Finish(true, "BusinessScene shelf click spawned one bottle and rejected a duplicate.");
                    break;
            }
        }
        catch (Exception exception)
        {
            Finish(false, exception.ToString());
        }
    }

    private static void ValidateShelfClick(BusinessBartendingBootstrap bartending)
    {
        LiquorBottleSlotUI slot = FindUsableSlot();
        Require(slot != null, "No unlocked shelf bottle with a matching ItemDef was found.");

        int before = bartending.SessionBottleCount;
        PointerEventData click = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left
        };

        slot.OnPointerClick(click);
        Require(bartending.SessionBottleCount == before + 1,
            $"Shelf click did not add exactly one bottle: {before} -> {bartending.SessionBottleCount}.");

        slot.OnPointerClick(click);
        Require(bartending.SessionBottleCount == before + 1,
            "A duplicate shelf click added the same bottle twice.");
    }

    private static LiquorBottleSlotUI FindUsableSlot()
    {
        ItemDefCatalog catalog = ItemDefCatalog.LoadFromResources("Items", null);
        foreach (LiquorBottleSlotUI slot in Resources.FindObjectsOfTypeAll<LiquorBottleSlotUI>())
        {
            if (slot == null
                || EditorUtility.IsPersistent(slot)
                || slot.gameObject.scene != SceneManager.GetActiveScene())
            {
                continue;
            }

            SerializedProperty definitionProperty =
                new SerializedObject(slot).FindProperty("def");
            LiquorBottleDef definition =
                definitionProperty?.objectReferenceValue as LiquorBottleDef;
            if (definition == null
                || string.IsNullOrWhiteSpace(definition.id)
                || (!string.IsNullOrEmpty(definition.unlockFlagKey)
                    && !GameProgress.Instance.HasFlag(definition.unlockFlagKey))
                || !catalog.TryGet(definition.id, out ItemDef item)
                || item == null
                || item.type != ItemType.Bottle
                || GameProgress.Instance.GetBottleAmount(definition.id, definition.MaxAmount) <= 0f)
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
