using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EpisodeDialogueRunner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DialogueController dialogue;
    [SerializeField] private CharacterPresenter presenter;
    [SerializeField] private CharacterDatabase characterDB;
    [SerializeField] private GameProgress progress;

    [Header("Choice UI")]
    [SerializeField] private Transform choiceRoot;
    [SerializeField] private EpisodeChoiceButtonUI choiceButtonPrefab;

    [Header("Scene Flow")]
    [SerializeField] private string returnSceneName = "BusinessScene";
    [SerializeField] private EpisodeData debugEpisode;

    [Header("Timing")]
    [SerializeField] private float startDelay = 0.15f;

    private EpisodeData _episode;
    private EpisodeNode _currentNode;
    private Coroutine _runRoutine;
    private bool _waitingForChoice;
    private bool _isRunning;

    public bool IsRunning => _isRunning;

    void Start()
    {
        if (progress == null)
            progress = GameProgress.Instance;

        _episode = EpisodeRuntimeContext.PendingEpisode != null
            ? EpisodeRuntimeContext.PendingEpisode
            : debugEpisode;

        if (_episode == null)
        {
            Debug.LogWarning("No episode assigned to EpisodeDialogueRunner.");
            return;
        }

        ClearChoices();
        dialogue.HideImmediate();
        StartCoroutine(BeginEpisode());
    }

    private IEnumerator BeginEpisode()
    {
        _isRunning = true;

        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        bool done = false;
        presenter.ShowCharacters(_episode.openingCharacterKeys, () => done = true);

        while (!done)
            yield return null;

        EnterNode(_episode.firstNodeId);
    }

    public void OnAdvanceInput()
    {
        if (!_isRunning || dialogue == null) return;

        if (dialogue.IsTyping)
        {
            dialogue.SkipTypingIfNeeded();
            return;
        }

        if (_waitingForChoice)
            return;

        GoToNextFromCurrentNode();
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
        _waitingForChoice = false;

        _currentNode = _episode.FindNode(nodeId);
        if (_currentNode == null)
        {
            Debug.LogWarning($"Episode node not found: {nodeId}");
            EndEpisode();
            yield break;
        }

        if (_currentNode.visibleCharacterKeys != null && _currentNode.visibleCharacterKeys.Count > 0)
        {
            bool shown = false;
            presenter.ShowCharacters(_currentNode.visibleCharacterKeys, () => shown = true);

            while (!shown)
                yield return null;
        }

        string speakerName = ResolveSpeakerName(_currentNode);
        dialogue.ShowSingleLine(speakerName, _currentNode.text);

        while (dialogue.IsTyping)
            yield return null;

        if (_currentNode.choices != null && _currentNode.choices.Count > 0)
        {
            _waitingForChoice = true;
            dialogue.SetNextHintVisible(false);
            ShowChoices(_currentNode.choices);
        }
        else
        {
            _waitingForChoice = false;
            dialogue.SetNextHintVisible(true);
        }
    }

    private void GoToNextFromCurrentNode()
    {
        if (_currentNode == null)
        {
            EndEpisode();
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentNode.nextNodeId))
        {
            EndEpisode();
            return;
        }

        EnterNode(_currentNode.nextNodeId);
    }

    private void ShowChoices(List<EpisodeChoice> choices)
    {
        ClearChoices();

        for (int i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            if (choice == null) continue;

            var button = Instantiate(choiceButtonPrefab, choiceRoot);
            button.Setup(choice.buttonText, () => OnChoiceSelected(choice));
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
            EndEpisode();
            return;
        }

        EnterNode(choice.nextNodeId);
    }

    private void ApplyChoiceEffects(EpisodeChoice choice)
    {
        if (progress == null || choice == null) return;

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
            var ch = characterDB.FindByKey(node.speakerKey);
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

    private void EndEpisode()
    {
        _isRunning = false;
        ClearChoices();
        dialogue.HideImmediate();

        if (progress != null && _episode != null)
            progress.MarkEpisodeCompleted(_episode.episodeId);

        EpisodeRuntimeContext.PendingEpisode = null;

        if (!string.IsNullOrWhiteSpace(returnSceneName))
            SceneManager.LoadScene(returnSceneName);
    }
}