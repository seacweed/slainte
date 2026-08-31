using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Order Ticket Data", fileName = "OrderTicketData_")]
public class OrderTicketData : ScriptableObject
{
    [Header("식별 정보")]
    public string key;

    [Header("표시 데이터")]
    public string customerName;

    [TextArea(2, 6)]
    public string memo;

    public List<OrderTicketItem> items = new();
}

[System.Serializable]
public class OrderTicketItem
{
    public string name;
    public int qty;
    [Tooltip("주문서 표시용 금액입니다. 실제 정산은 제출 결과 레시피 가격을 사용합니다.")]
    public int price;
}
