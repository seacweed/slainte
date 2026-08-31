using Slainte.Shared.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class InputRouter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GameModeManager    modeManager;
    [SerializeField] private FrontCameraRig     cameraRig;
    [SerializeField] private RecipeBookUI       recipeBook;
    [SerializeField] private OrderTicketManager orderTicketManager;
    [SerializeField] private LiquorShelfUI      liquorShelf;
    [SerializeField] private DialogueController dialogue;

    [Header("Dialogue Handlers")]
    [SerializeField] private EpisodeRunner episodeRunner;

    [Header("Test")]
    [SerializeField] private CustomerSpawner customerSpawner;
    [SerializeField] private EpisodeData     testEpisode;

    void Update()
    {
        Keyboard keyboard  = Keyboard.current;
        bool searchFocused = recipeBook != null && recipeBook.IsSearchFocused;

        bool advancePressed = Input.GetMouseButtonDown(0)
            || (!searchFocused && WasPressed(keyboard?.spaceKey, KeyCode.Space));
        bool bookPressed    = !searchFocused && WasPressed(keyboard?.aKey, KeyCode.A);
        bool ticketPressed  = !searchFocused && WasPressed(keyboard?.tabKey, KeyCode.Tab);
        bool shelfPressed   = !searchFocused && WasPressed(keyboard?.dKey, KeyCode.D);

        GameMode mode = modeManager != null ? modeManager.CurrentMode : GameMode.OrderMode;

        switch (mode)
        {
            case GameMode.OrderMode:
                HandleCameraInput(keyboard, searchFocused);
                if (advancePressed) dialogue?.Advance();
                if (bookPressed)    recipeBook?.Toggle();
                if (ticketPressed)  orderTicketManager?.ToggleTicket();
                if (shelfPressed)   liquorShelf?.Toggle();
                if (WasPressed(keyboard?.digit1Key, KeyCode.Alpha1))
                    customerSpawner?.ShowCustomers(new[] { "yukari" });
                if (WasPressed(keyboard?.digit2Key, KeyCode.Alpha2) && testEpisode != null)
                {
                    modeManager?.RequestModeChange(GameMode.EpisodeMode);
                    episodeRunner?.Begin(testEpisode);
                }
                break;

            case GameMode.EpisodeMode:
                if (advancePressed) RouteAdvanceToEncounter();
                break;

            case GameMode.CraftingMode:
                HandleCameraInput(keyboard, searchFocused);
                if (advancePressed) RouteAdvanceToEncounter();
                if (bookPressed)    recipeBook?.Toggle();
                if (ticketPressed)  orderTicketManager?.ToggleTicket();
                if (shelfPressed)   liquorShelf?.Toggle();
                break;
        }
    }

    private void HandleCameraInput(Keyboard keyboard, bool searchFocused)
    {
        if (cameraRig == null || cameraRig.IsAnimating || searchFocused) return;

        if (WasPressed(keyboard?.sKey, KeyCode.S)) cameraRig.OnCameraInput(CameraDirection.DrawerOpen);
        if (WasPressed(keyboard?.wKey, KeyCode.W)) cameraRig.OnCameraInput(CameraDirection.DrawerClose);
    }

    private static bool WasPressed(KeyControl inputSystemKey, KeyCode legacyKey)
    {
        bool pressed = inputSystemKey != null && inputSystemKey.wasPressedThisFrame;
#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetKeyDown(legacyKey);
#endif
        return pressed;
    }

    private void RouteAdvanceToEncounter()
    {
        if (episodeRunner != null && episodeRunner.IsRunning)
        {
            if (episodeRunner.CanReceiveAdvanceInput)
                episodeRunner.OnAdvanceInput();

            return;
        }

        dialogue?.Advance();
    }
}
