using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Slainte.Shared.Display;

public class TooltipManager : MonoBehaviour
{
    public static TooltipManager Instance;
    public GameObject tooltipPrefab;
    
    private GameObject _tooltipInstance;
    private RectTransform _rect;
    private TextMeshProUGUI _descText;
    
    private TooltipTrigger _currentOwner;
    
    // [추가] 현재 고정 권한을 가진 트리거를 저장합니다.
    public TooltipTrigger PinnedOwner { get; private set; }

    private void Awake() => Instance = this;

    public void ShowTooltip(string text, RectTransform targetRect, TooltipTrigger owner)
    {
        // [핵심 로직] 이미 고정된 주인이 있는데, 호출한 놈이 주인이 아니라면 무시합니다.
        if (PinnedOwner != null && PinnedOwner != owner) return;

        if (_tooltipInstance == null)
        {
            _tooltipInstance = Instantiate(tooltipPrefab, transform);
            _rect = _tooltipInstance.GetComponent<RectTransform>();
            _descText = _tooltipInstance.GetComponentInChildren<TextMeshProUGUI>();
        }

        _currentOwner = owner;
        _tooltipInstance.SetActive(true);
        _descText.text = text;

        LayoutRebuilder.ForceRebuildLayoutImmediate(_rect);
        UpdatePosition(targetRect);
    }

    public void HideTooltip(TooltipTrigger owner)
    {
        // 고정된 상태라면 숨기기 요청을 무시합니다. (본인이 직접 끌 때 제외)
        if (PinnedOwner != null && PinnedOwner != owner) return;

        if (_tooltipInstance != null && _currentOwner == owner)
        {
            _tooltipInstance.SetActive(false);
            _currentOwner = null;
        }
    }

    // [추가] 고정 권한을 설정하는 함수
    public void SetPin(TooltipTrigger owner, bool isPinned)
    {
        if (isPinned) PinnedOwner = owner;
        else if (PinnedOwner == owner) PinnedOwner = null;
    }

    private void UpdatePosition(RectTransform targetRect)
    {
        GameAspect.PlaceBesideTarget(_rect, targetRect);
    }
}