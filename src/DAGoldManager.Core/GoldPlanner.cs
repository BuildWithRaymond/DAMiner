namespace DAGoldManager.Core;

public sealed record GoldBalance(string Name, long Gold, bool CanCraftBars = true);
public sealed record GoldTransfer(string Sender, string Receiver, long Amount);

/// <summary>Chooses one funding transfer from scanned balances; never sends commands or changes state.</summary>
public static class GoldPlanner
{
    public const long CarryingLimit = 99_999_999;
    public const long DefaultBarCost = 50_500_000;

    public static GoldTransfer? NextTransfer(IReadOnlyList<GoldBalance> characters, long barCost, bool poolCrafterLeftovers = true)
    {
        if (barCost <= 0 || barCost > CarryingLimit)
            throw new ArgumentOutOfRangeException(nameof(barCost));
        if (characters.Any(c => c.Gold < 0 || c.Gold > CarryingLimit))
            throw new InvalidOperationException("A character's gold is outside the supported carrying range.");
        if (characters.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != characters.Count)
            throw new InvalidOperationException("Character names must be unique.");

        // Craft funded characters first. Moving their money would add needless trades.
        if (characters.Any(c => c.CanCraftBars && c.Gold >= barCost))
            return null;

        // Start with the richest eligible crafter to reach a purchase with fewer handoffs.
        var receiver = characters.Where(c => c.CanCraftBars).OrderByDescending(c => c.Gold).FirstOrDefault();
        if (receiver is null) return null;
        // Donor funding is required even when optional crafter-to-crafter pooling is off.
        var donors = characters.Where(c => !c.Name.Equals(receiver.Name, StringComparison.OrdinalIgnoreCase) &&
            c.Gold > 0 && (poolCrafterLeftovers || !c.CanCraftBars)).OrderByDescending(c => c.Gold).ToArray();
        if (receiver.Gold + donors.Sum(c => c.Gold) < barCost) return null;
        var donor = donors.First();
        long deficit = barCost - receiver.Gold;
        // Transfer only the deficit, so a receiver cannot exceed the supported carrying cap.
        return new(donor.Name, receiver.Name, Math.Min(donor.Gold, deficit));
    }
}
