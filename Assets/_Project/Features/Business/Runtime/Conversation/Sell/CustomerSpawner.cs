using System;
using System.Collections.Generic;
using Slainte.Business;
using UnityEngine;

public enum CustomerDialoguePresentation
{
    Missing,
    Played,
    IntentionallySkipped
}

// 영업 손님 한 팀을 무대에 표시하고, 주문 제시 대사와 결과 피드백 대사를 재생한다. 손님 데이터
// 형식이 CustomerVisitData(다인원 members 목록) 이전/이후로 나뉘어 있어, 곳곳에 "새 형식이
// 있으면 그것을, 없으면 옛 단일 characterKey 방식으로" 폴백하는 로직이 반복된다.
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

    public bool ShowVisit(
        string visitKey,
        string orderKey,
        string fallbackOrderLine = null,
        Action<bool> onOrderPresentationReady = null)
    {
        CanResolveOrder(orderKey, out _currentOrderData);
        _currentVisitData = visitDB != null ? visitDB.FindByKey(visitKey) : null;
        if (_currentOrderData == null) return false;

        List<CharacterSlotEntry> entries = BuildVisitEntries(_currentVisitData);
        if (entries.Count > 0)
        {
            characterStage?.ShowCharacters(
                entries,
                () => NotifyOrderPresentationReady(
                    _currentOrderData,
                    fallbackOrderLine,
                    onOrderPresentationReady));
            if (characterStage == null)
            {
                NotifyOrderPresentationReady(
                    _currentOrderData,
                    fallbackOrderLine,
                    onOrderPresentationReady);
            }
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_currentOrderData.characterKey))
        {
            var entry = new CharacterSlotEntry
            {
                characterKey  = _currentOrderData.characterKey,
                expressionKey = _currentOrderData.expressionKeyMid
            };
            characterStage?.ShowCharacters(
                new[] { entry },
                () => NotifyOrderPresentationReady(
                    _currentOrderData,
                    fallbackOrderLine,
                    onOrderPresentationReady));
            if (characterStage == null)
            {
                NotifyOrderPresentationReady(
                    _currentOrderData,
                    fallbackOrderLine,
                    onOrderPresentationReady);
            }
        }
        else
        {
            NotifyOrderPresentationReady(
                _currentOrderData,
                fallbackOrderLine,
                onOrderPresentationReady);
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

    // 결과 대사 우선순위: ① 작가가 의도적으로 대사를 생략하기로 한 경우(IntentionallySkipped —
    // 예: 손님이 별 반응 없이 넘어가야 하는 연출), ② 결과 등급별로 직접 작성된 대사
    // (TryGetAuthoredFeedback — Good/MidGlass/MidIce/... 세분화), ③ 그마저 없으면 Good/Mid/Bad
    // 3단계로 뭉뚱그린 옛 legacyLines. 호출자는 반환값으로 대화가 실제로 재생됐는지 판단해
    // 재생 안 됐을 때만 임시 폴백 대사를 붙인다(BusinessOrderSessionController.PresentPendingResult).
    public CustomerDialoguePresentation ShowFeedback(CraftingJobResult result)
    {
        if (_currentOrderData == null)
            return CustomerDialoguePresentation.Missing;

        OrderEvaluationGrade grade = ToEvaluationGrade(result);
        ApplyFeedbackExpressions(grade);

        if (_currentOrderData.IsFeedbackIntentionallySilent(result))
            return CustomerDialoguePresentation.IntentionallySkipped;

        if (_currentOrderData.TryGetAuthoredFeedback(result, out List<DialogueLine> authoredLines))
        {
            if (dialogue == null)
                return CustomerDialoguePresentation.Missing;

            dialogue.StartDialogue(authoredLines);
            return CustomerDialoguePresentation.Played;
        }

        List<DialogueLine> legacyLines = grade switch
        {
            OrderEvaluationGrade.Good => _currentOrderData.feedbackLinesGood,
            OrderEvaluationGrade.Mid => _currentOrderData.feedbackLinesMid,
            _ => _currentOrderData.feedbackLinesBad
        };
        if (legacyLines == null || legacyLines.Count == 0 || dialogue == null)
            return CustomerDialoguePresentation.Missing;

        dialogue.StartDialogue(legacyLines);
        return CustomerDialoguePresentation.Played;
    }

    public void Clear()
    {
        _currentOrderData = null;
        _currentVisitData = null;
        characterStage?.Clear();
    }

    // 주문 제시 시 보여줄 대사 우선순위: ① 작가가 직접 쓴 정식 대사(data.lines), ② 대사를
    // 의도적으로 안 쓰기로 한 경우(orderDialogueAuthored — 침묵도 연출의 일부), ③ CSV 등에서
    // 자동 생성된 한 줄짜리 fallbackOrderLine, ④ 아무것도 없으면 대화창을 그냥 닫는다.
    private bool OnCharactersShown(CustomerOrderData data, string fallbackOrderLine)
    {
        if (data == null) return false;

        ticketManager?.Prepare(
            data.key,
            OrderTicketMemoFormatter.Build(data, fallbackOrderLine));

        if (data.lines != null && data.lines.Count > 0)
        {
            dialogue?.StartDialogue(data.lines);
            return dialogue != null;
        }
        else if (data.orderDialogueAuthored)
        {
            dialogue?.HideImmediate();
            return false;
        }
        else if (!string.IsNullOrWhiteSpace(fallbackOrderLine))
        {
            string characterKey = ResolveOrderingCharacterKey(data);
            string speakerName = characterKey;
            Color nameColor = Color.white;
            characterStage?.TryGetDialogueIdentity(
                characterKey,
                out speakerName,
                out nameColor);
            dialogue?.ShowSingleLine(speakerName, fallbackOrderLine, nameColor);
            return dialogue != null;
        }
        else
        {
            dialogue?.HideImmediate();
            return false;
        }
    }

    private void NotifyOrderPresentationReady(
        CustomerOrderData data,
        string fallbackOrderLine,
        Action<bool> callback)
    {
        bool dialogueStarted = OnCharactersShown(data, fallbackOrderLine);
        callback?.Invoke(dialogueStarted);
    }

    private static OrderEvaluationGrade ToEvaluationGrade(CraftingJobResult result)
    {
        return result switch
        {
            CraftingJobResult.Good => OrderEvaluationGrade.Good,
            CraftingJobResult.Bad => OrderEvaluationGrade.Bad,
            _ => OrderEvaluationGrade.Mid
        };
    }

    private string ResolveOrderingCharacterKey(CustomerOrderData data)
    {
        if (data != null && !string.IsNullOrWhiteSpace(data.characterKey))
            return data.characterKey;

        if (_currentVisitData?.members == null)
            return string.Empty;

        for (int i = 0; i < _currentVisitData.members.Count; i++)
        {
            CustomerVisitMember member = _currentVisitData.members[i];
            if (member != null && !string.IsNullOrWhiteSpace(member.characterKey))
                return member.characterKey;
        }

        return string.Empty;
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
