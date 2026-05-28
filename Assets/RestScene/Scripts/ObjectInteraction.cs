using UnityEngine;
using UnityEngine.EventSystems;

public class ObjectInteraction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Visual Effects")]
    public GameObject highlightOverlay;

    [Header("UI Interaction")]
    // [핵심] 상점(Shop)이나 상황판(Situation) 모두 연결 가능
    public BaseUIManager targetUIManager; 

    [Header("Background Overlays")]
    public GameObject idleOverlay;
    public GameObject hoverOverlay;

    // 내부 변수
    private bool isHovered = false;

    private void Awake()
    {
        if (highlightOverlay != null) highlightOverlay.SetActive(false);
    }

    private void Start()
    {
        // 씬 시작 시 오버레이 초기화
        if (idleOverlay != null) idleOverlay.SetActive(false);
        if (hoverOverlay != null) hoverOverlay.SetActive(false);
    }

    private void Update()
    {
        UpdateVisuals();
        UpdateBackgrounds();
    }

    public void OnPointerEnter(PointerEventData eventData) { isHovered = true; }
    public void OnPointerExit(PointerEventData eventData) { isHovered = false; }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (targetUIManager != null)
        {
            // 켜져있으면 닫고, 꺼져있으면 염
            if (targetUIManager.gameObject.activeSelf)
                targetUIManager.CloseUI();
            else
                targetUIManager.OpenUI();
        }
    }

    private void UpdateVisuals()
    {
        bool isUIOpen = targetUIManager != null && targetUIManager.gameObject.activeSelf;

        if (highlightOverlay != null) highlightOverlay.SetActive(isHovered || isUIOpen);
    }

    private void UpdateBackgrounds()
    {
        bool isUIOpen = targetUIManager != null && targetUIManager.gameObject.activeSelf;

        if (isUIOpen)
        {
            if (hoverOverlay != null) hoverOverlay.SetActive(true);
            if (idleOverlay != null) idleOverlay.SetActive(false);
        }
        else if (isHovered)
        {
            if (idleOverlay != null) idleOverlay.SetActive(true);
            if (hoverOverlay != null) hoverOverlay.SetActive(false);
        }
        else
        {
            if (idleOverlay != null) idleOverlay.SetActive(false);
            if (hoverOverlay != null) hoverOverlay.SetActive(false);
        }
    }
}