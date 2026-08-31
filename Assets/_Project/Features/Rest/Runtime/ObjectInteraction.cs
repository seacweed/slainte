using System.Collections.Generic;
using Slainte.Business;
using Slainte.TV;
using UnityEngine;
using UnityEngine.EventSystems;

public class ObjectInteraction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private static readonly HashSet<ObjectInteraction> ActiveInteractions = new();

    [Header("Visual Effects")]
    public GameObject highlightOverlay;

    [Header("UI Interaction")]
    public BaseUIManager targetUIManager;

    [Header("Background Overlays")]
    [Tooltip("평상시 컬러 이미지입니다.")]
    public GameObject idleOverlay;
    [Tooltip("마우스 오버 또는 선택 상태의 노란 테두리 이미지입니다.")]
    public GameObject hoverOverlay;
    [Tooltip("다른 UI가 열렸거나 상호작용이 제한된 상태의 회색 이미지입니다.")]
    public GameObject grayOverlay;

    [Header("Availability")]
    [Tooltip("활성 TV 방송이 일반 상점 이용을 제한할 때 클릭을 막습니다.")]
    public bool disableWhenRestShopRestricted;
    public TVBroadcastDatabase tvDatabase;

    private bool isHovered;

    public bool IsUIOpen => targetUIManager != null && targetUIManager.gameObject.activeSelf;

    public static bool AnyUIOpen
    {
        get
        {
            foreach (ObjectInteraction interaction in ActiveInteractions)
            {
                if (interaction != null && interaction.IsUIOpen)
                    return true;
            }

            return false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveInteractions.Clear();
    }

    private void Awake()
    {
        if (highlightOverlay != null) highlightOverlay.SetActive(false);
    }

    private void OnEnable()
    {
        ActiveInteractions.Add(this);
    }

    private void OnDisable()
    {
        ActiveInteractions.Remove(this);
        isHovered = false;
    }

    private void Start()
    {
        if (idleOverlay != null) idleOverlay.SetActive(true);
        if (hoverOverlay != null) hoverOverlay.SetActive(false);
        if (grayOverlay != null) grayOverlay.SetActive(false);
    }

    private void Update()
    {
        UpdateVisuals();
        UpdateBackgrounds();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || targetUIManager == null)
            return;

        if (IsUIOpen)
        {
            targetUIManager.CloseUI();
            return;
        }

        // 다른 오브젝트의 패널이 열린 동안 이 오브젝트는 회색 비활성 상태다.
        if (AnyUIOpen)
            return;

        if (!IsInteractionAvailable(out string reason))
        {
            if (!string.IsNullOrWhiteSpace(reason))
                Debug.Log($"[ObjectInteraction] {reason}", this);
            return;
        }

        targetUIManager.OpenUI();
    }

    public bool IsInteractionAvailable(out string reason)
    {
        reason = string.Empty;
        if (targetUIManager is TVUIManager)
        {
            BusinessOrderFlowSettings settings = BusinessOrderFlowSettings.LoadDefault();
            if (settings == null || !settings.IsTVUnlocked(GameProgress.Instance))
            {
                reason = "야간근무 에피소드 완료 후 TV를 이용할 수 있습니다.";
                return false;
            }
        }

        if (!disableWhenRestShopRestricted)
            return true;

        TVBroadcastDatabase database = tvDatabase != null
            ? tvDatabase
            : TVBroadcastDatabase.LoadDefault();
        return !TVBroadcastRuntime.IsRestShopDisabled(
            GameProgress.Instance,
            database,
            out reason);
    }

    private void UpdateVisuals()
    {
        bool useLegacyHighlight = highlightOverlay != null && hoverOverlay == null;
        if (highlightOverlay != null)
            highlightOverlay.SetActive(useLegacyHighlight && (isHovered || IsUIOpen));
    }

    private void UpdateBackgrounds()
    {
        bool available = IsInteractionAvailable(out _);
        bool anyUIOpen = AnyUIOpen;
        bool showOutline = available && (IsUIOpen || (!anyUIOpen && isHovered));
        bool showGray = !available || (anyUIOpen && !IsUIOpen);
        bool showNormal = available && !showOutline && !showGray;

        if (idleOverlay != null) idleOverlay.SetActive(showNormal);
        if (hoverOverlay != null) hoverOverlay.SetActive(showOutline);
        if (grayOverlay != null) grayOverlay.SetActive(showGray);
    }
}
