using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EpisodeRunner : MonoBehaviour, IDialogueAdvanceHandler
{
    [Header("References")]
    [SerializeField] private GameModeManager   modeManager;
    [SerializeField] private CharacterStage    characterStage;
    [SerializeField] private DialogueController dialogue;
    [SerializeField] private CharacterDatabase  characterDB;
    [SerializeField] private GameProgress       progress;

    [Header("Choice UI")]
    [SerializeField] private Transform            choiceRoot;
    [SerializeField] private EpisodeChoiceButtonUI choiceButtonPrefab;

    [Header("Order Ticket")]
    [SerializeField] private OrderTicketManager ticketManager;

    [Header("Timing")]
    [SerializeField] private float startDelay = 0.15f;

    private EpisodeData _episode;
    private EpisodeNode _currentNode;
    private Coroutine   _runRoutine;

    private bool _isRunning;
    private bool _waitingForChoice;
    private bool _waitingForCrafting;

    public bool IsRunning => _isRunning;

    public bool CanReceiveAdvanceInput =>
        _isRunning && !_waitingForChoice && !_waitingForCrafting;

    public event Action OnEncounterCompleted;

    void Awake()
    {
        if (progress == null)
            progress = GameProgress.Instance;
    }

    public void Begin(EpisodeData episode)
    {
        if (episode == null)
        {
            Debug.LogWarning("[EpisodeRunner] Begin called with null episode.");
            return;
        }

        _episode = episode;
        _isRunning        = false;
        _waitingForChoice = false;
        _waitingForCrafting = false;

        ClearChoices();
        dialogue?.HideImmediate();
        StartCoroutine(BeginRoutine());
    }

    private IEnumerator BeginRoutine()
    {
        _isRunning = true;

        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        bool done = false;
        characterStage?.ShowCharacters(_episode.openingCharacters, () => done = true);
        if (characterStage == null) done = true;

        while (!done)
            yield return null;

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
            Debug.LogWarning($"[EpisodeRunner] Node not found: {nodeId}");
            EndEncounter();
            yield break;
        }

        if (_currentNode.characters != null && _currentNode.characters.Count > 0)
        {
            bool shown = false;
            characterStage?.ShowCharacters(_currentNode.characters, () => shown = true);
            if (characterStage == null) shown = true;

            while (!shown)
                yield return null;
        }

        if (_currentNode.requiresCrafting)
        {
            yield return StartCoroutine(HandleCraftingNode(_currentNode));
            yield break;
        }

        string speakerName = ResolveSpeakerName(_currentNode);
        dialogue?.ShowSingleLine(speakerName, _currentNode.text);

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

        if (!string.IsNullOrWhiteSpace(node.craftingTicketKey))
            ticketManager?.Prepare(node.craftingTicketKey);

        modeManager?.RequestModeChange(GameMode.CraftingMode);

        while (_waitingForCrafting)
            yield return null;
    }

    public void NotifyCraftingCompleted(bool isGood)
    {
        _waitingForCrafting = false;
        modeManager?.RequestModeChange(GameMode.EpisodeMode);
        GoToNextFromCrafting(isGood);
    }

    private void GoToNextFromCrafting(bool isGood)
    {
        if (_currentNode == null) { EndEncounter(); return; }

        string preferred = isGood ? _currentNode.nextNodeIdGood : _currentNode.nextNodeIdBad;
        string nextId = string.IsNullOrWhiteSpace(preferred) ? _currentNode.nextNodeId : preferred;

        if (string.IsNullOrWhiteSpace(nextId)) { EndEncounter(); return; }
        EnterNode(nextId);
    }

    private void GoToNext()
    {
        if (_currentNode == null || string.IsNullOrWhiteSpace(_currentNode.nextNodeId))
        {
            EndEncounter();
            return;
        }

        EnterNode(_currentNode.nextNodeId);
    }

    private void ShowChoices(List<EpisodeChoice> choices)
    {
        ClearChoices();

        for (int i = 0; i < choices.Count; i++)
        {
            EpisodeChoice choice = choices[i];
            if (choice == null) continue;

            EpisodeChoiceButtonUI btn = Instantiate(choiceButtonPrefab, choiceRoot);
            btn.Setup(choice.buttonText, () => OnChoiceSelected(choice));
        }
    }

    private void OnChoiceSelected(EpisodeChoice choice)
    {
        if (choice == null) return;

        ApplyChoiceEffects(choice);
        _waitingForChoice = false;
        ClearChoices();

        if (string.IsNullOrWhiteSpace(choice.nextNodeId))
        {
            EndEncounter();
            return;
        }

        EnterNode(choice.nextNodeId);
    }

    private void ApplyChoiceEffects(EpisodeChoice choice)
    {
        if (progress == null) return;

        for (int i = 0; i < choice.setFlags.Count; i++)
            progress.SetFlag(choice.setFlags[i]);

        for (int i = 0; i < choice.clearFlags.Count; i++)
            progress.ClearFlag(choice.clearFlags[i]);
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

    private void ClearChoices()
    {
        if (choiceRoot == null) return;

        for (int i = choiceRoot.childCount - 1; i >= 0; i--)
            Destroy(choiceRoot.GetChild(i).gameObject);
    }

    private void EndEncounter()
    {
        _isRunning = false;
        ClearChoices();
        dialogue?.HideImmediate();
        characterStage?.Clear();

        if (progress != null && _episode != null)
            progress.MarkEpisodeCompleted(_episode.episodeId);

        modeManager?.RequestModeChange(GameMode.OrderMode);
        OnEncounterCompleted?.Invoke();
    }
}
