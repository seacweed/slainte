using System;
using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    [Serializable]
    public class CustomNodeField
    {
        public string FieldName;
        public string FieldValue;
    }

    public abstract class NodeDataSO : ScriptableObject
    {
        public string Guid;
        public Rect Position;
        public List<CustomNodeField> CustomFields = new List<CustomNodeField>();
    }
}