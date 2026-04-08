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

    public bool IsRunning => _isRunning;

    public bool CanReceiveAdvanceInput =>
        _isRunning && !_waitingForChoice && !_waitingForCrafting && !_waitingForCharacterAnim && !_isTransitioning;

    public bool IsWaitingForChoice => _waitingForChoice || _isTransitioning;

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

        _waitingForCharacterAnim = true;
        bool done = false;
        characterStage?.ShowCharacters(_episode.openingCharacters, () => done = true);
        if (characterStage == null) done = true;

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
            Debug.LogWarning($"[EpisodeRunner] Node not found: {nodeId}");
            EndEncounter();
            yield break;
        }

        if (_currentNode.characters != null && _currentNode.characters.Count > 0)
        {
            _waitingForCharacterAnim = true;
            bool shown = false;
            characterStage?.ShowCharacters(_currentNode.characters, () => shown = true);
            if (characterStage == null) shown = true;

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
        if (_currentNode == null) { EndEncounter(); return; }

        string nextId = ResolveNextNodeId(_currentNode);
        if (string.IsNullOrWhiteSpace(nextId)) { EndEncounter(); return; }

        EnterNode(nextId);
    }

    private string ResolveNextNodeId(EpisodeNode node)
    {
        if (progress != null)
        {
            for (int i = 0; i < node.flagBranches.Count; i++)
            {
                NodeFlagBranch branch = node.flagBranches[i];
                if (!string.IsNullOrWhiteSpace(branch.requiredFlag)
                    && progress.HasFlag(branch.requiredFlag)
                    && !string.IsNullOrWhiteSpace(branch.nextNodeId))
                    return branch.nextNodeId;
            }

            for (int i = 0; i < node.varBranches.Count; i++)
            {
                NodeVarBranch branch = node.varBranches[i];
                if (branch.condition != null
                    && branch.condition.Evaluate(progress.GetVar(branch.condition.varName))
                    && !string.IsNullOrWhiteSpace(branch.nextNodeId))
                    return branch.nextNodeId;
            }
        }

        return node.nextNodeId;
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
        if (progress == null) return;

        for (int i = 0; i < choice.setFlags.Count; i++)
            progress.SetFlag(choice.setFlags[i]);

        for (int i = 0; i < choice.clearFlags.Count; i++)
            progress.ClearFlag(choice.clearFlags[i]);

        for (int i = 0; i < choice.varChanges.Count; i++)
            progress.AddVar(choice.varChanges[i].varName, choice.varChanges[i].delta);
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
        _choiceButtons.Clear();

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

        string epId = _episode != null ? _episode.episodeId : null;

        // 1. BusinessScene 내부 진행도 (GameProgress) 기록
        if (progress != null && epId != null)
            progress.MarkEpisodeCompleted(epId);

        // 2. CoreScene 전역 진행도 (EpisodeManager) 기록 + 자동 저장 트리거
        if (!string.IsNullOrEmpty(epId))
            EpisodeManager.Instance?.ClearEpisode(epId);

        modeManager?.RequestModeChange(GameMode.OrderMode);
        OnEncounterCompleted?.Invoke();

        // 3. RestScene으로 전환 (EpisodeManager.ClearEpisode 내부의 Save 완료 후 전환)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ChangeState(GameState.Rest);
        }
        else
        {
            Debug.LogWarning("[EpisodeRunner] GameManager가 없어 RestScene으로 전환할 수 없습니다.");
        }
    }
}
