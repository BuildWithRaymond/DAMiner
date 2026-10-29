using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Arbiter.Net.Client;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Server.Types;
using Arbiter.Net.Types;
using DAQuester.Core;
using DAQuester.DarkAges;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

// Simulated sockets use the packaged Loures Harbor Shop geometry, never a game connection.
public class NpcTravelApproachTests
{
    [Theory]
    [InlineData(8, 12, 4)]
    [InlineData(8, 7, 0)]
    public async Task QuardBehindCounterIsOpenedFromVisiblePublicFloor(int x, int y, int expectedMoves)
    {
        await using var world = new HarborWorld(new(3, 3), new(x, y));
        using var cancel = new CancellationTokenSource();
        var travel = world.Travel(cancel.Token);
        int moves = 0;
        while (true)
        {
            var packet = await world.Next(travel);
            if (packet.Command == ClientCommand.RequestObjectInfo) break;
            Assert.Equal(ClientCommand.Move, packet.Command);
            Assert.True(++moves <= expectedMoves, "Quard must be used from public floor, not approached behind the counter.");
            world.Acknowledge(packet);
        }
        Assert.Equal(expectedMoves, moves);
        var position = world.Session.Snapshot.Position!;
        Assert.InRange(Math.Abs(position.X - 3) + Math.Abs(position.Y - 3), 4, 10);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);
        Assert.Null(world.Session.SubmittedNpcTravel);
    }

    [Fact]
    public async Task PlannedInteriorApproachPrecedesAnUnreachableNpcPosition()
    {
        await using var world = new HarborWorld(new(2, 2));
        using var cancel = new CancellationTokenSource();
        var travel = world.Travel(cancel.Token);
        for (int step = 0; step < 2; step++)
        {
            var packet = await world.Next(travel);
            Assert.Equal(ClientCommand.Move, packet.Command);
            world.Acknowledge(packet, revealNpc: step == 1);
        }
        Assert.Equal(new Tile(7, 10), world.Session.Snapshot.Position);
        Assert.Equal(ClientCommand.RequestObjectInfo, (await world.Next(travel)).Command);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);
        Assert.Null(world.Session.SubmittedNpcTravel);
    }

    [Fact]
    public async Task UnseenNpcCanAppearAfterWalkingBeyondInitialInteractionRange()
    {
        await using var world = new HarborWorld();
        using var cancel = new CancellationTokenSource();
        var travel = world.Travel(cancel.Token);
        for (int step = 0; step < 3; step++)
        {
            var packet = await world.Next(travel);
            Assert.Equal(ClientCommand.Move, packet.Command);
            world.Acknowledge(packet, revealNpc: step == 2);
        }
        Assert.Equal(new Tile(7, 9), world.Session.Snapshot.Position);
        Assert.Equal(ClientCommand.RequestObjectInfo, (await world.Next(travel)).Command);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);
    }

    [Fact]
    public async Task AlreadyObservedNpcInRangeCanBeOpenedWithoutWalking()
    {
        await using var world = new HarborWorld(new(7, 7), new(7, 10));
        using var cancel = new CancellationTokenSource();
        var travel = world.Travel(cancel.Token);
        Assert.Equal(ClientCommand.RequestObjectInfo, (await world.Next(travel)).Command);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedNpcDrawCanUseTheValidFloorWhenCloserTilesAreOccupied(bool staleNpc)
    {
        await using var world = new HarborWorld(staleNpc ? new(2, 2) : null, new(7, 10));
        var grid = new MapRepository(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json")).Load(6926);
        var occupied = Enumerable.Range(0, 14).SelectMany(y => Enumerable.Range(0, 14).Select(x => new Tile(x, y)))
            .Where(tile => !grid.Blocked(tile) && tile != new Tile(7, 7) && Math.Abs(tile.X - 7) + Math.Abs(tile.Y - 7) <= 2)
            .Select((tile, index) => new Entity((uint)(100 + index), "Fictional occupant", tile, true) { IsPlayer = true });
        world.Change(state => state with { Entities = state.Entities.AddRange(occupied.Select(entity => KeyValuePair.Create(entity.Id, entity))) });
        using var cancel = new CancellationTokenSource();
        var travel = world.Travel(cancel.Token);
        await Task.Delay(30);
        Assert.False(travel.IsCompleted, "The safe range-three floor must allow time for the NPC draw.");
        world.DrawNpc();
        Assert.Equal(ClientCommand.RequestObjectInfo, (await world.Next(travel)).Command);
        Assert.Equal(new Tile(7, 10), world.Session.Snapshot.Position);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);
        world.AssertNoRequest();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PopupDuringDiscoveryIsPreservedWithoutOpeningNpc(bool textPopup)
    {
        await using var world = new HarborWorld();
        var travel = world.Travel();
        var packet = await world.Next(travel);
        var dialog = new Dialog(1, world.Session.Snapshot.Revision + 1, 99, 1, "Fictional popup",
            "Inspect this popup", true, 1, 1, 0, []);
        var popup = new ServerNotice(1, world.Session.Snapshot.Revision + 1, 9, "Inspect this popup", DateTimeOffset.UtcNow);
        world.Change(state => textPopup ? state with { Popup = popup } : state with { Dialog = dialog });
        world.Acknowledge(packet, revealNpc: true);
        var error = await Assert.ThrowsAsync<QuestBlockedException>(() => travel);
        Assert.Contains("popup", error.Message, StringComparison.OrdinalIgnoreCase);
        if (textPopup) Assert.Same(popup, world.Session.Snapshot.Popup);
        else Assert.Same(dialog, world.Session.Snapshot.Dialog);
        world.AssertNoRequest();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("identity")]
    [InlineData("map")]
    public async Task ChangedApproachOwnerCannotOpenNpc(string changed)
    {
        await using var world = new HarborWorld();
        var travel = world.Travel();
        var packet = await world.Next(travel);
        world.Change(state => changed switch
        {
            "generation" => state with { Generation = 2 },
            "identity" => state with { Id = 43, Name = "Fictional replacement" },
            _ => state with { Map = 10028 }
        });
        world.Acknowledge(packet, revealNpc: true);
        await Assert.ThrowsAnyAsync<Exception>(() => travel);
        world.AssertNoRequest();
    }

    [Fact]
    public async Task CancellingDuringDiscoverySendsNoNpcRequest()
    {
        await using var world = new HarborWorld();
        using var cancel = new CancellationTokenSource();
        var travel = world.Travel(cancel.Token);
        Assert.Equal(ClientCommand.Move, (await world.Next(travel)).Command);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => travel);
        world.AssertNoRequest();
    }

    [Fact]
    public async Task MissingNpcStopsAfterBoundedInteriorDiscoveryWithContext()
    {
        await using var world = new HarborWorld();
        var travel = world.Travel();
        int moves = 0;
        while (!travel.IsCompleted)
        {
            var packet = await world.NextOrStop(travel);
            if (packet is null) break;
            Assert.Equal(ClientCommand.Move, packet.Command);
            Assert.True(++moves <= 4, "Discovery must stop beside the recorded anchor.");
            world.Acknowledge(packet);
        }
        var error = await Assert.ThrowsAsync<QuestBlockedException>(() => travel);
        Assert.Equal(4, moves);
        Assert.Contains("6926", error.Message);
        Assert.Contains("7,12", error.Message);
        Assert.Contains("7,7", error.Message);
        world.AssertNoRequest();
    }

    [Fact]
    public async Task UnreachableObservedNpcAfterDiscoveryIsNotOpened()
    {
        await using var world = new HarborWorld(new(2, 2));
        var travel = world.Travel();
        int moves = 0;
        while (!travel.IsCompleted)
        {
            var packet = await world.NextOrStop(travel);
            if (packet is null) break;
            Assert.Equal(ClientCommand.Move, packet.Command);
            Assert.True(++moves <= 4);
            world.Acknowledge(packet);
        }
        var error = await Assert.ThrowsAsync<QuestBlockedException>(() => travel);
        Assert.Equal(4, moves);
        Assert.Contains("2,2", error.Message);
        world.AssertNoRequest();
    }

    sealed class HarborWorld : IAsyncDisposable
    {
        readonly SocketPair client = new(), remote = new();
        readonly ClientPacketEncryptor decrypt = new();
        public ProxyConnection Connection { get; }
        public GameSession Session { get; }
        public HarborWorld(Tile? npc = null, Tile? start = null)
        {
            Connection = new(client.Connection, remote.Connection, 1, _ => Task.FromResult(0));
            Connection.Tracker.Identity("Fictional traveler", 42);
            Change(state => state with
            {
                Map = 6926, Position = start ?? new(7, 12), Gold = 20000, InventoryKnown = true,
                Entities = npc is null ? ImmutableDictionary<uint, Entity>.Empty :
                    ImmutableDictionary<uint, Entity>.Empty.Add(77, new(77, "Quard", npc, true))
            });
            Session = new(Connection, new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json")), 1);
        }

        public Task Travel(CancellationToken cancellation = default) => Session.ExecuteAsync(new("navigate", 10028, new(43, 2)), cancellation);
        public void Change(Func<CharacterSnapshot, CharacterSnapshot> change) => typeof(StateTracker)
            .GetProperty(nameof(StateTracker.Snapshot))!.SetValue(Connection.Tracker, change(Connection.Tracker.Snapshot));

        public void Acknowledge(ClientPacket packet, bool revealNpc = false)
        {
            Assert.Equal(ClientCommand.Move, packet.Command);
            if (revealNpc)
                Change(state => state with { Entities = state.Entities.SetItem(77, new(77, "Quard", new(7, 7), true)) });
            var before = Connection.Tracker.Snapshot;
            var position = before.Position!;
            byte[] payload = [packet.Data[0], (byte)(position.X >> 8), (byte)position.X,
                (byte)(position.Y >> 8), (byte)position.Y, 0, 0, 0, 0, 0];
            typeof(ProxyConnection).GetMethod("ObserveMovement", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Connection, [before, (byte)0x0B, payload]);
            Connection.Tracker.Observe(0x0B, payload);
        }

        public void DrawNpc()
        {
            var message = new ServerDrawObjectsMessage
            {
                Entities = [new ServerCreatureEntity { Id = 77, Name = "Quard", X = 7, Y = 7, CreatureType = CreatureType.Mundane }]
            };
            var builder = new NetworkPacketBuilder(Arbiter.Net.Server.ServerCommand.DrawObjects);
            try
            {
                message.Serialize(ref builder);
                Connection.Tracker.Observe(0x07, builder.ToPacket().Data);
            }
            finally { builder.Dispose(); }
        }

        public async Task<ClientPacket> Next(Task travel)
        {
            if (await NextOrStop(travel) is { } packet) return packet;
            await travel;
            throw new InvalidOperationException("Travel stopped before its expected packet.");
        }
        public async Task<ClientPacket?> NextOrStop(Task travel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var packet = Request(timeout.Token);
            if (await Task.WhenAny(packet, travel) == travel)
            {
                timeout.Cancel();
                try { await packet; } catch (OperationCanceledException) { }
                return null;
            }
            return await packet;
        }
        async Task<ClientPacket> Request(CancellationToken cancellation)
        {
            byte[] header = new byte[3];
            await remote.Peer.GetStream().ReadExactlyAsync(header, cancellation);
            byte[] bytes = new byte[(header[1] << 8) | header[2]];
            await remote.Peer.GetStream().ReadExactlyAsync(bytes, cancellation);
            return (ClientPacket)decrypt.Decrypt(new ClientPacket(bytes[0], bytes[1..]));
        }
        public void AssertNoRequest()
        {
            Assert.False(remote.Peer.GetStream().DataAvailable);
            Assert.Null(Session.SubmittedNpcTravel);
        }
        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            client.Dispose(); remote.Dispose();
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
