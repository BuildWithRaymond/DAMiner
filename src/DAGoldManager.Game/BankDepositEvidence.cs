using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Proves one bar left the same connected character without changing gold or other items.</summary>
public static class BankDepositEvidence
{
    public static bool IsConfirmed(CharacterSnapshot before, CharacterSnapshot after, string barName)
    {
        // Revision excludes stale snapshots; generation excludes reconnects that reuse an entity ID.
        if (!before.Connected || !after.Connected || !before.InventoryKnown || !after.InventoryKnown ||
            before.Id is null || before.Id != after.Id || before.Name is null ||
            !before.Name.Equals(after.Name, StringComparison.OrdinalIgnoreCase) ||
            before.Generation != after.Generation || after.Revision <= before.Revision ||
            before.Map != after.Map || before.Gold is null || before.Gold != after.Gold ||
            before.Count(barName) - after.Count(barName) != 1) return false;
        var otherBefore = before.Inventory.Values.Where(i => !i.Name.Equals(barName, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Slot);
        var otherAfter = after.Inventory.Values.Where(i => !i.Name.Equals(barName, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Slot);
        return otherBefore.SequenceEqual(otherAfter);
    }
}
