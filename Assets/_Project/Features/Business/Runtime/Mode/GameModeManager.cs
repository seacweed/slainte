using System;
using Slainte.Shared.Lifecycle;
using UnityEngine;

public class GameModeManager : SceneSingleton<GameModeManager>
{
    [Header("Panels")]
    [SerializeField] private CanvasGroup frontWorldPanel;
    [SerializeField] private CanvasGroup dialoguePanel;
    [SerializeField] private CanvasGroup choiceContainer;
    [SerializeField] private CanvasGroup craftingJudgePanel;

    [Header("UI")]
    [SerializeField] private RecipeBookUI  recipeBook;
    [SerializeField] private OrderTicketUI orderTicketUI;
    [SerializeField] private LiquorShelfUI liquorShelf;
    [SerializeField] private Slainte.Bartending.IngredientSelectionUI ingredientSelection;

    [Header("Camera")]
    [SerializeField] private FrontCameraRig cameraRig;

    [Header("Initial Mode")]
    [SerializeField] private GameMode initialMode = GameMode.OrderMode;

    public GameMode CurrentMode { get; private set; }

    public event Action<GameMode, GameMode> OnModeChanged;

    protected override void Awake()
    {
        base.Awake();
    }

    void Start()
    {
        ApplyMode(initialMode, force: true);
    }

    public void RequestModeChange(GameMode newMode)
    {
        if (CurrentMode == newMode) return;
        ApplyMode(newMode, force: false);
    }

    private void ApplyMode(GameMode newMode, bool force)
    {
        GameMode oldMode = CurrentMode;
        CurrentMode = newMode;

        RefreshPanels(newMode);

        if (!force)
            OnModeChanged?.Invoke(oldMode, newMode);
    }

    private void RefreshPanels(GameMode mode)
    {
        bool showFrontWorld   = mode == GameMode.OrderMode
                             || mode == GameMode.EpisodeMode
                             || mode == GameMode.CraftingMode;
        bool showDialogue     = mode == GameMode.OrderMode
                             || mode == GameMode.EpisodeMode
                             || mode == GameMode.CraftingMode;
        bool showChoices      = mode == GameMode.EpisodeMode;
        bool showCraftingJudge = mode == GameMode.CraftingMode;

        SetGroup(frontWorldPanel,    showFrontWorld);
        SetGroup(dialoguePanel,      showDialogue);
        SetGroup(choiceContainer,    showChoices);
        SetGroup(craftingJudgePanel, showCraftingJudge);

        bool isEpisode = mode == GameMode.EpisodeMode;
        recipeBook?.SetInteractable(!isEpisode);
        orderTicketUI?.SetInteractable(!isEpisode);
        liquorShelf?.SetInteractable(!isEpisode);
        // 술 선택 공간은 사용자가 여닫지 못하고, 제조 가능 여부에 따라서만 올라오거나 내려간다.
        ingredientSelection?.SetCraftingAvailable(mode == GameMode.CraftingMode);

        // 에피소드 대화 중에는 카메라가 캐릭터를 따라 가로로 팬한 상태일 수 있다. 제조 구간에서는
        // 조작 영역(제작 공간·술 선택 공간)만 화면 가로 중앙에 고정해 한쪽으로 치우치지 않게 한다.
        // 대화 구간에는 꺼야 한다 — 계속 고정하면 제작대만 바 카운터에서 떨어져 미끄러진다.
        ResolveCameraRig()?.SetHorizontalFixed(mode == GameMode.CraftingMode);

        if (!isEpisode)
        {
            recipeBook?.Open();
        }
    }

    // 인스펙터 연결이 비어 있어도 동작하도록 씬에서 한 번 찾아 캐시한다.
    private FrontCameraRig ResolveCameraRig()
    {
        if (cameraRig == null)
            cameraRig = FindFirstObjectByType<FrontCameraRig>();
        return cameraRig;
    }

    private static void SetGroup(CanvasGroup cg, bool on)
    {
        if (cg == null) return;
        cg.alpha          = on ? 1f : 0f;
        cg.interactable   = on;
        cg.blocksRaycasts = on;
    }
}
