using UnityEngine;

public class InputRouter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GameModeManager modeManager;
    [SerializeField] private FrontCameraRig  cameraRig;
    [SerializeField] private OrderTicketUI   ticketUI;
    [SerializeField] private DialogueController dialogue;

    [Header("Dialogue Handlers")]
    [SerializeField] private EncounterRunner encounterRunner;

    void Update()
    {
        bool advancePressed = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space);
        bool ticketPressed  = Input.GetKeyDown(KeyCode.E);

        GameMode mode = modeManager != null ? modeManager.CurrentMode : GameMode.BusinessMode;

        switch (mode)
        {
            case GameMode.BusinessMode:
                HandleCameraInput();
                if (ticketPressed) ticketUI?.Toggle();
                break;

            case GameMode.OrderMode:
                if (advancePressed) dialogue?.Advance();
                if (ticketPressed)  ticketUI?.Toggle();
                break;

            case GameMode.EncounterMode:
                if (advancePressed) RouteAdvanceToEncounter();
                break;

            case GameMode.CraftingMode:
                HandleCameraInput();
                if (advancePressed) RouteAdvanceToEncounter();
                break;
        }
    }

    private void HandleCameraInput()
    {
        if (cameraRig == null || cameraRig.IsAnimating) return;

        if (Input.GetKeyDown(KeyCode.S)) cameraRig.OnCameraInput(CameraDirection.DrawerOpen);
        if (Input.GetKeyDown(KeyCode.W)) cameraRig.OnCameraInput(CameraDirection.DrawerClose);
        if (Input.GetKeyDown(KeyCode.D)) cameraRig.OnCameraInput(CameraDirection.ShelfOpen);
        if (Input.GetKeyDown(KeyCode.A)) cameraRig.OnCameraInput(CameraDirection.ShelfClose);
    }

    private void RouteAdvanceToEncounter()
    {
        if (encounterRunner != null && encounterRunner.CanReceiveAdvanceInput)
        {
            encounterRunner.OnAdvanceInput();
            return;
        }

        dialogue?.Advance();
    }
}
