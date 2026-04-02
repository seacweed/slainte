public enum CameraDirection
{
    DrawerOpen,
    DrawerClose,
    ShelfOpen,
    ShelfClose
}

public interface ICameraInputHandler
{
    bool IsAnimating { get; }
    void OnCameraInput(CameraDirection direction);
}
