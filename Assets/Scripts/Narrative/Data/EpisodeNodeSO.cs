using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    public class EpisodeNodeSO : NodeDataSO
    {
        private void Awake()
        {
            if (CustomFields == null || CustomFields.Count == 0)
            {
                CustomFields = new List<CustomNodeField>
                {
                    new CustomNodeField { FieldName = "Title", FieldValue = "New Episode" },
                    new CustomNodeField { FieldName = "Description", FieldValue = "" },
                    new CustomNodeField { FieldName = "DiscoveryCondition", FieldValue = "" },
                    new CustomNodeField { FieldName = "ActivationCondition", FieldValue = "" },
                    new CustomNodeField { FieldName = "IsCleared", FieldValue = "False" }
                };
            }
        }
    }
}