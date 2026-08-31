using System;
using System.Text;
using UnityEngine;

public class OrderTicketManager : MonoBehaviour
{
    [SerializeField] private DialogueController  dialogue;
    [SerializeField] private GameModeManager     modeManager;
    [SerializeField] private OrderTicketDatabase ticketDb;
    [SerializeField] private OrderTicketUI       ticketUI;

    private OrderTicketData _pendingTicketData;
    private string _pendingTicketKey;
    private bool _hasPendingMemoOverride;
    private string _pendingMemoOverride;

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
        _pendingTicketData = null;
        if (!string.Equals(_pendingTicketKey, ticketKey, StringComparison.Ordinal))
            ClearMemoOverride();
        _pendingTicketKey = ticketKey;
    }

    public void Prepare(string ticketKey, string memoOverride)
    {
        _pendingTicketData = null;
        _pendingTicketKey = ticketKey;
        _pendingMemoOverride = memoOverride ?? string.Empty;
        _hasPendingMemoOverride = true;
    }

    public void Prepare(OrderTicketData ticketData)
    {
        if (_pendingTicketData != ticketData)
            ClearMemoOverride();
        _pendingTicketData = ticketData;
        _pendingTicketKey = ticketData != null ? ticketData.key : null;
    }

    public void Prepare(OrderTicketData ticketData, string memoOverride)
    {
        _pendingTicketData = ticketData;
        _pendingTicketKey = ticketData != null ? ticketData.key : null;
        _pendingMemoOverride = memoOverride ?? string.Empty;
        _hasPendingMemoOverride = true;
    }

    public void ClearTicket()
    {
        _pendingTicketData = null;
        _pendingTicketKey = null;
        ClearMemoOverride();
        ticketUI?.ClearContent();
        ticketUI?.HideAnimated();
    }

    // Ignored when no ticket has been prepared yet, so an empty ticket can't be toggled into view.
    public void ToggleTicket()
    {
        if (_pendingTicketData == null
            && string.IsNullOrWhiteSpace(_pendingTicketKey)) return;
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
            _pendingTicketData = null;
            _pendingTicketKey = null;
            ClearMemoOverride();
            ticketUI?.ClearContent();
            ticketUI?.HideAnimated();
        }
    }

    private void ShowPendingTicket()
    {
        if (_pendingTicketData == null
            && string.IsNullOrWhiteSpace(_pendingTicketKey)) return;

        dialogue?.HideImmediate();

        OrderTicketData data = _pendingTicketData != null
            ? _pendingTicketData
            : ticketDb != null ? ticketDb.FindByKey(_pendingTicketKey) : null;
        if (data != null && ticketUI != null)
        {
            string memo = _hasPendingMemoOverride
                ? _pendingMemoOverride
                : data.memo;
            ticketUI.Show(data, memo);
        }
        else
        {
            ticketUI?.ClearContent();
        }
    }

    private void ClearMemoOverride()
    {
        _hasPendingMemoOverride = false;
        _pendingMemoOverride = null;
    }
}

public static class OrderTicketMemoFormatter
{
    public static string Build(CustomerOrderData order, string fallbackOrderLine)
    {
        if (order?.lines != null && order.lines.Count > 0)
        {
            StringBuilder result = new();
            for (int i = 0; i < order.lines.Count; i++)
            {
                DialogueLine line = order.lines[i];
                if (line == null)
                    continue;

                if (result.Length > 0)
                    result.Append('\n');
                result.Append(line.text ?? string.Empty);
            }
            return result.ToString();
        }

        if (order != null && order.orderDialogueAuthored)
            return string.Empty;

        return string.IsNullOrWhiteSpace(fallbackOrderLine)
            ? string.Empty
            : fallbackOrderLine;
    }
}
