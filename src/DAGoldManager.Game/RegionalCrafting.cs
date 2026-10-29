using DAGoldManager.Core;

namespace DAGoldManager.Game;

/// <summary>Changes the bank identity while retaining every demonstrated purchase term.</summary>
public static class RegionalCrafting
{
    public const int NoamMap = 10061;
    public const string NoamNpc = "Kelan";

    public static CraftRecipe ForBank(CraftRecipe saved, int bankMap)
    {
        if (saved.Map == bankMap) return saved;
        return bankMap switch
        {
            NoamMap => saved with { Map = NoamMap, NpcName = NoamNpc, X = 8, Y = 8 },
            AbelBank.Map => saved with { Map = AbelBank.Map, NpcName = AbelBank.NpcName, X = 3, Y = 4 },
            _ => throw new InvalidOperationException("No demonstrated crafting bank is available for map " + bankMap + ".")
        };
    }
}
