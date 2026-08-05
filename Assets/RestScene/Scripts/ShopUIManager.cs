using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopUIManager : BaseUIManager
{
    [Header("Shop Animation")]
    public float animDuration = 0.5f;
    public float lineWidth = 0.02f;

    [Header("Fixed Header")]
    public TextMeshProUGUI moneyText;

    [Header("Screens")]
    public GameObject homePanel;
    public GameObject ingredientCategoryPanel;
    public GameObject itemScrollPanel;
    public GameObject upgradePanel;

    [Header("Ingredient Categories")]
    public Transform categoryButtonContent;
    public LiquorCategoryButtonUI categoryButtonPrefab;
    public LiquorCategoryDef[] ingredientCategories;

    [Header("Ingredient List")]
    [Tooltip("Shown inside itemScrollPanel only, set dynamically to the clicked category's name. Home/IngredientCategories/Upgrade titles are authored directly in each panel, not set from code.")]
    public TextMeshProUGUI categoryNameText;
    public ScrollRect itemScrollView;
    public Transform contentRoot;
    public GameObject slotPrefab;
    public List<LiquorBottleDef> allBottles;

    [Header("Upgrades")]
    public Transform upgradeContentRoot;
    public UpgradeSlotUI upgradeSlotPrefab;
    public List<UpgradeDef> allUpgrades;

    private readonly List<UpgradeSlotUI> _upgradeSlots = new();

    // 부모 Awake 호출 필수
    protected override void Awake()
    {
        base.Awake();
        // 상점 전용 초기화: 점 형태로 시작
        transform.localScale = new Vector3(0, lineWidth, 1);
        if(_canvasGroup) _canvasGroup.alpha = 1; // Scale로 조절할 거라 알파는 1
    }

    protected override void OnOpen()
    {
        // 열릴 때마다 홈 화면으로 초기화
        ShowHome();
        RefreshMoneyText();
    }

    void Start()
    {
        BuildCategoryButtons();
        BuildUpgradeSlots();
        ShowHome();
    }

    // ▼▼▼ [애니메이션] 가로선 -> 펼쳐짐 ▼▼▼
    protected override IEnumerator AnimateOpen()
    {
        float timer = 0f;
        float halfDuration = animDuration * 0.5f;

        // 1. 가로로 펴지기
        transform.localScale = new Vector3(0, lineWidth, 1);
        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / halfDuration;
            transform.localScale = new Vector3(Mathf.Lerp(0f, 1f, timer), lineWidth, 1);
            yield return null;
        }

        // 2. 세로로 펴지기
        timer = 0f;
        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / halfDuration;
            transform.localScale = new Vector3(1, Mathf.Lerp(lineWidth, 1f, timer), 1);
            yield return null;
        }
        transform.localScale = Vector3.one;
        if(_canvasGroup) { _canvasGroup.interactable = true; _canvasGroup.blocksRaycasts = true; }
    }

    protected override IEnumerator AnimateClose()
    {
        if(_canvasGroup) { _canvasGroup.interactable = false; _canvasGroup.blocksRaycasts = false; }

        float timer = 0f;
        float halfDuration = animDuration * 0.5f;

        // 1. 세로로 접기
        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / halfDuration;
            transform.localScale = new Vector3(1, Mathf.Lerp(1f, lineWidth, timer), 1);
            yield return null;
        }

        // 2. 가로로 접기
        timer = 0f;
        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / halfDuration;
            transform.localScale = new Vector3(Mathf.Lerp(1f, 0f, timer), lineWidth, 1);
            yield return null;
        }

        gameObject.SetActive(false);
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

    // 레시피북 상점 화면은 기획 미정 — 클릭만 받고 아직 아무 동작도 하지 않는 스텁
    public void OnRecipeBookClicked()
    {
    }

    public void ShowListByCategory(LiquorCategoryDef category)
    {
        SetScreen(itemScrollPanel);
        if (categoryNameText) categoryNameText.text = category != null ? category.displayName : "";
        UpdateList(category);
        if (itemScrollView) itemScrollView.verticalNormalizedPosition = 1f;
    }

    private void SetScreen(GameObject target)
    {
        if (homePanel)               homePanel.SetActive(target == homePanel);
        if (ingredientCategoryPanel) ingredientCategoryPanel.SetActive(target == ingredientCategoryPanel);
        if (itemScrollPanel)         itemScrollPanel.SetActive(target == itemScrollPanel);
        if (upgradePanel)            upgradePanel.SetActive(target == upgradePanel);
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

        var result = allBottles.Where(bottle =>
            bottle != null && (categoryFilter == null || bottle.category == categoryFilter)
        ).ToList();

        foreach (var bottle in result)
        {
            GameObject slotObj = Instantiate(slotPrefab, contentRoot);
            var slot = slotObj.GetComponent<ItemSlotUI>();
            slot.Setup(bottle);
            slot.OnPurchased += RefreshMoneyText;
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
        if (moneyText) moneyText.text = $"{GameProgress.Instance.CurrentMoney:N0} G";
    }
}
