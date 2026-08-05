using System.Collections;
using System.Collections.Generic;
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
    public GameObject photoPrefab; // 생성할 사진 프리팹
    public Transform[] boardSlots; // 고정된 6개의 슬롯 자리
    
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
        if (EpisodeManager.Instance != null && EpisodeManager.Instance.HasPendingMandatoryEpisode())
        {
            ShowMandatoryGate(); // 미완료 필수 에피소드가 있으면 보드 선택 자체를 막음
            return;
        }

        RefreshBoard(); // 조건에 따라 에피소드 표출 여부 갱신
        ResetBoard();   // 열릴 때마다 선택 내역 깔끔하게 초기화
    }

    // 필수 에피소드가 남아있을 때: 보드를 비우고 안내 문구만 표시
    private void ShowMandatoryGate()
    {
        foreach (var photo in GetComponentsInChildren<EpisodePhotoTrigger>(true))
            Destroy(photo.gameObject);

        PinnedPhoto = null;
        if (bottomEpisodeNameText) bottomEpisodeNameText.text = "먼저 영업을 통해 필수 에피소드를 진행해주세요.";
        if (startButton) startButton.interactable = false;
        if (startButtonImage) startButtonImage.color = buttonInactiveColor;
    }

    // 6자리 슬롯에 맞게 에피소드를 랜덤 배치하고 위치 유지
    public void RefreshBoard()
    {
        if (EpisodeManager.Instance == null || GameProgress.Instance == null) return;
        
        var availableEpisodes = EpisodeManager.Instance.GetBoardEpisodes();
        var boardEpisodes = new List<EpisodeData>();
        foreach (var ep in availableEpisodes)
        {
            if (EpisodePhotoTrigger.HasBoardPhoto(ep))
            {
                boardEpisodes.Add(ep);
            }
        }
        
        // 1. 이미 슬롯에 있는 프리팹 캐싱 및 필요없는 프리팹 제거
        List<EpisodePhotoTrigger> currentPhotos = new List<EpisodePhotoTrigger>(GetComponentsInChildren<EpisodePhotoTrigger>(true));
        foreach (var photo in currentPhotos)
        {
            // 현재 사진이 가진 에피소드가 여전히 활성화(visible) 상태인지 확인
            bool stillVisible = false;
            foreach (var ep in boardEpisodes)
            {
                if (ep.episodeId == photo.episodeData?.episodeId)
                {
                    stillVisible = true;
                    break;
                }
            }

            // 조건 미달(진행불가 등)로 더이상 보이지 않아야 하거나 클리어된 경우 삭제 및 저장된 자리값 초기화
            if (!stillVisible)
            {
                if (photo.episodeData != null)
                {
                    GameProgress.Instance.ClearBoardSlot(photo.episodeData.episodeId);
                }
                Destroy(photo.gameObject);
            }
        }

        // 삭제가 즉시 반영되지 않으므로, 한 틱 뒤를 고려하거나 잔여 슬롯 추적
        bool[] filledSlots = new bool[boardSlots.Length];
        
        // 2. 이미 자리를 배정받았던 에피소드부터 예약
        foreach (var ep in boardEpisodes)
        {
            int savedSlot = GameProgress.Instance.GetBoardSlot(ep.episodeId) - 1;
            if (savedSlot >= 0 && savedSlot < boardSlots.Length)
            {
                filledSlots[savedSlot] = true;
            }
        }

        // 3. 자리가 없는(새로 발견된) 에피소드들에게 빈 슬롯 무작위 배정 및 생성
        foreach (var ep in boardEpisodes)
        {
            int savedSlot = GameProgress.Instance.GetBoardSlot(ep.episodeId) - 1;
            int targetSlot = savedSlot;

            if (savedSlot < 0 || savedSlot >= boardSlots.Length)
            {
                // 빈 슬롯 찾기
                System.Collections.Generic.List<int> emptySlots = new System.Collections.Generic.List<int>();
                for (int i = 0; i < boardSlots.Length; i++)
                {
                    if (!filledSlots[i]) emptySlots.Add(i);
                }

                if (emptySlots.Count == 0)
                {
                    Debug.LogWarning("[EpisodeBoardManager] 보드에 자리가 부족합니다!");
                    continue; // 6개 초과 시 스킵
                }

                int rnd = Random.Range(0, emptySlots.Count);
                targetSlot = emptySlots[rnd];
                filledSlots[targetSlot] = true;

                // 새 자리 저장 (1-indexed)
                GameProgress.Instance.SetBoardSlot(ep.episodeId, targetSlot + 1);
            }

            // 해당 에피소드의 프리팹이 이미 존재하면 생성 안 함
            bool alreadyExists = false;
            foreach (var p in GetComponentsInChildren<EpisodePhotoTrigger>(true))
            {
                if (p.episodeData?.episodeId == ep.episodeId)
                {
                    alreadyExists = true;
                    // 부모만 보장해주기
                    p.transform.SetParent(boardSlots[targetSlot], false);
                    p.transform.localPosition = Vector3.zero;
                    p.gameObject.SetActive(true);
                    p.SetEpisodeData(ep, this);
                    break;
                }
            }

            // 없으면 생성
            if (!alreadyExists && photoPrefab != null)
            {
                GameObject newPhoto = Instantiate(photoPrefab, boardSlots[targetSlot]);
                newPhoto.transform.localPosition = Vector3.zero;
                EpisodePhotoTrigger trigger = newPhoto.GetComponent<EpisodePhotoTrigger>();
                if (trigger != null)
                {
                    trigger.SetEpisodeData(ep, this);
                }
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
        if (DayFlowController.Instance == null) return;

        if (PinnedPhoto != null)
        {
            ApplySelectConditionFlag(PinnedPhoto);

            // 1. 하루 흐름 컨트롤러에 기본 에피소드 시작 요청 (완료 후 정산으로 이어짐)
            DayFlowController.Instance.StartDefaultEpisode(PinnedPhoto.episodeData.episodeId);
        }
        else
        {
            // 아무것도 선택하지 않은 기본 상태 = 영업 시작
            DayFlowController.Instance.StartBusinessDay();
        }

        // 2. 창 닫기
        CloseUI();
    }

    // 선택 조건 옵션들의 최종 on/off를 GameProgress 플래그에 반영 (에피소드 시작 직전에만 호출)
    // 옵션 중 하나만 on이어야 정상이지만(ToggleGroup으로 강제), 방어적으로 전체를 순회해 반영한다.
    private void ApplySelectConditionFlag(EpisodePhotoTrigger photo)
    {
        var selectConditions = photo.episodeData?.selectConditions;
        if (selectConditions == null || GameProgress.Instance == null || photo.infoUI == null) return;

        for (int i = 0; i < selectConditions.Count; i++)
        {
            string flag = selectConditions[i].flag;
            if (string.IsNullOrEmpty(flag)) continue;

            if (photo.infoUI.IsSelectOptionOn(i)) GameProgress.Instance.SetFlag(flag);
            else GameProgress.Instance.ClearFlag(flag);
        }
    }

    // 사진이 클릭되었을 때 호출됨 (EpisodePhotoTrigger에서 호출)
    public void PinEpisode(EpisodePhotoTrigger photo)
    {
        PinnedPhoto = photo;

        bool canPlay = false;
        if (EpisodeManager.Instance != null && GameProgress.Instance != null)
        {
            canPlay = EpisodeManager.Instance.IsPlayable(photo.episodeData, GameProgress.Instance);
        }

        // 하단 UI 활성화
        if (bottomEpisodeNameText)
        {
            bottomEpisodeNameText.text = canPlay ? photo.episodeData.episodeTitle : $"{photo.episodeData.episodeTitle} <color=#ff8888>(조건 미달성)</color>";
        }

        if (startButton) startButton.interactable = canPlay;
        if (startButtonImage) startButtonImage.color = canPlay ? buttonActiveColor : buttonInactiveColor;
    }

    // 초기화 (허공 클릭, 창 닫기, 혹은 다른 사진 클릭 시 이전 사진 Unpin용) — 아무것도 선택 안 한 기본 상태 = 영업
    public void ResetBoard()
    {
        if (PinnedPhoto != null)
        {
            PinnedPhoto.Unpin(); // 이전 사진 테두리 및 정보창 끄기
            PinnedPhoto = null;
        }

        if (bottomEpisodeNameText) bottomEpisodeNameText.text = "영업";
        if (startButton) startButton.interactable = true;
        if (startButtonImage) startButtonImage.color = buttonActiveColor;
    }
}
