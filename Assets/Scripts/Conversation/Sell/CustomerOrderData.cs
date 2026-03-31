using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Customer Order Data", fileName = "CustomerOrderData_")]
public class CustomerOrderData : ScriptableObject
{
    [Header("Identity")]
    public string key;                 // "yukari" 같은 호출 키(고유)
    public Sprite sprite;              // 손님 스프라이트

    [Header("Dialogue Script")]
    public List<DialogueLine> lines = new();
}
