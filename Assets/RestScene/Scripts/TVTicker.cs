using TMPro;
using UnityEngine;

namespace Slainte.TV
{
    public sealed class TVTicker : MonoBehaviour
    {
        public RectTransform viewport;
        public TextMeshProUGUI firstText;
        public TextMeshProUGUI secondText;
        [Min(1f)] public float pixelsPerSecond = 90f;
        [Min(0f)] public float gap = 80f;

        private float textWidth;
        private bool ready;

        public void SetMessage(string message)
        {
            string value = string.IsNullOrWhiteSpace(message) ? " " : message.Trim();
            if (firstText != null) firstText.text = value;
            if (secondText != null) secondText.text = value;
            RebuildLayout();
        }

        private void OnEnable()
        {
            RebuildLayout();
        }

        private void Update()
        {
            if (!ready || firstText == null || secondText == null)
                return;

            float delta = pixelsPerSecond * Time.unscaledDeltaTime;
            Move(firstText.rectTransform, -delta);
            Move(secondText.rectTransform, -delta);

            RectTransform first = firstText.rectTransform;
            RectTransform second = secondText.rectTransform;
            if (first.anchoredPosition.x + textWidth < 0f)
                SetX(first, second.anchoredPosition.x + textWidth + gap);
            if (second.anchoredPosition.x + textWidth < 0f)
                SetX(second, first.anchoredPosition.x + textWidth + gap);
        }

        private void RebuildLayout()
        {
            ready = false;
            if (viewport == null || firstText == null || secondText == null)
                return;

            Canvas.ForceUpdateCanvases();
            float viewportWidth = Mathf.Max(1f, viewport.rect.width);
            textWidth = Mathf.Max(viewportWidth, firstText.preferredWidth + 20f);
            float height = Mathf.Max(1f, viewport.rect.height);
            firstText.rectTransform.sizeDelta = new Vector2(textWidth, height);
            secondText.rectTransform.sizeDelta = new Vector2(textWidth, height);
            firstText.rectTransform.anchoredPosition = Vector2.zero;
            secondText.rectTransform.anchoredPosition = new Vector2(textWidth + gap, 0f);
            ready = true;
        }

        private static void Move(RectTransform rect, float x)
        {
            Vector2 position = rect.anchoredPosition;
            position.x += x;
            rect.anchoredPosition = position;
        }

        private static void SetX(RectTransform rect, float x)
        {
            Vector2 position = rect.anchoredPosition;
            position.x = x;
            rect.anchoredPosition = position;
        }
    }
}
