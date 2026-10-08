using System;
using System.Collections.Generic;
using System.Linq;
using Slainte.Business;
using Slainte.Content;
using Slainte.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal enum PlaytestLaunchKind
{
    Episode,
    Day
}

[Serializable]
internal sealed class PlaytestLaunchRequest
{
    public PlaytestLaunchKind kind;
    public string episodeId;
    public int targetDay = 1;
    public bool bypassEpisodeConditions = true;
    public bool prepareScheduledBaseline = true;
}

public sealed class PlaytestLauncherWindow : EditorWindow
{
    private enum LauncherTab
    {
        Episode,
        Day
    }

    private readonly List<EpisodeData> episodes = new List<EpisodeData>();
    private LauncherTab selectedTab;
    private Vector2 episodeScroll;
    private string episodeSearch = string.Empty;
    private string selectedEpisodeId = string.Empty;
    private int episodeDay = 1;
    private int targetDay = 1;
    private bool bypassEpisodeConditions = true;
    private bool prepareScheduledBaseline = true;

    [MenuItem("Slainte/Playtest Launcher")]
    public static void Open()
    {
        PlaytestLauncherWindow window = GetWindow<PlaytestLauncherWindow>(
            "Playtest Launcher");
        window.minSize = new Vector2(560f, 560f);
        window.Show();
    }

    private void OnEnable()
    {
        RefreshEpisodes();
    }

    private void OnProjectChange()
    {
        RefreshEpisodes();
        Repaint();
    }

    private void OnInspectorUpdate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Episode & Day Playtest Launcher", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Every launch uses an isolated progress snapshot. Disk save writes and save deletion "
            + "remain disabled until Play Mode ends. The Game View receives keyboard focus after launch.",
            MessageType.Info);

        if (EditorApplication.isPlaying)
        {
            DrawRunningState();
            return;
        }

        selectedTab = (LauncherTab)GUILayout.Toolbar(
            (int)selectedTab,
            new[] { "Episode", "Day" });
        EditorGUILayout.Space(8f);

        if (selectedTab == LauncherTab.Episode)
            DrawEpisodeTab();
        else
            DrawDayTab();

        EditorGUILayout.Space(10f);
        string status = PlaytestLaunchCoordinator.Status;
        if (!string.IsNullOrWhiteSpace(status))
            EditorGUILayout.HelpBox(status, ResolveStatusType(status));
    }

    private void DrawRunningState()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            string.IsNullOrWhiteSpace(PlaytestLaunchCoordinator.Status)
                ? "The isolated playtest is running."
                : PlaytestLaunchCoordinator.Status,
            MessageType.Info);
        EditorGUILayout.HelpBox(
            "Shortcuts: S opens the drawer, W closes it, and E toggles the order ticket. "
            + "If they do not respond, click the Game View once. Episode dialogue intentionally locks them.",
            MessageType.None);

        if (GUILayout.Button("Stop Isolated Playtest", GUILayout.Height(34f)))
            EditorApplication.isPlaying = false;
    }

    private void DrawEpisodeTab()
    {
        EditorGUILayout.LabelField("Episode Test", EditorStyles.boldLabel);
        episodeSearch = EditorGUILayout.TextField("Search", episodeSearch);

        List<EpisodeData> visibleEpisodes = episodes
            .Where(IsVisibleEpisode)
            .ToList();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            episodeScroll = EditorGUILayout.BeginScrollView(
                episodeScroll,
                GUILayout.Height(220f));

            if (visibleEpisodes.Count == 0)
            {
                EditorGUILayout.LabelField("No matching episodes.");
            }
            else
            {
                foreach (EpisodeData episode in visibleEpisodes)
                {
                    bool selected = string.Equals(
                        selectedEpisodeId,
                        episode.episodeId,
                        StringComparison.OrdinalIgnoreCase);
                    string label = BuildEpisodeLabel(episode);
                    if (GUILayout.Toggle(selected, label, "Button") && !selected)
                    {
                        selectedEpisodeId = episode.episodeId;
                        episodeDay = GetMinimumDay(episode);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        EpisodeData selectedEpisode = GetSelectedEpisode();
        if (selectedEpisode == null)
        {
            EditorGUILayout.HelpBox("Select an episode to continue.", MessageType.Warning);
            return;
        }

        DrawEpisodeDetails(selectedEpisode);
        episodeDay = Mathf.Max(1, EditorGUILayout.IntField("Progress Day", episodeDay));
        bypassEpisodeConditions = EditorGUILayout.ToggleLeft(
            "Bypass trigger condition",
            bypassEpisodeConditions);

        EditorGUILayout.HelpBox(
            "The episode runs as the first customer slot of a Business day "
            + "and then the day continues with the remaining slots.",
            MessageType.Info);

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Start Isolated Episode Test", GUILayout.Height(38f)))
        {
            PlaytestLaunchCoordinator.Queue(new PlaytestLaunchRequest
            {
                kind = PlaytestLaunchKind.Episode,
                episodeId = selectedEpisode.episodeId,
                targetDay = episodeDay,
                bypassEpisodeConditions = bypassEpisodeConditions
            });
        }
    }

    private void DrawDayTab()
    {
        EditorGUILayout.LabelField("Day Flow Test", EditorStyles.boldLabel);
        targetDay = Mathf.Max(1, EditorGUILayout.IntField("Target Day", targetDay));
        prepareScheduledBaseline = EditorGUILayout.ToggleLeft(
            "Prepare episodes scheduled before this day as completed",
            prepareScheduledBaseline);

        EditorGUILayout.HelpBox(
            "Day 1 starts through StartFirstDay. Later days stage Day N-1, then use "
            + "StartBusinessDay so the runtime advances to the requested day. The test continues "
            + "through Business (scheduled episodes and random customers), Settlement, and Rest.",
            MessageType.Info);

        if (prepareScheduledBaseline)
        {
            EditorGUILayout.HelpBox(
                "The baseline completes episodes scheduled before this day and reopens episodes "
                + "scheduled for this or later days. Flags, money, and affinity remain based on "
                + "the current save, so branch episodes may still differ from a real playthrough.",
                MessageType.None);
        }

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Start Isolated Day Test", GUILayout.Height(38f)))
        {
            PlaytestLaunchCoordinator.Queue(new PlaytestLaunchRequest
            {
                kind = PlaytestLaunchKind.Day,
                targetDay = targetDay,
                prepareScheduledBaseline = prepareScheduledBaseline
            });
        }
    }

    private void DrawEpisodeDetails(EpisodeData episode)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Title", episode.episodeTitle ?? string.Empty);
            EditorGUILayout.LabelField("ID", episode.episodeId ?? string.Empty);
            EditorGUILayout.LabelField("Schedule", DescribeSchedule(episode));

            string prerequisites = DescribePrerequisites(episode);
            if (!string.IsNullOrWhiteSpace(prerequisites))
                EditorGUILayout.LabelField("Prerequisites", prerequisites, EditorStyles.wordWrappedLabel);
        }
    }

    private void RefreshEpisodes()
    {
        string retainedId = selectedEpisodeId;
        episodes.Clear();
        episodes.AddRange(Resources.LoadAll<EpisodeData>(
            ProjectResourcePaths.NarrativeEpisodes)
            .Where(episode => episode != null && !string.IsNullOrWhiteSpace(episode.episodeId))
            .OrderBy(GetMinimumDay)
            .ThenBy(episode => episode.scheduledSlot)
            .ThenByDescending(episode => episode.slotPriority)
            .ThenBy(episode => episode.episodeTitle)
            .ThenBy(episode => episode.episodeId));

        EpisodeData retained = episodes.FirstOrDefault(episode => string.Equals(
            episode.episodeId,
            retainedId,
            StringComparison.OrdinalIgnoreCase));
        EpisodeData selection = retained ?? episodes.FirstOrDefault();
        selectedEpisodeId = selection?.episodeId ?? string.Empty;
        if (selection != null && retained == null)
            episodeDay = GetMinimumDay(selection);
    }

    private bool IsVisibleEpisode(EpisodeData episode)
    {
        if (episode == null)
            return false;

        if (string.IsNullOrWhiteSpace(episodeSearch))
            return true;

        return (episode.episodeTitle?.IndexOf(episodeSearch,
                    StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
            || (episode.episodeId?.IndexOf(episodeSearch,
                    StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
    }

    private EpisodeData GetSelectedEpisode()
    {
        return episodes.FirstOrDefault(episode => string.Equals(
            episode.episodeId,
            selectedEpisodeId,
            StringComparison.OrdinalIgnoreCase));
    }

    // 일정이 있으면 배정된 날짜, 없으면 등장 조건의 최소 날짜를 기본 테스트 날짜로 쓴다.
    private static int GetMinimumDay(EpisodeData episode)
    {
        if (episode != null && episode.IsScheduled)
            return episode.scheduledDay;
        return Mathf.Max(1, episode?.triggerCondition?.minDay ?? 1);
    }

    private static string DescribeSchedule(EpisodeData episode)
    {
        return episode.IsScheduled
            ? $"Day {episode.scheduledDay} / Slot {episode.scheduledSlot} / Priority {episode.slotPriority}"
            : "Unscheduled";
    }

    private static string BuildEpisodeLabel(EpisodeData episode)
    {
        string title = string.IsNullOrWhiteSpace(episode.episodeTitle)
            ? episode.episodeId
            : episode.episodeTitle;
        string slot = episode.IsScheduled ? $"S{episode.scheduledSlot}" : "--";
        return $"Day {GetMinimumDay(episode),2} {slot}  |  {title}  [{episode.episodeId}]";
    }

    private static string DescribePrerequisites(EpisodeData episode)
    {
        EpisodeTriggerCondition trigger = episode?.triggerCondition;
        if (trigger == null)
            return string.Empty;

        List<string> parts = new List<string>();
        if (trigger.prerequisiteEpisodeIds != null && trigger.prerequisiteEpisodeIds.Count > 0)
            parts.Add("episodes: " + string.Join(", ", trigger.prerequisiteEpisodeIds));
        if (trigger.requiredFlags != null && trigger.requiredFlags.Count > 0)
            parts.Add("flags: " + string.Join(", ", trigger.requiredFlags));
        if (trigger.minMoney > 0)
            parts.Add("money: " + trigger.minMoney);
        return string.Join(" | ", parts);
    }

    private static MessageType ResolveStatusType(string status)
    {
        return status.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
            ? MessageType.Error
            : MessageType.Info;
    }
}

[InitializeOnLoad]
internal static class PlaytestLaunchCoordinator
{
    private const string StartScenePath = ProjectScenePaths.Rest;
    private const string ActiveKey = "Slainte.PlaytestLauncher.Active";
    private const string RequestKey = "Slainte.PlaytestLauncher.Request";
    private const string StageKey = "Slainte.PlaytestLauncher.Stage";
    private const string StatusKey = "Slainte.PlaytestLauncher.Status";
    private const string OwnsStartSceneKey = "Slainte.PlaytestLauncher.OwnsStartScene";
    private const string HadPreviousStartSceneKey = "Slainte.PlaytestLauncher.HadPreviousStartScene";
    private const string PreviousStartScenePathKey = "Slainte.PlaytestLauncher.PreviousStartScenePath";
    private const double LaunchTimeoutSeconds = 45d;

    private static double waitStartedAt;

    public static string Status => SessionState.GetString(StatusKey, string.Empty);

    static PlaytestLaunchCoordinator()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    public static void Queue(PlaytestLaunchRequest request)
    {
        if (request == null || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        SceneAsset startScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartScenePath);
        if (startScene == null)
        {
            SetStatus("FAIL: The playtest start scene could not be found at " + StartScenePath);
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            SetStatus("Launch cancelled because modified scenes were not saved.");
            return;
        }

        SceneAsset previousStartScene = EditorSceneManager.playModeStartScene;
        SessionState.SetBool(HadPreviousStartSceneKey, previousStartScene != null);
        SessionState.SetString(
            PreviousStartScenePathKey,
            previousStartScene != null ? AssetDatabase.GetAssetPath(previousStartScene) : string.Empty);
        SessionState.SetBool(OwnsStartSceneKey, true);
        EditorSceneManager.playModeStartScene = startScene;

        request.targetDay = Mathf.Max(1, request.targetDay);
        SessionState.SetString(RequestKey, JsonUtility.ToJson(request));
        SessionState.SetInt(StageKey, 0);
        SessionState.SetBool(ActiveKey, true);
        waitStartedAt = EditorApplication.timeSinceStartup;
        SetStatus("Entering Play Mode and preparing isolated progress...");
        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying)
            return;

        if (waitStartedAt <= 0d)
            waitStartedAt = EditorApplication.timeSinceStartup;
        if (EditorApplication.timeSinceStartup - waitStartedAt > LaunchTimeoutSeconds)
        {
            Fail("Timed out while waiting for the playtest runtime.");
            return;
        }

        try
        {
            PlaytestLaunchRequest request = ReadRequest();
            if (request == null)
                throw new InvalidOperationException("The queued playtest request is missing.");

            if (SessionState.GetInt(StageKey, 0) == 0)
                DispatchInitialRequest(request);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Fail(exception.Message);
        }
    }

    private static void DispatchInitialRequest(PlaytestLaunchRequest request)
    {
        GameProgress progress = GameProgress.Instance;
        EpisodeManager episodeManager = EpisodeManager.Instance;
        DayFlowController dayFlow = DayFlowController.Instance;
        GameManager gameManager = GameManager.Instance;
        if (progress == null || episodeManager == null || dayFlow == null || gameManager == null)
            return;

        PlaytestProgressIsolation isolation = progress.GetComponent<PlaytestProgressIsolation>();
        if (isolation == null)
        {
            PlaytestProgressIsolation.Attach(progress.gameObject);
            SetStatus("Disabling disk saves and capturing the progress snapshot...");
            return;
        }

        if (!isolation.IsReady)
            return;

        isolation.RestoreNow();
        if (request.kind == PlaytestLaunchKind.Day)
        {
            LaunchDay(request, progress, dayFlow, isolation);
            SessionState.SetInt(StageKey, 2);
            SetStatus($"Running isolated Day {request.targetDay} flow test.");
            FocusGameView();
            return;
        }

        EpisodeData episode = episodeManager.GetEpisodeData(request.episodeId);
        if (episode == null)
            throw new InvalidOperationException("Episode data was not found: " + request.episodeId);

        if (!isolation.PrepareIncompleteEpisode(episode.episodeId))
            throw new InvalidOperationException("Failed to prepare the episode completion state.");

        progress.SetCurrentDay(request.targetDay);
        progress.ResetDaySettlement();
        if (!request.bypassEpisodeConditions
            && !ProgressConditionEvaluator.IsMet(episode.triggerCondition, progress))
        {
            throw new InvalidOperationException("The selected episode trigger condition is not met.");
        }

        // 영업 씬이 로드되면서 영업이 자동으로 시작되므로, 상태 전환 전에 1번 슬롯을 예약해 둔다.
        episodeManager.QueueDebugEncounter(episode.episodeId);
        SessionState.SetInt(StageKey, 2);
        gameManager.ChangeState(GameState.Business);
        SetStatus($"Running isolated episode test: {episode.episodeTitle} [{episode.episodeId}]");
        FocusGameView();
    }

    private static void LaunchDay(
        PlaytestLaunchRequest request,
        GameProgress progress,
        DayFlowController dayFlow,
        PlaytestProgressIsolation isolation)
    {
        if (request.prepareScheduledBaseline)
            PrepareScheduledBaseline(request.targetDay, progress, isolation);

        progress.ResetDaySettlement();
        if (request.targetDay <= 1)
        {
            progress.SetCurrentDay(1);
            dayFlow.StartFirstDay();
            return;
        }

        progress.SetCurrentDay(request.targetDay - 1);
        dayFlow.StartBusinessDay();
    }

    private static void PrepareScheduledBaseline(
        int targetDay,
        GameProgress progress,
        PlaytestProgressIsolation isolation)
    {
        List<string> completedBeforeDay = new List<string>();
        List<string> incompleteFromDay = new List<string>();
        EpisodeData[] allEpisodes = Resources.LoadAll<EpisodeData>(
            ProjectResourcePaths.NarrativeEpisodes);
        foreach (EpisodeData episode in allEpisodes)
        {
            if (episode == null || !episode.IsScheduled)
                continue;
            if (!string.IsNullOrWhiteSpace(progress.CurrentChapterId)
                && !string.Equals(episode.chapterId, progress.CurrentChapterId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (episode.scheduledDay < targetDay)
                completedBeforeDay.Add(episode.episodeId);
            else
                incompleteFromDay.Add(episode.episodeId);
        }

        if (!isolation.PrepareEpisodeCompletionState(completedBeforeDay, incompleteFromDay))
            throw new InvalidOperationException("Failed to prepare the scheduled episode baseline.");
    }

    private static PlaytestLaunchRequest ReadRequest()
    {
        string json = SessionState.GetString(RequestKey, string.Empty);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonUtility.FromJson<PlaytestLaunchRequest>(json);
    }

    private static void FocusGameView()
    {
        Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gameViewType == null)
            return;

        EditorWindow gameView = Resources.FindObjectsOfTypeAll(gameViewType)
            .OfType<EditorWindow>()
            .FirstOrDefault();
        gameView?.Focus();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            waitStartedAt = EditorApplication.timeSinceStartup;
            return;
        }

        if (change != PlayModeStateChange.EnteredEditMode)
            return;

        RestorePreviousStartScene();
        bool wasActive = SessionState.GetBool(ActiveKey, false);
        SessionState.SetBool(ActiveKey, false);
        SessionState.SetInt(StageKey, 0);
        waitStartedAt = 0d;
        if (wasActive && !Status.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
            SetStatus("Isolated playtest stopped. The original disk save was not changed.");
    }

    private static void RestorePreviousStartScene()
    {
        if (!SessionState.GetBool(OwnsStartSceneKey, false))
            return;

        SceneAsset previous = null;
        if (SessionState.GetBool(HadPreviousStartSceneKey, false))
        {
            string path = SessionState.GetString(PreviousStartScenePathKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(path))
                previous = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        }

        EditorSceneManager.playModeStartScene = previous;
        SessionState.SetBool(OwnsStartSceneKey, false);
    }

    private static void Fail(string message)
    {
        SetStatus("FAIL: " + message);
        SessionState.SetBool(ActiveKey, false);
        Debug.LogError("[PlaytestLauncher] " + message);
        EditorApplication.isPlaying = false;
    }

    private static void SetStatus(string message)
    {
        SessionState.SetString(StatusKey, message ?? string.Empty);
    }
}
