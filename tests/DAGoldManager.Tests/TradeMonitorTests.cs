using DAGoldManager.Game;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using Xunit;

namespace DAGoldManager.Tests;

public class TradeMonitorTests
{
    [Fact]
    public void TradeTracksBothAcceptanceSignalsAndExactOfferedQuantity()
    {
        var trade = new TradeMonitor("Gold Bar");
        trade.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 42, TargetName = "Vaultkeeper" });
        trade.Observe(new() { Event = ExchangeServerEventType.ItemAdded, Party = ExchangeParty.You, ItemIndex = 1, ItemName = "Gold Bar(3)" });
        trade.Observe(new() { Event = ExchangeServerEventType.Accepted, Party = ExchangeParty.You });
        Assert.False(trade.View.PartnerAccepted);
        Assert.Equal(3, trade.View.OwnBars);
        trade.Observe(new() { Event = ExchangeServerEventType.Accepted, Party = ExchangeParty.Them });
        Assert.True(trade.View.LocalAccepted && trade.View.PartnerAccepted);
    }

    [Fact]
    public void AnotherTradeStartResetsPreviouslyOfferedItems()
    {
        var trade = new TradeMonitor("Gold Bar");
        trade.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 42, TargetName = "Vaultkeeper" });
        trade.Observe(new() { Event = ExchangeServerEventType.ItemAdded, Party = ExchangeParty.You, ItemIndex = 1, ItemName = "Gold Bar" });
        trade.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 99, TargetName = "WrongPlayer" });
        Assert.Equal(0, trade.View.OwnBars);
        Assert.Equal(99u, trade.View.TargetId);
    }

    [Fact]
    public void UnrelatedItemsAreNeverAcceptedAsBars()
    {
        var trade = new TradeMonitor("Gold Bar");
        trade.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 42, TargetName = "Vaultkeeper" });
        trade.Observe(new() { Event = ExchangeServerEventType.ItemAdded, Party = ExchangeParty.Them, ItemIndex = 1, ItemName = "Gold Bar Fake" });
        Assert.True(trade.View.UnexpectedItem);
        Assert.Equal(0, trade.View.OtherBars);
    }
}
