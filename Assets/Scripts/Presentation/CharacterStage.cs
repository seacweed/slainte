using System;
using System.Collections.Generic;
using UnityEngine;

public class CharacterStage : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private CharacterView characterPrefab;

    [Header("Database")]
    [SerializeField] private CharacterDatabase characterDB;

    [Header("Slots (Center, Left, Right, Left2, Right2...)")]
    [SerializeField] private List<RectTransform> slots = new();

    private readonly Dictionary<string, CharacterView> _activeViews      = new();
    private readonly Dictionary<string, int>           _activeSlotIndices = new();

    public void Clear()
    {
        foreach (var view in _activeViews.Values)
            if (view != null) Destroy(view.gameObject);

        _activeViews.Clear();
        _activeSlotIndices.Clear();
    }

    public void ShowCharacters(IReadOnlyList<CharacterSlotEntry> entries, Action onAllShown = null)
    {
        if (entries == null || entries.Count == 0)
        {
            ClearWithAnimation(onAllShown);
            return;
        }

        // Keys that should remain visible after this call
        var newKeySet = new HashSet<string>();
        for (int i = 0; i < entries.Count; i++)
            if (entries[i] != null && !string.IsNullOrWhiteSpace(entries[i].characterKey))
                newKeySet.Add(entries[i].characterKey);

        // Slots occupied by characters that are staying — must not be overwritten
        var occupiedSlots = new HashSet<int>();
        foreach (var kvp in _activeSlotIndices)
            if (newKeySet.Contains(kvp.Key))
                occupiedSlots.Add(kvp.Value);

        // Exit characters that are no longer needed
        var toRemove = new List<string>();
        foreach (var key in _activeViews.Keys)
            if (!newKeySet.Contains(key)) toRemove.Add(key);

        foreach (var key in toRemove)
        {
            var view = _activeViews[key];
            _activeViews.Remove(key);
            _activeSlotIndices.Remove(key);
            if (view != null)
            {
                var captured = view;
                captured.PlayDisappearAnimation(() => Destroy(captured.gameObject));
            }
        }

        // Build queue of free slot indices in ascending order
        var freeSlots = new Queue<int>();
        for (int i = 0; i < slots.Count; i++)
            if (!occupiedSlots.Contains(i))
                freeSlots.Enqueue(i);

        // Process entries
        int pending = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            CharacterSlotEntry entry = entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.characterKey)) continue;

            CharacterData data = characterDB != null ? characterDB.FindByKey(entry.characterKey) : null;
            if (data == null)
            {
                Debug.LogWarning($"[CharacterStage] Key not found in DB: {entry.characterKey}");
                continue;
            }

            Sprite sprite = data.GetSprite(entry.expressionKey);

            if (_activeViews.TryGetValue(entry.characterKey, out CharacterView existing))
            {
                // Already on stage: swap sprite only, slot unchanged
                existing.SwapSprite(sprite);
            }
            else
            {
                // New character: assign first available free slot
                if (freeSlots.Count == 0)
                {
                    Debug.LogWarning($"[CharacterStage] No free slot for: {entry.characterKey}");
                    continue;
                }

                int slotIndex = freeSlots.Dequeue();
                CharacterView view = Instantiate(characterPrefab, slots[slotIndex]);
                _activeViews[entry.characterKey]       = view;
                _activeSlotIndices[entry.characterKey] = slotIndex;

                view.ApplySlotLayout(slots[slotIndex]);
                view.Setup(sprite);

                pending++;
                view.PlayAppearAnimation(() =>
                {
                    pending--;
                    if (pending <= 0)
                        onAllShown?.Invoke();
                });
            }
        }

        if (pending == 0)
            onAllShown?.Invoke();
    }

    public void SwapExpression(string characterKey, string expressionKey)
    {
        if (!_activeViews.TryGetValue(characterKey, out CharacterView view)) return;

        CharacterData data = characterDB != null ? characterDB.FindByKey(characterKey) : null;
        if (data == null) return;

        view.SwapSprite(data.GetSprite(expressionKey));
    }

    private void ClearWithAnimation(Action onComplete)
    {
        if (_activeViews.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int pending = _activeViews.Count;

        foreach (var pair in _activeViews)
        {
            CharacterView view = pair.Value;
            if (view != null)
            {
                view.PlayDisappearAnimation(() =>
                {
                    Destroy(view.gameObject);
                    pending--;
                    if (pending <= 0)
                    {
                        _activeViews.Clear();
                        _activeSlotIndices.Clear();
                        onComplete?.Invoke();
                    }
                });
            }
            else
            {
                pending--;
                if (pending <= 0)
                {
                    _activeViews.Clear();
                    _activeSlotIndices.Clear();
                    onComplete?.Invoke();
                }
            }
        }
    }
}
