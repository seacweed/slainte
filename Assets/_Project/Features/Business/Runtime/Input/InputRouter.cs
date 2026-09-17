using Slainte.Bartending;
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
    [SerializeField] private IngredientSelectionUI ingredientSelection;
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
        bool bookPressed     = !searchFocused && WasPressed(keyboard?.qKey, KeyCode.Q);
        bool ticketPressed   = !searchFocused && WasPressed(keyboard?.eKey, KeyCode.E);
        bool previousPressed = !searchFocused && WasPressed(keyboard?.aKey, KeyCode.A);
        bool nextPressed     = !searchFocused && WasPressed(keyboard?.dKey, KeyCode.D);

        GameMode mode = modeManager != null ? modeManager.CurrentMode : GameMode.OrderMode;

        switch (mode)
        {
            case GameMode.OrderMode:
                HandleCameraInput(keyboard, searchFocused);
                if (advancePressed) dialogue?.Advance();
                if (bookPressed)    recipeBook?.Toggle();
                if (ticketPressed)  orderTicketManager?.ToggleTicket();
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
                // 술 선택 공간은 제조 중에만 올라와 있으므로 대분류 전환도 제조 모드에서만 받는다.
                if (previousPressed) ingredientSelection?.ShowPrevious();
                if (nextPressed)     ingredientSelection?.ShowNext();
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
