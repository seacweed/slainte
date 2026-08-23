using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 이미지+텍스트 슬라이드를 순서대로 재생하는 컷씬 플레이어.
// 슬라이드 하나: 이미지 페이드인 -> (텍스트 있으면) 한 글자씩 타이핑 -> 대기 -> 즉시 사라짐(다음 슬라이드로).
// 클릭/스페이스로 현재 슬라이드를 건너뛰고 바로 다음 슬라이드의 페이드인부터 시작할 수 있다.
public class CutsceneUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CanvasGroup slideGroup;
    [SerializeField] private Image slideImage;
    [SerializeField] private TMP_Text slideText;

    [Header("Timing")]
    [SerializeField] private float imageFadeInDuration = 1f;
    [SerializeField] private float charDelay = 0.03f;
    [SerializeField] private float postTypingHoldDuration = 3f;
    [SerializeField] private float entryHoldDuration = 0.3f;
    [SerializeField] private float exitHoldDuration = 0.3f;

    private Coroutine _playRoutine;
    private Action _onComplete;
    private List<CutsceneData.CutsceneSlide> _slides;
    private bool _isPlaying;
    private bool _skipRequested;

    void Awake()
    {
        gameObject.SetActive(false);
    }

    public void Play(CutsceneData data, Action onComplete)
    {
        if (data == null || data.slides == null || data.slides.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        if (_playRoutine != null)
            StopCoroutine(_playRoutine);

        _slides = data.slides;
        _onComplete = onComplete;
        gameObject.SetActive(true);
        _playRoutine = StartCoroutine(PlayRoutine());
    }

    void Update()
    {
        if (!_isPlaying) return;
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            _skipRequested = true;
    }

    private IEnumerator PlayRoutine()
    {
        _isPlaying = true;

        // 에디터에 남아있던 이미지/텍스트가 잠깐이라도 보이지 않도록 먼저 완전히 비운 뒤,
        // 화면이 까맣게 덮인 채로 잠깐 멈췄다가 첫 슬라이드를 시작한다.
        ResetSlideVisuals();
        yield return Wait(entryHoldDuration);

        for (int i = 0; i < _slides.Count; i++)
        {
            _skipRequested = false;
            yield return PlaySlide(_slides[i]);
        }

        // 스킵으로 마지막 슬라이드가 도중에 끊겼더라도 확실히 비운 뒤,
        // 까만 화면 상태로 잠깐 멈췄다가 컷씬을 닫는다.
        ResetSlideVisuals();
        yield return Wait(exitHoldDuration);

        _isPlaying = false;
        _playRoutine = null;

        // 여기서 바로 SetActive(false)로 감추지 않는다. onComplete가 보통 다음 화면으로의
        // SceneTransitionManager 페이드를 시작시키는데, 그 페이드가 완전히 까매지기까지는
        // 시간이 걸려서 그 틈에 이 패널 뒤(예: 메인메뉴)가 잠깐 노출된다. 대신 이 패널은
        // (여전히 까만 채로) 그대로 켜둔 채 onComplete를 호출하고, 새 화면이 완전히 덮인
        // 뒤(onFadeOutComplete 시점)에 호출자가 HideImmediate()로 감추게 한다.
        Action callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

    // 다음 화면(SceneTransitionManager)이 완전히 화면을 덮은 뒤 호출자가 호출한다.
    // 재생 중이면 무시(다른 컷씬이 이미 다시 시작된 경우 등에 대한 안전장치).
    public void HideImmediate()
    {
        if (_isPlaying) return;
        gameObject.SetActive(false);
    }

    private void ResetSlideVisuals()
    {
        slideGroup.alpha = 0f;
        slideImage.enabled = false;
        slideText.text = string.Empty;
    }

    private IEnumerator PlaySlide(CutsceneData.CutsceneSlide slide)
    {
        slideImage.sprite = slide.image;
        slideImage.enabled = slide.image != null;
        slideText.text = string.Empty;
        slideGroup.alpha = 0f;

        yield return RunSkippable(FadeIn());
        if (_skipRequested) yield break;

        if (!string.IsNullOrEmpty(slide.text))
        {
            yield return RunSkippable(TypeText(slide.text));
            if (_skipRequested) yield break;
        }

        yield return RunSkippable(Wait(postTypingHoldDuration));
        if (_skipRequested) yield break;

        slideGroup.alpha = 0f;
    }

    // 내부 단계 하나를 실행하다가 스킵 요청이 들어오면 그 자리에서 즉시 중단한다.
    private IEnumerator RunSkippable(IEnumerator step)
    {
        while (!_skipRequested && step.MoveNext())
            yield return step.Current;
    }

    private IEnumerator FadeIn()
    {
        if (imageFadeInDuration <= 0f)
        {
            slideGroup.alpha = 1f;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < imageFadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            slideGroup.alpha = Mathf.Clamp01(elapsed / imageFadeInDuration);
            yield return null;
        }
        slideGroup.alpha = 1f;
    }

    // DialogueController.TypeLine과 동일한 방식으로 <...> 리치텍스트 태그는 통째로 붙이고 넘어간다.
    private IEnumerator TypeText(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            if (c == '<')
            {
                int close = text.IndexOf('>', i);
                if (close == -1)
                {
                    slideText.text += c;
                    i++;
                }
                else
                {
                    slideText.text += text.Substring(i, close - i + 1);
                    i = close + 1;
                }
                continue;
            }

            slideText.text += c;
            i++;
            yield return new WaitForSecondsRealtime(charDelay);
        }
    }

    private IEnumerator Wait(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}
