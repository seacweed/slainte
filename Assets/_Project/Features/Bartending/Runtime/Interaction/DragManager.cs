using UnityEngine;
using UnityEngine.UI;

public class DragManager : MonoBehaviour
{
    public static DragManager Instance { get; private set; }

    [SerializeField] Image ghostImage;

    public UIItemDraggable HeldUI { get; private set; }
    public ItemDef HeldDef => HeldUI != null ? HeldUI.Def : null;

    void Awake()
    {
        Instance = this;
        Clear();
    }

    void Update()
    {
        if (ghostImage != null && ghostImage.enabled)
            ghostImage.transform.position = Input.mousePosition;
    }

    public bool IsHolding => HeldUI != null;

    public void BeginDrag(UIItemDraggable ui, Sprite fallbackSprite = null, Vector2? startPos = null)
    {
        HeldUI = ui;

        if (ghostImage != null)
        {
            ghostImage.enabled = true;
            var def = ui.Def;
            ghostImage.sprite = (def != null && def.icon != null) ? def.icon : fallbackSprite;
            ghostImage.color = Color.white;

            if (startPos.HasValue)
                ghostImage.transform.position = startPos.Value;
        }
    }

    public UIItemDraggable EndDragTakeUI()
    {
        var ui = HeldUI;
        Clear();
        return ui;
    }

    public void CancelDrag()
    {
        // 취소 시, 원본 복구는 UIItemDraggable이 스스로 처리하게
        if (HeldUI != null)
            HeldUI.OnDragCanceled();

        Clear();
    }

    void Clear()
    {
        HeldUI = null;
        if (ghostImage != null)
        {
            ghostImage.enabled = false;
            ghostImage.sprite = null;
        }
    }
}