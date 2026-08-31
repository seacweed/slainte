using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; // Image 컴포넌트를 제어하기 위해 필수 추가

public class EpisodePhotoTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Data & Manager")]
    public EpisodeData episodeData; 
    private EpisodeBoardManager boardManager; 
    public EpisodeInfoUI infoUI; // 자식 객체인 상세 정보창 UI

    private Sprite normalSprite;
    private Sprite hoverSprite;
    private Sprite selectedSprite;

    private Image photoImage;     // 내 오브젝트에 붙어있는 Image 컴포넌트
    private bool isPinned = false; 

    public static bool HasBoardPhoto(EpisodeData data)
    {
        string baseName = GetBoardPhotoBaseName(data);
        return !string.IsNullOrEmpty(baseName) && LoadBoardSprite(baseName, "idle") != null;
    }

    // 툴팁(EpisodeInfoUI) 등 다른 UI에서 보드 사진과 동일한 이미지를 재사용할 때 사용
    public static Sprite GetIdleSprite(EpisodeData data)
    {
        string baseName = GetBoardPhotoBaseName(data);
        if (string.IsNullOrEmpty(baseName)) return null;

        Sprite sprite = LoadBoardSprite(baseName, "idle");
        return sprite != null ? sprite : Resources.Load<Sprite>(baseName + "-idle");
    }

    public void SetEpisodeData(EpisodeData data, EpisodeBoardManager manager)
    {
        episodeData = data;
        boardManager = manager;

        normalSprite = null;
        hoverSprite = null;
        selectedSprite = null;

        string baseName = GetBoardPhotoBaseName(data);
        if (!string.IsNullOrEmpty(baseName))
        {
            normalSprite = LoadBoardSprite(baseName, "idle");
            hoverSprite = LoadBoardSprite(baseName, "hover");
            selectedSprite = LoadBoardSprite(baseName, "selected");

            // 예외처리: 파일을 찾지 못한 경우 폴더 구조에 따라 다를 수 있으므로 기본 로드 시도
            if (normalSprite == null) normalSprite = Resources.Load<Sprite>(baseName + "-idle");
            if (hoverSprite == null) hoverSprite = Resources.Load<Sprite>(baseName + "-hover");
            if (selectedSprite == null) selectedSprite = Resources.Load<Sprite>(baseName + "-selected");

            if (photoImage != null && normalSprite != null)
                photoImage.sprite = normalSprite;
        }
    }

    private static string GetBoardPhotoBaseName(EpisodeData data)
    {
        if (data == null) return null;
        if (!string.IsNullOrEmpty(data.iconNameBoard)) return data.iconNameBoard;
        if (string.IsNullOrEmpty(data.episodeId)) return null;

        return data.episodeId.Replace("_", "-");
    }

    private static Sprite LoadBoardSprite(string baseName, string state)
    {
        Sprite sprite = Resources.Load<Sprite>($"Sprites/EpisodeBoard/{baseName}-{state}");
        if (sprite == null) sprite = Resources.Load<Sprite>($"{baseName}-{state}");
        return sprite;
    }

    private void Awake() 
    {
        photoImage = GetComponent<Image>(); // Image 컴포넌트 찾아오기
    }

    public void OnPointerEnter(PointerEventData eventData) 
    {
        // 이미 다른 사진이 선택되어 있다면 무시
        if (boardManager.PinnedPhoto != null) return;
        
        // 호버 이미지로 교체 및 정보창 띄우기
        if (photoImage != null && hoverSprite != null) photoImage.sprite = hoverSprite;
        if (infoUI != null) infoUI.Show(episodeData, transform as RectTransform); 
    }

    public void OnPointerExit(PointerEventData eventData) 
    {
        // 내가 선택(Pin)된 상태라면 마우스가 나가도 이미지나 창을 유지
        if (isPinned) return;
        
        // 기본 이미지로 원상복구 및 정보창 끄기
        if (photoImage != null && normalSprite != null) photoImage.sprite = normalSprite;
        if (infoUI != null) infoUI.Hide(); 
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (boardManager.PinnedPhoto == this)
        {
            // 이미 포커스된 나를 다시 클릭해서 선택 해제한 경우
            isPinned = false;
            boardManager.ResetBoard();
            return;
        }

        if (boardManager.PinnedPhoto != null)
        {
            // 다른 사진이 포커스되어 있었다면 그쪽부터 선택 해제하고 포커스 전환
            boardManager.PinnedPhoto.Unpin();
        }

        isPinned = true;

        // 선택 이미지로 교체
        if (photoImage != null && selectedSprite != null) photoImage.sprite = selectedSprite;

        if (infoUI != null) infoUI.Show(episodeData, transform as RectTransform);
        boardManager.PinEpisode(this);
    }

    // 매니저가 초기화할 때 외부에서 호출하는 함수
    public void Unpin() 
    { 
        isPinned = false; 
        // 선택 해제 시 기본 이미지로 원상복구
        if (photoImage != null && normalSprite != null) photoImage.sprite = normalSprite;
        if (infoUI != null) infoUI.Hide();
    }
}
