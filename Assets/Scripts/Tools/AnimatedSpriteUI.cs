using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class AnimatedSpriteUI : MonoBehaviour
{
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float fps = 12f;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool playOnAwake = true;

    private Image _image;
    private Coroutine _playCoroutine;

    private void Awake()
    {
        _image = GetComponent<Image>();
    }

    private void Start()
    {
        if (playOnAwake && frames != null && frames.Length > 0)
            Play();
    }

    public void SetFrames(Sprite[] newFrames, float newFps = -1f)
    {
        frames = newFrames;
        if (newFps > 0f) fps = newFps;
    }

    public void Play()
    {
        Stop();
        if (frames == null || frames.Length == 0) return;
        _playCoroutine = StartCoroutine(PlayRoutine());
    }

    public void Stop()
    {
        if (_playCoroutine == null) return;
        StopCoroutine(_playCoroutine);
        _playCoroutine = null;
    }

    private IEnumerator PlayRoutine()
    {
        float interval = fps > 0f ? 1f / fps : 0.083f;

        if (loop)
        {
            int index = 0;
            while (true)
            {
                _image.sprite = frames[index];
                yield return new WaitForSeconds(interval);
                index = (index + 1) % frames.Length;
            }
        }
        else
        {
            for (int i = 0; i < frames.Length; i++)
            {
                _image.sprite = frames[i];
                yield return new WaitForSeconds(interval);
            }
            _playCoroutine = null;
        }
    }

    private void OnDisable()
    {
        Stop();
    }
}
