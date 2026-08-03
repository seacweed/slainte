using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Business
{
    public sealed class BusinessOrderSessionUI : MonoBehaviour
    {
        private RectTransform uiRoot;
        private TMP_Text statusText;
        private GameObject decisionGroup;
        private GameObject craftingGroup;
        private GameObject abandonConfirmGroup;
        private BusinessOrderSessionController controller;

        public void Initialize(RectTransform canvasRoot, BusinessOrderSessionController sessionController)
        {
            controller = sessionController;
            if (uiRoot != null || canvasRoot == null)
                return;

            uiRoot = CreateRect("BusinessOrderSessionUI", canvasRoot);
            uiRoot.anchorMin = Vector2.zero;
            uiRoot.anchorMax = Vector2.one;
            uiRoot.offsetMin = Vector2.zero;
            uiRoot.offsetMax = Vector2.zero;
            uiRoot.SetAsLastSibling();

            statusText = CreateStatusText(uiRoot);
            decisionGroup = CreateButtonGroup(
                "OrderDecisionControls",
                uiRoot,
                new[]
                {
                    new ButtonDefinition("주문 수락", () => controller?.AcceptOrder()),
                    new ButtonDefinition("주문 거절", () => controller?.RejectOrder())
                });
            craftingGroup = CreateButtonGroup(
                "CraftingControls",
                uiRoot,
                new[]
                {
                    new ButtonDefinition("제출", () => controller?.SubmitOrder()),
                    new ButtonDefinition("버리기", () => controller?.DiscardCocktail()),
                    new ButtonDefinition("주문 포기", ShowAbandonConfirmation)
                });
            abandonConfirmGroup = CreateButtonGroup(
                "AbandonConfirmation",
                uiRoot,
                new[]
                {
                    new ButtonDefinition("포기 확정", () => controller?.ConfirmAbandonOrder()),
                    new ButtonDefinition("돌아가기", HideAbandonConfirmation)
                });

            ShowIdle();
        }

        public void ShowPresentingOrder(string customerOrderKey)
        {
            SetStatus("손님이 주문하는 중입니다.");
            SetGroups(false, false, false);
        }

        public void ShowDecision(bool allowReject = true)
        {
            SetStatus("주문을 수락하거나 거절하세요.");
            SetButtonVisible(decisionGroup, "주문 거절Button", allowReject);
            SetGroups(true, false, false);
        }

        public void ShowCrafting(string recipeName, bool allowAbandon = true)
        {
            SetStatus("제조 중: " + recipeName);
            SetButtonVisible(craftingGroup, "주문 포기Button", allowAbandon);
            SetGroups(false, true, false);
        }

        public void ShowEvaluating()
        {
            ShowIdle();
        }

        public void ShowFeedback(OrderEvaluationGrade grade, int moneyDelta, int reputationDelta)
        {
            SetStatus($"결과: {GetGradeLabel(grade)} | 돈 {FormatDelta(moneyDelta)} | 명성 {FormatDelta(reputationDelta)}");
            SetGroups(false, false, false);
        }

        public void ShowSequenceProgress(int currentIndex, int totalCount)
        {
            SetStatus($"영업 진행 {currentIndex}/{totalCount}");
            SetGroups(false, false, false);
        }

        public void ShowDayComplete()
        {
            SetStatus("오늘 영업이 끝났습니다. 진행 상황을 저장했습니다.");
            SetGroups(false, false, false);
        }

        public void ShowError(string message)
        {
            SetStatus(message);
            SetGroups(false, false, false);
        }

        public void ShowIdle()
        {
            SetStatus(string.Empty);
            SetGroups(false, false, false);
        }

        private void ShowAbandonConfirmation()
        {
            SetStatus("이 주문을 포기할까요? 사용한 재료는 복구되지 않습니다.");
            SetGroups(false, false, true);
        }

        private void HideAbandonConfirmation()
        {
            if (controller != null)
                ShowCrafting(controller.CurrentRecipeName, controller.CanAbandonCurrentOrder);
            else
                SetGroups(false, true, false);
        }

        private static void SetButtonVisible(GameObject group, string buttonName, bool visible)
        {
            if (group == null)
                return;

            Transform button = group.transform.Find(buttonName);
            if (button != null)
                button.gameObject.SetActive(visible);
        }

        private void SetStatus(string message)
        {
            if (uiRoot != null)
                uiRoot.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
            if (statusText != null)
                statusText.text = message ?? string.Empty;
        }

        private void SetGroups(bool decision, bool crafting, bool confirmation)
        {
            decisionGroup?.SetActive(decision);
            craftingGroup?.SetActive(crafting);
            abandonConfirmGroup?.SetActive(confirmation);
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

        private static TMP_Text CreateStatusText(RectTransform parent)
        {
            RectTransform panel = CreateRect("BusinessStatus", parent);
            panel.anchorMin = new Vector2(0.5f, 1f);
            panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -32f);
            panel.sizeDelta = new Vector2(920f, 64f);

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.05f, 0.04f, 0.035f, 0.86f);
            background.raycastTarget = false;

            RectTransform textRect = CreateRect("StatusText", panel);
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

        private static GameObject CreateButtonGroup(
            string name,
            RectTransform parent,
            ButtonDefinition[] definitions)
        {
            RectTransform group = CreateRect(name, parent);
            group.anchorMin = new Vector2(0.5f, 0f);
            group.anchorMax = new Vector2(0.5f, 0f);
            group.pivot = new Vector2(0.5f, 0f);
            group.anchoredPosition = new Vector2(0f, 150f);
            group.sizeDelta = new Vector2(Mathf.Max(1, definitions.Length) * 250f, 88f);

            HorizontalLayoutGroup layout = group.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            Image background = group.gameObject.AddComponent<Image>();
            background.color = new Color(0.06f, 0.045f, 0.035f, 0.9f);

            for (int i = 0; i < definitions.Length; i++)
                CreateButton(group, definitions[i]);

            return group.gameObject;
        }

        private static void CreateButton(RectTransform parent, ButtonDefinition definition)
        {
            RectTransform buttonRect = CreateRect(definition.Label + "Button", parent);
            Image image = buttonRect.gameObject.AddComponent<Image>();
            image.color = new Color(0.48f, 0.27f, 0.12f, 1f);

            Button button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(definition.Callback);

            RectTransform labelRect = CreateRect("Label", buttonRect);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 4f);
            labelRect.offsetMax = new Vector2(-8f, -4f);

            TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 24f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = definition.Label;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private readonly struct ButtonDefinition
        {
            public ButtonDefinition(string label, UnityEngine.Events.UnityAction callback)
            {
                Label = label;
                Callback = callback;
            }

            public string Label { get; }
            public UnityEngine.Events.UnityAction Callback { get; }
        }
    }
}
