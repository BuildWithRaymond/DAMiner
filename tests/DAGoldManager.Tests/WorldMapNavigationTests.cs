using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Server.Types;
using DAQuester.Core;
using DAQuester.DarkAges;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

// Only loopback sockets and the packaged route geometry participate in these tests.
public class WorldMapNavigationTests
{
    [Theory]
    [InlineData(3020, 16, 0)]
    [InlineData(3071, 0, 9)]
    public async Task ToleratedPravatDestinationSelectsPravatInsteadOfAnotherWorldMapHub(int map, int x, int y)
    {
        await using var world = new WorldMapWorld(map, new(x, y));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var travel = world.Navigate(new(48, 7), 3, cancel.Token);

        var selection = await world.Selection(travel);
        Assert.Equal(3052, selection.MapId);
        world.Arrive(new(34, 24));
        await world.CompleteWalking(travel);

        Assert.Equal(new Tile(48, 10), world.Session.Snapshot.Position);
        Assert.Equal(3052, world.Session.Snapshot.Map);
        Assert.True(world.Maps.Load(3052).Blocked(new(48, 7)));
        world.AssertNoRequest();
    }

    [Fact]
    public async Task ExactBlockedDestinationDoesNotGainToleranceDuringWorldSelection()
    {
        await using var world = new WorldMapWorld(3020, new(16, 0), onlyPravat: true);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<QuestBlockedException>(() => world.Navigate(new(48, 7), 0, cancel.Token));
        Assert.Contains("no walkable recorded route", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3020, world.Session.Snapshot.Map);
        world.AssertNoRequest();
    }

    [Fact]
    public async Task RequiredRowIsStillEnforcedAfterToleratedWorldMapArrival()
    {
        await using var world = new WorldMapWorld(3020, new(16, 0));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var travel = world.Navigate(new(48, 7), 3, cancel.Token, requiredRow: 11);
        Assert.Equal(3052, (await world.Selection(travel)).MapId);
        world.Arrive(new(34, 24));

        var error = await Assert.ThrowsAsync<QuestBlockedException>(() => travel);
        Assert.Contains("row 11", error.Message);
        Assert.Equal(new Tile(34, 24), world.Session.Snapshot.Position);
        world.AssertNoRequest();
    }

    [Fact]
    public async Task FreshOccupancyIsStillEnforcedAfterToleratedWorldMapArrival()
    {
        await using var world = new WorldMapWorld(3071, new(0, 9));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var travel = world.Navigate(new(48, 7), 3, cancel.Token, requiredRow: 10);
        Assert.Equal(3052, (await world.Selection(travel)).MapId);
        world.Arrive(new(34, 24), new Entity(77, "Fictional occupant", new(48, 10), true) { IsPlayer = true });

        var error = await Assert.ThrowsAsync<QuestBlockedException>(() => travel);
        Assert.Contains("row 10", error.Message);
        Assert.Equal(new Tile(34, 24), world.Session.Snapshot.Position);
        world.AssertNoRequest();
    }

    sealed class WorldMapWorld : IAsyncDisposable
    {
        readonly SocketPair client = new(), remote = new();
        readonly ClientPacketEncryptor decrypt = new();
        readonly ServerWorldMapNode pravat = new() { MapId = 3052, Name = "Pravat", MapX = 9, MapY = 11, Checksum = 123 };
        public ProxyConnection Connection { get; }
        public GameSession Session { get; }
        public MapRepository Maps { get; }

        public WorldMapWorld(int map, Tile entrance, bool onlyPravat = false)
        {
            Connection = new(client.Connection, remote.Connection, 1, _ => Task.FromResult(0));
            Connection.Tracker.Identity("Fictional traveler", 42);
            Change(state => state with { Map = map, Position = entrance, InventoryKnown = true });
            Maps = new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json"));
            Session = new(Connection, Maps, 1);
            Observe(ServerCommand.FieldMap, new ServerFieldMapMessage
            {
                FieldName = "Fictional world map",
                Locations = onlyPravat ? [pravat] : [
                    new() { MapId = 3020, Name = "Piet", MapX = 1, MapY = 2, Checksum = 45 },
                    new() { MapId = 3071, Name = "Mehadi", MapX = 3, MapY = 4, Checksum = 67 },
                    pravat]
            });
        }

        public Task Navigate(Tile target, int tolerance, CancellationToken cancellation, int? requiredRow = null) =>
            Session.ExecuteAsync(new("navigate", 3052, target, Tolerance: tolerance)
                { StopWithinTolerance = true, TargetRow = requiredRow }, cancellation);

        public async Task<ClientFieldMapMessage> Selection(Task travel)
        {
            var packet = await Next(travel);
            var selection = Assert.IsType<ClientFieldMapMessage>(ClientMessageFactory.Default.Create(packet));
            if (selection.MapId == pravat.MapId)
            {
                Assert.Equal(pravat.Checksum, selection.Checksum);
                Assert.Equal(pravat.MapX, selection.X);
                Assert.Equal(pravat.MapY, selection.Y);
            }
            return selection;
        }

        public void Arrive(Tile landing, Entity? occupant = null)
        {
            var record = Maps.Record(3052);
            Observe(ServerCommand.MapSize, new ServerMapSizeMessage
            {
                MapId = 3052, Name = record.Name, Width = (ushort)record.Width, Height = (ushort)record.Height
            });
            if (occupant is not null)
                Change(state => state with { Entities = ImmutableDictionary<uint, Entity>.Empty.Add(occupant.Id, occupant) });
            Observe(ServerCommand.UserPosition, new ServerUserPositionMessage { X = (ushort)landing.X, Y = (ushort)landing.Y });
        }

        public async Task CompleteWalking(Task travel)
        {
            int moves = 0;
            while (!travel.IsCompleted)
            {
                var packet = await NextOrStop(travel);
                if (packet is null) break;
                Assert.Equal(ClientCommand.Move, packet.Command);
                Assert.True(++moves <= 50, "Arrival must walk to the tolerated floor without opening another world map.");
                Acknowledge(packet);
            }
            await travel;
        }

        void Acknowledge(ClientPacket packet)
        {
            var before = Connection.Tracker.Snapshot;
            var position = before.Position!;
            byte[] payload = [packet.Data[0], (byte)(position.X >> 8), (byte)position.X,
                (byte)(position.Y >> 8), (byte)position.Y, 0, 0, 0, 0, 0];
            typeof(ProxyConnection).GetMethod("ObserveMovement", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Connection, [before, (byte)0x0B, payload]);
            Connection.Tracker.Observe(0x0B, payload);
        }

        void Change(Func<CharacterSnapshot, CharacterSnapshot> change) => typeof(StateTracker)
            .GetProperty(nameof(StateTracker.Snapshot))!.SetValue(Connection.Tracker, change(Connection.Tracker.Snapshot));

        void Observe(ServerCommand command, ServerMessage message)
        {
            var builder = new NetworkPacketBuilder(command);
            try
            {
                message.Serialize(ref builder);
                Connection.Tracker.Observe((byte)command, builder.ToPacket().Data);
            }
            finally { builder.Dispose(); }
        }

        async Task<ClientPacket> Next(Task travel)
        {
            if (await NextOrStop(travel) is { } packet) return packet;
            await travel;
            throw new InvalidOperationException("Navigation ended before its expected packet.");
        }

        async Task<ClientPacket?> NextOrStop(Task travel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var packet = Request(timeout.Token);
            if (await Task.WhenAny(packet, travel) != travel) return await packet;
            timeout.Cancel();
            try { await packet; } catch (OperationCanceledException) { }
            return null;
        }

        async Task<ClientPacket> Request(CancellationToken cancellation)
        {
            byte[] header = new byte[3];
            await remote.Peer.GetStream().ReadExactlyAsync(header, cancellation);
            byte[] bytes = new byte[(header[1] << 8) | header[2]];
            await remote.Peer.GetStream().ReadExactlyAsync(bytes, cancellation);
            return (ClientPacket)decrypt.Decrypt(new ClientPacket(bytes[0], bytes[1..]));
        }

        public void AssertNoRequest() => Assert.False(remote.Peer.GetStream().DataAvailable);
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
