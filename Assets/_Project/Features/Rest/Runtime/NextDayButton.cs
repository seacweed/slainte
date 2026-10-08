using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 휴식 화면에서 다음 날 영업으로 넘어가는 임시 버튼(작전판 제거 후 대체). 누르면 확인 없이 바로
// DayFlowController.StartBusinessDay()로 날짜를 넘기고 영업 씬으로 간다.
// 상점·TV 같은 팝업이 열려 있는 동안에는 숨긴다 — 팝업 위로 눌려 의도치 않게 하루가 넘어가지 않게 하기 위함.
[RequireComponent(typeof(Button))]
public class NextDayButton : MonoBehaviour
{
    [Tooltip("숨길 대상. 비우면 이 오브젝트의 CanvasGroup을 쓴다(없으면 자동 추가). 버튼 자신을 SetActive로 끄면 Update가 멈춰 다시 켤 수 없기 때문.")]
    [SerializeField] private CanvasGroup visibilityGroup;
    [Tooltip("선택 사항. 연결하면 labelFormat으로 글자를 갱신한다.")]
    [SerializeField] private TextMeshProUGUI label;
    [Tooltip("{0} = 넘어갈 날짜. 인스펙터에서 원하는 문구로 바꿔 쓴다.")]
    [SerializeField] private string labelFormat = "Next Day (Day {0})";

    private Button _button;
    private bool _pressed;
    private bool? _lastVisible;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OnClicked);
        // UnityEngine.Object는 ?? 연산자가 "파괴된 null"을 구분하지 못하므로 명시적으로 비교한다.
        if (visibilityGroup == null)
            visibilityGroup = GetComponent<CanvasGroup>();
        if (visibilityGroup == null)
            visibilityGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        RefreshLabel();
        _lastVisible = null;
        UpdateVisibility();
    }

    private void Update()
    {
        UpdateVisibility();
    }

    // 상태가 바뀐 프레임에만 CanvasGroup을 건드린다(매 프레임 대입하면 레이아웃·렌더 갱신이 반복됨).
    private void UpdateVisibility()
    {
        bool visible = !_pressed && !ObjectInteraction.AnyUIOpen;
        if (_lastVisible == visible) return;
        _lastVisible = visible;

        visibilityGroup.alpha = visible ? 1f : 0f;
        visibilityGroup.interactable = visible;
        visibilityGroup.blocksRaycasts = visible;
    }

    private void RefreshLabel()
    {
        if (label == null) return;
        int nextDay = (GameProgress.Instance != null ? GameProgress.Instance.CurrentDay : 0) + 1;
        label.text = string.Format(labelFormat, nextDay);
    }

    private void OnClicked()
    {
        // 화면 전환 중 연타로 하루가 두 번 넘어가지 않게 첫 클릭 뒤 잠근다(씬이 바뀌면 이 오브젝트도 사라짐).
        if (_pressed || DayFlowController.Instance == null) return;
        _pressed = true;
        _button.interactable = false;
        DayFlowController.Instance.StartBusinessDay();
    }
}
