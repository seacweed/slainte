using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Order Ticket Data", fileName = "OrderTicketData_")]
public class OrderTicketData : ScriptableObject
{
    [Header("Identity")]
    public string key;

    [Header("Data")]
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
