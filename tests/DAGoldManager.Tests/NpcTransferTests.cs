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
using DAQuester.Core;
using DAQuester.DarkAges;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

// Fictional menus and characters exercise owned protocol evidence without a game server.
public class NpcTransferTests
{
    [Fact]
    public async Task UnknownTravelServiceStaysOpenAndCannotBeRepeated()
    {
        await using var world = new TravelWorld();
        var travel = world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), default);
        Assert.Equal(ClientCommand.RequestObjectInfo, (await world.Request()).Command);
        world.Menu("Fictional travel", "Other service");
        await Assert.ThrowsAsync<QuestBlockedException>(() => travel);
        var preserved = world.Session.Snapshot.Dialog;
        await Assert.ThrowsAsync<QuestBlockedException>(() => world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), default));
        Assert.Same(preserved, world.Session.Snapshot.Dialog);
        Assert.False(world.Remote.Peer.GetStream().DataAvailable);
    }

    [Fact]
    public async Task UnaffordableObservedFareNeverGetsAccepted()
    {
        await using var world = new TravelWorld();
        var travel = world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), default);
        await world.Request();
        world.Menu("Fictional greeting", "Carpet Merchant");
        await world.Request();
        world.Menu("Would you like to travel to Tagor? The fare is 30,000 gold.", "No", "Yes");
        var error = await Assert.ThrowsAsync<QuestBlockedException>(() => travel);
        Assert.Contains("gold", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(world.Remote.Peer.GetStream().DataAvailable);
    }

    [Fact]
    public async Task MissingReplyAfterFareIsNotResubmittedOnResume()
    {
        await using var world = new TravelWorld();
        using var cancel = new CancellationTokenSource();
        var travel = world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), cancel.Token);
        await world.Request();
        world.Menu("Fictional greeting", "Carpet Merchant");
        await world.Request();
        world.Menu("Would you like to travel to Tagor? The fare is 10,000 gold.", "No", "Yes");
        await world.Request();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);
        await Assert.ThrowsAsync<QuestBlockedException>(() => world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), default));
        Assert.False(world.Remote.Peer.GetStream().DataAvailable);
    }

    [Fact]
    public async Task PaidArrivalWaitsForGoldReceiptAndRecordsTheFareOnce()
    {
        await using var world = new TravelWorld();
        var travel = world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), default);
        await world.Request();
        world.Menu("Fictional greeting", "Carpet Merchant");
        await world.Request();
        world.Menu("Would you like to travel to Tagor? The fare is 10,000 gold.", "No", "Yes");
        await world.Request();
        Assert.NotNull(world.Session.SubmittedNpcTravel);
        world.Arrive();
        await Task.Delay(30);
        Assert.False(travel.IsCompleted);
        world.Gold(10000);
        await travel;
        var receipt = Assert.Single(world.Session.VerifiedTravelFares);
        Assert.Equal(10000u, receipt.Checkpoint.QuotedFare);
        Assert.Equal(10000u, receipt.GoldAfter);
        Assert.Null(world.Session.SubmittedNpcTravel);
        await world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), default);
        Assert.Single(world.Session.VerifiedTravelFares);
    }

    [Fact]
    public async Task ChangedInventoryCannotConfirmAPaidArrival()
    {
        await using var world = new TravelWorld();
        var travel = world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), default);
        await world.Request();
        world.Menu("Fictional greeting", "Carpet Merchant");
        await world.Request();
        world.Menu("Would you like to travel to Tagor? The fare is 10,000 gold.", "No", "Yes");
        await world.Request();
        world.AddItem();
        world.Gold(10000);
        world.Arrive();
        await Assert.ThrowsAsync<QuestBlockedException>(() => travel);
        Assert.NotNull(world.Session.SubmittedNpcTravel);
        Assert.Empty(world.Session.VerifiedTravelFares);
    }

    [Fact]
    public async Task CaravanCanChooseDestinationAfterItsFareHasAlreadyBeenPaid()
    {
        await using var world = new TravelWorld(fareFirst: true, gold: 15000);
        var travel = world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), default);
        await world.Request();
        world.Menu("Fictional greeting", "Carpet Merchant");
        await world.Request();
        world.Menu("It will cost you 10000 coins to take the caravan. Would you like to go?", "Yes", "No, I'd rather walk");
        await world.Request();
        world.Gold(5000);
        world.Menu("Where would you like to go?", "Tagor");
        await world.Request();
        world.Arrive();
        await travel;
        Assert.Single(world.Session.VerifiedTravelFares);
    }

    [Fact]
    public async Task FailedCheckpointWritePreventsTheNextTravelPacket()
    {
        await using var world = new TravelWorld();
        world.Session.BeforeNpcTravelSubmission = (_, _) => throw new IOException("Fictional journal write failure");
        var travel = world.Session.ExecuteAsync(new("navigate", 2, new(2, 2)), default);
        await world.Request();
        world.Menu("Fictional greeting", "Carpet Merchant");
        await Assert.ThrowsAsync<IOException>(() => travel);
        Assert.False(world.Remote.Peer.GetStream().DataAvailable);
    }

    sealed class TravelWorld : IAsyncDisposable
    {
        public SocketPair Remote { get; } = new();
        readonly SocketPair client = new();
        readonly string directory = Path.Combine(Path.GetTempPath(), "DAMinerNpcTravel", Guid.NewGuid().ToString("N"));
        readonly ClientPacketEncryptor decrypt = new();
        public ProxyConnection Connection { get; }
        public GameSession Session { get; }

        public TravelWorld(bool fareFirst = false, uint gold = 20000)
        {
            Directory.CreateDirectory(Path.Combine(directory, "maps"));
            var records = JsonSerializer.Deserialize<MapRecord[]>("""
                [{"Id":1,"Name":"Fictional departure","Width":8,"Height":8,"Portals":[],
                  "NpcTransfers":[{"Anchor":{"X":4,"Y":1},"Map":2,"PlanningArrival":{"X":2,"Y":2},
                  "Npc":"Habab","Service":"Carpet Merchant","Destinations":["Tagor"],"MinimumGold":10001}]},
                 {"Id":2,"Name":"Fictional arrival","Width":8,"Height":8,"Portals":[]}]
                """)!;
            if (fareFirst)
                records[0] = records[0] with
                {
                    NpcTransfers = [records[0].NpcTransfers[0] with { FareBeforeDestination = true }]
                };
            File.WriteAllText(Path.Combine(directory, "routes.json"), JsonSerializer.Serialize(records));
            var collision = new
            {
                SchemaVersion = 1, ServerProfile = "datester-7.41", SotpSha256 = new string('0', 64),
                Maps = records.Select(r => new { r.Id, r.Width, r.Height,
                    BlockedBits = Convert.ToBase64String(new byte[8]), SourceSha256 = new string('0', 64) }),
                MissingMapIds = Array.Empty<int>()
            };
            File.WriteAllText(Path.Combine(directory, "maps", "datester-7.41.walkability.json"), JsonSerializer.Serialize(collision));
            Connection = new(client.Connection, Remote.Connection, 1, _ => Task.FromResult(0));
            Connection.Tracker.Identity("Fictional", 42);
            SetState(Connection.Tracker.Snapshot with
            {
                Map = 1, Position = new(4, 4), Gold = gold, InventoryKnown = true,
                Entities = ImmutableDictionary<uint, Entity>.Empty.Add(77, new(77, "Habab", new(4, 1), true))
            });
            Session = new(Connection, new(directory, Path.Combine(directory, "routes.json")), 1);
        }

        void SetState(CharacterSnapshot state) => typeof(StateTracker).GetProperty(nameof(StateTracker.Snapshot))!.SetValue(Connection.Tracker, state);

        public void Menu(string text, params string[] choices)
        {
            var message = new ServerPursuitMessage
            {
                DialogType = DialogType.Menu, EntityType = EntityTypeFlags.Creature, EntityId = 77,
                PursuitId = 765, StepId = 20, Name = "Habab", Content = text, MenuChoices = [.. choices]
            };
            Observe(0x30, message);
        }

        public void Gold(uint amount) => Observe(8, new ServerStatusMessage { Fields = StatsFieldFlags.ExperienceGold, Gold = amount });
        public void AddItem() => SetState(Connection.Tracker.Snapshot with
        {
            Inventory = ImmutableDictionary<byte, Item>.Empty.Add(1, new(1, "Fictional change", 1))
        });
        public void Arrive()
        {
            Connection.Tracker.Observe(0x15, [0, 2, 8, 8, 0, 0, 0, 0, 0, 0]);
            Connection.Tracker.Observe(4, [0, 2, 0, 2, 0, 0, 0, 0]);
        }

        void Observe(byte opcode, ServerMessage message)
        {
            var builder = new NetworkPacketBuilder(ServerMessageFactory.Default.GetMessageCommand(message.GetType())!.Value);
            try
            {
                message.Serialize(ref builder);
                Connection.Tracker.Observe(opcode, builder.ToPacket().Data);
            }
            finally { builder.Dispose(); }
        }

        public async Task<ClientPacket> Request()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            byte[] header = new byte[3];
            await Remote.Peer.GetStream().ReadExactlyAsync(header, timeout.Token);
            byte[] bytes = new byte[(header[1] << 8) | header[2]];
            await Remote.Peer.GetStream().ReadExactlyAsync(bytes, timeout.Token);
            return (ClientPacket)decrypt.Decrypt(new ClientPacket(bytes[0], bytes[1..]));
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            client.Dispose();
            Remote.Dispose();
            Directory.Delete(directory, true);
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
