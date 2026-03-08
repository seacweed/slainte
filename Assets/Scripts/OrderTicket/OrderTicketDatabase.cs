using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Order Ticket Database", fileName = "OrderTicketDatabase")]
public class OrderTicketDatabase : ScriptableObject
{
    public List<OrderTicketData> orders = new();

    public OrderTicketData FindByKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        for (int i = 0; i < orders.Count; i++)
        {
            var c = orders[i];
            if (c != null && string.Equals(c.key, key, System.StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }
}
