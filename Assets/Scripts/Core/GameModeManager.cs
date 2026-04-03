using System;
using System.Collections.Generic;
using UnityEngine;

public class GameModeManager : MonoBehaviour
{
    public static GameModeManager Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private CanvasGroup frontWorldPanel;
    [SerializeField] private CanvasGroup dialoguePanel;
    [SerializeField] private CanvasGroup orderTicketPanel;
    [SerializeField] private CanvasGroup choiceContainer;

    [Header("Initial Mode")]
    [SerializeField] private GameMode initialMode = GameMode.OrderMode;

    public GameMode CurrentMode { get; private set; }

    public event Action<GameMode, GameMode> OnModeChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        ApplyMode(initialMode, force: true);
    }

    public void RequestModeChange(GameMode newMode)
    {
        if (CurrentMode == newMode) return;
        ApplyMode(newMode, force: false);
    }

    private void ApplyMode(GameMode newMode, bool force)
    {
        GameMode oldMode = CurrentMode;
        CurrentMode = newMode;

        RefreshPanels(newMode);

        if (!force)
            OnModeChanged?.Invoke(oldMode, newMode);
    }

    private void RefreshPanels(GameMode mode)
    {
        bool showFrontWorld   = mode == GameMode.OrderMode
                             || mode == GameMode.EpisodeMode
                             || mode == GameMode.CraftingMode;
        bool showDialogue     = mode == GameMode.OrderMode
                             || mode == GameMode.EpisodeMode
                             || mode == GameMode.CraftingMode;
        bool showOrderTicket  = mode == GameMode.OrderMode;
        bool showChoices      = mode == GameMode.EpisodeMode;

        SetGroup(frontWorldPanel,  showFrontWorld);
        SetGroup(dialoguePanel,    showDialogue);
        SetGroup(orderTicketPanel, showOrderTicket);
        SetGroup(choiceContainer,  showChoices);
    }

    private static void SetGroup(CanvasGroup cg, bool on)
    {
        if (cg == null) return;
        cg.alpha          = on ? 1f : 0f;
        cg.interactable   = on;
        cg.blocksRaycasts = on;
    }
}
