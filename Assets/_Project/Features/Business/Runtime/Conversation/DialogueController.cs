using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

// 대사창 하나를 담당하는 컨트롤러. 대사 큐(_queue)를 한 줄씩 타이핑 애니메이션(TypeLine)으로
// 보여주고, Advance() 호출(클릭/스페이스)마다 "타이핑 중이면 스킵 → 다음 줄 있으면 다음 줄 →
// 없으면 닫기" 순으로 진행한다. 대사마다 말풍선을 페이드 아웃 → 화자 기준으로 다시 배치 → 페이드 인하며,
// 배치(위치·화살표)는 등록된 리졸버가 정하고 크기는 전체 문장 높이로 미리 계산해 타이핑 중에 커지지 않는다.
// 선택지 UI가 뜨는 동안은 SlideUpForChoices로 말풍선을 위로 밀어 겹치지 않게 한다.
public class DialogueController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text bodyText;

    [Header("Bubble Layout")]
    [Tooltip("9-sliced bubble background. Empty = the Image on bubbleRect.")]
    [SerializeField] private Image bubbleImage;
    [Tooltip("Shown above the bubble when a character on stage speaks.")]
    [SerializeField] private RectTransform arrowFront;
    [Tooltip("Shown below the bubble when the protagonist speaks.")]
    [SerializeField] private RectTransform arrowBack;
    [SerializeField] private float bubbleWidth = 840f;
    [SerializeField] private float topPadding = 20f;
    [SerializeField] private float nameRowHeight = 80f;
    [SerializeField] private float indicatorRowHeight = 80f;
    [SerializeField] private float bottomPadding = 20f;
    [SerializeField] private float textSideMargin = 20f;
    [SerializeField] private float nameLeftMargin = 30f;
    [SerializeField] private float indicatorRightMargin = 20f;
    [Tooltip("Top edge Y (from the panel center) of a character's bubble. It grows downward.")]
    [SerializeField] private float characterBubbleTopY = -361f;
    [Tooltip("Bottom edge Y (from the panel center) of the protagonist / offstage bubble. It grows upward.")]
    [SerializeField] private float protagonistBubbleBottomY = -661f;

    [Header("Fade")]
    [Tooltip("Fade out/in time per line. 0 = switch instantly.")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.12f;

    [Header("Choice Slide")]
    [SerializeField] private float slideDuration = 0.25f;

    [Header("Typing")]
    [SerializeField] private float charDelay = 0.03f;
    [SerializeField] private int soundEveryNChars = 1;

    [Header("SFX")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip typeClip;
    [Range(0f, 0.2f)]
    [SerializeField] private float randomPitchRange = 0.06f;

    [Header("NextIndicator")]
    [SerializeField] private TMP_Text nextIndicator;
    [SerializeField] private float blinkInterval = 0.35f;

    private readonly Queue<DialogueLine> _queue = new();
    private Coroutine _presentRoutine;
    private Coroutine _fadeRoutine;
    private Coroutine _blinkRoutine;
    private Coroutine _slideRoutine;
    private bool _isOpen;
    private bool _isTyping;
    private bool _contentApplied;
    private bool _skipRequested;
    private string _fullLine;
    private Vector2 _basePosition;
    private IDialogueBubblePlacementResolver _placementResolver;

    public bool IsTyping => _isTyping;
    public bool IsOpen => _isOpen;

    public event Action DialogueClosed;

    void Awake()
    {
        if (bubbleImage == null && bubbleRect != null)
            bubbleImage = bubbleRect.GetComponent<Image>();
        if (bubbleImage != null)
            bubbleImage.type = Image.Type.Sliced;
        if (bubbleRect != null)
            _basePosition = bubbleRect.anchoredPosition;
        HideImmediate();
    }

    public void SetPlacementResolver(IDialogueBubblePlacementResolver resolver)
    {
        _placementResolver = resolver;
    }

    public void HideImmediate()
    {
        StopRoutine(ref _presentRoutine);
        StopRoutine(ref _fadeRoutine);
        StopRoutine(ref _blinkRoutine);
        StopRoutine(ref _slideRoutine);
        if (bubbleRect != null) bubbleRect.anchoredPosition = _basePosition;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        nameText.text = "";
        bodyText.text = "";
        _queue.Clear();
        _isOpen = false;
        _isTyping = false;
        _contentApplied = false;
        _skipRequested = false;
        SetNextIndicator(false);
    }

    // 말풍선을 페이드 아웃한 뒤 닫는다(제조 진입 전 연출). 사용자가 닫은 것이 아니므로 DialogueClosed는
    // 보내지 않는다. 이미 닫혀 있거나 비활성이면 바로 닫고 콜백을 호출한다.
    public void FadeOutAndHide(Action onComplete)
    {
        if (!_isOpen || !isActiveAndEnabled || fadeDuration <= 0f)
        {
            HideImmediate();
            onComplete?.Invoke();
            return;
        }

        StopRoutine(ref _presentRoutine);
        StopRoutine(ref _fadeRoutine);
        StopRoutine(ref _blinkRoutine);
        _isTyping = false;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        _presentRoutine = StartCoroutine(FadeOutAndHideRoutine(onComplete));
    }

    private IEnumerator FadeOutAndHideRoutine(Action onComplete)
    {
        yield return FadeTo(0f);
        _presentRoutine = null;
        HideImmediate();
        onComplete?.Invoke();
    }

    // ✅ 손님 데이터에서 대사 리스트 통째로 주입
    public void StartDialogue(List<DialogueLine> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            HideImmediate();
            return;
        }

        _queue.Clear();
        for (int i = 0; i < lines.Count; i++)
            _queue.Enqueue(lines[i]);

        Open();
        ShowNextLineFromQueue();
    }

    // episode 씬용: 한 줄만 직접 표시
    public void ShowSingleLine(DialogueLine line)
    {
        if (line == null)
        {
            HideImmediate();
            return;
        }

        _queue.Clear();
        Open();
        ShowLine(line);
    }

    public void ShowSingleLine(string speakerName, string text, Color nameColor)
    {
        ShowSingleLine(new DialogueLine
        {
            speakerName = speakerName,
            text = text,
            nameColor = nameColor
        });
    }

    // ✅ 클릭/스페이스로 호출
    public void Advance()
    {
        // 1) 타이핑 중이면 스킵
        if (SkipTypingIfNeeded())
            return;

        // 2) 다음 줄 있으면 다음 줄
        if (_queue.Count > 0)
        {
            ShowNextLineFromQueue();
            return;
        }

        // 3) 없으면 닫기
        DialogueClosed?.Invoke();
        HideImmediate();
    }

    // episode runner가 직접 호출할 수 있도록 분리
    public bool SkipTypingIfNeeded()
    {
        if (!_isTyping) return false;

        // 아직 이전 말풍선이 사라지는 중이면 새 내용이 들어가는 순간 전체 문장을 바로 보이게 예약한다.
        if (!_contentApplied)
        {
            _skipRequested = true;
            return true;
        }

        StopRoutine(ref _presentRoutine);
        bodyText.text = _fullLine;
        _isTyping = false;
        SetNextIndicator(true);
        return true;
    }

    private void Open()
    {
        _isOpen = true;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private void ShowNextLineFromQueue()
    {
        if (_queue.Count == 0)
        {
            DialogueClosed?.Invoke();
            HideImmediate();
            return;
        }

        var line = _queue.Dequeue();
        ShowLine(line);
    }

    // 배치는 대사 시작 시점에 묻는다 — 리졸버가 이때 카메라 이동을 시작하므로, 페이드 아웃되는 동안
    // 카메라가 먼저 움직이고 새 말풍선은 최종 위치에 바로 나타난다.
    private void ShowLine(DialogueLine line)
    {
        StopRoutine(ref _presentRoutine);
        SetNextIndicator(false);

        DialogueBubblePlacement placement = _placementResolver != null
            ? _placementResolver.Resolve(line)
            : DialogueBubblePlacement.Centered(DialogueBubbleStyle.Offstage);

        _fullLine = line.text ?? string.Empty;
        _isTyping = true;
        _contentApplied = false;
        _skipRequested = false;
        _presentRoutine = StartCoroutine(PresentLine(line, placement));
    }

    private IEnumerator PresentLine(DialogueLine line, DialogueBubblePlacement placement)
    {
        if (canvasGroup.alpha > 0.001f && fadeDuration > 0f)
        {
            StopRoutine(ref _fadeRoutine);
            yield return FadeTo(0f);
        }

        nameText.text = line.speakerName;
        nameText.color = line.nameColor;
        bodyText.text = string.Empty;
        ApplyLayout(placement);
        _contentApplied = true;

        StopRoutine(ref _fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeInRoutine());

        if (_skipRequested)
        {
            bodyText.text = _fullLine;
            _isTyping = false;
            _presentRoutine = null;
            SetNextIndicator(true);
            yield break;
        }

        yield return TypeLine(_fullLine);
        _presentRoutine = null;
    }

    // 세로: 위 여백 + 이름 줄 + 본문(전체 문장 높이) + 다음 표시 줄 + 아래 여백. 가로는 항상 bubbleWidth.
    // 캐릭터 말풍선은 윗변(화살표가 붙는 쪽)을, 주인공·화면 밖 화자는 아랫변을 고정해 반대쪽으로 늘어난다.
    private void ApplyLayout(DialogueBubblePlacement placement)
    {
        if (bubbleRect == null)
            return;

        float textWidth = Mathf.Max(1f, bubbleWidth - textSideMargin * 2f);
        float textHeight = Mathf.Ceil(bodyText.GetPreferredValues(_fullLine, textWidth, 0f).y);
        float height = topPadding + nameRowHeight + textHeight + indicatorRowHeight + bottomPadding;
        bool anchoredTop = placement.Style == DialogueBubbleStyle.Character;

        bubbleRect.anchorMin = new Vector2(0.5f, 0.5f);
        bubbleRect.anchorMax = new Vector2(0.5f, 0.5f);
        bubbleRect.pivot = new Vector2(0.5f, anchoredTop ? 1f : 0f);
        bubbleRect.sizeDelta = new Vector2(bubbleWidth, height);
        _basePosition = new Vector2(
            ClampToPanel(placement.ScreenX),
            anchoredTop ? characterBubbleTopY : protagonistBubbleBottomY);
        StopRoutine(ref _slideRoutine);
        bubbleRect.anchoredPosition = _basePosition;

        SetTopBand(nameText.rectTransform, topPadding, nameRowHeight, nameLeftMargin, nameLeftMargin);
        SetTopBand(bodyText.rectTransform, topPadding + nameRowHeight, textHeight, textSideMargin, textSideMargin);

        if (nextIndicator != null)
        {
            RectTransform indicator = nextIndicator.rectTransform;
            indicator.anchorMin = new Vector2(1f, 0f);
            indicator.anchorMax = new Vector2(1f, 0f);
            indicator.pivot = new Vector2(1f, 0.5f);
            indicator.anchoredPosition = new Vector2(-indicatorRightMargin, bottomPadding + indicatorRowHeight * 0.5f);
        }

        PlaceArrow(arrowFront, placement.Style == DialogueBubbleStyle.Character, top: true);
        PlaceArrow(arrowBack, placement.Style == DialogueBubbleStyle.Protagonist, top: false);
    }

    // 말풍선 전체가 패널(QHD) 안에 남도록 중심 X를 제한한다. 맨 끝 슬롯에서는 슬롯 중심과 살짝 어긋날 수 있다.
    private float ClampToPanel(float screenX)
    {
        RectTransform panel = bubbleRect.parent as RectTransform;
        if (panel == null)
            return screenX;

        float limit = Mathf.Max(0f, panel.rect.width * 0.5f - bubbleWidth * 0.5f);
        return Mathf.Clamp(screenX, -limit, limit);
    }

    // 말풍선 윗변에서 top만큼 내려온 위치에 height 높이의 가로 띠로 배치한다(좌우 여백 지정).
    private static void SetTopBand(RectTransform rect, float top, float height, float left, float right)
    {
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -(top + height));
        rect.offsetMax = new Vector2(-right, -top);
    }

    // 화살표는 말풍선 바깥쪽에 붙인다 — 위쪽 화살표는 윗변 위에, 아래쪽 화살표는 아랫변 아래에.
    private static void PlaceArrow(RectTransform arrow, bool visible, bool top)
    {
        if (arrow == null)
            return;

        arrow.gameObject.SetActive(visible);
        if (!visible)
            return;

        float edge = top ? 1f : 0f;
        arrow.anchorMin = new Vector2(0.5f, edge);
        arrow.anchorMax = new Vector2(0.5f, edge);
        arrow.pivot = new Vector2(0.5f, top ? 0f : 1f);
        arrow.anchoredPosition = Vector2.zero;
    }

    private IEnumerator FadeInRoutine()
    {
        yield return FadeTo(1f);
        _fadeRoutine = null;
    }

    private IEnumerator FadeTo(float target)
    {
        float start = canvasGroup.alpha;
        if (fadeDuration <= 0f || Mathf.Approximately(start, target))
        {
            canvasGroup.alpha = target;
            yield break;
        }

        float duration = fadeDuration * Mathf.Abs(target - start);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        canvasGroup.alpha = target;
    }

    private IEnumerator TypeLine(string line)
    {
        bodyText.text = "";
        int printedVisibleChars = 0; // 실제로 보이는 글자만 카운트
        int i = 0;

        while (i < line.Length)
        {
            char c = line[i];

            // ✅ 리치텍스트 태그 처리: <...> 는 통째로 붙이고 넘어감 (딜레이/사운드 없음)
            if (c == '<')
            {
                int close = line.IndexOf('>', i);
                if (close == -1)
                {
                    // 태그가 깨진 경우: 그냥 문자로 처리
                    bodyText.text += c;
                    i++;
                }
                else
                {
                    string tag = line.Substring(i, close - i + 1);
                    bodyText.text += tag;
                    i = close + 1;
                }

                // 태그 붙인 뒤 즉시 다음 루프로 (yield 없음)
                continue;
            }

            // 일반 문자 출력
            bodyText.text += c;
            i++;

            // 공백/줄바꿈은 사운드 제외 (원하면 포함 가능)
            if (c != ' ' && c != '\n')
            {
                printedVisibleChars++;

                if (printedVisibleChars % soundEveryNChars == 0 && typeClip != null && sfxSource != null)
                {
                    sfxSource.pitch = 1f + UnityEngine.Random.Range(-randomPitchRange, randomPitchRange);
                    sfxSource.PlayOneShot(typeClip, 0.6f);
                }
            }

            yield return new WaitForSeconds(charDelay);
        }

        _isTyping = false;
        SetNextIndicator(true);
    }

    public void SlideUpForChoices(float amount, Action onComplete = null)
    {
        if (bubbleRect == null) { onComplete?.Invoke(); return; }

        StopRoutine(ref _slideRoutine);
        Vector2 target = _basePosition + new Vector2(0f, amount);
        _slideRoutine = StartCoroutine(SlideRoutine(bubbleRect.anchoredPosition, target, onComplete));
    }

    public void SlideBackToOrigin(Action onComplete = null)
    {
        if (bubbleRect == null) { onComplete?.Invoke(); return; }

        StopRoutine(ref _slideRoutine);
        _slideRoutine = StartCoroutine(SlideRoutine(bubbleRect.anchoredPosition, _basePosition, onComplete));
    }

    private IEnumerator SlideRoutine(Vector2 from, Vector2 to, Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / slideDuration);
            t = t * t * (3f - 2f * t); // smoothstep
            bubbleRect.anchoredPosition = Vector2.Lerp(from, to, t);
            yield return null;
        }
        bubbleRect.anchoredPosition = to;
        _slideRoutine = null;
        onComplete?.Invoke();
    }

    public void SetNextHintVisible(bool visible)
    {
        SetNextIndicator(visible);
    }

    // 타이핑이 끝나 다음으로 넘어갈 수 있는 상태임을 알리는 깜빡이 인디케이터를 켜고 끈다.
    private void SetNextIndicator(bool on)
    {
        if (nextIndicator == null) return;

        nextIndicator.gameObject.SetActive(on);
        StopRoutine(ref _blinkRoutine);

        if (on)
            _blinkRoutine = StartCoroutine(BlinkIndicator());
    }

    private IEnumerator BlinkIndicator()
    {
        nextIndicator.enabled = true;
        while (true)
        {
            nextIndicator.enabled = !nextIndicator.enabled;
            yield return new WaitForSeconds(blinkInterval);
        }
    }

    private void StopRoutine(ref Coroutine routine)
    {
        if (routine == null)
            return;
        StopCoroutine(routine);
        routine = null;
    }
}
