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
    public int price;
}
