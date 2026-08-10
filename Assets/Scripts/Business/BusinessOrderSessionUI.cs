using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Business
{
    public sealed class BusinessOrderSessionUI : MonoBehaviour
    {
        private RectTransform uiRoot;
        private RectTransform statusPanel;
        private TMP_Text statusText;

        public void Initialize(RectTransform canvasRoot, BusinessOrderSessionController sessionController)
        {
            if (uiRoot != null || canvasRoot == null)
                return;

            uiRoot = CreateRect("BusinessOrderSessionUI", canvasRoot);
            uiRoot.anchorMin = Vector2.zero;
            uiRoot.anchorMax = Vector2.one;
            uiRoot.offsetMin = Vector2.zero;
            uiRoot.offsetMax = Vector2.zero;
            uiRoot.SetAsLastSibling();

            statusText = CreateStatusText(uiRoot);

            ShowIdle();
        }

        public void ShowPresentingOrder(string customerOrderKey)
        {
            SetStatus("손님이 주문하는 중입니다.");
        }

        public void ShowCrafting(string recipeName)
        {
            SetStatus(string.Empty);
        }

        public void ShowEvaluating()
        {
            SetStatus(string.Empty);
        }

        public void ShowFeedback(OrderEvaluationGrade grade, int moneyDelta, int reputationDelta)
        {
            SetStatus($"결과: {GetGradeLabel(grade)} | 돈 {FormatDelta(moneyDelta)} | 명성 {FormatDelta(reputationDelta)}");
        }

        public void ShowSequenceProgress(int currentIndex, int totalCount)
        {
            SetStatus($"영업 진행 {currentIndex}/{totalCount}");
        }

        public void ShowDayComplete()
        {
            SetStatus("오늘 영업이 끝났습니다. 진행 상황을 저장했습니다.");
        }

        public void ShowError(string message)
        {
            SetStatus(message);
        }

        public void ShowIdle()
        {
            SetStatus(string.Empty);
        }

        private void SetStatus(string message)
        {
            if (statusPanel != null)
                statusPanel.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
            if (statusText != null)
                statusText.text = message ?? string.Empty;
        }

        private static string FormatDelta(int value)
        {
            return value > 0 ? "+" + value : value.ToString();
        }

        private static string GetGradeLabel(OrderEvaluationGrade grade)
        {
            return grade switch
            {
                OrderEvaluationGrade.Good => "좋음",
                OrderEvaluationGrade.Mid => "보통",
                _ => "나쁨"
            };
        }

        private TMP_Text CreateStatusText(RectTransform parent)
        {
            statusPanel = CreateRect("BusinessStatus", parent);
            statusPanel.anchorMin = new Vector2(0.5f, 1f);
            statusPanel.anchorMax = new Vector2(0.5f, 1f);
            statusPanel.pivot = new Vector2(0.5f, 1f);
            statusPanel.anchoredPosition = new Vector2(0f, -32f);
            statusPanel.sizeDelta = new Vector2(920f, 64f);

            Image background = statusPanel.gameObject.AddComponent<Image>();
            background.color = new Color(0.05f, 0.04f, 0.035f, 0.86f);
            background.raycastTarget = false;

            RectTransform textRect = CreateRect("StatusText", statusPanel);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 8f);
            textRect.offsetMax = new Vector2(-18f, -8f);

            TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = 27f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.9f, 0.72f, 1f);
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
