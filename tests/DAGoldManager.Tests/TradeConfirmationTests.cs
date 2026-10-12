using System.Collections.Immutable;
using DAGoldManager.Game;
using DAQuester.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class TradeConfirmationTests
{
    static CharacterSnapshot Before(string name, uint id, uint gold, int bars = 0) => new()
    {
        Generation = id, Revision = 10, Name = name, Id = id, Connected = true,
        Gold = gold, InventoryKnown = true,
        Inventory = bars == 0 ? [] : ImmutableDictionary<byte, Item>.Empty.Add(1, new(1, "Gold Bar", (uint)bars))
    };
    static TradeView Offer(bool acknowledged = false) => new(true, 2, "Receiver", 0, 0,
        12_000_000, 0, acknowledged, acknowledged, false, false, false, 0);

    [Fact]
    public async Task BothExactGoldChangesConfirmLocalTradeWithoutAcceptancePackets()
    {
        var sender = Before("Donor", 1, 20_000_000);
        var receiver = Before("Receiver", 2, 38_500_000);
        await TradeConfirmation.WaitAsync(() => Offer(),
            () => sender with { Revision = 11, Gold = 8_000_000 },
            () => receiver with { Revision = 11, Gold = 50_500_000 },
            sender, receiver, 0, 12_000_000, "Gold Bar", TimeSpan.FromMilliseconds(60), default);
    }

    [Fact]
    public async Task AcceptanceWithoutBalanceChangesDoesNotConfirmTrade()
    {
        var sender = Before("Donor", 1, 20_000_000);
        var receiver = Before("Receiver", 2, 38_500_000);
        await Assert.ThrowsAsync<TimeoutException>(() => TradeConfirmation.WaitAsync(() => Offer(true),
            () => sender, () => receiver, sender, receiver, 0, 12_000_000, "Gold Bar",
            TimeSpan.FromMilliseconds(60), default));
    }

    [Fact]
    public async Task OneSidedGoldChangeDoesNotConfirmLocalTrade()
    {
        var sender = Before("Donor", 1, 20_000_000);
        var receiver = Before("Receiver", 2, 38_500_000);
        await Assert.ThrowsAsync<TimeoutException>(() => TradeConfirmation.WaitAsync(() => Offer(true),
            () => sender with { Revision = 11, Gold = 8_000_000 }, () => receiver,
            sender, receiver, 0, 12_000_000, "Gold Bar", TimeSpan.FromMilliseconds(60), default));
    }

    [Fact]
    public async Task ExternalRecipientStillRequiresAcceptanceAndExactInventoryChange()
    {
        var sender = Before("Donor", 1, 20_000_000, bars: 3);
        var after = sender with { Revision = 11, Inventory = [] };
        await Assert.ThrowsAsync<TimeoutException>(() => TradeConfirmation.WaitAsync(() => Offer() with { OwnGold = 0, OwnBars = 3 },
            () => after, null, sender, null, 3, 0, "Gold Bar", TimeSpan.FromMilliseconds(60), default));
    }

    [Fact]
    public async Task ChangedConnectionCannotConfirmOldTrade()
    {
        var sender = Before("Donor", 1, 20_000_000);
        var receiver = Before("Receiver", 2, 38_500_000);
        await Assert.ThrowsAsync<IOException>(() => TradeConfirmation.WaitAsync(() => Offer(true),
            () => sender with { Generation = 99, Revision = 11, Gold = 8_000_000 },
            () => receiver with { Revision = 11, Gold = 50_500_000 },
            sender, receiver, 0, 12_000_000, "Gold Bar", TimeSpan.FromMilliseconds(60), default));
    }

    [Fact]
    public async Task LocalBarCollectionConfirmsBothInventoriesWithoutAcceptanceEchoes()
    {
        var sender = Before("Donor", 1, 20_000_000, bars: 3);
        var receiver = Before("Receiver", 2, 30_000_000);
        await TradeConfirmation.WaitAsync(() => Offer() with { OwnGold = 0, OwnBars = 3 },
            () => sender with { Revision = 11, Inventory = [] },
            () => receiver with { Revision = 11, Inventory = sender.Inventory },
            sender, receiver, 3, 0, "Gold Bar", TimeSpan.FromMilliseconds(60), default);
    }

    [Fact]
    public async Task CompletionPopupDoesNotBypassRequiredBalanceEvidence()
    {
        var sender = Before("Donor", 1, 20_000_000, bars: 3);
        await Assert.ThrowsAsync<TimeoutException>(() => TradeConfirmation.WaitAsync(
            () => Offer() with { Completed = true, OwnGold = 0, OwnBars = 3 }, () => sender,
            null, sender, null, 3, 0, "Gold Bar", TimeSpan.FromMilliseconds(60), default));
    }

    [Fact]
    public async Task ExternalCompletionWithExactCourierChangeConfirmsDelivery()
    {
        var sender = Before("Donor", 1, 20_000_000, bars: 3);
        await TradeConfirmation.WaitAsync(() => Offer() with { Completed = true, OwnGold = 0, OwnBars = 3 },
            () => sender with { Revision = 11, Inventory = [] }, null, sender, null,
            3, 0, "Gold Bar", TimeSpan.FromMilliseconds(60), default);
    }
}
