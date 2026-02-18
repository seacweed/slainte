using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [TextArea] public string description;
    private bool isPinned = false;

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 매니저 내부에서 PinnedOwner 여부를 체크하므로 그대로 호출하면 됩니다.
        TooltipManager.Instance?.ShowTooltip(description, transform as RectTransform, this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 고정 상태가 아닐 때만 숨기기 시도
        if (!isPinned)
        {
            TooltipManager.Instance?.HideTooltip(this);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        // 다른 놈이 이미 고정하고 있다면 클릭 무시 (선점 시스템)
        if (TooltipManager.Instance.PinnedOwner != null && TooltipManager.Instance.PinnedOwner != this) return;

        isPinned = !isPinned;

        // 매니저에게 고정 상태 알림
        TooltipManager.Instance.SetPin(this, isPinned);

        if (isPinned)
            TooltipManager.Instance.ShowTooltip(description, transform as RectTransform, this);
        else
            TooltipManager.Instance.HideTooltip(this);
    }
}