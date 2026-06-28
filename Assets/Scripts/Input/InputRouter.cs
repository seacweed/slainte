using UnityEngine;

public class InputRouter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GameModeManager    modeManager;
    [SerializeField] private FrontCameraRig     cameraRig;
    [SerializeField] private RecipeBookUI       recipeBook;
    [SerializeField] private DialogueController dialogue;
    [SerializeField] private OrderTicketManager orderTicketManager;

    [Header("Dialogue Handlers")]
    [SerializeField] private EpisodeRunner episodeRunner;

    [Header("Test")]
    [SerializeField] private CustomerSpawner customerSpawner;
    [SerializeField] private EpisodeData     testEpisode;

    void Update()
    {
        bool advancePressed = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space);
        bool bookPressed    = Input.GetKeyDown(KeyCode.Tab);
        bool ticketPressed  = Input.GetKeyDown(KeyCode.E);

        GameMode mode = modeManager != null ? modeManager.CurrentMode : GameMode.OrderMode;

        switch (mode)
        {
            case GameMode.OrderMode:
                if (dialogue == null || !dialogue.IsOpen) HandleCameraInput();
                if (advancePressed) dialogue?.Advance();
                if (bookPressed)    recipeBook?.Toggle();
                if (ticketPressed)  orderTicketManager?.ToggleTicket();
                if (Input.GetKeyDown(KeyCode.Alpha1))
                    customerSpawner?.ShowCustomers(new[] { "yukari" });
                if (Input.GetKeyDown(KeyCode.Alpha2) && testEpisode != null)
                {
                    modeManager?.RequestModeChange(GameMode.EpisodeMode);
                    episodeRunner?.Begin(testEpisode);
                }
                break;

            case GameMode.EpisodeMode:
                if (advancePressed) RouteAdvanceToEncounter();
                break;

            case GameMode.CraftingMode:
                HandleCameraInput();
                if (advancePressed) RouteAdvanceToEncounter();
                if (bookPressed)    recipeBook?.Toggle();
                if (ticketPressed)  orderTicketManager?.ToggleTicket();
                break;
        }
    }

    private void HandleCameraInput()
    {
        if (cameraRig == null || cameraRig.IsAnimating) return;

        if (Input.GetKeyDown(KeyCode.S)) cameraRig.OnCameraInput(CameraDirection.DrawerOpen);
        if (Input.GetKeyDown(KeyCode.W)) cameraRig.OnCameraInput(CameraDirection.DrawerClose);
    }

    private void RouteAdvanceToEncounter()
    {
        if (episodeRunner != null && episodeRunner.CanReceiveAdvanceInput)
        {
            episodeRunner.OnAdvanceInput();
            return;
        }

        if (episodeRunner != null && episodeRunner.IsWaitingForChoice)
            return;

        dialogue?.Advance();
    }
}
