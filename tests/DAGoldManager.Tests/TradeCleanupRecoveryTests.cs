using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using DAGoldManager.Game;
using DAQuester.Core;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

public class TradeCleanupRecoveryTests
{
    [Fact]
    public async Task LateCompletionPopupCanBeDismissedAfterTheFirstCleanupWaitExpires()
    {
        using var world = new CleanupWorld();
        world.RecordVerifiedTrade();
        await world.Gateway.FinishTradeDialogsAsync("Sender", "External", default);
        Assert.True(world.Receipts.ContainsKey("Sender"));

        world.Notice(9, "Exchange completed.");
        await world.Gateway.FinishTradeDialogsAsync("Sender", "External", default);
        Assert.True(world.Client.Peer.Client.Poll(1_000_000, SelectMode.SelectRead));
        Assert.False(world.Receipts.ContainsKey("Sender"));
    }

    [Fact]
    public async Task UnrelatedPopupDoesNotDiscardVerifiedTradeCleanupEvidence()
    {
        using var world = new CleanupWorld();
        world.RecordVerifiedTrade();
        world.Notice(9, "Unrelated server notice");
        await world.Gateway.FinishTradeDialogsAsync("Sender", "External", default);
        Assert.Equal(0, world.Client.Peer.Available);
        Assert.True(world.Receipts.ContainsKey("Sender"));

        world.Notice(9, "Exchange completed.");
        await world.Gateway.FinishTradeDialogsAsync("Sender", "External", default);
        Assert.True(world.Client.Peer.Client.Poll(1_000_000, SelectMode.SelectRead));
    }

    [Fact]
    public async Task CompletionPopupWithoutVerifiedTradeIsLeftAlone()
    {
        using var world = new CleanupWorld();
        world.Notice(9, "Exchange completed.");
        await world.Gateway.FinishTradeDialogsAsync("Sender", "External", default);
        Assert.Equal(0, world.Client.Peer.Available);
    }

    [Fact]
    public async Task PopupChangedWhileWaitingToCloseDoesNotDiscardTheReceipt()
    {
        using var world = new CleanupWorld();
        world.RecordVerifiedTrade();
        world.Notice(9, "Exchange completed.");
        var presentationLock = (SemaphoreSlim)typeof(ProxyConnection)
            .GetField("toClient", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world.Connection)!;
        await presentationLock.WaitAsync();
        Task cleanup;
        try
        {
            cleanup = world.Gateway.FinishTradeDialogsAsync("Sender", "External", default);
            world.Notice(9, "Unrelated server notice");
        }
        finally { presentationLock.Release(); }
        await cleanup;
        Assert.Equal(0, world.Client.Peer.Available);
        Assert.True(world.Receipts.ContainsKey("Sender"));
    }

    [Fact]
    public async Task ReceiptFromAnotherConnectionCannotDismissNewConnectionsPopup()
    {
        using var world = new CleanupWorld();
        world.Receipts["Sender"] = (99, 0);
        world.Notice(9, "Exchange completed.");
        await world.Gateway.FinishTradeDialogsAsync("Sender", "External", default);
        Assert.Equal(0, world.Client.Peer.Available);
        Assert.False(world.Receipts.ContainsKey("Sender"));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(9)]
    public void ServerSystemCompletionClosesAnObservedExchangeWindow(byte channel)
    {
        using var world = new CleanupWorld();
        world.StartExchange();
        Assert.True(world.Character.TradeWindow.BlocksNewTrade);
        world.Notice(channel, "Exchange completed.");
        Assert.False(world.Character.TradeWindow.BlocksNewTrade);
    }

    [Theory]
    [InlineData(0, "Exchange completed.")]
    [InlineData(9, "Other player accepted.")]
    [InlineData(9, "Exchange completed?")]
    public void ChatOrUnrelatedMessagesCannotCloseAnObservedWindow(byte channel, string text)
    {
        using var world = new CleanupWorld();
        world.StartExchange();
        world.Notice(channel, text);
        Assert.True(world.Character.TradeWindow.BlocksNewTrade);
    }

    [Fact]
    public async Task SessionInterruptionKeepsTradeStageInTheReportedFailure()
    {
        using var world = new CleanupWorld();
        Task TradeAction(CancellationToken token)
        {
            typeof(ProxyConnection).GetMethod("Interrupt", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(world.Connection, ["Disconnected"]);
            return Task.FromException(new TradeCancellationException("confirming the bar offer", false, false,
                new OperationCanceledException(token)));
        }
        var control = (Task)typeof(GameGateway).GetMethod("Control", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(world.Gateway, [new[] { world.Character }, (Func<CancellationToken, Task>)TradeAction, CancellationToken.None])!;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => control);
        Assert.Contains("confirming the bar offer", error.Message);
        Assert.Contains("Disconnected", error.Message);
    }

    sealed class CleanupWorld : IDisposable
    {
        public SocketPair Client { get; } = new();
        readonly SocketPair remote = new();
        public ProxyConnection Connection { get; }
        public CharacterConnection Character { get; }
        public GameGateway Gateway { get; } = new(new(), null, _ => throw new InvalidOperationException("No real credentials."), "");
        public ConcurrentDictionary<string, (long Generation, long Revision)> Receipts =>
            (ConcurrentDictionary<string, (long, long)>)Field("completedTradeBaselines");

        public CleanupWorld()
        {
            Connection = new(Client.Connection, remote.Connection, 7, _ => Task.FromResult(0));
            Connection.Tracker.Identity("Sender", 1);
            Character = new(Connection, new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json")), 400);
            ((ConcurrentDictionary<string, CharacterConnection>)Field("characters"))["Sender"] = Character;
        }

        object Field(string name) => typeof(GameGateway).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Gateway)!;
        public void RecordVerifiedTrade() => Receipts["Sender"] = (Character.Snapshot.Generation, Character.Snapshot.Revision);
        public void StartExchange() => Connection.Tracker.Observe(0x42, [0, 0, 0, 0, 2, 8, .. Encoding.ASCII.GetBytes("External")]);
        public void Notice(byte type, string text)
        {
            byte[] encoded = Encoding.ASCII.GetBytes(text);
            Connection.Tracker.Observe(0x0A, [type, (byte)(encoded.Length >> 8), (byte)encoded.Length, .. encoded]);
        }
        public void Dispose() { Client.Dispose(); remote.Dispose(); }
    }

    sealed class SocketPair : IDisposable
    {
        public TcpClient Connection { get; } = new();
        public TcpClient Peer { get; }
        public SocketPair()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                Connection.Connect((IPEndPoint)listener.LocalEndpoint);
                Peer = listener.AcceptTcpClient();
            }
            finally { listener.Stop(); }
        }
        public void Dispose() { Connection.Dispose(); Peer.Dispose(); }
    }
}
