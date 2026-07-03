using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NotificationManager : MonoSingleton<NotificationManager>
{
    [SerializeField] private AffinityNotificationUI notificationPrefab;
    [SerializeField] private Transform container;
    [SerializeField] private CharacterDatabase characterDB;

    private readonly Queue<(string displayName, int delta)> _queue = new();
    private bool _isShowing;

    private void OnEnable()
    {
        GameProgress.OnAffinityChanged += OnAffinityChanged;
    }

    private void OnDisable()
    {
        GameProgress.OnAffinityChanged -= OnAffinityChanged;
    }

    private void OnAffinityChanged(string varName, int delta)
    {
        if (characterDB == null) return;
        CharacterData ch = characterDB.FindByKey(varName);
        if (ch == null) return;

        string displayName = string.IsNullOrWhiteSpace(ch.displayName) ? varName : ch.displayName;
        _queue.Enqueue((displayName, delta));

        if (!_isShowing)
            StartCoroutine(ShowNext());
    }

    private IEnumerator ShowNext()
    {
        _isShowing = true;

        while (_queue.Count > 0)
        {
            var (displayName, delta) = _queue.Dequeue();

            AffinityNotificationUI notification = Instantiate(notificationPrefab, container);
            notification.Setup(displayName, delta);

            bool done = false;
            notification.PlayAndDestroy(() => done = true);

            while (!done)
                yield return null;
        }

        _isShowing = false;
    }
}
