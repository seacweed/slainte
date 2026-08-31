using System.Collections;
using Slainte.Content;
using Slainte.Shared.Lifecycle;
using UnityEngine;

public class AudioManager : MonoSingleton<AudioManager>
{
    [Header("BGM")]
    [SerializeField] private AudioSource bgmSourceA;
    [SerializeField] private AudioSource bgmSourceB;
    [SerializeField] private float defaultFadeDuration = 1.5f;

    [Header("SFX")]
    [SerializeField] private AudioSource sfxSource;

    private AudioSource _activeSource;
    private AudioSource _inactiveSource;
    private Coroutine   _fadeRoutine;

    protected override void Awake()
    {
        base.Awake();

        if (bgmSourceA == null) bgmSourceA = CreateAudioSource("BGM_A", loop: true);
        if (bgmSourceB == null) bgmSourceB = CreateAudioSource("BGM_B", loop: true);
        if (sfxSource == null) sfxSource = CreateAudioSource("SFX", loop: false);

        _activeSource   = bgmSourceA;
        _inactiveSource = bgmSourceB;
    }

    // BGM(크로스페이드 채널)과 완전히 독립된 소스에서 원샷으로 재생한다.
    // 루프하지 않고, 다른 SFX/BGM과 서로 끊거나 멈추지 않는다.
    public void PlaySfx(string clipName, float volume = 1f)
    {
        if (string.IsNullOrWhiteSpace(clipName)) return;

        AudioClip clip = Resources.Load<AudioClip>(
            $"{ProjectResourcePaths.CoreSfx}/{clipName}");
        if (clip == null)
        {
            Debug.LogWarning(
                $"[AudioManager] SFX clip not found: Resources/"
                + $"{ProjectResourcePaths.CoreSfx}/{clipName}");
            return;
        }

        sfxSource.PlayOneShot(clip, volume);
    }

    public void PlayBgm(string clipName, float fadeDuration = -1f)
    {
        if (string.IsNullOrWhiteSpace(clipName)) return;

        AudioClip clip = Resources.Load<AudioClip>(
            $"{ProjectResourcePaths.CoreBgm}/{clipName}");
        if (clip == null)
        {
            Debug.LogWarning(
                $"[AudioManager] BGM clip not found: Resources/"
                + $"{ProjectResourcePaths.CoreBgm}/{clipName}");
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

    private AudioSource CreateAudioSource(string sourceName, bool loop)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform);
        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop        = loop;
        return src;
    }
}
