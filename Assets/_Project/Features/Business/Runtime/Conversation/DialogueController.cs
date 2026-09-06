using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System;

// 대사창 하나를 담당하는 컨트롤러. 대사 큐(_queue)를 한 줄씩 타이핑 애니메이션(TypeLine)으로
// 보여주고, Advance() 호출(클릭/스페이스)마다 "타이핑 중이면 스킵 → 다음 줄 있으면 다음 줄 →
// 없으면 닫기" 순으로 진행한다. 선택지 UI가 뜨는 동안은 SlideUpForChoices로 말풍선을 위로
// 밀어 겹치지 않게 한다.
public class DialogueController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text bodyText;

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
    private Coroutine _typingRoutine;
    private Coroutine _blinkRoutine;
    private Coroutine _slideRoutine;
    private bool _isTyping;
    private string _fullLine;
    private Vector2 _originalBubblePos;

    public bool IsTyping => _isTyping;
    public bool IsOpen => canvasGroup != null && canvasGroup.alpha > 0.001f;

    public event Action DialogueClosed;

    void Awake()
    {
        if (bubbleRect != null)
            _originalBubblePos = bubbleRect.anchoredPosition;
        HideImmediate();
    }

    public void HideImmediate()
    {
        if (_typingRoutine != null) { StopCoroutine(_typingRoutine); _typingRoutine = null; }
        if (_blinkRoutine != null) { StopCoroutine(_blinkRoutine); _blinkRoutine = null; }
        if (_slideRoutine != null) { StopCoroutine(_slideRoutine); _slideRoutine = null; }
        if (bubbleRect != null) bubbleRect.anchoredPosition = _originalBubblePos;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        nameText.text = "";
        bodyText.text = "";
        _queue.Clear();
        _isTyping = false;
        SetNextIndicator(false);
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

        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

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
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
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

        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        bodyText.text = _fullLine;
        _isTyping = false;
        SetNextIndicator(true);
        return true;
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

    private void ShowLine(DialogueLine line)
    {
        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        SetNextIndicator(false);

        nameText.text = line.speakerName;
        nameText.color = line.nameColor;
        _typingRoutine = StartCoroutine(TypeLine(line.text));
    }

    private IEnumerator TypeLine(string line)
    {
        _isTyping = true;
        _fullLine = line;

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
        _typingRoutine = null;
        SetNextIndicator(true);
    }

    public void SlideUpForChoices(float amount, Action onComplete = null)
    {
        if (bubbleRect == null) { onComplete?.Invoke(); return; }

        if (_slideRoutine != null) StopCoroutine(_slideRoutine);
        Vector2 target = _originalBubblePos + new Vector2(0f, amount);
        _slideRoutine = StartCoroutine(SlideRoutine(bubbleRect.anchoredPosition, target, onComplete));
    }

    public void SlideBackToOrigin(Action onComplete = null)
    {
        if (bubbleRect == null) { onComplete?.Invoke(); return; }

        if (_slideRoutine != null) StopCoroutine(_slideRoutine);
        _slideRoutine = StartCoroutine(SlideRoutine(bubbleRect.anchoredPosition, _originalBubblePos, onComplete));
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

        if (_blinkRoutine != null)
        {
            StopCoroutine(_blinkRoutine);
            _blinkRoutine = null;
        }

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
}
