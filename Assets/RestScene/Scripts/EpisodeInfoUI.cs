using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EpisodeInfoWindow : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI episodeNameText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI conditionsText;

    [Header("Portraits")]
    public Transform portraitContainer;
    public GameObject portraitPrefab;
    public Sprite unknownPortrait;

    private RectTransform rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        gameObject.SetActive(false);
    }

    public void Show(EpisodeData data, RectTransform targetPhoto)
    {
        if (data == null)
        {
            Debug.LogError("[EpisodeInfoWindow] EpisodeData is null.");
            return;
        }

        gameObject.SetActive(true);

        if (episodeNameText) episodeNameText.text = data.episodeTitle;
        if (descriptionText) descriptionText.text = data.episodeDescription;

        // TODO: populate conditionsText once EpisodeData gains a conditions field
        if (conditionsText) conditionsText.text = "";

        if (portraitContainer != null)
        {
            foreach (Transform child in portraitContainer) Destroy(child.gameObject);

            for (int i = 0; i < data.characters.Count; i++)
            {
                if (portraitPrefab == null) break;
                GameObject obj = Instantiate(portraitPrefab, portraitContainer);
                Image img = obj.GetComponent<Image>();
                if (img != null) img.sprite = unknownPortrait;
            }
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        UpdatePosition(targetPhoto);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void UpdatePosition(RectTransform targetPhoto)
    {
        Vector3[] corners = new Vector3[4];
        targetPhoto.GetWorldCorners(corners);

        Vector3 targetRightCenter = (corners[2] + corners[3]) * 0.5f;

        rectTransform.pivot = new Vector2(0, 0.5f);
        transform.position = targetRightCenter + new Vector3(10, 0, 0);
    }
}
