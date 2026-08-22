using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopUIManager : BaseUIManager
{
    // 패널과 무관하게 ShopWindow에 상시 깔려있는 배경류 이미지 하나를 상점 모드(일반/이상한)에 따라
    // 다른 스프라이트로 교체할 때 쓰는 단위. 같은 3줄짜리 교체 로직이 여러 곳에 반복되는 걸 막기 위함.
    [System.Serializable]
    private class ThemedSprite
    {
        public Image  image;
        public Sprite normalSprite;
        public Sprite strangeSprite;

        public void Apply(bool strange)
        {
            if (image == null) return;
            Sprite sprite = strange ? strangeSprite : normalSprite;
            if (sprite != null) image.sprite = sprite;
        }
    }

    [Header("Fixed Header")]
    public TextMeshProUGUI moneyText;
    [Tooltip("Parent containing the currency icon + moneyText. Hidden while the first-open loading screen plays.")]
    public GameObject moneyDisplayRoot;

    [Header("Navigation")]
    // 현재 화면이 홈이 아닐 때만 활성화되는 뒤로가기 버튼 오브젝트
    public GameObject backButtonObject;

    [Header("Loading Screen")]
    // 씬 진입 후 상점을 처음 열 때만 재생
    public ShopLoadingScreen loadingScreen;

    [Header("Screens")]
    public GameObject homePanel;
    public GameObject ingredientCategoryPanel;
    public GameObject itemScrollPanel;
    public GameObject upgradePanel;
    public GameObject recipeBookPanel;

    [Header("Ingredient Categories")]
    public Transform categoryButtonContent;
    public LiquorCategoryButtonUI categoryButtonPrefab;
    public LiquorCategoryDef[] ingredientCategories;

    [Header("Ingredient List")]
    [Tooltip("Shown inside itemScrollPanel only, set dynamically to the clicked category's name. Home/IngredientCategories/Upgrade titles are authored directly in each panel, not set from code.")]
    public TextMeshProUGUI categoryNameText;
    [Tooltip("Same-shape accent image next to categoryNameText, tinted with the category's unique color. Sprite is authored in-scene; only the color is set from code.")]
    public Image categoryNameColorImage;
    public ScrollRect itemScrollView;
    public Transform contentRoot;
    public GameObject slotPrefab;
    public LiquorBottleCatalog catalog;

    [Header("Upgrades")]
    public Transform upgradeContentRoot;
    public UpgradeSlotUI upgradeSlotPrefab;
    public List<UpgradeDef> allUpgrades;

    [Header("Recipe Books")]
    public Transform recipeBookContentRoot;
    public RecipeBookSlotUI recipeBookSlotPrefab;
    public List<RecipeBookDef> allRecipeBooks;

    [Header("Strange Shop")]
    [Tooltip("이 에피소드를 클리어하면 이상한 상점이 해금됨(코인 아이콘 노출).")]
    public string strangeShopUnlockEpisodeId = "StrangeCoin_0";
    public Button strangeShopIconButton;
    public Image  strangeShopIconImage;
    public Sprite strangeShopIconOffSprite;
    public Sprite strangeShopIconOnSprite;
    public TextMeshProUGUI strangeCoinText;
    [Tooltip("이상한 동전 아이콘 + strangeCoinText를 담은 부모. moneyDisplayRoot와 상호 배타적으로 표시됨.")]
    public GameObject strangeCoinDisplayRoot;

    public GameObject strangeIngredientCategoryPanel;
    public GameObject strangeItemScrollPanel;

    public Transform strangeCategoryButtonContent;
    public LiquorCategoryButtonUI strangeCategoryButtonPrefab;

    public TextMeshProUGUI strangeCategoryNameText;
    public Image strangeCategoryNameColorImage;
    public ScrollRect strangeItemScrollView;
    public Transform strangeContentRoot;
    public GameObject strangeSlotPrefab;

    [Header("Strange Shop Skin (패널 밖, ShopWindow에 상시 깔려있는 배경류)")]
    [Tooltip("ShopWindow 루트 자신의 배경 Image.")]
    [SerializeField] private ThemedSprite windowBackgroundSkin;
    [SerializeField] private ThemedSprite upperOverlaySkin;
    [SerializeField] private ThemedSprite backButtonSkin;

    private readonly List<ItemSlotUI> _itemSlots = new();
    private readonly List<UpgradeSlotUI> _upgradeSlots = new();
    private readonly List<RecipeBookSlotUI> _recipeBookSlots = new();
    private readonly List<ItemSlotUI> _strangeItemSlots = new();
    private readonly IShopCurrency _strangeCurrency = new StrangeCoinShopCurrency();
    private bool _inStrangeShop = false;

    // 화면별 "뒤로가기" 시 돌아갈 상위 화면 매핑 (화면이 늘어나도 매핑만 추가하면 됨)
    private Dictionary<GameObject, GameObject> _parentScreenMap;
    private GameObject _currentScreen;
    private bool _hasShownLoadingOnce = false;

    protected override void OnOpen()
    {
        // 열릴 때마다 일반 상점 홈 화면으로 초기화(이상한 상점에 있다가 닫았어도 리셋)
        _inStrangeShop = false;
        if (moneyDisplayRoot)      moneyDisplayRoot.SetActive(true);
        if (strangeCoinDisplayRoot) strangeCoinDisplayRoot.SetActive(false);
        RefreshStrangeShopIcon();
        ApplyShopSkin(false);

        ShowHome();
        RefreshMoneyText();
        RefreshStrangeCoinText();
    }

    void Start()
    {
        // ShowHome()은 OnOpen()에서 항상 호출됨. 여기서 또 부르면 최초 오픈 시
        // Start()가 OpenUI() 같은 프레임에 지연 실행되면서 AnimateOpen()이 로딩 연출을
        // 위해 꺼둔 homePanel을 다시 켜버려 로딩 스크린과 메인 화면이 겹쳐 보이는 문제가 있었음.
        CategoryColorText.Register(ingredientCategories);
        BuildParentScreenMap();
        BuildCategoryButtons();
        BuildUpgradeSlots();
        BuildRecipeBookSlots();
        BuildStrangeCategoryButtons();

        if (strangeShopIconButton) strangeShopIconButton.onClick.AddListener(OnStrangeShopIconClicked);
    }

    // 펼침 애니메이션 없이 즉시 표시
    protected override IEnumerator AnimateOpen()
    {
        if (_canvasGroup) { _canvasGroup.alpha = 1; _canvasGroup.interactable = true; _canvasGroup.blocksRaycasts = true; }

        // 씬 진입 후 상점을 처음 열 때만: 표시 직후 로딩 화면을 재생
        if (!_hasShownLoadingOnce && loadingScreen != null)
        {
            _hasShownLoadingOnce = true;
            if (_canvasGroup) _canvasGroup.interactable = false;
            if (moneyDisplayRoot) moneyDisplayRoot.SetActive(false);
            // 별도 blocker로 가리는 대신 홈 화면 자체를 꺼서 로딩 UI만 보이게 함
            if (homePanel) homePanel.SetActive(false);

            // 로딩 연출 도중 작전판 등 다른 팝업으로 전환되지 않도록 막음
            LockTransitions();
            yield return loadingScreen.Play();
            UnlockTransitions();

            if (homePanel) homePanel.SetActive(true);
            if (moneyDisplayRoot) moneyDisplayRoot.SetActive(true);
            if (_canvasGroup) _canvasGroup.interactable = true;
        }
    }

    // 접힘 애니메이션 없이 즉시 숨김
    protected override IEnumerator AnimateClose()
    {
        if (_canvasGroup) { _canvasGroup.interactable = false; _canvasGroup.blocksRaycasts = false; }

        // 로딩 연출이 끝나기 전에 강제로 닫힌 경우를 대비한 방어적 리셋
        // (StopCoroutine으로 AnimateOpen이 중단되면 위 UnlockTransitions/homePanel 복원이 실행되지 않음)
        UnlockTransitions();
        if (loadingScreen) loadingScreen.ResetVisual();

        gameObject.SetActive(false);
        yield break;
    }

    // --- [화면 전환] ---
    // Home/재료 대분류/업그레이드 화면의 타이틀(텍스트+이미지)은 각 패널 안에 직접 배치되어
    // 패널 활성화만으로 자연히 보임 — 코드에서 문자를 넣지 않음.

    public void ShowHome()
    {
        SetScreen(homePanel);
    }

    public void OpenIngredients()
    {
        SetScreen(ingredientCategoryPanel);
    }

    public void OpenUpgrades()
    {
        SetScreen(upgradePanel);
        foreach (var slot in _upgradeSlots) slot.Refresh();
    }

    public void OpenRecipeBooks()
    {
        SetScreen(recipeBookPanel);
        foreach (var slot in _recipeBookSlots) slot.Refresh();
    }

    public void ShowListByCategory(LiquorCategoryDef category)
    {
        SetScreen(itemScrollPanel);
        if (categoryNameText)       categoryNameText.text = category != null ? category.displayName : "";
        if (categoryNameColorImage && category != null) categoryNameColorImage.color = category.color;
        UpdateList(category);
        if (itemScrollView) itemScrollView.verticalNormalizedPosition = 1f;
    }

    // --- [레시피북] ---

    private void BuildRecipeBookSlots()
    {
        if (recipeBookContentRoot == null || recipeBookSlotPrefab == null) return;

        foreach (var def in allRecipeBooks)
        {
            if (def == null) continue;
            var slot = Instantiate(recipeBookSlotPrefab, recipeBookContentRoot);
            slot.Bind(def);
            slot.OnPurchased += RefreshMoneyText;
            _recipeBookSlots.Add(slot);
        }
    }

    private void SetScreen(GameObject target)
    {
        if (homePanel)                      homePanel.SetActive(target == homePanel);
        if (ingredientCategoryPanel)        ingredientCategoryPanel.SetActive(target == ingredientCategoryPanel);
        if (itemScrollPanel)                itemScrollPanel.SetActive(target == itemScrollPanel);
        if (upgradePanel)                   upgradePanel.SetActive(target == upgradePanel);
        if (recipeBookPanel)                recipeBookPanel.SetActive(target == recipeBookPanel);
        if (strangeIngredientCategoryPanel) strangeIngredientCategoryPanel.SetActive(target == strangeIngredientCategoryPanel);
        if (strangeItemScrollPanel)         strangeItemScrollPanel.SetActive(target == strangeItemScrollPanel);

        _currentScreen = target;
        // 홈 화면과 이상한 상점 진입 화면(재료 카테고리)은 최상위 화면 — 뒤로가기 대신 코인 아이콘으로만 전환
        if (backButtonObject)
            backButtonObject.SetActive(target != homePanel && target != strangeIngredientCategoryPanel);
    }

    // 화면마다 정해진 상위 화면 하나로 돌아감 (히스토리 스택 아님)
    private void BuildParentScreenMap()
    {
        _parentScreenMap = new Dictionary<GameObject, GameObject>();
        if (ingredientCategoryPanel) _parentScreenMap[ingredientCategoryPanel] = homePanel;
        if (itemScrollPanel)         _parentScreenMap[itemScrollPanel] = ingredientCategoryPanel;
        if (upgradePanel)            _parentScreenMap[upgradePanel] = homePanel;
        if (recipeBookPanel)         _parentScreenMap[recipeBookPanel] = homePanel;
        // strangeIngredientCategoryPanel은 이상한 상점의 최상위 화면이라 상위 매핑 없음(코인 아이콘으로만 나감)
        if (strangeItemScrollPanel)  _parentScreenMap[strangeItemScrollPanel] = strangeIngredientCategoryPanel;
    }

    // --- [이상한 상점] ---

    private void RefreshStrangeShopIcon()
    {
        bool unlocked = GameProgress.Instance.IsEpisodeCompleted(strangeShopUnlockEpisodeId);
        if (strangeShopIconButton) strangeShopIconButton.gameObject.SetActive(unlocked);
        if (strangeShopIconImage && strangeShopIconOffSprite) strangeShopIconImage.sprite = strangeShopIconOffSprite;
    }

    // 코인 아이콘 클릭: 일반 상점 ↔ 이상한 상점 전환
    private void OnStrangeShopIconClicked()
    {
        _inStrangeShop = !_inStrangeShop;

        if (strangeShopIconImage)
            strangeShopIconImage.sprite = _inStrangeShop ? strangeShopIconOnSprite : strangeShopIconOffSprite;

        if (moneyDisplayRoot)       moneyDisplayRoot.SetActive(!_inStrangeShop);
        if (strangeCoinDisplayRoot) strangeCoinDisplayRoot.SetActive(_inStrangeShop);
        ApplyShopSkin(_inStrangeShop);

        if (_inStrangeShop) SetScreen(strangeIngredientCategoryPanel);
        else                ShowHome();
    }

    // 패널 밖에서 상시 표시되는 배경류(윈도우 배경/오버레이/뒤로가기 버튼)를 상점 모드에 맞춰 교체
    private void ApplyShopSkin(bool strange)
    {
        windowBackgroundSkin.Apply(strange);
        upperOverlaySkin.Apply(strange);
        backButtonSkin.Apply(strange);
    }

    private void BuildStrangeCategoryButtons()
    {
        if (strangeCategoryButtonContent == null || strangeCategoryButtonPrefab == null) return;

        foreach (var category in ingredientCategories)
        {
            if (category == null) continue;
            var btn = Instantiate(strangeCategoryButtonPrefab, strangeCategoryButtonContent);
            btn.Bind(category, ShowStrangeListByCategory);
        }
    }

    public void ShowStrangeListByCategory(LiquorCategoryDef category)
    {
        SetScreen(strangeItemScrollPanel);
        if (strangeCategoryNameText)       strangeCategoryNameText.text = category != null ? category.displayName : "";
        if (strangeCategoryNameColorImage && category != null) strangeCategoryNameColorImage.color = category.color;
        UpdateStrangeList(category);
        if (strangeItemScrollView) strangeItemScrollView.verticalNormalizedPosition = 1f;
    }

    private void UpdateStrangeList(LiquorCategoryDef categoryFilter)
    {
        foreach (Transform child in strangeContentRoot) Destroy(child.gameObject);
        _strangeItemSlots.Clear();

        var result = catalog.bottles.Where(bottle =>
            bottle != null && (categoryFilter == null || bottle.category == categoryFilter)
        ).ToList();

        foreach (var bottle in result)
        {
            GameObject slotObj = Instantiate(strangeSlotPrefab, strangeContentRoot);
            var slot = slotObj.GetComponent<ItemSlotUI>();
            slot.Setup(bottle, _strangeCurrency, null, bottle.DefaultAmount);
            slot.OnPurchased += RefreshStrangeCoinText;
            _strangeItemSlots.Add(slot);
        }
    }

    public void RefreshStrangeCoinText()
    {
        if (strangeCoinText) strangeCoinText.text = $"{_strangeCurrency.CurrentAmount:N0}";

        // 잔액이 바뀌면 다른 슬롯들의 구매 가능 여부도 같이 갱신되어야 함
        foreach (var slot in _strangeItemSlots) slot.Refresh();
    }

    // 뒤로가기 버튼 OnClick에 연결
    public void GoBack()
    {
        if (_currentScreen == null || _parentScreenMap == null) return;
        if (_parentScreenMap.TryGetValue(_currentScreen, out var parent))
        {
            SetScreen(parent);
        }
    }

    // --- [재료] ---

    private void BuildCategoryButtons()
    {
        if (categoryButtonContent == null || categoryButtonPrefab == null) return;

        foreach (var category in ingredientCategories)
        {
            if (category == null) continue;
            var btn = Instantiate(categoryButtonPrefab, categoryButtonContent);
            btn.Bind(category, ShowListByCategory);
        }
    }

    private void UpdateList(LiquorCategoryDef categoryFilter)
    {
        foreach (Transform child in contentRoot) Destroy(child.gameObject);
        _itemSlots.Clear();

        var result = catalog.bottles.Where(bottle =>
            bottle != null && (categoryFilter == null || bottle.category == categoryFilter)
        ).ToList();

        foreach (var bottle in result)
        {
            GameObject slotObj = Instantiate(slotPrefab, contentRoot);
            var slot = slotObj.GetComponent<ItemSlotUI>();
            slot.Setup(bottle, null, null, bottle.DefaultAmount);
            slot.OnPurchased += RefreshMoneyText;
            _itemSlots.Add(slot);
        }
    }

    // --- [업그레이드] ---

    private void BuildUpgradeSlots()
    {
        if (upgradeContentRoot == null || upgradeSlotPrefab == null) return;

        foreach (var def in allUpgrades)
        {
            if (def == null) continue;
            var slot = Instantiate(upgradeSlotPrefab, upgradeContentRoot);
            slot.Bind(def);
            slot.OnPurchased += RefreshMoneyText;
            _upgradeSlots.Add(slot);
        }
    }

    // --- [소지금] ---

    public void RefreshMoneyText()
    {
        // Currency unit is a fixed icon image placed before moneyText in the scene, not part of this string.
        if (moneyText) moneyText.text = $"{GameProgress.Instance.CurrentMoney:N0}";

        // 잔액이 바뀌면 다른 슬롯들의 구매 가능 여부(버튼 활성화/가격 색상)도 같이 갱신되어야 함
        foreach (var slot in _itemSlots) slot.Refresh();
        foreach (var slot in _upgradeSlots) slot.Refresh();
        foreach (var slot in _recipeBookSlots) slot.Refresh();
    }
}
