using UnityEngine;

public class OrderTicketManager : MonoBehaviour
{
    [SerializeField] private DialogueController dialogue;
    [SerializeField] private GameModeManager    modeManager;
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

    private void HandleDialogueClosed()
    {
        if (string.IsNullOrWhiteSpace(_pendingTicketKey)) return;

        GameMode mode = modeManager != null ? modeManager.CurrentMode : GameMode.OrderMode;
        if (mode != GameMode.OrderMode) return;

        OrderTicketData data = ticketDb != null ? ticketDb.FindByKey(_pendingTicketKey) : null;
        if (data != null && ticketUI != null)
            ticketUI.Show(data);
    }

    private void HandleModeChanged(GameMode oldMode, GameMode newMode)
    {
        if (newMode == GameMode.EpisodeMode)
        {
            _pendingTicketKey = null;
            ticketUI?.HideImmediate();
        }
    }
}
