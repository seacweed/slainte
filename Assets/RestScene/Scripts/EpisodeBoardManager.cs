using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// BaseUIManager를 상속받아 열기/닫기 슬라이드 애니메이션을 그대로 사용합니다.
public class EpisodeBoardManager : BaseUIManager
{
    [Header("Board Animation")]
    public float animDuration = 0.3f;
    public float slideDistance = 150f; // 아래에서 얼마나 올라올지
    private Vector3 originalPos;

    [Header("Episode Board UI")]
    public EpisodeInfoWindow infoWindow; // 사진 옆에 뜨는 상세 정보창
    
    [Header("Bottom UI")]
    public TextMeshProUGUI bottomEpisodeNameText; // 하단 텍스트 박스
    public Button startButton;                    // 시작 버튼
    public Image startButtonImage;                // 버튼 색상 변경용

    public Color buttonActiveColor = Color.red;   
    public Color buttonInactiveColor = Color.gray; 

    // 현재 클릭(고정)된 사진 트리거
    public EpisodePhotoTrigger PinnedPhoto { get; private set; }

    protected override void Awake()
    {
        base.Awake(); // BaseUIManager의 필수 초기화 실행
        // UI의 원래 위치(정중앙) 저장
        originalPos = GetComponent<RectTransform>().anchoredPosition;

        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartButtonClicked);
        }
    }

    // 창이 열릴 때 자동으로 실행되는 함수
    protected override void OnOpen()
    {
        RefreshBoard(); // 조건에 따라 에피소드 표출 여부 갱신
        ResetBoard();   // 열릴 때마다 선택 내역 깔끔하게 초기화
    }

    // 모든 사진(자식 오브젝트)들을 스캔하여 선행 조건에 부합하는지 판별합니다.
    public void RefreshBoard()
    {
        // true: 비활성화되어 있는 사진들도 모두 긁어모음
        EpisodePhotoTrigger[] allPhotos = GetComponentsInChildren<EpisodePhotoTrigger>(true);
        
        foreach (var photo in allPhotos)
        {
            if (photo.episodeData == null) continue;

            if (EpisodeManager.Instance != null && EpisodeManager.Instance.IsAvailableToStart(photo.episodeData))
            {
                photo.gameObject.SetActive(true);
            }
            else
            {
                // 조건 미달성이거나, 이미 클리어/수락된 상태면 화면에서 완전히 숨깁니다.
                photo.gameObject.SetActive(false);
            }
        }
    }

    // ▼▼▼ [애니메이션] 아래에서 위로 슬라이드 ▼▼▼
    protected override IEnumerator AnimateOpen()
    {
        float timer = 0f;
        RectTransform rt = GetComponent<RectTransform>();
        Vector3 startPos = originalPos - new Vector3(0, slideDistance, 0); 

        rt.anchoredPosition = startPos;
        _canvasGroup.alpha = 0;
        transform.localScale = Vector3.one; 

        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / animDuration;
            float t = Mathf.SmoothStep(0, 1, timer); 

            rt.anchoredPosition = Vector3.Lerp(startPos, originalPos, t);
            _canvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }

        rt.anchoredPosition = originalPos;
        _canvasGroup.alpha = 1f;
        if(_canvasGroup) { _canvasGroup.interactable = true; _canvasGroup.blocksRaycasts = true; }
    }

    protected override IEnumerator AnimateClose()
    {
        if(_canvasGroup) { _canvasGroup.interactable = false; _canvasGroup.blocksRaycasts = false; }

        float timer = 0f;
        RectTransform rt = GetComponent<RectTransform>();
        Vector3 endPos = originalPos - new Vector3(0, slideDistance, 0);

        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / animDuration;
            float t = Mathf.SmoothStep(0, 1, timer);

            rt.anchoredPosition = Vector3.Lerp(originalPos, endPos, t);
            _canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            yield return null;
        }

        gameObject.SetActive(false);
    }

    // ▼▼▼ [에피소드 보드 핵심 로직] ▼▼▼

    private void OnStartButtonClicked()
    {
        if (PinnedPhoto != null && EpisodeManager.Instance != null)
        {
            // 1. 코어 씬 매니저에 에피소드 시작 기록
            EpisodeManager.Instance.StartEpisode(PinnedPhoto.episodeData.episodeID);
            
            // 2. 창 닫기
            CloseUI();
        }
    }

    // 사진이 클릭되었을 때 호출됨 (EpisodePhotoTrigger에서 호출)
    public void PinEpisode(EpisodePhotoTrigger photo)
    {
        PinnedPhoto = photo;

        // 하단 UI 활성화
        if (bottomEpisodeNameText) bottomEpisodeNameText.text = photo.episodeData.episodeName;
        if (startButton) startButton.interactable = true; 
        if (startButtonImage) startButtonImage.color = buttonActiveColor; 
    }

    // 초기화 (허공 클릭, 창 닫기, 혹은 다른 사진 클릭 시 이전 사진 Unpin용)
    public void ResetBoard()
    {
        if (PinnedPhoto != null)
        {
            PinnedPhoto.Unpin(); // 이전 사진 테두리 및 오버레이 끄기
            PinnedPhoto = null;
        }

        if (infoWindow != null) infoWindow.Hide(); // 상세 정보창 끄기
        
        // 하단 UI 비활성화
        if (bottomEpisodeNameText) bottomEpisodeNameText.text = "에피소드를 선택해주세요.";
        if (startButton) startButton.interactable = false; 
        if (startButtonImage) startButtonImage.color = buttonInactiveColor; 
    }
}