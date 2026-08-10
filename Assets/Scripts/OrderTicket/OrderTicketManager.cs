using UnityEngine;

public class OrderTicketManager : MonoBehaviour
{
    [SerializeField] private DialogueController  dialogue;
    [SerializeField] private GameModeManager     modeManager;
    [SerializeField] private OrderTicketDatabase ticketDb;
    [SerializeField] private OrderTicketUI       ticketUI;

    private string _pendingTicketKey;

    void OnEnable()
    {
        if (dialogue != null)    dialogue.DialogueClosed    += HandleDialogueClosed;
        if (modeManager != null) modeManager.OnModeChanged += HandleModeChanged;
    }

    void OnDisable()
    {
        if (dialogue != null)    dialogue.DialogueClosed    -= HandleDialogueClosed;
        if (modeManager != null) modeManager.OnModeChanged -= HandleModeChanged;
    }

    public void Prepare(string ticketKey)
    {
        _pendingTicketKey = ticketKey;
    }

    public void ClearTicket()
    {
        _pendingTicketKey = null;
        ticketUI?.HideAnimated();
    }

    // Ignored when no ticket has been prepared yet, so an empty ticket can't be toggled into view.
    public void ToggleTicket()
    {
        if (string.IsNullOrWhiteSpace(_pendingTicketKey)) return;
        ticketUI?.Toggle();
    }

    // OrderMode: dialogue closes -> show ticket
    private void HandleDialogueClosed()
    {
        if (modeManager == null || modeManager.CurrentMode != GameMode.OrderMode) return;
        ShowPendingTicket();
    }

    // CraftingMode: mode change itself is the trigger (no dialogue close involved)
    private void HandleModeChanged(GameMode oldMode, GameMode newMode)
    {
        if (newMode == GameMode.CraftingMode)
        {
            ShowPendingTicket();
        }
        else if (newMode == GameMode.EpisodeMode)
        {
            _pendingTicketKey = null;
            ticketUI?.HideAnimated();
        }
    }

    private void ShowPendingTicket()
    {
        if (string.IsNullOrWhiteSpace(_pendingTicketKey)) return;

        dialogue?.HideImmediate();

        OrderTicketData data = ticketDb != null ? ticketDb.FindByKey(_pendingTicketKey) : null;
        if (data != null && ticketUI != null)
            ticketUI.Show(data);
    }
}
