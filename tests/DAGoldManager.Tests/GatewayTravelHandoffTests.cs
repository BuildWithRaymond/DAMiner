using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Arbiter.Net.Client;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAGoldManager.Core;
using DAGoldManager.Game;
using DAGoldManager.Storage;
using DAQuester.Core;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

public class GatewayTravelHandoffTests
{
    [Fact]
    public async Task NpcChoiceWaitsForDurableStartedMarkerBeforeSending()
    {
        var ledger = new BlockingTravelLedger();
        await using var world = new TravelWorld(ledger);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var travel = world.Navigate(stop.Token);
        await world.Request();
        world.Menu("Fictional greeting", "Carpet Merchant");
        await ledger.StartedReached.Task.WaitAsync(stop.Token);
        Assert.False(world.Departure.Remote.Peer.GetStream().DataAvailable);

        ledger.AllowStarted.TrySetResult();
        await world.Request();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);
        Assert.Contains(ledger.Records, operation => operation.Kind == "travel" && operation.Outcome == OperationOutcome.Started);
        Assert.DoesNotContain(ledger.Records, operation => operation.Outcome == OperationOutcome.Verified);
    }

    [Fact]
    public async Task InterruptedNpcFareStaysUnresolvedWhenStoreIsReopened()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAMinerTravelLedger", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(directory);
            await using (var world = new TravelWorld(store))
            {
                using var stop = new CancellationTokenSource();
                var travel = world.Navigate(stop.Token);
                await world.AcceptFare();
                stop.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);
            }
            var reopened = new LocalStore(directory);
            Assert.True(reopened.HasUnresolvedOperations());
            Assert.Contains(reopened.RecentOperations(), operation => operation.Kind == "travel" &&
                operation.Outcome == OperationOutcome.Started && operation.Amount == 10_000);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ExactRedirectArrivalVerifiesOnlyItsOwnTravelLedgerEntry()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAMinerTravelReceipt", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(directory);
            await store.AppendAsync(new(Guid.NewGuid(), Guid.NewGuid(), "trade-bars", "Other", "Vault", 1,
                OperationOutcome.Started, DateTimeOffset.UtcNow), default);
            await using var world = new TravelWorld(store);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var travel = world.Navigate(timeout.Token);
            await world.AcceptFare();
            world.Interrupt("Server redirect. Resume on replacement connection.");
            world.Replace();
            await travel;

            var receipt = Assert.Single(store.RecentOperations(), operation => operation.Kind == "travel" &&
                operation.Outcome == OperationOutcome.Verified);
            Assert.Equal(10_000, receipt.Amount);
            Assert.True(store.HasUnresolvedOperations());
            Assert.True(new LocalStore(directory).HasUnresolvedOperations());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AcceptedNpcRedirectContinuesFromOwnReplacementWithoutBuyingPassageAgain()
    {
        await using var world = new TravelWorld();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var travel = world.Navigate(timeout.Token);
        await world.AcceptFare();
        world.Interrupt("Server redirect. Resume on replacement connection.");
        world.Interrupt("Disconnected");
        world.Replace();

        await travel;
        Assert.Equal(10_000, world.Gateway.TravelGoldSpent("Fictional runner"));
        Assert.Equal(3, world.RequestCount);
        Assert.False(world.Departure.Remote.Peer.GetStream().DataAvailable);
        Assert.False(world.Replacement!.Remote.Peer.GetStream().DataAvailable);
    }

    [Fact]
    public async Task RedirectAfterStopRemainsReconciliableOnNextGoWithoutRepeatingFare()
    {
        await using var world = new TravelWorld();
        using var stop = new CancellationTokenSource();
        var travel = world.Navigate(stop.Token);
        await world.AcceptFare();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);

        world.Interrupt("Server redirect. Resume on replacement connection.");
        world.Replace();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await world.Navigate(timeout.Token);

        Assert.Equal(10_000, world.Gateway.TravelGoldSpent("Fictional runner"));
        Assert.Equal(3, world.RequestCount);
        Assert.False(world.Departure.Remote.Peer.GetStream().DataAvailable);
        Assert.False(world.Replacement!.Remote.Peer.GetStream().DataAvailable);
    }

    [Fact]
    public async Task OrdinaryDisconnectCannotAuthorizeFreshConnectionRecovery()
    {
        await using var world = new TravelWorld();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var travel = world.Navigate(timeout.Token);
        await world.AcceptFare();
        world.Interrupt("Disconnected");
        world.Replace();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => travel);
        Assert.Contains("Disconnected", error.Message);
        Assert.Equal(0, world.Gateway.TravelGoldSpent("Fictional runner"));
        Assert.Equal(3, world.RequestCount);
        Assert.False(world.Replacement!.Remote.Peer.GetStream().DataAvailable);
    }

    [Fact]
    public async Task ReplacementManualInputStillInterruptsPostNavigationControlledWork()
    {
        await using var world = new TravelWorld();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = world.ControlledNavigation(navigated, timeout.Token);
        await world.AcceptFare();
        world.Interrupt("Server redirect. Resume on replacement connection.");
        world.Replace();
        await navigated.Task.WaitAsync(timeout.Token);
        world.InterruptReplacement("Manual movement paused automation.");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => work);
        Assert.Contains("Manual movement", error.Message);
        Assert.Equal(3, world.RequestCount);
    }

    sealed class TravelWorld : IAsyncDisposable
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), "DAMinerGatewayTravel", Guid.NewGuid().ToString("N"));
        readonly ClientPacketEncryptor decrypt = new();
        public Participant Departure { get; }
        public Participant? Replacement { get; private set; }
        public GameGateway Gateway { get; }
        public int RequestCount { get; private set; }
        ConcurrentDictionary<string, CharacterConnection> Characters =>
            (ConcurrentDictionary<string, CharacterConnection>)typeof(GameGateway)
                .GetField("characters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Gateway)!;

        public TravelWorld(IOperationLedger? ledger = null)
        {
            Directory.CreateDirectory(Path.Combine(directory, "maps"));
            var records = JsonSerializer.Deserialize<MapRecord[]>("""
                [{"Id":1,"Name":"Fictional departure","Width":8,"Height":8,"Portals":[],
                  "NpcTransfers":[{"Anchor":{"X":4,"Y":1},"Map":2,"PlanningArrival":{"X":2,"Y":2},
                  "Npc":"Habab","Service":"Carpet Merchant","Destinations":["Tagor"],"MinimumGold":10001}]},
                 {"Id":2,"Name":"Fictional arrival","Width":8,"Height":8,"Portals":[]}]
                """)!;
            File.WriteAllText(Path.Combine(directory, "routes.json"), JsonSerializer.Serialize(records));
            var collision = new
            {
                SchemaVersion = 1, ServerProfile = "datester-7.41", SotpSha256 = new string('0', 64),
                Maps = records.Select(r => new { r.Id, r.Width, r.Height,
                    BlockedBits = Convert.ToBase64String(new byte[8]), SourceSha256 = new string('0', 64) }),
                MissingMapIds = Array.Empty<int>()
            };
            File.WriteAllText(Path.Combine(directory, "maps", "datester-7.41.walkability.json"), JsonSerializer.Serialize(collision));
            Gateway = new(new(), null, _ => throw new InvalidOperationException("No real credentials."), directory, ledger);
            Departure = new(directory, 1, 1, new(4, 4), 20_000);
            Characters["Fictional runner"] = Departure.Character;
            Observe(Departure);
        }

        void Observe(Participant participant) => Method("ObserveTravelConnection").Invoke(Gateway, [participant.Character]);

        public Task Navigate(CancellationToken cancellation) =>
            (Task)Method("NavigateManagedAsync").Invoke(Gateway, ["Fictional runner", new GameCommand("navigate", 2, new(2, 2)), cancellation])!;

        public Task ControlledNavigation(TaskCompletionSource navigated, CancellationToken cancellation)
        {
            Func<CancellationToken, Task> action = async token =>
            {
                await Navigate(token);
                navigated.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            };
            return (Task)Method("Control").Invoke(Gateway, [new[] { Departure.Character }, action, cancellation])!;
        }

        static MethodInfo Method(string name) => typeof(GameGateway).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

        public void Interrupt(string reason) => typeof(ProxyConnection).GetMethod("Interrupt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Departure.Connection, [reason]);

        public void InterruptReplacement(string reason) => typeof(ProxyConnection).GetMethod("Interrupt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Replacement!.Connection, [reason]);

        public void Replace()
        {
            Replacement = new(directory, 2, 2, new(2, 2), 10_000);
            Characters["Fictional runner"] = Replacement.Character;
            Observe(Replacement);
        }

        public async Task AcceptFare()
        {
            await Request();
            Menu("Fictional greeting", "Carpet Merchant");
            await Request();
            Menu("Would you like to travel to Tagor? The fare is 10,000 gold.", "No", "Yes");
            await Request();
        }

        public void Menu(string text, params string[] choices)
        {
            var message = new ServerPursuitMessage
            {
                DialogType = DialogType.Menu, EntityType = EntityTypeFlags.Creature, EntityId = 77,
                PursuitId = 765, StepId = 20, Name = "Habab", Content = text, MenuChoices = [.. choices]
            };
            var builder = new NetworkPacketBuilder(ServerMessageFactory.Default.GetMessageCommand(message.GetType())!.Value);
            try
            {
                message.Serialize(ref builder);
                Departure.Connection.Tracker.Observe(0x30, builder.ToPacket().Data);
            }
            finally { builder.Dispose(); }
        }

        public async Task Request()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            byte[] header = new byte[3];
            await Departure.Remote.Peer.GetStream().ReadExactlyAsync(header, timeout.Token);
            byte[] bytes = new byte[(header[1] << 8) | header[2]];
            await Departure.Remote.Peer.GetStream().ReadExactlyAsync(bytes, timeout.Token);
            decrypt.Decrypt(new ClientPacket(bytes[0], bytes[1..]));
            RequestCount++;
        }

        public async ValueTask DisposeAsync()
        {
            await Departure.DisposeAsync();
            if (Replacement is not null) await Replacement.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    sealed class BlockingTravelLedger : IOperationLedger
    {
        public TaskCompletionSource StartedReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<OperationRecord> Records { get; } = [];

        public async Task AppendAsync(OperationRecord operation, CancellationToken cancellation)
        {
            if (operation.Outcome == OperationOutcome.Started)
            {
                StartedReached.TrySetResult();
                await AllowStarted.Task.WaitAsync(cancellation);
            }
            Records.Add(operation);
        }
    }

    sealed class Participant : IAsyncDisposable
    {
        readonly SocketPair client = new();
        public SocketPair Remote { get; } = new();
        public ProxyConnection Connection { get; }
        public CharacterConnection Character { get; }

        public Participant(string directory, long generation, int map, Tile position, uint gold)
        {
            Connection = new(client.Connection, Remote.Connection, generation, _ => Task.FromResult(0));
            Connection.Tracker.Identity("Fictional runner", (uint)(40 + generation));
            Character = new(Connection, new(directory, Path.Combine(directory, "routes.json")), 1);
            typeof(StateTracker).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Connection.Tracker, [Character.Snapshot with
                {
                    Map = map, Position = position, Gold = gold, InventoryKnown = true, Weight = 0, MaxWeight = 100,
                    Entities = map == 1 ? ImmutableDictionary<uint, Entity>.Empty.Add(77,
                        new(77, "Habab", new(4, 1), true)) : ImmutableDictionary<uint, Entity>.Empty
                }]);
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            client.Dispose();
            Remote.Dispose();
        }
    }

    sealed class SocketPair : IDisposable
    {
        public TcpClient Connection { get; } = new();
        public TcpClient Peer { get; }
        public SocketPair()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Connection.Connect((IPEndPoint)listener.LocalEndpoint);
            Peer = listener.AcceptTcpClient();
        }
        public void Dispose() { Connection.Dispose(); Peer.Dispose(); }
    }
}
