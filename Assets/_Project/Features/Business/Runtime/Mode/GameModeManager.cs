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
    // 제조 모드 요청이 카메라 축소를 기다리는 중인지. 그동안 대화 포커스가 다시 확대하면 안 된다.
    public bool IsCraftingRequestPending => _craftingRequestPending;

    private bool _craftingRequestPending;
    private int _craftingRequestToken;
    private ICraftingEntryTransition _craftingEntryTransition;

    public event Action<GameMode, GameMode> OnModeChanged;

    protected override void Awake()
    {
        base.Awake();
    }

    void Start()
    {
        ApplyMode(initialMode, force: true);
    }

    // 제조 진입 전 연출(말풍선 페이드 아웃 → 카메라 축소)을 맡을 대상. 없으면 카메라 축소만 기다린다.
    public void SetCraftingEntryTransition(ICraftingEntryTransition transition)
    {
        _craftingEntryTransition = transition;
    }

    // 제조 모드는 카메라가 QHD 전체로 축소된 뒤에 들어간다. 바텐딩 세션이 생성 시점의 화면 배치로
    // 슬롯 줄을 한 번만 정렬하므로, 확대된 상태에서 세션을 만들면 슬롯 위치가 어긋나기 때문이다.
    // 기다리는 동안 다른 모드 요청이 오면 대기 중인 제조 진입은 취소한다(토큰으로 늦게 온 콜백 무시).
    // 연출이 즉시 끝나면(이미 전체 화면·말풍선 없음) 콜백이 동기로 불려 기존처럼 바로 전환된다.
    public void RequestModeChange(GameMode newMode)
    {
        int token = ++_craftingRequestToken;
        _craftingRequestPending = false;
        if (CurrentMode == newMode) return;

        if (newMode != GameMode.CraftingMode)
        {
            ApplyMode(newMode, force: false);
            return;
        }

        _craftingRequestPending = true;
        Action enterCrafting = () =>
        {
            if (token != _craftingRequestToken || CurrentMode == newMode)
                return;
            _craftingRequestPending = false;
            ApplyMode(newMode, force: false);
        };

        if (_craftingEntryTransition != null)
        {
            _craftingEntryTransition.PlayBeforeCrafting(enterCrafting);
            return;
        }

        FrontCameraRig rig = ResolveCameraRig();
        if (rig != null)
            rig.ResetView(enterCrafting);
        else
            enterCrafting();
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
        // 술 선택 공간은 항상 보이고 제조 중에만 조작을 받는다(도구 공간은 바텐딩 세션 유무로 스스로 판단).
        ingredientSelection?.SetCraftingAvailable(mode == GameMode.CraftingMode);

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
