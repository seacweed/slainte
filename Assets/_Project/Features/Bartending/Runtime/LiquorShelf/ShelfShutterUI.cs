using System;
using System.Collections;
using UnityEngine;

// 술장에서 배송 화면으로 들어갈 때만 재생되는 위/아래 셔터 연출(완전히 닫힘 -> 화면 전환 -> 살짝 열린 채로 정지).
// 배송 -> 술장 복귀는 애니메이션 없이 ResetImmediate()로 즉시 원위치(완전히 열린/숨겨진 위치)로 되돌린다.
public class ShelfShutterUI : MonoBehaviour
{
    [SerializeField] private RectTransform topShutter;
    [SerializeField] private RectTransform bottomShutter;
    [SerializeField] private Vector2 topOpenPosition;
    [SerializeField] private Vector2 bottomOpenPosition;
    [SerializeField] private Vector2 topClosedPosition;
    [SerializeField] private Vector2 bottomClosedPosition;
    [SerializeField] private Vector2 topPartialOpenPosition;
    [SerializeField] private Vector2 bottomPartialOpenPosition;
    [SerializeField, Min(0.01f)] private float closeDuration = 0.35f;
    [SerializeField, Min(0.01f)] private float openDuration = 0.35f;

    private Coroutine _co;

    private void Awake()
    {
        ResetImmediate();
    }

    // 완전히 닫힘 -> onScreenSwitch 호출(화면 내용 교체) -> 살짝 열린 상태로 정지
    public void PlayEnterDelivery(Action onScreenSwitch)
    {
        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(Run(onScreenSwitch));
    }

    // 배송 -> 술장 복귀 시 애니메이션 없이 즉시 완전히 열린(숨김) 위치로 되돌린다.
    public void ResetImmediate()
    {
        if (_co != null)
        {
            StopCoroutine(_co);
            _co = null;
        }

        if (topShutter != null) topShutter.anchoredPosition = topOpenPosition;
        if (bottomShutter != null) bottomShutter.anchoredPosition = bottomOpenPosition;
    }

    private IEnumerator Run(Action onScreenSwitch)
    {
        yield return SlideBoth(topClosedPosition, bottomClosedPosition, closeDuration);
        onScreenSwitch?.Invoke();
        yield return SlideBoth(topPartialOpenPosition, bottomPartialOpenPosition, openDuration);
        _co = null;
    }

    private IEnumerator SlideBoth(Vector2 topTarget, Vector2 bottomTarget, float duration)
    {
        Vector2 topFrom = topShutter != null ? topShutter.anchoredPosition : Vector2.zero;
        Vector2 bottomFrom = bottomShutter != null ? bottomShutter.anchoredPosition : Vector2.zero;

        float timer = 0f;
        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / duration;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer));
            if (topShutter != null) topShutter.anchoredPosition = Vector2.Lerp(topFrom, topTarget, t);
            if (bottomShutter != null) bottomShutter.anchoredPosition = Vector2.Lerp(bottomFrom, bottomTarget, t);
            yield return null;
        }

        if (topShutter != null) topShutter.anchoredPosition = topTarget;
        if (bottomShutter != null) bottomShutter.anchoredPosition = bottomTarget;
    }
}
