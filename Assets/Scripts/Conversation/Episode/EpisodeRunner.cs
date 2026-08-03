using System;
using System.Collections;
using System.Collections.Generic;
using Slainte.Bartending;
using Slainte.Business;
using UnityEngine;

public class EpisodeRunner : MonoBehaviour, IDialogueAdvanceHandler
{
    [Header("References")]
    [SerializeField] private GameModeManager   modeManager;
    [SerializeField] private CharacterStage    characterStage;
    [SerializeField] private FrontCameraRig    cameraRig;
    [SerializeField] private DialogueController dialogue;
    [SerializeField] private CharacterDatabase  characterDB;

    [Header("Choice UI")]
    [SerializeField] private Transform            choiceRoot;
    [SerializeField] private EpisodeChoiceButtonUI choiceButtonPrefab;

    [Header("Order Ticket")]
    [SerializeField] private OrderTicketManager ticketManager;

    [Header("Timing")]
    [SerializeField] private float startDelay = 0.15f;

    private GameProgress Progress => GameProgress.Instance;

    private const float ChoiceButtonHeight  = 80f;
    private const float ChoiceButtonSpacing = 20f;
    private const float ChoiceFadeDuration  = 0.5f;

    private EpisodeData _episode;
    private EpisodeNode _currentNode;
    private Coroutine   _runRoutine;

    private bool _isRunning;
    private bool _waitingForChoice;
    private bool _waitingForCrafting;
    private bool _waitingForCharacterAnim;
    private bool _isTransitioning;

    private readonly List<EpisodeChoiceButtonUI> _choiceButtons = new();
    private Coroutine _panCoroutine;

    public bool IsRunning => _isRunning;

    public bool CanReceiveAdvanceInput =>
        _isRunning && !_waitingForChoice && !_waitingForCrafting && !_waitingForCharacterAnim && !_isTransitioning;

    public bool IsWaitingForChoice => _waitingForChoice || _isTransitioning;

    public event Action OnEncounterCompleted;

    void Awake() { }

    public void Begin(EpisodeData episode)
    {
        if (episode == null)
        {
            Debug.LogWarning("[에피소드 진행] 비어 있는 에피소드로 시작을 요청했습니다.");
            return;
        }

        _episode = episode;
        _isRunning          = false;
        _waitingForChoice   = false;
        _waitingForCrafting = false;

        modeManager?.RequestModeChange(GameMode.EpisodeMode);
        ClearChoices();
        dialogue?.HideImmediate();
        StartCoroutine(BeginRoutine());
    }

    private IEnumerator BeginRoutine()
    {
        _isRunning = true;

        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        _waitingForCharacterAnim = true;
        bool done = false;
        characterStage?.ShowCharacters(_episode.openingCharacters, () => done = true);
        if (characterStage == null) done = true;
        StartPanCoroutine();

        while (!done)
            yield return null;

        _waitingForCharacterAnim = false;
        EnterNode(_episode.firstNodeId);
    }

    public void OnAdvanceInput()
    {
        if (!CanReceiveAdvanceInput || dialogue == null) return;

        if (dialogue.IsTyping)
        {
            dialogue.SkipTypingIfNeeded();
            return;
        }

        GoToNext();
    }

    private void EnterNode(string nodeId)
    {
        if (_runRoutine != null)
            StopCoroutine(_runRoutine);

        _runRoutine = StartCoroutine(RunNode(nodeId));
    }

    private IEnumerator RunNode(string nodeId)
    {
        ClearChoices();
        _waitingForChoice   = false;
        _waitingForCrafting = false;

        _currentNode = _episode.FindNode(nodeId);
        if (_currentNode == null)
        {
            Debug.LogWarning($"[에피소드 진행] 노드를 찾을 수 없습니다: {nodeId}");
            EndEncounter();
            yield break;
        }

        ApplyBgmCommand(_currentNode);

        if (_currentNode.characters != null && _currentNode.characters.Count > 0)
        {
            _waitingForCharacterAnim = true;
            bool shown = false;
            characterStage?.ShowCharacters(_currentNode.characters, () => shown = true);
            if (characterStage == null) shown = true;
            StartPanCoroutine();

            while (!shown)
                yield return null;

            _waitingForCharacterAnim = false;
        }

        if (_currentNode.requiresCrafting)
        {
            yield return StartCoroutine(HandleCraftingNode(_currentNode));
            yield break;
        }

        string speakerName = ResolveSpeakerName(_currentNode);
        Color speakerColor = ResolveSpeakerColor(_currentNode);
        dialogue?.ShowSingleLine(speakerName, _currentNode.text, speakerColor);

        while (dialogue != null && dialogue.IsTyping)
            yield return null;

        if (_currentNode.choices != null && _currentNode.choices.Count > 0)
        {
            _waitingForChoice = true;
            dialogue?.SetNextHintVisible(false);
            ShowChoices(_currentNode.choices);
        }
        else
        {
            dialogue?.SetNextHintVisible(true);
        }
    }

    private IEnumerator HandleCraftingNode(EpisodeNode node)
    {
        _waitingForCrafting = true;

        if (string.IsNullOrWhiteSpace(node.craftingRecipeId))
        {
            Debug.LogError(
                $"[에피소드 진행] 제조 노드 '{node.nodeId}'에 레시피 ID가 없습니다. "
                + "실패 분기로 진행합니다.");
            CompleteCraftingNode(false);
            yield break;
        }

        const float bootstrapTimeout = 5f;
        float timeoutAt = Time.realtimeSinceStartup + bootstrapTimeout;
        BusinessFlowBootstrap bootstrap = null;
        while (bootstrap == null || !bootstrap.IsRuntimeReady)
        {
            bootstrap = FindFirstObjectByType<BusinessFlowBootstrap>();
            if (Time.realtimeSinceStartup >= timeoutAt)
            {
                Debug.LogError("[에피소드 진행] 공용 주문 처리를 기다리다 제한 시간을 초과했습니다.");
                CompleteCraftingNode(false);
                yield break;
            }
            yield return null;
        }

        var request = new OrderSessionRequest
        {
            sessionId = $"{_episode.episodeId}:{node.nodeId}",
            owner = OrderSessionOwner.Episode,
            requestedRecipeId = node.craftingRecipeId,
            ticketKey = node.craftingTicketKey,
            orderType = CocktailOrderType.EpisodeOrder,
            presentOrder = false,
            presentFeedback = false,
            allowReject = false,
            allowAbandon = false,
            applyProgressRewards = false,
            clearCustomerOnComplete = false
        };

        if (!bootstrap.StartEpisodeOrder(request, HandleEpisodeOrderCompleted))
        {
            Debug.LogError(
                $"[에피소드 진행] 노드 '{node.nodeId}'의 공용 주문 처리를 시작하지 못했습니다.");
            CompleteCraftingNode(false);
            yield break;
        }

        while (_waitingForCrafting)
            yield return null;
    }

    private void HandleEpisodeOrderCompleted(BusinessOrderSessionResult result)
    {
        bool isGood = result != null
            && result.outcome == OrderSessionOutcome.Served
            && result.grade == OrderEvaluationGrade.Good;
        CompleteCraftingNode(isGood);
    }

    public void NotifyCraftingCompleted(bool isGood)
    {
        CompleteCraftingNode(isGood);
    }

    private void CompleteCraftingNode(bool isGood)
    {
        _waitingForCrafting = false;
        modeManager?.RequestModeChange(GameMode.EpisodeMode);
        GoToNextFromCrafting(isGood);
    }

    private void GoToNextFromCrafting(bool isGood)
    {
        if (_currentNode == null) { EndEncounter(); return; }

        string flag = isGood ? _currentNode.craftingFlagGood : _currentNode.craftingFlagBad;
        if (!string.IsNullOrWhiteSpace(flag))
            Progress?.SetFlag(flag);

        var varChanges = isGood ? _currentNode.craftingVarChangesGood : _currentNode.craftingVarChangesBad;
        for (int i = 0; i < varChanges.Count; i++)
            Progress?.AddAffinity(varChanges[i].varName, varChanges[i].delta);

        string preferred = isGood ? _currentNode.nextNodeIdGood : _currentNode.nextNodeIdBad;
        string nextId = string.IsNullOrWhiteSpace(preferred) ? _currentNode.nextNodeId : preferred;

        DataManager.Instance?.Save();

        if (string.IsNullOrWhiteSpace(nextId)) { EndEncounter(); return; }
        EnterNode(nextId);
    }

    private void GoToNext()
    {
        if (_currentNode == null) { EndEncounter(); return; }

        string nextId = ResolveNextNodeId(_currentNode);
        if (string.IsNullOrWhiteSpace(nextId)) { EndEncounter(); return; }

        EnterNode(nextId);
    }

    private string ResolveNextNodeId(EpisodeNode node)
    {
        if (Progress != null)
        {
            for (int i = 0; i < node.flagBranches.Count; i++)
            {
                NodeFlagBranch branch = node.flagBranches[i];
                if (!string.IsNullOrWhiteSpace(branch.nextNodeId) && EvaluateFlagBranch(branch))
                    return branch.nextNodeId;
            }

            for (int i = 0; i < node.varBranches.Count; i++)
            {
                NodeVarBranch branch = node.varBranches[i];
                if (branch.condition != null
                    && branch.condition.Evaluate(Progress.GetAffinity(branch.condition.varName))
                    && !string.IsNullOrWhiteSpace(branch.nextNodeId))
                    return branch.nextNodeId;
            }
        }

        return node.nextNodeId;
    }

    private bool EvaluateFlagBranch(NodeFlagBranch branch)
    {
        if (branch.requiredAllFlags.Count > 0)
        {
            for (int i = 0; i < branch.requiredAllFlags.Count; i++)
                if (!Progress.HasFlag(branch.requiredAllFlags[i])) return false;
            return true;
        }
        if (branch.requiredAnyFlags.Count > 0)
        {
            for (int i = 0; i < branch.requiredAnyFlags.Count; i++)
                if (Progress.HasFlag(branch.requiredAnyFlags[i])) return true;
            return false;
        }
        return false;
    }

    private void ShowChoices(List<EpisodeChoice> choices)
    {
        ClearChoices();

        int count = choices.Count;
        float slideAmount = count * ChoiceButtonHeight + (count - 1) * ChoiceButtonSpacing;

        _isTransitioning = true;
        dialogue?.SlideUpForChoices(slideAmount, () =>
        {
            _isTransitioning = false;
            for (int i = 0; i < choices.Count; i++)
            {
                EpisodeChoice choice = choices[i];
                if (choice == null) continue;

                EpisodeChoiceButtonUI btn = Instantiate(choiceButtonPrefab, choiceRoot);
                _choiceButtons.Add(btn);
                btn.Setup(choice.buttonText, () => OnChoiceSelected(btn, choice));
            }
        });
    }

    private void OnChoiceSelected(EpisodeChoiceButtonUI selectedBtn, EpisodeChoice choice)
    {
        if (choice == null) return;

        ApplyChoiceEffects(choice);
        _waitingForChoice = false;

        for (int i = 0; i < _choiceButtons.Count; i++)
        {
            EpisodeChoiceButtonUI btn = _choiceButtons[i];
            if (btn == null || btn == selectedBtn) continue;
            btn.HideImmediate();
        }
        _choiceButtons.Clear();

        string nextId = choice.nextNodeId;

        _isTransitioning = true;
        selectedBtn.FadeOutAndDestroy(ChoiceFadeDuration, () =>
        {
            dialogue?.SlideBackToOrigin(() =>
            {
                _isTransitioning = false;
                ProceedAfterChoice(nextId);
            });
        });
    }

    private void ProceedAfterChoice(string nextId)
    {
        if (string.IsNullOrWhiteSpace(nextId))
        {
            EndEncounter();
            return;
        }

        EnterNode(nextId);
    }

    private void ApplyChoiceEffects(EpisodeChoice choice)
    {
        if (Progress == null) return;

        for (int i = 0; i < choice.setFlags.Count; i++)
            Progress.SetFlag(choice.setFlags[i]);

        for (int i = 0; i < choice.clearFlags.Count; i++)
            Progress.ClearFlag(choice.clearFlags[i]);

        for (int i = 0; i < choice.varChanges.Count; i++)
            Progress.AddAffinity(choice.varChanges[i].varName, choice.varChanges[i].delta);
    }

    private void ApplyBgmCommand(EpisodeNode node)
    {
        if (node.bgmCommand == BgmCommand.None || AudioManager.Instance == null) return;

        if (node.bgmCommand == BgmCommand.Play)
            AudioManager.Instance.PlayBgm(node.bgmClipName);
        else if (node.bgmCommand == BgmCommand.Stop)
            AudioManager.Instance.StopBgm();
    }

    private string ResolveSpeakerName(EpisodeNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.overrideSpeakerName))
            return node.overrideSpeakerName;

        if (characterDB != null && !string.IsNullOrWhiteSpace(node.speakerKey))
        {
            CharacterData ch = characterDB.FindByKey(node.speakerKey);
            if (ch != null && !string.IsNullOrWhiteSpace(ch.displayName))
                return ch.displayName;
        }

        return node.speakerKey;
    }

    private Color ResolveSpeakerColor(EpisodeNode node)
    {
        if (characterDB != null && !string.IsNullOrWhiteSpace(node.speakerKey))
        {
            CharacterData ch = characterDB.FindByKey(node.speakerKey);
            if (ch != null)
                return ch.nameColor;
        }

        return Color.white;
    }

    private void ClearChoices()
    {
        _choiceButtons.Clear();

        if (choiceRoot == null) return;

        for (int i = choiceRoot.childCount - 1; i >= 0; i--)
            Destroy(choiceRoot.GetChild(i).gameObject);
    }

    private void StartPanCoroutine()
    {
        if (cameraRig == null || characterStage == null) return;
        if (_panCoroutine != null) StopCoroutine(_panCoroutine);
        _panCoroutine = StartCoroutine(PanToCenterNextFrame());
    }

    private IEnumerator PanToCenterNextFrame()
    {
        yield return null;
        cameraRig.PanToWorldCenterX(characterStage.GetActiveGroupCenterWorldX());
    }

    private void EndEncounter()
    {
        _isRunning = false;
        ClearChoices();
        dialogue?.HideImmediate();
        characterStage?.Clear();
        cameraRig?.ResetPan();
        AudioManager.Instance?.StopBgm();

        string episodeId = _episode?.episodeId;
        _episode = null;
        OnEncounterCompleted?.Invoke();

        DayFlowManager.Instance.CompleteEpisode(episodeId);
    }
}
