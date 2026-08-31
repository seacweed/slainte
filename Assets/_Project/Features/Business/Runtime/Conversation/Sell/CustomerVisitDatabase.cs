using System;
using System.Collections.Generic;
using Slainte.Content;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/손님 방문 데이터베이스", fileName = "CustomerVisitDatabase")]
public sealed class CustomerVisitDatabase : ScriptableObject
{
    [InspectorName("방문 목록")]
    public List<CustomerVisitData> visits = new();

    public CustomerVisitData FindByKey(string visitKey)
    {
        if (string.IsNullOrWhiteSpace(visitKey))
            return null;

        for (int i = 0; i < visits.Count; i++)
        {
            CustomerVisitData visit = visits[i];
            if (visit != null
                && string.Equals(visit.visitKey, visitKey, StringComparison.OrdinalIgnoreCase))
                return visit;
        }

        return null;
    }

    public static CustomerVisitDatabase LoadDefault()
    {
        return Resources.Load<CustomerVisitDatabase>(
            ProjectResourcePaths.BusinessCustomerVisitDatabase);
    }
}
