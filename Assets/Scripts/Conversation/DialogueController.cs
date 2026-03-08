using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using System;

public class DialogueController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text bodyText;

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
    private bool _isTyping;
    private string _fullLine;

    public event Action DialogueClosed;

    void Awake()
    {
        HideImmediate();
    }

    public void HideImmediate()
    {
        if (_typingRoutine != null) { StopCoroutine(_typingRoutine); _typingRoutine = null; }
        if (_blinkRoutine != null) { StopCoroutine(_blinkRoutine); _blinkRoutine = null; }
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

        ShowNextLine();
    }

    // ✅ 클릭/스페이스로 호출
    public void Advance()
    {
        // 1) 타이핑 중이면 스킵
        if (_isTyping)
        {
            if (_typingRoutine != null) StopCoroutine(_typingRoutine);
            bodyText.text = _fullLine;
            _isTyping = false;
            SetNextIndicator(true);
            return;
        }

        // 2) 다음 줄 있으면 다음 줄
        if (_queue.Count > 0)
        {
            ShowNextLine();
            return;
        }

        // 3) 없으면 닫기
        DialogueClosed?.Invoke();
        HideImmediate();
    }

    private void ShowNextLine()
    {
        if (_typingRoutine != null) StopCoroutine(_typingRoutine);

        if (_queue.Count == 0)
        {
            DialogueClosed?.Invoke();
            HideImmediate();
            return;
        }

        SetNextIndicator(false);

        var line = _queue.Dequeue();
        nameText.text = line.speakerName;
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
        SetNextIndicator(true);
    }

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
        while (true)
        {
            nextIndicator.enabled = !nextIndicator.enabled;
            yield return new WaitForSeconds(blinkInterval);
        }
    }
}
