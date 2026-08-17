using System.Collections.Generic;
using Slainte.Bartending;
using UnityEngine;

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
    [InspectorName("주문 태그")]
    public List<string> tags = new();

    [Header("캐릭터 (이전 데이터 호환용)")]
    public string characterKey;
    public string expressionKeyMid;
    public string expressionKeyGood;
    public string expressionKeyBad;

    [Header("주문 대사")]
    public List<DialogueLine> lines = new();

    [Header("결과 대사")]
    public List<DialogueLine> feedbackLinesGood = new();
    public List<DialogueLine> feedbackLinesMid = new();
    public List<DialogueLine> feedbackLinesBad  = new();
}
