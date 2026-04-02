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

    private readonly List<CharacterView> _spawned = new();

    public void Clear()
    {
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i] != null) Destroy(_spawned[i].gameObject);

        _spawned.Clear();
    }

    public void ShowCharacters(IReadOnlyList<string> keys, Action onAllShown = null)
    {
        Clear();

        if (keys == null || keys.Count == 0)
        {
            onAllShown?.Invoke();
            return;
        }

        int count   = Mathf.Min(keys.Count, slots.Count);
        int pending = 0;

        for (int i = 0; i < count; i++)
        {
            CharacterData data = characterDB != null ? characterDB.FindByKey(keys[i]) : null;
            if (data == null)
            {
                Debug.LogWarning($"[CharacterStage] Key not found in DB: {keys[i]}");
                continue;
            }

            CharacterView view = Instantiate(characterPrefab, slots[i]);
            _spawned.Add(view);

            view.ApplySlotLayout(slots[i]);
            view.Setup(data.sprite);

            pending++;
            view.PlayAppearAnimation(() =>
            {
                pending--;
                if (pending <= 0)
                    onAllShown?.Invoke();
            });
        }

        if (pending == 0)
            onAllShown?.Invoke();
    }
}
