using System;

[Serializable]
public class VarCondition
{
    public string   varName;
    public CompareOp op        = CompareOp.GreaterOrEqual;
    public int      threshold;

    public bool Evaluate(int value)
    {
        return op switch
        {
            CompareOp.GreaterOrEqual => value >= threshold,
            CompareOp.Greater        => value >  threshold,
            CompareOp.Equal          => value == threshold,
            CompareOp.Less           => value <  threshold,
            CompareOp.LessOrEqual    => value <= threshold,
            _                        => false
        };
    }
}
