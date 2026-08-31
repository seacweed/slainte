public enum CraftingJobResult
{
    Good,
    MidIce,
    MidGlass,
    MidIceGlass,
    MidWrongMenu,
    Bad
}

// Graph editor port order/labels for crafting nodes.
// Port 0=Good, 1=Bad match the indices already saved in existing graph assets;
// the new Mid results are appended on previously unused ports 2-5.
public static class CraftingJobResultPorts
{
    public static readonly CraftingJobResult[] Order =
    {
        CraftingJobResult.Good,
        CraftingJobResult.Bad,
        CraftingJobResult.MidIce,
        CraftingJobResult.MidGlass,
        CraftingJobResult.MidIceGlass,
        CraftingJobResult.MidWrongMenu,
    };

    public static string Label(CraftingJobResult result) => result switch
    {
        CraftingJobResult.Good         => "Good",
        CraftingJobResult.Bad          => "Bad",
        CraftingJobResult.MidIce       => "Mid-Ice",
        CraftingJobResult.MidGlass     => "Mid-Glass",
        CraftingJobResult.MidIceGlass  => "Mid-Ice+Glass",
        CraftingJobResult.MidWrongMenu => "Mid-WrongMenu",
        _                               => result.ToString()
    };
}
