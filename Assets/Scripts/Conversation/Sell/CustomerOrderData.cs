using System;
using System.Collections.Generic;
using Slainte.Bartending;
using Slainte.Economy;
using UnityEngine;

[Flags]
public enum CustomerOrderFeedbackMask
{
    None = 0,
    Good = 1 << 0,
    MidIce = 1 << 1,
    MidGlass = 1 << 2,
    MidIceGlass = 1 << 3,
    MidWrongMenu = 1 << 4,
    Bad = 1 << 5,
    All = Good | MidIce | MidGlass | MidIceGlass | MidWrongMenu | Bad
}

[CreateAssetMenu(menuName = "Slainte/손님 주문 데이터", fileName = "CustomerOrderData_")]
public class CustomerOrderData : ScriptableObject
{
    [Header("식별 정보")]
    public string key;

    [Header("주문 칵테일")]
    [InspectorName("판정 레시피 ID")]
    public string requestedRecipeId;
    [InspectorName("주문 유형")]
    public CocktailOrderType orderType = CocktailOrderType.RecipeOrder;
    [InspectorName("결제 통화")]
    public GameCurrency paymentCurrency = GameCurrency.Money;
    [InspectorName("주문 태그")]
    public List<string> tags = new();

    [Header("캐릭터 (이전 데이터 호환용)")]
    public string characterKey;
    public string expressionKeyMid;
    public string expressionKeyGood;
    public string expressionKeyBad;

    [Header("주문 대사")]
    [Tooltip("켜져 있고 대사가 비어 있으면 의도적으로 주문 대사를 생략합니다.")]
    public bool orderDialogueAuthored;
    public List<DialogueLine> lines = new();

    [Header("결과 대사 (기존 3단계)")]
    public List<DialogueLine> feedbackLinesGood = new();
    public List<DialogueLine> feedbackLinesMid = new();
    public List<DialogueLine> feedbackLinesBad  = new();

    [Header("결과 대사 (상세 판정)")]
    [Tooltip("실제 대사가 작성된 결과입니다. 체크되어 있어도 대사 목록이 비어 있으면 누락으로 간주합니다.")]
    public CustomerOrderFeedbackMask authoredFeedback;
    [Tooltip("대사를 의도적으로 재생하지 않을 결과입니다. 빈 대사 누락과 명시적 침묵을 구분합니다.")]
    public CustomerOrderFeedbackMask intentionallySilentFeedback;
    public List<DialogueLine> feedbackLinesMidIce = new();
    public List<DialogueLine> feedbackLinesMidGlass = new();
    public List<DialogueLine> feedbackLinesMidIceGlass = new();
    public List<DialogueLine> feedbackLinesMidWrongMenu = new();

    public bool TryGetAuthoredFeedback(
        CraftingJobResult result,
        out List<DialogueLine> feedbackLines)
    {
        CustomerOrderFeedbackMask mask = GetFeedbackMask(result);
        if ((authoredFeedback & mask) == 0)
        {
            feedbackLines = null;
            return false;
        }

        feedbackLines = GetFeedbackLines(result);
        return feedbackLines != null && feedbackLines.Count > 0;
    }

    public bool IsFeedbackIntentionallySilent(CraftingJobResult result)
    {
        return (intentionallySilentFeedback & GetFeedbackMask(result)) != 0;
    }

    public static CustomerOrderFeedbackMask GetFeedbackMask(CraftingJobResult result)
    {
        return result switch
        {
            CraftingJobResult.Good => CustomerOrderFeedbackMask.Good,
            CraftingJobResult.MidIce => CustomerOrderFeedbackMask.MidIce,
            CraftingJobResult.MidGlass => CustomerOrderFeedbackMask.MidGlass,
            CraftingJobResult.MidIceGlass => CustomerOrderFeedbackMask.MidIceGlass,
            CraftingJobResult.MidWrongMenu => CustomerOrderFeedbackMask.MidWrongMenu,
            _ => CustomerOrderFeedbackMask.Bad
        };
    }

    private List<DialogueLine> GetFeedbackLines(CraftingJobResult result)
    {
        return result switch
        {
            CraftingJobResult.Good => feedbackLinesGood,
            CraftingJobResult.MidIce => feedbackLinesMidIce,
            CraftingJobResult.MidGlass => feedbackLinesMidGlass,
            CraftingJobResult.MidIceGlass => feedbackLinesMidIceGlass,
            CraftingJobResult.MidWrongMenu => feedbackLinesMidWrongMenu,
            _ => feedbackLinesBad
        };
    }
}
