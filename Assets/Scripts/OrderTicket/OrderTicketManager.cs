using UnityEngine;

public class OrderTicketManager : MonoBehaviour
{
    [SerializeField] private DialogueController dialogue;
    [SerializeField] private OrderTicketDatabase ticketDb;
    [SerializeField] private OrderTicketUI ticketUI;

    private string pendingTicketKey;

    void OnEnable()
    {
        if (dialogue != null) dialogue.DialogueClosed += HandleDialogueClosed;
    }

    void OnDisable()
    {
        if (dialogue != null) dialogue.DialogueClosed -= HandleDialogueClosed;
    }

    public void Prepare(string ticketKey)
    {
        pendingTicketKey = ticketKey;
    }

    private void HandleDialogueClosed()
    {
        if (string.IsNullOrWhiteSpace(pendingTicketKey)) return;
        var data = ticketDb != null ? ticketDb.FindByKey(pendingTicketKey) : null;
        if (data != null && ticketUI != null)
            ticketUI.Show(data);
    }
}
