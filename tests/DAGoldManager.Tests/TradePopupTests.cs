using System.Text;
using DAQuester.Core;
using DAQuester.DarkAges.Protocol;
using DAGoldManager.Game;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using Xunit;

namespace DAGoldManager.Tests;

public class TradePopupTests
{
    static byte[] Text(string text) => Encoding.ASCII.GetBytes(text);

    [Theory]
    [InlineData("You have exchanged")]
    [InlineData("You have exchanged.")]
    [InlineData("YOU HAVE EXCHANGED!")]
    [InlineData(" \r\n(You have exchanged.)\r\n ")]
    public void ActualExchangeCompletionWordingCanBeDismissed(string text)
    {
        var popup = new ServerNotice(7, 11, 9, text, DateTimeOffset.UtcNow);
        var state = new CharacterSnapshot { Generation = 7, Popup = popup };
        Assert.True(TradePopupCleanup.IsCompletion(text));
        Assert.Equal(popup, TradePopupCleanup.CompletionPopup(state, 7, 10));
    }

    [Theory]
    [InlineData("You have exchanged?")]
    [InlineData("You have exchanged nothing.")]
    [InlineData("You have exchanged 3 Gold Bars.")]
    [InlineData("Someone said You have exchanged.")]
    public void OtherTextDoesNotBecomeAnExchangeCompletion(string text)
    {
        Assert.False(TradePopupCleanup.IsCompletion(text));
    }

    [Fact]
    public void ExchangeCompletionPopupRemainsAvailableForSafeDismissal()
    {
        var tracker = new StateTracker(7);
        tracker.Identity("Fictional", 1);
        byte[] text = Text("Exchange completed.");
        tracker.Observe(0x42, [5, 1, (byte)text.Length, .. text]);
        // Popup is presentation data; observing it must not invent a gold change.
        Assert.Equal("Exchange completed.", tracker.Snapshot.Popup?.Text);
        Assert.Null(tracker.Snapshot.Gold);
    }

    [Theory]
    [InlineData("Exchange completed.")]
    [InlineData("You have exchanged.")]
    public void CompletionMessageIsServerAcknowledgementEvenWithoutBothAcceptanceEchoes(string text)
    {
        var monitor = new TradeMonitor("Gold Bar");
        monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 42, TargetName = "Fictional" });
        monitor.Observe(new() { Event = ExchangeServerEventType.Accepted, Party = ExchangeParty.Them, Message = text });
        Assert.True(monitor.View.Completed);
        Assert.False(monitor.View.LocalAccepted);
    }

    [Theory]
    [InlineData("Exchange cancelled.")]
    [InlineData("Other player accepted.")]
    [InlineData("Exchange completed?")]
    public void OtherExchangeMessagesDoNotProveCompletion(string message)
    {
        var monitor = new TradeMonitor("Gold Bar");
        monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 42, TargetName = "Fictional" });
        monitor.Observe(new() { Event = ExchangeServerEventType.Accepted, Party = ExchangeParty.Them, Message = message });
        Assert.False(monitor.View.Completed);
    }

    [Fact]
    public void OnlyFreshCompletionPopupFromSameConnectionCanBeDismissed()
    {
        var popup = new ServerNotice(7, 11, 9, "Exchange completed.", DateTimeOffset.UtcNow);
        var state = new CharacterSnapshot { Generation = 7, Popup = popup };
        Assert.Equal(popup, TradePopupCleanup.CompletionPopup(state, 7, 10));
        Assert.Null(TradePopupCleanup.CompletionPopup(state, 7, 11));
        Assert.Null(TradePopupCleanup.CompletionPopup(state, 8, 10));
        Assert.Null(TradePopupCleanup.CompletionPopup(state with { Popup = popup with { Type = 0 } }, 7, 10));
        Assert.Null(TradePopupCleanup.CompletionPopup(state with { Popup = popup with { Text = "Unrelated prompt" } }, 7, 10));
    }

    [Fact]
    public void NewUnrelatedPopupReplacesEarlierCompletionPopup()
    {
        var tracker = new StateTracker(7);
        tracker.Identity("Fictional", 1);
        byte[] done = Text("Exchange completed.");
        tracker.Observe(0x42, [5, 1, (byte)done.Length, .. done]);
        byte[] unrelated = Text("Unrelated prompt");
        tracker.Observe(0x0A, [9, 0, (byte)unrelated.Length, .. unrelated]);
        Assert.Null(TradePopupCleanup.CompletionPopup(tracker.Snapshot, 7, 1));
        tracker.Observe(0x0A, [17, 0, 0]);
        Assert.Null(tracker.Snapshot.Popup);
    }

    [Fact]
    public void ClientPopupClosePacketUsesPinnedClosePopupMessageType()
    {
        var message = Assert.IsType<ServerSystemMessage>(ServerMessageFactory.Default.Create(new ServerPacket(0x0A, [17, 0, 0])));
        Assert.Equal(WorldMessageType.ClosePopup, message.MessageType);
        Assert.Equal("", message.Message);
    }
}
