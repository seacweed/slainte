using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(LineRenderer))]
public class EpisodePhotoTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Data & Manager")]
    public EpisodeBoardData episodeData; 
    private EpisodeBoardManager boardManager; 

    [Header("Visual Effects")]
    public GameObject yellowOverlay; 
    public Color outlineColor = Color.yellow;
    public float outlineWidth = 0.05f;

    private LineRenderer line;
    private bool isPinned = false; 

    public void SetEpisodeData(EpisodeBoardData data)
    {
        episodeData = data;
        if (data == null) return;
        
        if (!string.IsNullOrEmpty(data.iconNameBoard))
        {
            // Resources 기반 이미지 경로에서 Sprite 로드 시도
            Sprite newSprite = Resources.Load<Sprite>($"Sprites/{data.iconNameBoard}");
            if (newSprite == null) newSprite = Resources.Load<Sprite>(data.iconNameBoard);
            
            if (newSprite != null)
            {
                UnityEngine.UI.Image img = GetComponent<UnityEngine.UI.Image>();
                if (img != null) img.sprite = newSprite;
                else
                {
                    SpriteRenderer sr = GetComponent<SpriteRenderer>();
                    if (sr != null) sr.sprite = newSprite;
                }
            }
        }
    }

    private void Awake()
    {
        boardManager = GetComponentInParent<EpisodeBoardManager>();
        
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.startWidth = outlineWidth;
        line.endWidth = outlineWidth;
        line.material = new Material(Shader.Find("Sprites/Default")); 
        line.startColor = outlineColor;
        line.endColor = outlineColor;
        line.loop = true;
        line.enabled = false;

        if (yellowOverlay) yellowOverlay.SetActive(false); 

        DrawOutlineShape();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (boardManager.PinnedPhoto != null) return;

        line.enabled = true; 
        boardManager.infoWindow.Show(episodeData, transform as RectTransform); 
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isPinned) return;

        line.enabled = false; 
        boardManager.infoWindow.Hide(); 
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (boardManager.PinnedPhoto != null && boardManager.PinnedPhoto != this) return;

        isPinned = !isPinned; 

        if (isPinned)
        {
            line.enabled = true;
            if (yellowOverlay) yellowOverlay.SetActive(true); 
            
            boardManager.infoWindow.Show(episodeData, transform as RectTransform);
            boardManager.PinEpisode(this);
        }
        else
        {
            boardManager.ResetBoard();
        }
    }

    public void Unpin()
    {
        isPinned = false;
        line.enabled = false;
        if (yellowOverlay) yellowOverlay.SetActive(false);
    }

    private void DrawOutlineShape()
    {
        PolygonCollider2D poly = GetComponent<PolygonCollider2D>();
        if (poly != null)
        {
            line.positionCount = poly.GetTotalPointCount();
            for (int i = 0; i < poly.points.Length; i++) line.SetPosition(i, poly.points[i]);
            return;
        }

        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            line.positionCount = 4;
            Vector2 s = box.size; Vector2 o = box.offset;
            line.SetPosition(0, o + new Vector2(-s.x, -s.y) * 0.5f);
            line.SetPosition(1, o + new Vector2(-s.x, s.y) * 0.5f);
            line.SetPosition(2, o + new Vector2(s.x, s.y) * 0.5f);
            line.SetPosition(3, o + new Vector2(s.x, -s.y) * 0.5f);
        }
    }
}