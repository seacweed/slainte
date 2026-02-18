using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class SituationUIManager : BaseUIManager
{
    [Header("Situation Animation")]
    public float animDuration = 0.3f;
    public float slideDistance = 150f; // 아래에서 얼마나 올라올지

    [Header("Dashboard UI")]
    public TextMeshProUGUI moneyText;
    public TextMeshProUGUI statusText;
    public Button toggleButton;
    public TextMeshProUGUI btnText;

    // 임시 데이터 (나중에 GameManager 연동)
    private bool isBusinessOpen = false;
    private int money = 5000;
    private Vector3 originalPos;

    protected override void Awake()
    {
        base.Awake();
        // UI의 원래 위치(정중앙) 저장
        originalPos = GetComponent<RectTransform>().anchoredPosition;
        
        if (toggleButton) toggleButton.onClick.AddListener(OnToggleClick);
    }

    protected override void OnOpen()
    {
        UpdateDashboard(); // 열릴 때 정보 갱신
    }

    // ▼▼▼ [애니메이션] 아래에서 위로 슬라이드 ▼▼▼
    protected override IEnumerator AnimateOpen()
    {
        float timer = 0f;
        RectTransform rt = GetComponent<RectTransform>();
        Vector3 startPos = originalPos - new Vector3(0, slideDistance, 0); // 아래

        // 초기 상태
        rt.anchoredPosition = startPos;
        _canvasGroup.alpha = 0;
        transform.localScale = Vector3.one; // 크기는 그대로

        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / animDuration;
            float t = Mathf.SmoothStep(0, 1, timer); // 부드럽게

            rt.anchoredPosition = Vector3.Lerp(startPos, originalPos, t);
            _canvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }

        rt.anchoredPosition = originalPos;
        _canvasGroup.alpha = 1f;
        if(_canvasGroup) { _canvasGroup.interactable = true; _canvasGroup.blocksRaycasts = true; }
    }

    protected override IEnumerator AnimateClose()
    {
        if(_canvasGroup) { _canvasGroup.interactable = false; _canvasGroup.blocksRaycasts = false; }

        float timer = 0f;
        RectTransform rt = GetComponent<RectTransform>();
        Vector3 endPos = originalPos - new Vector3(0, slideDistance, 0);

        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / animDuration;
            float t = Mathf.SmoothStep(0, 1, timer);

            rt.anchoredPosition = Vector3.Lerp(originalPos, endPos, t);
            _canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            yield return null;
        }

        gameObject.SetActive(false);
    }

    // --- [비즈니스 로직] ---
    void OnToggleClick()
    {
        isBusinessOpen = !isBusinessOpen;
        UpdateDashboard();
    }

    void UpdateDashboard()
    {
        if (moneyText) moneyText.text = $"{money:N0} G";
        
        if (isBusinessOpen)
        {
            if(statusText) { statusText.text = "영업 중 (OPEN)"; statusText.color = Color.green; }
            if(btnText) btnText.text = "마감하기";
        }
        else
        {
            if(statusText) { statusText.text = "준비 중 (CLOSED)"; statusText.color = Color.red; }
            if(btnText) btnText.text = "오픈하기";
        }
    }
}