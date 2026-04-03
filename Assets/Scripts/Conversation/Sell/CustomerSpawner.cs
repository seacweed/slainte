using System.Collections.Generic;
using UnityEngine;

public class CustomerSpawner : MonoBehaviour
{
    [SerializeField] private CustomerOrderDatabase customerDB;
    [SerializeField] private CharacterStage        characterStage;
    [SerializeField] private DialogueController    dialogue;
    [SerializeField] private OrderTicketManager    ticketManager;

    public void ShowCustomers(IReadOnlyList<string> orderKeys)
    {
        if (orderKeys == null || orderKeys.Count == 0) return;

        CustomerOrderData data = customerDB != null ? customerDB.FindByKey(orderKeys[0]) : null;
        if (data == null) return;

        string characterKey = string.IsNullOrWhiteSpace(data.characterKeyMid)
            ? null
            : data.characterKeyMid;

        if (characterKey != null)
            characterStage?.ShowCharacters(new[] { characterKey }, () => OnCharactersShown(orderKeys));
        else
            OnCharactersShown(orderKeys);
    }

    public void Clear()
    {
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

    private void ShowCharacterByKey(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey)) return;
        characterStage?.ShowCharacters(new[] { characterKey });
    }
}
