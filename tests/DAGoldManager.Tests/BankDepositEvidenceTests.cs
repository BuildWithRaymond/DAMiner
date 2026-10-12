using System.Collections.Immutable;
using DAGoldManager.Game;
using DAQuester.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class BankDepositEvidenceTests
{
    static CharacterSnapshot Before => new()
    {
        Connected = true, Name = "Banker", Id = 7, Generation = 1, Revision = 10,
        Map = 167, Gold = 100, InventoryKnown = true,
        Inventory = new Item[] { new(1, "Gold Bar", 3), new(2, "Sword", 1) }.ToImmutableDictionary(i => i.Slot)
    };
    static CharacterSnapshot Deposited => Before with { Revision = 11, Inventory = Before.Inventory.SetItem(1, new(1, "Gold Bar", 2)) };

    [Fact]
    public void FreshOneBarDecreaseConfirmsDeposit() => Assert.True(BankDepositEvidence.IsConfirmed(Before, Deposited, "Gold Bar"));

    [Fact]
    public void StaleReconnectWrongAmountOrUnrelatedChangesNeverConfirm()
    {
        Assert.False(BankDepositEvidence.IsConfirmed(Before, Deposited with { Revision = 10 }, "Gold Bar"));
        Assert.False(BankDepositEvidence.IsConfirmed(Before, Deposited with { Generation = 2 }, "Gold Bar"));
        Assert.False(BankDepositEvidence.IsConfirmed(Before, Deposited with { Gold = 99 }, "Gold Bar"));
        Assert.False(BankDepositEvidence.IsConfirmed(Before, Deposited with { Inventory = Deposited.Inventory.Remove(2) }, "Gold Bar"));
        Assert.False(BankDepositEvidence.IsConfirmed(Before, Deposited with { Inventory = Deposited.Inventory.SetItem(1, new(1, "Gold Bar", 1)) }, "Gold Bar"));
    }
}
