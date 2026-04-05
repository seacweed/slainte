using System;

[Serializable]
public class CharacterSlotEntry
{
    public string characterKey;
    public string expressionKey;
    // -1 = auto-assign, 0+ = explicit slot index
    // 0=Center, 1=Left, 2=Right, 3=Left2, 4=Right2
    // 5=Interaction0, 6=Interaction1, 7=Interaction2, 8=Interaction3 (combined-sprite slots)
    public int slotIndex = -1;
}
