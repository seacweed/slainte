using System;
using System.Collections.Generic;
using UnityEngine;

// 현재 씬에 등장 중인 캐릭터 뷰들을 슬롯(Center/Left/Right...)에 배치·유지·정리하는 무대.
// ShowCharacters()는 기존에 이미 떠 있는 캐릭터는 스프라이트만 교체하고, 새로 등장한 캐릭터만
// 빈 슬롯에 스폰해 매번 전체를 다시 그리지 않는다. 이번 목록에 없는 캐릭터는 사라짐
// 애니메이션 후 파괴된다.
public class CharacterStage : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private CharacterView characterPrefab;

    [Header("Database")]
    [SerializeField] private CharacterDatabase characterDB;

    [Header("Slots (Center, Left, Right, Left2, Right2...)")]
    [SerializeField] private List<RectTransform> slots = new();

    [Header("Front Layer Container (above bar table)")]
    [SerializeField] private RectTransform frontContainer;

    private readonly Dictionary<string, CharacterView> _activeViews      = new();
    private readonly Dictionary<string, int>           _activeSlotIndices = new();

    public void Clear()
    {
        foreach (var view in _activeViews.Values)
            if (view != null) Destroy(view.gameObject);

        _activeViews.Clear();
        _activeSlotIndices.Clear();
    }

    public bool TryGetDialogueIdentity(
        string characterKey,
        out string displayName,
        out Color nameColor)
    {
        CharacterData data = characterDB != null
            ? characterDB.FindByKey(characterKey)
            : null;
        if (data == null)
        {
            displayName = characterKey;
            nameColor = Color.white;
            return false;
        }

        displayName = string.IsNullOrWhiteSpace(data.displayName)
            ? characterKey
            : data.displayName;
        nameColor = data.nameColor;
        return true;
    }

    public void ShowCharacters(IReadOnlyList<CharacterSlotEntry> entries, Action onAllShown = null)
    {
        if (entries == null || entries.Count == 0)
        {
            ClearWithAnimation(onAllShown);
            return;
        }

        var newKeySet = new HashSet<string>();
        for (int i = 0; i < entries.Count; i++)
            if (entries[i] != null && !string.IsNullOrWhiteSpace(entries[i].characterKey))
                newKeySet.Add(entries[i].characterKey);

        // 이번에도 계속 등장하는 캐릭터가 차지한 슬롯은 빈 슬롯 큐에서 미리 제외해,
        // 새로 등장하는 캐릭터가 그 자리에 겹쳐 배치되지 않게 한다.
        var occupiedSlots = new HashSet<int>();
        foreach (var kvp in _activeSlotIndices)
            if (newKeySet.Contains(kvp.Key))
                occupiedSlots.Add(kvp.Value);

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

        var freeSlots = new Queue<int>();
        for (int i = 0; i < slots.Count; i++)
            if (!occupiedSlots.Contains(i))
                freeSlots.Enqueue(i);

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

            Sprite sprite             = data.GetSprite(entry.expressionKey);
            Sprite overlaySprite      = data.GetOverlaySprite(entry.expressionKey);
            Sprite blinkSprite        = data.GetBlinkSprite(entry.expressionKey);
            Sprite blinkOverlaySprite = data.GetBlinkOverlaySprite(entry.expressionKey);

            if (_activeViews.TryGetValue(entry.characterKey, out CharacterView existing))
            {
                existing.SwapSprite(sprite, overlaySprite, blinkSprite, blinkOverlaySprite);
            }
            else
            {
                int slotIndex;
                if (entry.slotIndex >= 0 && entry.slotIndex < slots.Count)
                {
                    slotIndex = entry.slotIndex;
                    freeSlots = RemoveFromQueue(freeSlots, slotIndex);
                }
                else
                {
                    if (freeSlots.Count == 0)
                    {
                        Debug.LogWarning($"[CharacterStage] No free slot for: {entry.characterKey}");
                        continue;
                    }
                    slotIndex = freeSlots.Dequeue();
                }

                CharacterView view = Instantiate(characterPrefab, slots[slotIndex]);
                _activeViews[entry.characterKey]       = view;
                _activeSlotIndices[entry.characterKey] = slotIndex;

                view.ApplySlotLayout(slots[slotIndex]);
                view.Setup(sprite, overlaySprite, blinkSprite, blinkOverlaySprite);
                view.AttachOverlayToFrontContainer(frontContainer);

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

        view.SwapSprite(
            data.GetSprite(expressionKey),
            data.GetOverlaySprite(expressionKey),
            data.GetBlinkSprite(expressionKey),
            data.GetBlinkOverlaySprite(expressionKey));
    }

    public float GetActiveGroupCenterWorldX()
    {
        if (_activeViews.Count == 0) return Screen.width * 0.5f;

        float minX = float.MaxValue;
        float maxX = float.MinValue;

        foreach (var kvp in _activeViews)
        {
            if (kvp.Value == null) continue;
            kvp.Value.GetVisualWorldBoundsX(out float left, out float right);
            if (left  < minX) minX = left;
            if (right > maxX) maxX = right;
        }

        return minX == float.MaxValue ? Screen.width * 0.5f : (minX + maxX) * 0.5f;
    }

    public bool TryGetActiveGroupScreenRect(out Rect screenRect)
    {
        screenRect = default;
        bool hasBounds = false;

        foreach (CharacterView view in _activeViews.Values)
        {
            if (view == null || !view.TryGetVisualScreenRect(out Rect viewRect))
                continue;

            if (!hasBounds)
            {
                screenRect = viewRect;
                hasBounds = true;
                continue;
            }

            screenRect = Rect.MinMaxRect(
                Mathf.Min(screenRect.xMin, viewRect.xMin),
                Mathf.Min(screenRect.yMin, viewRect.yMin),
                Mathf.Max(screenRect.xMax, viewRect.xMax),
                Mathf.Max(screenRect.yMax, viewRect.yMax));
        }

        return hasBounds;
    }

    private static Queue<int> RemoveFromQueue(Queue<int> queue, int value)
    {
        var result = new Queue<int>(queue.Count);
        foreach (int item in queue)
            if (item != value) result.Enqueue(item);
        return result;
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
