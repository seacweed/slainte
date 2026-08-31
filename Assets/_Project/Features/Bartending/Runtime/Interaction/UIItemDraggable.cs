using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum DragSpawnMode { Move, Copy }

public class UIItemDraggable : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] Image iconImage;
    [SerializeField] Sprite fallbackSprite;

    [Header("Drag")]
    public DragSpawnMode spawnMode = DragSpawnMode.Copy; // Shelf는 Bottle에 대해 Move로 세팅

    public ItemInstance Instance { get; private set; }
    public ItemDef Def => Instance != null ? Instance.def : null;

    // Move 모드일 때 원래 자리 복구용
    Transform originalParent;
    int originalSibling;
    bool hiddenWhileDragging;

    public void Bind(ItemDef def)
    {
        if (Instance == null) Instance = GetComponent<ItemInstance>();
        Instance.def = def;

        if (iconImage != null)
        {
            iconImage.sprite = (def != null && def.icon != null) ? def.icon : fallbackSprite;
            iconImage.color = Color.white;
            iconImage.enabled = true;
        }

        hiddenWhileDragging = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (Def == null) return;

        // Move 아이템은 "원본이 이동"처럼 보여야 하니까
        // 드래그 중엔 원본 아이콘을 숨기고 ghost가 대신 보이게 한다.
        if (spawnMode == DragSpawnMode.Move && iconImage != null)
        {
            originalParent = transform.parent;
            originalSibling = transform.GetSiblingIndex();

            iconImage.enabled = false;
            hiddenWhileDragging = true;
        }

        DragManager.Instance.BeginDrag(this, fallbackSprite, startPos: eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        // ghost는 DragManager.Update에서 따라감
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // 드롭 성공/실패 여부는 드롭 타겟이 처리
        // 여기서는 '아무데도 안 놓였으면' 취소 처리만 해주면 됨
        // (드롭 타겟이 EndDragTake()하면 이미 손에서 빠짐)
        if (DragManager.Instance.IsHolding)
        {
            DragManager.Instance.CancelDrag();
        }
    }

    public void OnDragCanceled()
    {
        // Move 모드면 숨긴 원본 복구
        RestoreVisual();
    }

    public void RestoreVisual()
    {
        if (hiddenWhileDragging && iconImage != null)
        {
            iconImage.enabled = true;
            hiddenWhileDragging = false;
        }
    }

    public void RestoreToOriginalParent()
    {
        if (originalParent != null)
        {
            transform.SetParent(originalParent, worldPositionStays: false);
            transform.SetSiblingIndex(originalSibling);
        }
        RestoreVisual();
    }
}
