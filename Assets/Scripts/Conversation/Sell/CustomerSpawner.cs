using System.Collections.Generic;
using UnityEngine;

public class CustomerSpawner : MonoBehaviour
{
    [SerializeField] private CustomerOrderDatabase customerDB;
    [SerializeField] private CharacterStage        characterStage;
    [SerializeField] private DialogueController    dialogue;
    [SerializeField] private OrderTicketManager    ticketManager;

    private CustomerOrderData _currentOrderData;

    public void ShowCustomers(IReadOnlyList<string> orderKeys)
    {
        if (orderKeys == null || orderKeys.Count == 0) return;

        _currentOrderData = customerDB != null ? customerDB.FindByKey(orderKeys[0]) : null;
        if (_currentOrderData == null) return;

        if (!string.IsNullOrWhiteSpace(_currentOrderData.characterKey))
        {
            var entry = new CharacterSlotEntry
            {
                characterKey  = _currentOrderData.characterKey,
                expressionKey = _currentOrderData.expressionKeyMid
            };
            characterStage?.ShowCharacters(new[] { entry }, () => OnCharactersShown(orderKeys));
        }
        else
        {
            OnCharactersShown(orderKeys);
        }
    }

    public void ShowFeedbackExpression(bool isGood)
    {
        if (_currentOrderData == null) return;
        if (string.IsNullOrWhiteSpace(_currentOrderData.characterKey)) return;

        string expressionKey = isGood
            ? _currentOrderData.expressionKeyGood
            : _currentOrderData.expressionKeyBad;

        characterStage?.SwapExpression(_currentOrderData.characterKey, expressionKey);
    }

    public void Clear()
    {
        _currentOrderData = null;
        characterStage?.Clear();
    }

    private void OnCharactersShown(IReadOnlyList<string> keys)
    {
        if (keys == null || keys.Count == 0) return;

        CustomerOrderData data = customerDB != null ? customerDB.FindByKey(keys[0]) : null;
        if (data == null) return;

        if (data.lines != null && data.lines.Count > 0)
        {
            ticketManager?.Prepare(data.key);
            dialogue?.StartDialogue(data.lines);
        }
        else
        {
            dialogue?.HideImmediate();
        }
    }
}
