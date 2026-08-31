using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 실제 영업(손님 응대) 시스템 구현 전까지 사용하는 임시 화면.
// GameMode.OrderMode 진입 시 표시되고, 스킵 버튼으로 그날의 영업 단계를 종료 처리한다.
public class BusinessStubUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Button skipButton;
    [SerializeField] private TextMeshProUGUI label;

    private void Awake()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipClicked);

        if (label != null)
            label.text = "영업 진행 중 (임시 화면)";
    }

    private void Start()
    {
        SetVisible(GameModeManager.Instance != null && GameModeManager.Instance.CurrentMode == GameMode.OrderMode);
    }

    private void OnEnable()
    {
        if (GameModeManager.Instance != null)
            GameModeManager.Instance.OnModeChanged += HandleModeChanged;
    }

    private void OnDisable()
    {
        if (GameModeManager.Instance != null)
            GameModeManager.Instance.OnModeChanged -= HandleModeChanged;
    }

    private void HandleModeChanged(GameMode previous, GameMode next)
    {
        SetVisible(next == GameMode.OrderMode);
    }

    private void SetVisible(bool visible)
    {
        if (root != null) root.SetActive(visible);
    }

    private void OnSkipClicked()
    {
        DayFlowController.Instance?.OnBusinessCompleted();
    }
}
