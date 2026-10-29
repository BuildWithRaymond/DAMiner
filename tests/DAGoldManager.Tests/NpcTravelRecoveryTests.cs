using System.Collections.Immutable;
using DAGoldManager.Game;
using DAQuester.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class NpcTravelRecoveryTests
{
    [Fact]
    public void SubmittedFareSurvivesStopAndOnlyOwnFreshExpectedArrivalCompletesRedirect()
    {
        var recovery = new NpcTravelRecovery("Fictional runner");
        var source = Source();
        var checkpoint = Passage(source);

        Assert.True(recovery.Capture(checkpoint, source, redirected: false));
        Assert.False(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 9 }));
        Assert.Same(checkpoint, recovery.Pending);

        // The redirect can arrive after Go was canceled. Retain this same receipt.
        Assert.True(recovery.Capture(checkpoint, source, redirected: true));
        Assert.False(recovery.TryComplete(Arrival(source, checkpoint)));
        Assert.False(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 7 }));
        Assert.False(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 9, Name = "Other runner" }));
        Assert.False(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 9, Map = 999 }));
        Assert.False(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 9, InventoryKnown = false }));

        // Server-local entity ids can change on the new owned connection.
        Assert.True(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 9, Id = 800 }));
        Assert.Null(recovery.Pending);
        Assert.Equal(5_000, recovery.VerifiedGoldSpent);
        Assert.False(recovery.Capture(checkpoint, source, redirected: true));
        Assert.Equal(5_000, recovery.VerifiedGoldSpent);
    }

    [Theory]
    [InlineData(94_999)]
    [InlineData(95_001)]
    [InlineData(100_000)]
    public void ExactQuotedGoldDecreaseRequiredBeforeFareCounts(uint gold)
    {
        var recovery = new NpcTravelRecovery("Fictional runner");
        var source = Source();
        var checkpoint = Passage(source);
        recovery.Capture(checkpoint, source, redirected: true);

        Assert.False(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 9, Gold = gold }));
        Assert.NotNull(recovery.Pending);
        Assert.Equal(0, recovery.VerifiedGoldSpent);
    }

    [Fact]
    public void InventoryChangeCannotBeMistakenForTravelExpense()
    {
        var recovery = new NpcTravelRecovery("Fictional runner");
        var source = Source();
        var checkpoint = Passage(source);
        recovery.Capture(checkpoint, source, redirected: true);

        var changed = Arrival(source, checkpoint) with { Generation = 9,
            Inventory = source.Inventory.SetItem(1, new(1, "Gold Bar", 1)) };
        Assert.False(recovery.TryComplete(changed));
        Assert.Equal(0, recovery.VerifiedGoldSpent);
        Assert.True(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 9 }));
        Assert.Equal(5_000, recovery.VerifiedGoldSpent);
    }

    [Fact]
    public void SameConnectionFreshArrivalReconcilesSubmittedFareWithoutRedirect()
    {
        var recovery = new NpcTravelRecovery("Fictional runner");
        var source = Source();
        var checkpoint = Passage(source);
        recovery.Capture(checkpoint, source, redirected: false);

        Assert.False(recovery.TryComplete(Arrival(source, checkpoint) with { Revision = checkpoint.Revision }));
        Assert.False(recovery.TryComplete(Arrival(source, checkpoint) with { Id = source.Id + 1 }));
        Assert.True(recovery.TryComplete(Arrival(source, checkpoint)));
        Assert.Equal(5_000, recovery.VerifiedGoldSpent);
    }

    [Fact]
    public void StaleOrDifferentCharacterCheckpointDoesNotAuthorizeConnectionRecovery()
    {
        var recovery = new NpcTravelRecovery("Fictional runner");
        var source = Source();
        var checkpoint = Passage(source);

        Assert.False(recovery.Capture(checkpoint with { Character = "Other runner" }, source, redirected: true));
        Assert.False(recovery.Capture(checkpoint with { Generation = source.Generation - 1 }, source, redirected: true));
        Assert.False(recovery.Capture(checkpoint with { CharacterId = source.Id + 1 }, source, redirected: true));
        Assert.False(recovery.Capture(checkpoint with { Revision = source.Revision + 1 }, source, redirected: true));
        Assert.Null(recovery.Pending);
    }

    [Fact]
    public void SecondSubmissionCannotReplaceAnUnresolvedFare()
    {
        var recovery = new NpcTravelRecovery("Fictional runner");
        var source = Source();
        var checkpoint = Passage(source);
        recovery.Capture(checkpoint, source, redirected: true);

        Assert.False(recovery.Capture(checkpoint with { OperationId = Guid.NewGuid() }, source, redirected: true));
        Assert.Same(checkpoint, recovery.Pending);
    }

    [Fact]
    public void FreeConsentRequiresUnchangedGoldAndInventory()
    {
        var recovery = new NpcTravelRecovery("Fictional runner");
        var source = Source();
        var checkpoint = Passage(source) with { QuotedFare = null };
        recovery.Capture(checkpoint, source, redirected: true);

        Assert.False(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 9, Gold = source.Gold - 1 }));
        Assert.True(recovery.TryComplete(Arrival(source, checkpoint) with { Generation = 9, Gold = source.Gold }));
        Assert.Equal(0, recovery.VerifiedGoldSpent);
    }

    static CharacterSnapshot Source() => new()
    {
        Connected = true, Name = "Fictional runner", Id = 21, Generation = 8, Revision = 12,
        Map = 520, Position = new(4, 4), Gold = 100_000, InventoryKnown = true,
        Weight = 2, MaxWeight = 100,
        Inventory = ImmutableDictionary<byte, Item>.Empty.Add(1, new(1, "Gold Bar", 2))
    };

    static NpcTravelCheckpoint Passage(CharacterSnapshot source) => new(source.Generation, 3010,
        source.Name!, "Fictional captain", DateTimeOffset.UtcNow)
    {
        OperationId = Guid.NewGuid(), CharacterId = source.Id, Revision = source.Revision,
        GoldBefore = source.Gold, QuotedFare = 5_000, InventoryBefore = source.Inventory
    };

    static CharacterSnapshot Arrival(CharacterSnapshot source, NpcTravelCheckpoint checkpoint) => source with
    {
        Revision = source.Revision + 1, Map = checkpoint.Map, Position = new(10, 10),
        Gold = source.Gold - (checkpoint.QuotedFare ?? 0)
    };
}
