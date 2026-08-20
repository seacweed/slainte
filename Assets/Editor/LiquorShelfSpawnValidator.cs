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
    private const string ManualToolCabinetKey =
        "Slainte.ToolCabinetManualPlaytest.Running";
    private const string ManualToolCabinetReadyKey =
        "Slainte.ToolCabinetManualPlaytest.Ready";
    private const string ManualToolCabinetAutoStartKey =
        "Slainte.ToolCabinetManualPlaytest.PreviousAutoStart";
    private static int phase;
    private static int phaseFrames;
    private static double phaseStartedAt;

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
