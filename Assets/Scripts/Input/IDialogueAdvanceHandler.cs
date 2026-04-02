public interface IDialogueAdvanceHandler
{
    bool CanReceiveAdvanceInput { get; }
    void OnAdvanceInput();
}
