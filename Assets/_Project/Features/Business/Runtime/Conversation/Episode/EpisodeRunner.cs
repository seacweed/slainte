using System;
using System.Collections;
using System.Collections.Generic;
using Slainte.Business;
using Slainte.Shared.Input;
using UnityEngine;

// 에피소드 그래프(EpisodeData의 노드 그래프)를 한 스텝씩 해석해 대사·선택지·제조 노드를
// 순서대로 실행하는 인터프리터. 모든 에피소드는 영업 슬롯에 끼어드는 인카운터로만 실행되며,
// 종료 시 영업 모드로 되돌린 뒤 시작할 때 받은 완료 콜백(EpisodeManager)에 제어를 넘긴다.
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
    private const int   MaxConsecutiveRouterHops = 64;

    private EpisodeData _episode;
    private EpisodeNode _currentNode;
    private Coroutine   _runRoutine;

    private bool _isRunning;
    private bool _waitingForChoice;
    private bool _waitingForCrafting;
    private bool _waitingForCharacterAnim;
    private bool _isTransitioning;
    private bool _isUsingManualCrafting;
    private int  _consecutiveRouterHops;
    private Action _businessEncounterCompleted;
    private EpisodeCraftingBridge _craftingBridge;

    private readonly List<EpisodeChoiceButtonUI> _choiceButtons = new();
    private Coroutine _panCoroutine;

    public bool IsRunning => _isRunning;
    public bool IsUsingManualCrafting => _isUsingManualCrafting;
    public string CurrentEpisodeId => _episode?.episodeId ?? string.Empty;

    public bool CanReceiveAdvanceInput =>
        _isRunning && _currentNode != null
        && !_waitingForChoice && !_waitingForCrafting && !_waitingForCharacterAnim && !_isTransitioning;

    public bool IsWaitingForChoice => _waitingForChoice || _isTransitioning;

    public event Action OnEncounterCompleted;

    void Awake() { }

    public bool BeginBusinessEncounter(EpisodeData episode, Action onCompleted)
    {
        return BeginInternal(episode, onCompleted);
    }

    public void SetCraftingBridge(EpisodeCraftingBridge bridge)
    {
        _craftingBridge = bridge;
    }

    public bool TryForceCompleteForRecovery()
    {
        if (!_isRunning || _episode == null)
            return false;

        _craftingBridge?.AbortForEpisodeRecovery();

        if (_runRoutine != null)
        {
            StopCoroutine(_runRoutine);
            _runRoutine = null;
        }

        if (_panCoroutine != null)
        {
            StopCoroutine(_panCoroutine);
            _panCoroutine = null;
        }

        _waitingForChoice = false;
        _waitingForCrafting = false;
        _waitingForCharacterAnim = false;
        _isTransitioning = false;
        FinishEncounter(applySettlementRewards: false);
        return true;
    }

    private bool BeginInternal(
        EpisodeData episode,
        Action onBusinessCompleted)
    {
        if (episode == null)
        {
            Debug.LogWarning("[EpisodeRunner] Begin called with null episode.");
            return false;
        }

        if (_isRunning)
        {
            Debug.LogWarning("[EpisodeRunner] An episode is already running.");
            return false;
        }

        _episode = episode;
        _currentNode = null;
        _businessEncounterCompleted = onBusinessCompleted;
        _isRunning          = true;
        _waitingForChoice   = false;
        _waitingForCrafting = false;
        _waitingForCharacterAnim = false;
        _isTransitioning = false;
        _isUsingManualCrafting = false;
        _consecutiveRouterHops = 0;

        modeManager?.RequestModeChange(GameMode.EpisodeMode);
        ClearChoices();
        dialogue?.HideImmediate();
        _runRoutine = StartCoroutine(BeginRoutine());
        return true;
    }

    private IEnumerator BeginRoutine()
    {
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        _waitingForCharacterAnim = true;
        bool done = false;
        // 시작 전 무대를 비운다 — 등장 캐릭터는 첫 노드의 NODE_CHARS가 정한다.
        characterStage?.ShowCharacters(null, () => done = true);
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
            Debug.LogWarning($"[EpisodeRunner] Node not found: {nodeId}");
            EndEncounter();
            yield break;
        }

        ApplyBgmCommand(_currentNode);
        ApplySfxCommand(_currentNode);

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
            _consecutiveRouterHops = 0;
            yield return StartCoroutine(HandleCraftingNode(_currentNode));
            yield break;
        }

        // 화자·대사·선택지가 모두 없는 노드는 조건 분기만 하는 라우터다(그래프 에디터의 Trigger 노드 등).
        // 빈 대화창을 띄우지 않고 곧장 다음 노드로 넘긴다. 한 프레임 쉬는 이유는 라우터가 연달아 이어질 때
        // 동기 재귀로 깊어지지 않게 하기 위해서고, 홉 수 제한은 라우터끼리 순환하는 데이터 오류 방어용이다.
        if (IsRouterNode(_currentNode))
        {
            if (++_consecutiveRouterHops > MaxConsecutiveRouterHops)
            {
                Debug.LogError($"[EpisodeRunner] 대사 없는 라우터 노드가 순환합니다: {_currentNode.nodeId}");
                EndEncounter();
                yield break;
            }

            yield return null;
            GoToNext();
            yield break;
        }

        _consecutiveRouterHops = 0;
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

    // 실제 제조 판정(EpisodeCraftingBridge)을 우선 시도하고, 대상 레시피가 없거나 브리지 자체가
    // 기술적으로 실패하면 그때만 BeginManualCrafting(6버튼 수동 판정 패널)으로 전환한다.
    // 수동 판정은 정상 경로가 아니라 판정 시스템이 망가졌을 때의 안전망이다.
    private IEnumerator HandleCraftingNode(EpisodeNode node)
    {
        _waitingForCrafting = true;
        _isUsingManualCrafting = false;

        if (!string.IsNullOrWhiteSpace(node.craftingOrderTarget) && _craftingBridge != null)
        {
            bool fallbackRequested = false;
            string fallbackReason = string.Empty;
            bool started = _craftingBridge.TryStart(
                node,
                NotifyCraftingCompleted,
                reason =>
                {
                    fallbackReason = reason;
                    fallbackRequested = true;
                });

            if (started)
            {
                while (_waitingForCrafting && !fallbackRequested)
                    yield return null;

                if (!_waitingForCrafting)
                    yield break;

                Debug.LogError(
                    $"[EpisodeRunner] 실제 제조를 시작하지 못해 수동 판정으로 전환합니다: "
                    + $"{node.nodeId}/{fallbackReason}");
                BeginManualCrafting(node);
                while (_waitingForCrafting)
                    yield return null;
                yield break;
            }

            Debug.LogWarning(
                $"[EpisodeRunner] 제조 브리지를 사용할 수 없어 수동 판정으로 전환합니다: {node.nodeId}");
        }

        BeginManualCrafting(node);

        while (_waitingForCrafting)
            yield return null;
    }

    private void BeginManualCrafting(EpisodeNode node)
    {
        _isUsingManualCrafting = true;

        if (node.craftingOrderTicket != null)
            ticketManager?.Prepare(node.craftingOrderTicket);
        else if (!string.IsNullOrWhiteSpace(node.craftingTicketKey))
            ticketManager?.Prepare(node.craftingTicketKey);

        modeManager?.RequestModeChange(GameMode.CraftingMode);
    }

    public void NotifyCraftingCompleted(CraftingJobResult result)
    {
        if (!_waitingForCrafting)
            return;

        _waitingForCrafting = false;
        _isUsingManualCrafting = false;
        modeManager?.RequestModeChange(GameMode.EpisodeMode);
        GoToNextFromCrafting(result);
    }

    private void GoToNextFromCrafting(CraftingJobResult result)
    {
        if (_currentNode == null) { EndEncounter(); return; }

        string flag = _currentNode.GetCraftingFlag(result);
        if (!string.IsNullOrWhiteSpace(flag))
            Progress?.SetFlag(flag);

        var varChanges = _currentNode.GetCraftingVarChanges(result);
        for (int i = 0; i < varChanges.Count; i++)
            Progress?.AddAffinity(varChanges[i].varName, varChanges[i].delta);

        string preferred = _currentNode.GetNextNodeId(result);
        string nextId = string.IsNullOrWhiteSpace(preferred) ? _currentNode.nextNodeId : preferred;

        if (string.IsNullOrWhiteSpace(nextId)) { EndEncounter(); return; }
        EnterNode(nextId);
    }

    private static bool IsRouterNode(EpisodeNode node)
    {
        return string.IsNullOrWhiteSpace(node.speakerKey)
            && string.IsNullOrWhiteSpace(node.overrideSpeakerName)
            && string.IsNullOrWhiteSpace(node.text)
            && (node.choices == null || node.choices.Count == 0);
    }

    private void GoToNext()
    {
        if (_currentNode == null)
        {
            Debug.LogWarning("[EpisodeRunner] Ignored advance input before the first node was ready.");
            return;
        }

        string nextId = ResolveNextNodeId(_currentNode);
        if (string.IsNullOrWhiteSpace(nextId)) { EndEncounter(); return; }

        EnterNode(nextId);
    }

    // 조건 분기는 목록 순서가 우선순위다(CSV 줄 순서 = 그래프 포트 순서). 처음 만족하는 분기로 가고,
    // 하나도 맞지 않으면 노드의 기본 nextNodeId.
    private string ResolveNextNodeId(EpisodeNode node)
    {
        GameProgress progress = Progress;
        if (progress != null)
        {
            for (int i = 0; i < node.branches.Count; i++)
            {
                NodeBranch branch = node.branches[i];
                if (!string.IsNullOrWhiteSpace(branch.nextNodeId) && branch.IsSatisfied(progress))
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

    private void ApplySfxCommand(EpisodeNode node)
    {
        if (node.sfxCommand != SfxCommand.Play || AudioManager.Instance == null) return;

        AudioManager.Instance.PlaySfx(node.sfxClipName);
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

    private void ApplySettlementRewards(EpisodeData episode)
    {
        if (episode?.settlementRewards == null)
            return;

        for (int i = 0; i < episode.settlementRewards.Count; i++)
        {
            EpisodeSettlementReward reward = episode.settlementRewards[i];
            if (reward == null || string.IsNullOrWhiteSpace(reward.requiredFlag))
                continue;

            if (Progress != null && Progress.HasFlag(reward.requiredFlag))
                Progress.AddSettlementReward(reward.label, reward.amount);
        }
    }

    private void EndEncounter()
    {
        FinishEncounter(applySettlementRewards: true);
    }

    private void FinishEncounter(bool applySettlementRewards)
    {
        _isRunning = false;
        _runRoutine = null;
        ClearChoices();
        dialogue?.HideImmediate();
        characterStage?.Clear();
        cameraRig?.ResetPan();
        ticketManager?.ClearTicket();
        AudioManager.Instance?.StopBgm();

        if (applySettlementRewards)
            ApplySettlementRewards(_episode);
        Action businessCompleted = _businessEncounterCompleted;
        _episode = null;
        _currentNode = null;
        _isUsingManualCrafting = false;
        _businessEncounterCompleted = null;
        OnEncounterCompleted?.Invoke();

        // 하루 진행(완료 기록·다음 슬롯)은 EpisodeManager/영업 컨트롤러 소관이므로, 러너는 모드만
        // 영업으로 되돌리고 시작할 때 받은 콜백에 제어를 넘긴다.
        modeManager?.RequestModeChange(GameMode.OrderMode);
        businessCompleted?.Invoke();
    }
}
