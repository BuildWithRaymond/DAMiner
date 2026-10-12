using DAGoldManager.Core;
using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Chooses reachable bank floor tiles without navigating onto the NPC counter or exit portals.</summary>
public static class BankNavigation
{
    public static GameCommand Arrival(CraftRecipe recipe)
    {
        // Lamont stands behind a counter. Row 8 is the public floor, clear of the row-11 exits.
        if (recipe.Map == AbelBank.Map)
            return AbelArrival();
        return new("navigate", recipe.Map, new(recipe.X, recipe.Y), Tolerance: 3);
    }

    public static GameCommand AbelArrival() =>
        new("navigate", AbelBank.Map, new(AbelBank.FloorX, AbelBank.FloorY), Tolerance: 5)
        { TargetRow = AbelBank.FloorY };

    public static GameCommand OpenNpc(CraftRecipe recipe) =>
        new("open", Text: recipe.NpcName);
}
