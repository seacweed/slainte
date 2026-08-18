using System.Collections.Generic;
using Slainte.Business;
using UnityEngine;

public class CustomerSpawner : MonoBehaviour
{
    [SerializeField] private CustomerOrderDatabase customerDB;
    [SerializeField] private CustomerVisitDatabase visitDB;
    [SerializeField] private CharacterStage        characterStage;
    [SerializeField] private DialogueController    dialogue;
    [SerializeField] private OrderTicketManager    ticketManager;

    private CustomerOrderData _currentOrderData;
    private CustomerVisitData _currentVisitData;

    public CustomerOrderData CurrentOrderData => _currentOrderData;
    public CustomerVisitData CurrentVisitData => _currentVisitData;

    private void Awake()
    {
        if (visitDB == null)
            visitDB = CustomerVisitDatabase.LoadDefault();
    }

    public void ShowCustomers(IReadOnlyList<string> orderKeys)
    {
        if (orderKeys == null || orderKeys.Count == 0) return;

        ShowVisit(string.Empty, orderKeys[0]);
    }

    public bool CanResolveOrder(string orderKey, out CustomerOrderData order)
    {
        order = customerDB != null ? customerDB.FindByKey(orderKey) : null;
        return order != null;
    }

    public bool ShowVisit(string visitKey, string orderKey)
    {
        CanResolveOrder(orderKey, out _currentOrderData);
        _currentVisitData = visitDB != null ? visitDB.FindByKey(visitKey) : null;
        if (_currentOrderData == null) return false;

        List<CharacterSlotEntry> entries = BuildVisitEntries(_currentVisitData);
        if (entries.Count > 0)
        {
            characterStage?.ShowCharacters(entries, () => OnCharactersShown(_currentOrderData));
            if (characterStage == null)
                OnCharactersShown(_currentOrderData);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_currentOrderData.characterKey))
        {
            var entry = new CharacterSlotEntry
            {
                characterKey  = _currentOrderData.characterKey,
                expressionKey = _currentOrderData.expressionKeyMid
            };
            characterStage?.ShowCharacters(new[] { entry }, () => OnCharactersShown(_currentOrderData));
            if (characterStage == null)
                OnCharactersShown(_currentOrderData);
        }
        else
        {
            OnCharactersShown(_currentOrderData);
        }

        return true;
    }

    public void ShowFeedbackExpression(bool isGood)
    {
        ApplyFeedbackExpressions(isGood ? OrderEvaluationGrade.Good : OrderEvaluationGrade.Bad);
    }

    public bool ShowFeedback(OrderEvaluationGrade grade)
    {
        if (_currentOrderData == null)
            return false;

        ApplyFeedbackExpressions(grade);

        List<DialogueLine> lines = grade switch
        {
            OrderEvaluationGrade.Good => _currentOrderData.feedbackLinesGood,
            OrderEvaluationGrade.Mid => _currentOrderData.feedbackLinesMid,
            _ => _currentOrderData.feedbackLinesBad
        };

        if (lines == null || lines.Count == 0)
            return false;

        dialogue?.StartDialogue(lines);
        return dialogue != null;
    }

    public void Clear()
    {
        _currentOrderData = null;
        _currentVisitData = null;
        characterStage?.Clear();
    }

    private void OnCharactersShown(CustomerOrderData data)
    {
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

    private void ApplyFeedbackExpressions(OrderEvaluationGrade grade)
    {
        if (_currentOrderData == null)
            return;

        if (_currentVisitData?.members != null && _currentVisitData.members.Count > 0)
        {
            for (int i = 0; i < _currentVisitData.members.Count; i++)
            {
                CustomerVisitMember member = _currentVisitData.members[i];
                if (member == null || string.IsNullOrWhiteSpace(member.characterKey))
                    continue;

                string expressionKey = grade switch
                {
                    OrderEvaluationGrade.Good => member.expressionKeyGood,
                    OrderEvaluationGrade.Mid => member.expressionKeyMid,
                    _ => member.expressionKeyBad
                };
                if (!string.IsNullOrWhiteSpace(expressionKey))
                    characterStage?.SwapExpression(member.characterKey, expressionKey);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentOrderData.characterKey))
            return;

        string legacyExpressionKey = grade switch
        {
            OrderEvaluationGrade.Good => _currentOrderData.expressionKeyGood,
            OrderEvaluationGrade.Mid => _currentOrderData.expressionKeyMid,
            _ => _currentOrderData.expressionKeyBad
        };
        characterStage?.SwapExpression(_currentOrderData.characterKey, legacyExpressionKey);
    }

    private static List<CharacterSlotEntry> BuildVisitEntries(CustomerVisitData visit)
    {
        List<CharacterSlotEntry> entries = new List<CharacterSlotEntry>();
        if (visit?.members == null)
            return entries;

        for (int i = 0; i < visit.members.Count; i++)
        {
            CustomerVisitMember member = visit.members[i];
            if (member == null || string.IsNullOrWhiteSpace(member.characterKey))
                continue;

            entries.Add(new CharacterSlotEntry
            {
                characterKey = member.characterKey,
                expressionKey = member.expressionKeyMid,
                slotIndex = member.slotIndex
            });
        }

        return entries;
    }
}
