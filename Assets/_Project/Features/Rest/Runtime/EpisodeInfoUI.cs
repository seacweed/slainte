using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EpisodeInfoUI : MonoBehaviour
{
    [System.Serializable]
    public struct ConditionRow
    {
        public GameObject container;
        public Image lockIcon;
        public TextMeshProUGUI label;
    }

    // 선택 조건 옵션 하나를 표시하는 UI 묶음. EpisodeData.selectConditions[i]와 인덱스로 1:1 매칭된다.
    // 옵션당 조건은 항상 하나라 행도 하나(자물쇠 아이콘 하나 + 설명 한 줄)다.
    [System.Serializable]
    public struct SelectConditionGroup
    {
        public GameObject container;
        public ConditionRow conditionRow;
        public Toggle toggle;
    }

    [Header("Header")]
    public Image boardImage;
    public TextMeshProUGUI episodeNameText;

    [Header("Description")]
    public TextMeshProUGUI descriptionText;

    [Header("Trigger Condition (해금 조건)")]
    public GameObject triggerSection;
    public ConditionRow[] triggerConditionRows;

    [Header("Select Condition (선택 조건)")]
    public GameObject selectSection;
    public ToggleGroup selectToggleGroup; // 옵션 중 동시에 하나만 on 되도록 강제 (유니티 내장)
    public SelectConditionGroup[] selectConditionGroups;

    [Header("Lock State Sprites")]
    public Sprite lockedSprite;
    public Sprite unlockedSprite;

    [Header("Condition Text Colors")]
    [Tooltip("해금 조건 충족 / 선택 조건이 해금+선택된 상태의 텍스트 색")]
    public Color conditionMetTextColor = Color.yellow;
    [Tooltip("해금 조건 미충족 / 선택 조건이 미해금이거나 선택 안 된 상태의 텍스트 색")]
    public Color conditionUnmetTextColor = Color.gray;

    [System.Serializable]
    public struct PortraitSlot
    {
        public GameObject container;   // 배경 포함 슬롯 루트. 캐릭터 없으면 통째로 비활성화
        public Image characterImage;   // 캐릭터 스프라이트를 넣을 자식 Image
    }

    [System.Serializable]
    public struct CharacterPortraitSprite
    {
        public string characterKey;
        public Sprite sprite;
    }

    [Header("Portraits")]
    public PortraitSlot[] portraitSlots;
    public CharacterPortraitSprite[] characterPortraitSprites; // Sprites/UI/episode_board/character의 slot_* 이미지를 캐릭터 키별로 등록
    public Sprite unknownPortrait;

    [Header("Rendering")]
    [SerializeField] private int tooltipSortingOrder = 100;

    private RectTransform rectTransform;
    private EpisodeData currentData;
    private Dictionary<string, Sprite> characterPortraitLookup;
    private Sprite[] selectIconDefaultSprites;
    private UnityEngine.Events.UnityAction<bool>[] selectToggleListeners;

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

        // 중첩 Canvas는 자체 GraphicRaycaster가 없으면 그 하위 UI(토글 등)가 포인터 클릭을 못 받는다.
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        characterPortraitLookup = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
        if (characterPortraitSprites != null)
        {
            foreach (var entry in characterPortraitSprites)
            {
                if (string.IsNullOrEmpty(entry.characterKey) || entry.sprite == null) continue;
                characterPortraitLookup[entry.characterKey] = entry.sprite;
            }
        }

        if (selectConditionGroups != null)
        {
            selectIconDefaultSprites = new Sprite[selectConditionGroups.Length];
            selectToggleListeners = new UnityEngine.Events.UnityAction<bool>[selectConditionGroups.Length];

            for (int i = 0; i < selectConditionGroups.Length; i++)
            {
                SelectConditionGroup group = selectConditionGroups[i];
                if (group.conditionRow.lockIcon != null)
                    selectIconDefaultSprites[i] = group.conditionRow.lockIcon.sprite;

                if (group.toggle == null) continue;
                group.toggle.group = selectToggleGroup;

                int index = i;
                selectToggleListeners[i] = isOn => OnSelectToggleChanged(index, isOn);
                group.toggle.onValueChanged.AddListener(selectToggleListeners[i]);
            }
        }

        gameObject.SetActive(false);
    }

    public void Show(EpisodeData data, RectTransform targetPhoto)
    {
        if (data == null)
        {
            Debug.LogError("[EpisodeInfoUI] EpisodeData is null.");
            return;
        }

        currentData = data;
        gameObject.SetActive(true);

        if (boardImage != null) boardImage.sprite = EpisodePhotoTrigger.GetIdleSprite(data);
        if (episodeNameText != null) episodeNameText.text = data.episodeTitle;
        if (descriptionText != null) descriptionText.text = data.episodeDescription;

        GameProgress gp = GameProgress.Instance;
        bool hasManager = gp != null && EpisodeManager.Instance != null;

        var triggerEntries = BuildTriggerConditionEntries(data.triggerConditionEntries, gp);
        triggerEntries.AddRange(BuildTriggerConditionEntries(data.playConditionEntries, gp));
        ApplyConditionRows(triggerConditionRows, triggerEntries);
        if (triggerSection != null) triggerSection.SetActive(triggerEntries.Count > 0);

        UpdateSelectGroups(data, gp, hasManager);

        UpdatePortraits(data);

        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        UpdatePosition(targetPhoto);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    // EpisodeBoardManager가 Play 버튼 클릭 시 각 옵션의 최종 on/off를 읽어 플래그에 반영할 때 사용
    public bool IsSelectOptionOn(int index)
    {
        if (selectConditionGroups == null || index < 0 || index >= selectConditionGroups.Length) return false;
        Toggle t = selectConditionGroups[index].toggle;
        return t != null && t.isOn;
    }

    private void OnSelectToggleChanged(int index, bool isOn)
    {
        if (currentData != null) UpdatePortraits(currentData);

        if (selectConditionGroups == null || index < 0 || index >= selectConditionGroups.Length) return;
        if (currentData?.selectConditions == null || index >= currentData.selectConditions.Count) return;

        GameProgress gp = GameProgress.Instance;
        bool hasManager = gp != null && EpisodeManager.Instance != null;
        SelectConditionEntry entry = currentData.selectConditions[index];
        bool available = hasManager && EpisodeManager.Instance.EvaluateSelectCondition(entry.condition, gp);
        bool revealed = hasManager && EpisodeManager.Instance.EvaluateSelectCondition(entry.revealCondition, gp);

        ApplySelectIconAndText(index, selectConditionGroups[index], entry, available, revealed);
    }

    private void UpdateSelectGroups(EpisodeData data, GameProgress gp, bool hasManager)
    {
        int optionCount = data.selectConditions?.Count ?? 0;

        if (selectConditionGroups != null)
        {
            for (int i = 0; i < selectConditionGroups.Length; i++)
            {
                SelectConditionGroup group = selectConditionGroups[i];
                bool hasEntry = i < optionCount;

                if (group.container != null) group.container.SetActive(hasEntry);
                if (!hasEntry)
                {
                    if (group.toggle != null) group.toggle.gameObject.SetActive(false);
                    continue;
                }

                SelectConditionEntry entry = data.selectConditions[i];
                bool available = hasManager && EpisodeManager.Instance.EvaluateSelectCondition(entry.condition, gp);
                bool revealed = hasManager && EpisodeManager.Instance.EvaluateSelectCondition(entry.revealCondition, gp);

                if (group.toggle != null)
                {
                    bool hasFlag = !string.IsNullOrEmpty(entry.flag);
                    group.toggle.gameObject.SetActive(hasFlag);
                    if (selectToggleListeners != null && i < selectToggleListeners.Length && selectToggleListeners[i] != null)
                        group.toggle.onValueChanged.RemoveListener(selectToggleListeners[i]);
                    group.toggle.isOn = available && hasFlag && gp != null && gp.HasFlag(entry.flag);
                    group.toggle.interactable = available && hasFlag;
                    if (selectToggleListeners != null && i < selectToggleListeners.Length && selectToggleListeners[i] != null)
                        group.toggle.onValueChanged.AddListener(selectToggleListeners[i]);
                }

                ApplySelectIconAndText(i, group, entry, available, revealed);
            }
        }

        if (selectSection != null) selectSection.SetActive(optionCount > 0);
    }

    // 선택 조건 옵션 하나(= 행 하나)를 채운다. conditionText가 있으면 그걸, 없으면 condition에서 자동 생성한 문구를 사용.
    // 옵션에는 항상 조건이 하나 있다고 가정하므로(기획상 "조건 없음" 옵션은 없음) 행을 숨길 필요가 없다.
    // revealCondition이 미충족이면 구체적인 문구 대신 hiddenText(비어있으면 "???")로 가려서 표시한다.
    // 아이콘은 토글 컴포넌트에 설정된 3-상태 스프라이트(미해금=disabled/해금+on=selected)를 그대로 읽어 쓰고,
    // 해금+off 상태는 Awake에서 캐싱해둔 프리팹 원본 스프라이트로 복원한다.
    private void ApplySelectIconAndText(int index, SelectConditionGroup group, SelectConditionEntry entry, bool available, bool revealed)
    {
        bool isOn = group.toggle != null && group.toggle.isOn;

        string text = revealed
            ? (!string.IsNullOrEmpty(entry.conditionText) ? entry.conditionText : BuildSelectConditionText(entry.condition))
            : (!string.IsNullOrEmpty(entry.hiddenText) ? entry.hiddenText : "???");

        if (group.conditionRow.label != null)
        {
            group.conditionRow.label.text = text;
            group.conditionRow.label.color = (available && isOn) ? conditionMetTextColor : conditionUnmetTextColor;
        }

        if (group.conditionRow.lockIcon != null && group.toggle != null)
        {
            if (!available)
                group.conditionRow.lockIcon.sprite = group.toggle.spriteState.disabledSprite;
            else if (isOn)
                group.conditionRow.lockIcon.sprite = group.toggle.spriteState.selectedSprite;
            else if (selectIconDefaultSprites != null && index < selectIconDefaultSprites.Length)
                group.conditionRow.lockIcon.sprite = selectIconDefaultSprites[index];
        }
    }

    private string BuildSelectConditionText(SelectSingleCondition cond)
    {
        if (cond == null) return null;

        return cond.type switch
        {
            SelectConditionType.MinDay => $"{cond.minDay}일차 이상",
            SelectConditionType.RequiredFlag => cond.requiredFlag,
            SelectConditionType.PrerequisiteEpisode =>
                $"선행 에피소드 '{EpisodeManager.Instance?.GetEpisodeData(cond.prerequisiteEpisodeId)?.episodeTitle ?? cond.prerequisiteEpisodeId}'",
            SelectConditionType.RequiredVar => $"{cond.varName} {GetOpString(cond.varOp)} {cond.varThreshold}",
            SelectConditionType.MinMoney => $"소지금 {cond.minMoney}원 이상",
            _ => null
        };
    }

    private void ApplyConditionRows(ConditionRow[] rows, List<(string text, bool met)> entries)
    {
        if (rows == null) return;

        for (int i = 0; i < rows.Length; i++)
        {
            ConditionRow row = rows[i];
            bool hasEntry = i < entries.Count;

            if (row.container != null) row.container.SetActive(hasEntry);
            if (!hasEntry) continue;

            (string text, bool met) = entries[i];
            if (row.label != null)
            {
                row.label.text = text;
                row.label.color = met ? conditionMetTextColor : conditionUnmetTextColor;
            }
            if (row.lockIcon != null) row.lockIcon.sprite = met ? unlockedSprite : lockedSprite;
        }
    }

    // TRIGGER/PLAY_TRIGGER 항목 목록을 각각 개별 평가해 (텍스트, 충족 여부) 쌍으로 변환한다.
    private List<(string text, bool met)> BuildTriggerConditionEntries(List<TriggerConditionEntry> entries, GameProgress gp)
    {
        var result = new List<(string, bool)>();
        if (entries == null || gp == null || EpisodeManager.Instance == null) return result;

        foreach (TriggerConditionEntry entry in entries)
        {
            bool met = EpisodeManager.Instance.EvaluateSelectCondition(entry.condition, gp);
            string text = !string.IsNullOrEmpty(entry.conditionText)
                ? entry.conditionText
                : BuildSelectConditionText(entry.condition);
            result.Add((text, met));
        }

        return result;
    }

    // 캐릭터가 3개 이하면 왼쪽(0번 슬롯)부터 채우고, 남는 슬롯은 배경까지 통째로 비활성화한다.
    private void UpdatePortraits(EpisodeData data)
    {
        if (portraitSlots == null) return;

        List<CharacterDisplay> list = GetActiveCharacterList(data);

        for (int i = 0; i < portraitSlots.Length; i++)
        {
            PortraitSlot slot = portraitSlots[i];
            bool hasCharacter = list != null && i < list.Count;

            if (slot.container != null) slot.container.SetActive(hasCharacter);
            if (!hasCharacter || slot.characterImage == null) continue;

            CharacterDisplay display = list[i];

            if (display.isHidden || string.IsNullOrEmpty(display.characterName))
            {
                slot.characterImage.sprite = unknownPortrait;
                continue;
            }

            Sprite s = characterPortraitLookup != null && characterPortraitLookup.TryGetValue(display.characterName, out Sprite found)
                ? found
                : null;
            slot.characterImage.sprite = s != null ? s : unknownPortrait;
        }
    }

    // 선택된 옵션이 있고 그 옵션에 초상화 override가 채워져 있으면 그걸, 아니면 기본 characters를 반환
    private List<CharacterDisplay> GetActiveCharacterList(EpisodeData data)
    {
        int idx = GetSelectedIndex();
        if (idx >= 0 && data.selectConditions != null && idx < data.selectConditions.Count)
        {
            List<CharacterDisplay> overrides = data.selectConditions[idx].characterOverrides;
            if (overrides != null && overrides.Count > 0) return overrides;
        }
        return data.characters;
    }

    private int GetSelectedIndex()
    {
        if (selectConditionGroups == null) return -1;
        for (int i = 0; i < selectConditionGroups.Length; i++)
        {
            Toggle t = selectConditionGroups[i].toggle;
            if (t != null && t.isOn) return i;
        }
        return -1;
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
