using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 셔터가 닫히는 연출 후 모니터가 내려오고, 챕터명/Day → 음료 판매 줄(스크롤) → 총 소득/구분선/보유 자산 순으로
// 한 스텝씩 표시되는 정산 화면 UI. 애니메이션 도중 클릭/스페이스 입력 시 남은 스텝을 즉시 전부 표시(스킵)하고,
// 다 표시된 후 한 번 더 입력하면 다음 상태(Rest)로 전환을 시작한다. 이때 화면 자체는 바로 사라지지 않고
// SceneTransitionManager의 페이드아웃이 완전히 끝날 때까지 유지되며, HideAndReset()이 호출된 시점(화면이
// 완전히 검게 된 후)에야 셔터/모니터가 원위치로 리셋되고 감춰진다.
public class SettlementUI : MonoBehaviour
{
    [Header("Shutter")]
    [SerializeField] private RectTransform shutter;
    [SerializeField] private Vector2 shutterClosedPosition = Vector2.zero;
    [SerializeField] private float shutterCloseDuration = 0.6f;

    [Header("Monitor")]
    [SerializeField] private RectTransform monitorRect;
    [SerializeField] private Vector2 monitorShownPosition = Vector2.zero;
    [SerializeField] private float monitorSlideDuration = 0.4f;
    [SerializeField] private CanvasGroup monitorGroup;

    [Header("Report Header")]
    [SerializeField] private TextMeshProUGUI chapterNameText;
    [SerializeField] private TextMeshProUGUI dayText;

    [Header("Report Body")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform scrollContent;
    [SerializeField] private SettlementLineView lineViewPrefab;

    [Header("Report Footer")]
    [SerializeField] private SettlementLineView totalIncomeLine;
    [SerializeField] private TextMeshProUGUI dividerText;
    [SerializeField] private SettlementLineView currentMoneyLine;

    [Header("Timing")]
    [SerializeField] private float lineRevealInterval = 0.15f;

    [Header("Prompt")]
    [SerializeField] private TextMeshProUGUI pressAnyKeyLabel;

    private const string DividerLine = "------------------------------------------------------------------";

    private Action _onClosed;
    private Vector2 _shutterOpenPosition;
    private Vector2 _monitorHiddenPosition;
    private bool _openPositionCaptured;
    private bool _waitingForInput;

    public void Show(SettlementData data, Action onClosed)
    {
        CaptureOpenPositions();

        _onClosed = onClosed;
        _waitingForInput = false;
        gameObject.SetActive(true);

        ResetHeaderFooter();
        ClearScrollContent();
        if (pressAnyKeyLabel != null) pressAnyKeyLabel.gameObject.SetActive(false);

        if (monitorGroup != null)
        {
            monitorGroup.interactable = false;
            monitorGroup.blocksRaycasts = false;
        }

        StopAllCoroutines();
        StartCoroutine(PlaySequence(data));
    }

    private void Update()
    {
        if (_waitingForInput && AdvanceInputPressed())
        {
            _waitingForInput = false;
            Close();
        }
    }

    private void CaptureOpenPositions()
    {
        if (_openPositionCaptured) return;

        if (shutter != null) _shutterOpenPosition = shutter.anchoredPosition;
        if (monitorRect != null) _monitorHiddenPosition = monitorRect.anchoredPosition;
        _openPositionCaptured = true;
    }

    private void ResetHeaderFooter()
    {
        if (chapterNameText) chapterNameText.gameObject.SetActive(false);
        if (dayText) dayText.gameObject.SetActive(false);
        if (totalIncomeLine) totalIncomeLine.gameObject.SetActive(false);
        if (dividerText) dividerText.gameObject.SetActive(false);
        if (currentMoneyLine) currentMoneyLine.gameObject.SetActive(false);
    }

    private void ClearScrollContent()
    {
        if (scrollContent == null) return;
        for (int i = scrollContent.childCount - 1; i >= 0; i--)
            Destroy(scrollContent.GetChild(i).gameObject);
    }

    private IEnumerator PlaySequence(SettlementData data)
    {
        yield return SlideTo(shutter, _shutterOpenPosition, shutterClosedPosition, shutterCloseDuration);
        yield return SlideTo(monitorRect, _monitorHiddenPosition, monitorShownPosition, monitorSlideDuration);

        if (monitorGroup != null)
        {
            monitorGroup.interactable = true;
            monitorGroup.blocksRaycasts = true;
        }

        yield return RevealSteps(BuildRevealSteps(data));

        // 스킵으로 마지막 스텝이 입력과 같은 프레임에 끝났을 수 있으니,
        // 그 입력이 곧바로 종료 트리거로 이어지지 않도록 한 프레임 흘려보낸다.
        yield return null;

        if (pressAnyKeyLabel != null) pressAnyKeyLabel.gameObject.SetActive(true);
        _waitingForInput = true;
    }

    // 값이 0인 항목(판매/팁/실수/배송 등)은 아예 스텝 목록에서 빼서, 정산 결과에 없는
    // 줄을 빈 값으로 보여주지 않는다. 헤더 2줄과 소득 합계/구분선/보유 자산 3줄은 항상 포함.
    private List<Action> BuildRevealSteps(SettlementData data)
    {
        List<Action> steps = new()
        {
            () => SetLine(chapterNameText, data.chapterName),
            () => SetLine(dayText, $"Day {data.day} 결과 보고")
        };

        if (data.totalSalesCount != 0)
            steps.Add(() => InstantiateLine(
                $"총 판매량 x {data.totalSalesCount}",
                FormatSigned(data.totalSalesRevenue)));

        if (data.goodCount != 0)
            steps.Add(() => InstantiateLine(
                $"팁 x {data.goodCount}",
                FormatSigned(data.tipTotal)));

        if (data.badCount != 0)
            steps.Add(() => InstantiateLine(
                $"실수 x {data.badCount}",
                FormatSigned(-data.missedRevenue)));

        if (data.deliveryCount != 0)
            steps.Add(() => InstantiateLine(
                $"배송 이용 x {data.deliveryCount}",
                FormatSigned(-data.deliverySpend)));

        if (data.customRewards != null)
        {
            foreach (SettlementRewardEntry reward in data.customRewards)
            {
                SettlementRewardEntry r = reward;
                steps.Add(() => InstantiateLine(r.label, FormatSigned(r.amount)));
            }
        }

        steps.Add(() => SetLineView(totalIncomeLine, "총 소득", $"{data.totalIncome:N0}"));
        steps.Add(() => SetLine(dividerText, DividerLine));
        steps.Add(() => SetLineView(currentMoneyLine, "보유 자산", $"{data.currentMoney:N0}"));

        return steps;
    }

    private static string FormatSigned(int value)
    {
        return value > 0 ? $"+{value:N0}" : value.ToString("N0");
    }

    // 스텝을 lineRevealInterval 간격으로 하나씩 실행하되, 대기 중 클릭/스페이스가 들어오면
    // 남은 스텝을 전부 즉시 실행해 스킵한다.
    private IEnumerator RevealSteps(List<Action> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            steps[i]();

            float timer = 0f;
            while (timer < lineRevealInterval)
            {
                if (AdvanceInputPressed())
                {
                    for (int j = i + 1; j < steps.Count; j++)
                        steps[j]();
                    yield break;
                }

                timer += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }

    private void SetLine(TextMeshProUGUI text, string content)
    {
        if (text == null) return;
        text.text = content;
        text.gameObject.SetActive(true);
    }

    private void SetLineView(SettlementLineView view, string label, string value)
    {
        if (view == null) return;
        view.SetLine(label, value);
        view.gameObject.SetActive(true);
    }

    private void InstantiateLine(string label, string value)
    {
        if (lineViewPrefab == null || scrollContent == null) return;
        SettlementLineView line = Instantiate(lineViewPrefab, scrollContent);
        line.SetLine(label, value);
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        if (scrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private static bool AdvanceInputPressed()
    {
        return Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space);
    }

    private IEnumerator SlideTo(RectTransform rect, Vector2 from, Vector2 to, float duration)
    {
        if (rect == null) yield break;

        float timer = 0f;
        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / duration;
            rect.anchoredPosition = Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, timer));
            yield return null;
        }
        rect.anchoredPosition = to;
    }

    private void Close()
    {
        Action callback = _onClosed;
        _onClosed = null;
        callback?.Invoke();
    }

    // 화면이 완전히 검게 된(fadeout 완료) 후 외부(SettlementManager)에서 호출.
    // 이 시점 이후엔 화면이 안 보이므로 셔터/모니터를 원위치로 되돌려도 티가 나지 않는다.
    public void HideAndReset()
    {
        StopAllCoroutines();
        _waitingForInput = false;

        if (shutter != null) shutter.anchoredPosition = _shutterOpenPosition;
        if (monitorRect != null) monitorRect.anchoredPosition = _monitorHiddenPosition;

        if (monitorGroup != null)
        {
            monitorGroup.interactable = false;
            monitorGroup.blocksRaycasts = false;
        }

        gameObject.SetActive(false);
    }
}
