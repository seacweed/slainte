using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    public class EpisodeNodeSO : NodeDataSO
    {
        [Header("Episode Content")]
        public string StartEventGuid;
        public List<EpisodeEvent> Events = new();
        public List<string> OutgoingBranches = new() { "Next" }; // Default branch

        private void Awake()
        {
            // Default fields for metadata
            if (CustomFields == null || CustomFields.Count == 0)
            {
                CustomFields = new List<CustomNodeField>
                {
                    new CustomNodeField { FieldName = "Title", FieldValue = "New Block" },
                    new CustomNodeField { FieldName = "Description", FieldValue = "" }
                };
            }
        }
    }
}
