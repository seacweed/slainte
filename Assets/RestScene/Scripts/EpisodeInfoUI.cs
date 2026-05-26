using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EpisodeInfoUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI episodeNameText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI conditionsText;

    [Header("Portraits")]
    public Transform portraitContainer;
    public GameObject portraitPrefab;
    public Sprite unknownPortrait;

    [Header("Rendering")]
    [SerializeField] private int tooltipSortingOrder = 100;

    private RectTransform rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        Canvas tooltipCanvas = GetComponent<Canvas>();
        if (tooltipCanvas == null)
        {
            tooltipCanvas = gameObject.AddComponent<Canvas>();
        }

        tooltipCanvas.overrideSorting = true;
        tooltipCanvas.sortingOrder = tooltipSortingOrder;
        gameObject.SetActive(false);
    }

    public void Show(EpisodeData data, RectTransform targetPhoto)
    {
        if (data == null)
        {
            Debug.LogError("[EpisodeInfoUI] EpisodeData is null.");
            return;
        }

        gameObject.SetActive(true);

        if (episodeNameText) episodeNameText.text = data.episodeTitle;
        if (descriptionText) descriptionText.text = data.episodeDescription;

        if (conditionsText) 
        {
            GameProgress gp = GameProgress.Instance;
            if (data.triggerCondition != null && gp != null)
            {
                string cText = "";
                var cond = data.triggerCondition;

                if (cond.minDay > 0)
                {
                    bool met = gp.CurrentDay >= cond.minDay;
                    cText += (met ? "<color=green>🔓</color>" : "<color=red>🔒</color>") + $" {cond.minDay}일차 이상\n";
                }

                foreach (string epId in cond.prerequisiteEpisodeIds)
                {
                    bool met = gp.IsEpisodeCompleted(epId);
                    string title = EpisodeManager.Instance?.GetEpisodeData(epId)?.episodeTitle ?? epId;
                    cText += (met ? "<color=green>🔓</color>" : "<color=red>🔒</color>") + $" 선행 에피소드 '{title}'\n";
                }

                foreach (string flag in cond.requiredFlags)
                {
                    bool met = gp.HasFlag(flag);
                    // 플래그 이름 자체를 조건 설명으로 사용
                    cText += (met ? "<color=green>🔓</color>" : "<color=red>🔒</color>") + $" {flag}\n";
                }

                foreach (var vc in cond.requiredVars)
                {
                    bool met = vc.Evaluate(gp.GetVar(vc.varName));
                    string opStr = GetOpString(vc.op);
                    cText += (met ? "<color=green>🔓</color>" : "<color=red>🔒</color>") + $" {vc.varName} {opStr} {vc.threshold}\n";
                }

                // 커스텀 텍스트가 있다면 하단에 일괄 표시 (단순 힌트용)
                foreach (string customText in data.customConditionTexts)
                {
                    bool overallMet = EpisodeManager.Instance.CanStart(data, gp);
                    cText += (overallMet ? "<color=green>🔓</color>" : "<color=red>🔒</color>") + $" {customText}\n";
                }

                if (string.IsNullOrEmpty(cText))
                {
                    cText = "<color=green>🔓</color> 시작 가능\n";
                }
                
                conditionsText.text = cText;
            }
            else
            {
                conditionsText.text = "";
            }
        }

        if (portraitContainer != null)
        {
            foreach (Transform child in portraitContainer) Destroy(child.gameObject);

            for (int i = 0; i < data.characters.Count; i++)
            {
                if (portraitPrefab == null) break;
                string charId = data.characters[i].characterName;

                GameObject obj = Instantiate(portraitPrefab, portraitContainer);
                Image img = obj.GetComponent<Image>();
                if (img != null)
                {
                    // 1. 캐릭터 ID와 일치하는 사진을 Resources/Sprites 또는 Resources/Portraits 에서 찾습니다.
                    Sprite s = Resources.Load<Sprite>($"Sprites/{charId}");
                    if (s == null) s = Resources.Load<Sprite>($"Portraits/{charId}");
                    
                    img.sprite = s != null ? s : unknownPortrait;
                }
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

        Vector3 targetLeftCenter = (corners[0] + corners[1]) * 0.5f;
        Vector3 targetRightCenter = (corners[2] + corners[3]) * 0.5f;

        // 기본: 오른쪽에 배치
        rectTransform.pivot = new Vector2(0, 0.5f);
        transform.position = targetRightCenter + new Vector3(10, 0, 0);

        // 변경된 위치를 즉시 반영하여 코너를 얻어오기 위해 레이아웃 재계산
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

        Vector3[] uiCorners = new Vector3[4];
        rectTransform.GetWorldCorners(uiCorners);

        Canvas canvas = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
        if (canvas != null)
        {
            Vector3[] canvasCorners = new Vector3[4];
            canvas.GetComponent<RectTransform>().GetWorldCorners(canvasCorners);
            float rightBound = canvasCorners[2].x; // 캔버스의 오른쪽 끝 월드 좌표

            // UI의 오른쪽 끝 단위가 캔버스 범위를 넘어간다면 왼쪽으로 튕겨냅니다
            if (uiCorners[2].x > rightBound)
            {
                rectTransform.pivot = new Vector2(1, 0.5f);
                transform.position = targetLeftCenter - new Vector3(10, 0, 0);
            }
        }
    }

    private string GetOpString(CompareOp op)
    {
        return op switch
        {
            CompareOp.GreaterOrEqual => ">=",
            CompareOp.Greater => ">",
            CompareOp.Equal => "==",
            CompareOp.Less => "<",
            CompareOp.LessOrEqual => "<=",
            _ => ""
        };
    }
}
