using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Customer Order Data", fileName = "CustomerOrderData_")]
public class CustomerOrderData : ScriptableObject
{
    [Header("Identity")]
    public string key;

    [Header("Character")]
    public string characterKey;
    public string expressionKeyMid;
    public string expressionKeyGood;
    public string expressionKeyBad;

    [Header("Order Dialogue")]
    public List<DialogueLine> lines = new();

    [Header("Feedback")]
    public List<DialogueLine> feedbackLinesGood = new();
    public List<DialogueLine> feedbackLinesBad  = new();
}
