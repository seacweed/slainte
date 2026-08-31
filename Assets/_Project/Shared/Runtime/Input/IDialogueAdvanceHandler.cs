namespace Slainte.Shared.Input
{
    public interface IDialogueAdvanceHandler
    {
        bool CanReceiveAdvanceInput { get; }
        void OnAdvanceInput();
    }
}
