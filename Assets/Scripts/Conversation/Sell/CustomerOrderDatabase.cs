using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Customer Order Database", fileName = "CustomerOrderDatabase")]
public class CustomerOrderDatabase : ScriptableObject
{
    public List<CustomerOrderData> customers = new();

    public CustomerOrderData FindByKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        for (int i = 0; i < customers.Count; i++)
        {
            var c = customers[i];
            if (c != null && string.Equals(c.key, key, System.StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }
}
