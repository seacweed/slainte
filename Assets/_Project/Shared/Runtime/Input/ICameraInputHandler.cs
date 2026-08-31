namespace Slainte.Shared.Input
{
    public enum CameraDirection
    {
        DrawerOpen,
        DrawerClose
    }

    public interface ICameraInputHandler
    {
        bool IsAnimating { get; }
        void OnCameraInput(CameraDirection direction);
    }
}
