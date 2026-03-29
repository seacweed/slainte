using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;

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
        gameObject.SetActive(true);

        // 1. 텍스트 세팅
        episodeNameText.text = data.episodeName;
        descriptionText.text = data.episodeDescription;

        // 2. 조건 세팅
        StringBuilder sb = new StringBuilder();
        foreach (var condition in data.conditions)
        {
            string colorHex = condition.isUnlocked ? "#FFFF00" : "#808080"; 
            sb.AppendLine($"<color={colorHex}>- {condition.conditionText}</color>");
        }
        conditionsText.text = sb.ToString();

        // 3. 초상화 세팅
        foreach (Transform child in portraitContainer) Destroy(child.gameObject);
        
        foreach (var character in data.characters)
        {
            GameObject portraitObj = Instantiate(portraitPrefab, portraitContainer);
            Image img = portraitObj.GetComponent<Image>();
            img.sprite = character.hasMet ? character.portraitSprite : unknownPortrait;
        }

        // 4. 레이아웃 갱신 및 위치 잡기
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