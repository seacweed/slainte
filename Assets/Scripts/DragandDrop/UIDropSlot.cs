using UnityEngine;
using UnityEngine.EventSystems;

public class UIDropSlot : MonoBehaviour, IDropHandler
{
    [SerializeField] Transform anchor;              // 실제 아이템 UI가 붙을 자리(없으면 this.transform 사용)
    [SerializeField] UIItemDraggable itemPrefab;    // Copy일 때 생성할 프리팹

    public UIItemDraggable PlacedUI { get; private set; }

    public void OnDrop(PointerEventData eventData)
    {
        if (!DragManager.Instance.IsHolding) return;
        if (PlacedUI != null) return;

        var heldUI = DragManager.Instance.EndDragTakeUI();
        var def = heldUI.Def;
        if (def == null) { heldUI.RestoreToOriginalParent(); return; }

        // 테이블 슬롯 규칙:
        // - Bottle/Glass는 전부 OK
        // - Tool은 기본 금지, 단 toolPlaceableOnTable=true만 OK
        if (def.type == ItemType.Tool && !def.toolPlaceableOnTable)
        {
            heldUI.RestoreToOriginalParent();
            return;
        }

        var parent = anchor != null ? anchor : transform;

        if (heldUI.spawnMode == DragSpawnMode.Move)
        {
            // ✅ 실제 오브젝트 이동 (술병)
            heldUI.transform.SetParent(parent, worldPositionStays: false);
            heldUI.transform.localPosition = Vector3.zero;
            heldUI.RestoreVisual();

            PlacedUI = heldUI;
        }
        else
        {
            // ✅ Copy: 원본은 그대로(서랍에 남음), 테이블에 복사본 생성
            heldUI.RestoreVisual(); // (혹시라도) 원본 숨김 복구

            var copy = Instantiate(itemPrefab, parent);
            copy.Bind(def);

            // 테이블에 생성된 복사본은 "테이블 위에서 옮겨 다니는 것"이 자연스러우니 Move로
            copy.spawnMode = DragSpawnMode.Move;

            copy.transform.localPosition = Vector3.zero;
            PlacedUI = copy;
        }
    }

    public void ClearSlot()
    {
        // 슬롯에서 제거 정책은 나중에 정의:
        // - PlacedUI를 다른 데로 옮기면 자동 null 처리 등
        PlacedUI = null;
    }
}