using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.TV
{
    public sealed class TVUIManager : BaseUIManager
    {
        [Header("Data")]
        public TVBroadcastDatabase database;

        [Header("Layout")]
        public RectTransform tvFrame;
        public Image screenBackgroundImage;
        public Image frameImage;
        public Image headlineImage;
        public Image cardBackgroundImage;
        public Image presenterImage;
        public Image eventImage;
        public TextMeshProUGUI presenterPlaceholderText;
        public TextMeshProUGUI eventPlaceholderText;
        public TextMeshProUGUI titleText;
        public TVTicker ticker;
        public Button closeButton;

        [Header("Temporary Artwork")]
        [Tooltip("최종 진행자 에셋이 들어오기 전까지 사용하는 임시 이미지입니다.")]
        public Sprite fallbackPresenterSprite;

        [Header("Animation")]
        [Min(0.01f)] public float animationDuration = 0.25f;

        private Vector3 shownScale = Vector3.one;

        protected override void Awake()
        {
            base.Awake();
            if (closeButton != null)
                closeButton.onClick.AddListener(CloseUI);
        }

        protected override void OnOpen()
        {
            GameProgress progress = GameProgress.Instance;
            TVBroadcastEntry entry = TVBroadcastRuntime.EnsureForecast(progress, database);
            if (progress != null)
                progress.MarkTVForecastRevealed();
            DataManager.Instance?.Save();
            Bind(entry);
        }

        protected override IEnumerator AnimateOpen()
        {
            if (_canvasGroup == null)
                yield break;

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 0f;
            if (tvFrame != null)
            {
                shownScale = Vector3.one;
                tvFrame.localScale = shownScale * 0.92f;
            }

            float elapsed = 0f;
            while (elapsed < animationDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / animationDuration));
                _canvasGroup.alpha = t;
                if (tvFrame != null)
                    tvFrame.localScale = Vector3.LerpUnclamped(shownScale * 0.92f, shownScale, t);
                yield return null;
            }

            _canvasGroup.alpha = 1f;
            if (tvFrame != null) tvFrame.localScale = shownScale;
            _canvasGroup.interactable = true;
        }

        protected override IEnumerator AnimateClose()
        {
            if (_canvasGroup == null)
            {
                gameObject.SetActive(false);
                yield break;
            }

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            float elapsed = 0f;
            while (elapsed < animationDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / animationDuration));
                _canvasGroup.alpha = 1f - t;
                if (tvFrame != null)
                    tvFrame.localScale = Vector3.LerpUnclamped(shownScale, shownScale * 0.92f, t);
                yield return null;
            }

            gameObject.SetActive(false);
        }

        private void Bind(TVBroadcastEntry entry)
        {
            string title = entry != null ? entry.title : "방송 데이터 없음";
            if (titleText != null) titleText.text = title;
            ticker?.SetMessage(entry != null ? entry.tickerText : "방송 정보를 불러올 수 없습니다.");
            Sprite presenter = entry?.presenterSprite != null
                ? entry.presenterSprite
                : fallbackPresenterSprite;
            SetImage(presenterImage, presenterPlaceholderText, presenter, "진행자 임시 영역\n(최종 에셋 대기)");
            SetImage(eventImage, eventPlaceholderText, entry?.eventSprite, title + "\n(카드 에셋 누락)");
        }

        private static void SetImage(
            Image image,
            TMP_Text placeholder,
            Sprite sprite,
            string fallback)
        {
            if (image != null)
            {
                image.sprite = sprite;
                image.preserveAspect = true;
                image.color = sprite != null ? Color.white : new Color(0.18f, 0.24f, 0.14f, 1f);
            }

            if (placeholder != null)
            {
                placeholder.text = fallback;
                placeholder.gameObject.SetActive(sprite == null);
            }
        }
    }
}
