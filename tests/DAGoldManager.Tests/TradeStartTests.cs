using System.Net.Sockets;
using System.Text;
using System.Collections.Immutable;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAGoldManager.Game;
using DAQuester.Core;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

public class TradeStartTests
{
    [Fact]
    public void FreshServerTradeRefusalIsReportedWithoutWaitingForGenericTimeout()
    {
        using var client = new TcpClient();
        using var remote = new TcpClient();
        var connection = new ProxyConnection(client, remote, 1, _ => Task.FromResult(0));
        connection.Tracker.Identity("Fictional", 1);
        var maps = new MapRepository(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json"));
        var character = new CharacterConnection(connection, maps, 400);
        using var trade = new ObservedTrade(character, "Gold Bar");
        connection.Tracker.Observe(0x0A, Message(3, "That player is refusing to exchange."));
        Assert.Contains("refusing to exchange", trade.Error ?? "");
        Assert.False(trade.Monitor.View.Started);
    }

    [Fact]
    public void ChatContainingTradeWordsDoesNotBecomeARefusal()
    {
        using var client = new TcpClient();
        using var remote = new TcpClient();
        var connection = new ProxyConnection(client, remote, 1, _ => Task.FromResult(0));
        connection.Tracker.Identity("Fictional", 1);
        var maps = new MapRepository(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json"));
        using var trade = new ObservedTrade(new(connection, maps, 400), "Gold Bar");
        connection.Tracker.Observe(0x0A, Message(0, "I am refusing to exchange."));
        Assert.Null(trade.Error);
    }

    static byte[] Message(byte type, string text)
    {
        byte[] encoded = Encoding.ASCII.GetBytes(text);
        return [type, (byte)(encoded.Length >> 8), (byte)encoded.Length, .. encoded];
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(9)]
    public void FreshSystemCompletionConfirmsTheActiveTradeWithoutInventingAcceptance(byte type)
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        trade.Monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver" });
        world.Connection.Tracker.Observe(0x0A, Message(type, "You have exchanged."));
        Assert.True(trade.Monitor.View.Completed);
        Assert.False(trade.Monitor.View.LocalAccepted);
        Assert.False(trade.Monitor.View.PartnerAccepted);
        Assert.Null(trade.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(11)]
    [InlineData(12)]
    public void ChatCannotConfirmAnExchange(byte type)
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        trade.Monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver" });
        world.Connection.Tracker.Observe(0x0A, Message(type, "You have exchanged."));
        Assert.False(trade.Monitor.View.Completed);
        Assert.Null(trade.Error);
    }

    [Fact]
    public void AStaleCompletionCannotConfirmANewTrade()
    {
        using var world = new TradeWorld();
        world.Connection.Tracker.Observe(0x0A, Message(9, "You have exchanged."));
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        trade.Monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver" });
        Assert.False(trade.Monitor.View.Completed);
    }

    [Fact]
    public void ACompletionBeforeTradeStartCannotConfirmTheLaterTrade()
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        world.Connection.Tracker.Observe(0x0A, Message(9, "You have exchanged."));
        trade.Monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver" });
        Assert.False(trade.Monitor.View.Completed);
    }

    [Fact]
    public void ACancelledTradeCannotBeConfirmedByLaterCompletionText()
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        trade.Monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver" });
        trade.Monitor.Observe(new() { Event = ExchangeServerEventType.Cancelled });
        world.Connection.Tracker.Observe(0x0A, Message(9, "You have exchanged."));
        Assert.False(trade.Monitor.View.Completed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SystemCompletionRequiresExactCourierInventoryChangeForExternalDelivery(bool balancesChanged)
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        trade.Monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver" });
        trade.Monitor.Observe(new() { Event = ExchangeServerEventType.ItemAdded, Party = ExchangeParty.You,
            ItemIndex = 1, ItemName = "Gold Bar(3)" });
        world.Connection.Tracker.Observe(0x0A, Message(9, "You have exchanged."));
        var before = world.Character.Snapshot with
        {
            Gold = 1_000_000, InventoryKnown = true,
            Inventory = ImmutableDictionary<byte, Item>.Empty.Add(1, new(1, "Gold Bar", 3))
        };
        var after = before with { Revision = before.Revision + 1,
            Inventory = balancesChanged ? ImmutableDictionary<byte, Item>.Empty : before.Inventory };
        var confirmation = TradeConfirmation.WaitAsync(() => trade.Monitor.View, () => after, null,
            before, null, 3, 0, "Gold Bar", TimeSpan.FromMilliseconds(80), default);
        if (balancesChanged) await confirmation;
        else await Assert.ThrowsAsync<TimeoutException>(() => confirmation);
    }

    [Fact]
    public async Task DelayedStartCanArriveWithinTheConfiguredWait()
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        var response = Task.Run(async () =>
        {
            await Task.Delay(60);
            trade.Monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver" });
        });
        await trade.WaitAsync(v => v.Started, TimeSpan.FromSeconds(2), default);
        await response;
        Assert.Equal(2u, trade.Monitor.View.TargetId);
    }

    [Fact]
    public async Task MissingStartReportsBothWindowStatesAndTheConfiguredLimit()
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        var error = await Assert.ThrowsAsync<TimeoutException>(() => trade.WaitAsync(v => v.Started,
            TimeSpan.FromMilliseconds(60), default, () => "Sender window started: False; receiver window started: True."));
        Assert.Contains("0.06 seconds", error.Message);
        Assert.Contains("receiver window started: True", error.Message);
        Assert.Contains("No allowed server notice", error.Message);
        Assert.False(trade.Monitor.View.Started);
    }

    [Fact]
    public async Task AStaleRefusalDoesNotRejectANewObservation()
    {
        using var world = new TradeWorld();
        world.Connection.Tracker.Observe(0x0A, Message(3, "That player is refusing to exchange."));
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        trade.Monitor.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver" });
        await trade.WaitAsync(v => v.Started, TimeSpan.FromSeconds(1), default);
        Assert.Null(trade.Error);
    }

    [Fact]
    public async Task CancellationDoesNotBecomeATimeout()
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trade.WaitAsync(v => v.Started,
            TimeSpan.FromSeconds(1), cancellation.Token));
    }

    [Fact]
    public async Task DisconnectStopsResponseWaiting()
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        world.Connection.Tracker.Disconnect();
        await Assert.ThrowsAsync<IOException>(() => trade.WaitAsync(v => v.Started, TimeSpan.FromSeconds(1), default));
    }

    [Fact]
    public void MalformedExchangeDoesNotPreventTheAttemptObserverReportingItsError()
    {
        using var world = new TradeWorld();
        using var trade = new ObservedTrade(world.Character, "Gold Bar");
        var failure = Record.Exception(() => world.Connection.Tracker.Observe(0x42, [0, 1]));
        Assert.Null(failure);
        Assert.Contains("could not be decoded", trade.Error ?? "");
        Assert.True(world.Character.TradeWindow.BlocksNewTrade);
    }

    [Fact]
    public void AnAcceptanceEchoDoesNotClearAConflictingOpenWindow()
    {
        var window = new TradeWindowState();
        window.Observe(new() { Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver" });
        window.Observe(new() { Event = ExchangeServerEventType.Accepted, Party = ExchangeParty.You });
        Assert.True(window.IsOpen);
        Assert.Throws<InvalidOperationException>(() => TradeReadiness.RequireClosedWindows(window.IsOpen, false));
        window.Observe(new() { Event = ExchangeServerEventType.Accepted, Party = (ExchangeParty)2 });
        Assert.False(window.IsOpen);
    }

    [Theory]
    [InlineData(503, 11, 1)]
    [InlineData(502, 14, 1)]
    [InlineData(502, 11, 2)]
    public void ChangedManagedStateCannotSendAnotherTradeCommand(int map, int x, long generation)
    {
        var target = new Entity(2, "Receiver", new(11, 10), true) { IsPlayer = true };
        var sender = new CharacterSnapshot { Connected = true, Id = 1, Name = "Sender", Generation = 1,
            Map = 502, Position = new(10, 10), Entities = ImmutableDictionary<uint, Entity>.Empty.Add(2, target) };
        var receiver = new CharacterSnapshot { Connected = true, Id = 2, Name = "Receiver", Generation = generation,
            Map = map, Position = new(x, 10) };
        Assert.Throws<InvalidOperationException>(() => TradeReadiness.Validate(sender, receiver, target, 1, 1));
    }

    sealed class TradeWorld : IDisposable
    {
        readonly TcpClient client = new(), remote = new();
        public ProxyConnection Connection { get; }
        public CharacterConnection Character { get; }
        public TradeWorld()
        {
            Connection = new(client, remote, 1, _ => Task.FromResult(0));
            Connection.Tracker.Identity("Fictional", 1);
            Character = new(Connection, new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json")), 400);
        }
        public void Dispose() { client.Dispose(); remote.Dispose(); }
    }
}
