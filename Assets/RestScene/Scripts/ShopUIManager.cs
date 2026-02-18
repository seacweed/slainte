using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

public class ShopUIManager : BaseUIManager
{
    [Header("Shop Animation")]
    public float animDuration = 0.5f;
    public float lineWidth = 0.02f;

    [Header("Fixed Header")]
    public TMP_InputField searchInput;

    [Header("UI Objects")]
    public TextMeshProUGUI categoryTitleText;
    public GameObject categoryPanel;   
    public GameObject itemScrollPanel; 

    [Header("List Components")]
    public ScrollRect itemScrollView;
    public Transform contentRoot;      
    public GameObject slotPrefab;      
    
    [Header("Database")]
    public List<ItemData> allItems;    

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
        GoHome();
    }

    void Start()
    {
        GoHome();
        if(searchInput) searchInput.onValueChanged.AddListener(OnSearchValueChange);

        var catButtons = categoryPanel.GetComponentsInChildren<CategoryButton>();
        foreach (var btn in catButtons) btn.Init(this);
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

    // --- [기존 상점 로직들] ---

    public void GoHome()
    {
        categoryPanel.SetActive(true);
        itemScrollPanel.SetActive(false);
        if(searchInput) searchInput.text = ""; 
    }

    public void ShowListByCategory(ItemData.ItemType category)
    {
        categoryPanel.SetActive(false);
        itemScrollPanel.SetActive(true);
        if (categoryTitleText != null) categoryTitleText.text = GetCategoryName(category);
        UpdateList(category, ""); 
        itemScrollView.verticalNormalizedPosition = 1f;
    }

    public void OnSearchValueChange(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { GoHome(); return; }
        if (!itemScrollPanel.activeSelf) { categoryPanel.SetActive(false); itemScrollPanel.SetActive(true); }
        if (categoryTitleText != null) categoryTitleText.text = $"'{text}' 검색 결과";
        
        UpdateList(null, text);
        if (itemScrollView.verticalNormalizedPosition != 1f) itemScrollView.verticalNormalizedPosition = 1f;
    }

    private void UpdateList(ItemData.ItemType? categoryFilter, string searchFilter)
    {
        foreach (Transform child in contentRoot) Destroy(child.gameObject);

        var result = allItems.Where(item => 
            (categoryFilter == null || item.category == categoryFilter) && 
            (string.IsNullOrEmpty(searchFilter) || item.itemName.Contains(searchFilter))
        ).ToList();

        foreach (var item in result)
        {
            GameObject slot = Instantiate(slotPrefab, contentRoot);
            slot.GetComponent<ItemSlotUI>().Setup(item);
        }
    }

    private string GetCategoryName(ItemData.ItemType type)
    {
        return type switch
        {
            ItemData.ItemType.Alcohol => "알코올",
            ItemData.ItemType.Liqueur => "리큐르",
            ItemData.ItemType.NonAlcohol => "논알콜",
            ItemData.ItemType.Powder => "파우더",
            ItemData.ItemType.Tool => "도구",
            ItemData.ItemType.Glass => "잔",
            _ => "검색 결과" // 기본값
        };
    }
}