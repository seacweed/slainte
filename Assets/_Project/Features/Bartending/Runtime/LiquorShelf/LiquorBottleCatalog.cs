using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Bartending/Liquor Bottle Catalog")]
public class LiquorBottleCatalog : ScriptableObject
{
    public List<LiquorBottleDef> bottles;
}
