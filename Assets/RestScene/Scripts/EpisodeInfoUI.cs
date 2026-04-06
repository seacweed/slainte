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

        // [핵심 변경] 코어 씬 매니저에게 플레이어의 현재 진행 상태를 물어봅니다.
        EpisodeProgressData progress = EpisodeManager.Instance.GetProgress(data);

        // 1. 텍스트 세팅
        episodeNameText.text = data.episodeName;
        descriptionText.text = data.episodeDescription;

        // 2. 조건 세팅 (진행 상태의 배열 인덱스 활용)
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < data.conditions.Count; i++)
        {
            // progress.conditionUnlocks 배열에서 해당 조건의 달성 여부를 확인
            bool isUnlocked = (progress.conditionUnlocks != null && i < progress.conditionUnlocks.Length) ? progress.conditionUnlocks[i] : false;
            string colorHex = isUnlocked ? "#FFFF00" : "#808080"; 
            sb.AppendLine($"<color={colorHex}>- {data.conditions[i].conditionText}</color>");
        }
        conditionsText.text = sb.ToString();

        // 3. 초상화 세팅 (진행 상태의 배열 인덱스 활용)
        foreach (Transform child in portraitContainer) Destroy(child.gameObject);
        
        for (int i = 0; i < data.characters.Count; i++)
        {
            GameObject portraitObj = Instantiate(portraitPrefab, portraitContainer);
            Image img = portraitObj.GetComponent<Image>();
            
            // progress.characterMeets 배열에서 해당 인물과의 조우 여부를 확인
            bool hasMet = (progress.characterMeets != null && i < progress.characterMeets.Length) ? progress.characterMeets[i] : false;
            img.sprite = hasMet ? data.characters[i].portraitSprite : unknownPortrait;
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