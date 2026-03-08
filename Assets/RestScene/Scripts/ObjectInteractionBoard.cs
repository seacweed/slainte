using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(LineRenderer))]
public class ObjectInteractionBoard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Visual Effects")]
    public GameObject highlightOverlay;
    public Color outlineColor = Color.yellow;
    public float outlineWidth = 0.05f;

    [Header("UI Interaction")]
    // [핵심] 상점(Shop)이나 상황판(Situation) 모두 연결 가능
    public BaseUIManager targetUIManager; 

    // 내부 변수
    private LineRenderer line;
    private bool isHovered = false;

    private void Awake()
    {
        if (highlightOverlay != null) highlightOverlay.SetActive(false);

        line = GetComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.startWidth = outlineWidth;
        line.endWidth = outlineWidth;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = outlineColor;
        line.endColor = outlineColor;
        line.loop = true;
        line.enabled = false;

        DrawOutlineShape();
    }

    private void Update()
    {
        UpdateVisuals();
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
        
        // UI가 열려있을 때만 테두리 켜기
        if (isUIOpen) line.enabled = true;
        else line.enabled = false;
    }

    private void DrawOutlineShape()
    {
        PolygonCollider2D poly = GetComponent<PolygonCollider2D>();
        if (poly != null)
        {
            line.positionCount = poly.GetTotalPointCount();
            for (int i = 0; i < poly.points.Length; i++) line.SetPosition(i, poly.points[i]);
            return;
        }

        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            line.positionCount = 4;
            Vector2 s = box.size; Vector2 o = box.offset;
            line.SetPosition(0, o + new Vector2(-s.x, -s.y) * 0.5f);
            line.SetPosition(1, o + new Vector2(-s.x, s.y) * 0.5f);
            line.SetPosition(2, o + new Vector2(s.x, s.y) * 0.5f);
            line.SetPosition(3, o + new Vector2(s.x, -s.y) * 0.5f);
        }
    }
}