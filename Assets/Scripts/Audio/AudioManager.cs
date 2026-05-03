using System.Collections;
using UnityEngine;

public class AudioManager : MonoSingleton<AudioManager>
{
    [Header("BGM")]
    [SerializeField] private AudioSource bgmSourceA;
    [SerializeField] private AudioSource bgmSourceB;
    [SerializeField] private float defaultFadeDuration = 1.5f;

    private AudioSource _activeSource;
    private AudioSource _inactiveSource;
    private Coroutine   _fadeRoutine;

    protected override void Awake()
    {
        base.Awake();

        if (bgmSourceA == null) bgmSourceA = CreateAudioSource("BGM_A");
        if (bgmSourceB == null) bgmSourceB = CreateAudioSource("BGM_B");

        _activeSource   = bgmSourceA;
        _inactiveSource = bgmSourceB;
    }

    public void PlayBgm(string clipName, float fadeDuration = -1f)
    {
        if (string.IsNullOrWhiteSpace(clipName)) return;

        AudioClip clip = Resources.Load<AudioClip>($"BGM/{clipName}");
        if (clip == null)
        {
            Debug.LogWarning($"[AudioManager] BGM clip not found: Resources/BGM/{clipName}");
            return;
        }

        float duration = fadeDuration < 0f ? defaultFadeDuration : fadeDuration;

        if (_activeSource.clip == clip && _activeSource.isPlaying) return;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(CrossfadeRoutine(clip, duration));
    }

    public void StopBgm(float fadeDuration = -1f)
    {
        float duration = fadeDuration < 0f ? defaultFadeDuration : fadeDuration;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeOutRoutine(_activeSource, duration));
    }

    private IEnumerator CrossfadeRoutine(AudioClip clip, float duration)
    {
        _inactiveSource.clip   = clip;
        _inactiveSource.volume = 0f;
        _inactiveSource.loop   = true;
        _inactiveSource.Play();

        float elapsed      = 0f;
        float startVolume  = _activeSource.volume;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _inactiveSource.volume = t;
            _activeSource.volume   = startVolume * (1f - t);
            yield return null;
        }

        _activeSource.Stop();
        _activeSource.clip   = null;
        _activeSource.volume = 1f;

        (_activeSource, _inactiveSource) = (_inactiveSource, _activeSource);
        _fadeRoutine = null;
    }

    private IEnumerator FadeOutRoutine(AudioSource source, float duration)
    {
        float startVolume = source.volume;
        float elapsed     = 0f;

        while (elapsed < duration)
        {
            elapsed        += Time.deltaTime;
            source.volume   = Mathf.Lerp(startVolume, 0f, elapsed / duration);
            yield return null;
        }

        source.Stop();
        source.clip   = null;
        source.volume = 1f;
        _fadeRoutine  = null;
    }

    private AudioSource CreateAudioSource(string sourceName)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform);
        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop        = true;
        return src;
    }
}
