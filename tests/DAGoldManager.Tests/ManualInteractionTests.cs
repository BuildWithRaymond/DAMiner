using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Arbiter.Net.Client;
using DAGoldManager.Game;
using DAQuester.Core;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

public class ManualInteractionTests
{
    [Theory]
    [InlineData(0x06)]
    [InlineData(0x11)]
    [InlineData(0x39)]
    [InlineData(0x3A)]
    [InlineData(0x43)]
    [InlineData(0x4A)]
    public async Task NativeGameplayContinuesControlledWorkAndRecording(byte opcode)
    {
        await using var world = new NativeWorld();
        await world.Control(async cancellation =>
        {
            byte[] payload = opcode == 0x06 ? [1, 0] : [0, 0, 0, 0, 2];
            var forwarded = await world.Native(opcode, payload);
            Assert.Equal(opcode, (byte)forwarded.Command);
            Assert.Equal(payload, forwarded.Data);
            Assert.False(cancellation.IsCancellationRequested);
            Assert.Empty(world.Interruptions);
            Assert.Equal(opcode is 0x39 or 0x3A ? new[] { opcode } : [], world.DialogActions.ToArray());
            Assert.Equal(opcode is 0x39 or 0x3A or 0x43 or 0x4A ? new[] { opcode } : [], world.NativeActions.ToArray());

            await world.Connection.SendAsync(new ClientPacket(0x38, []), 1, cancellation);
            Assert.Equal(0x38, (byte)(await world.Request()).Command);
        });
    }

    [Fact]
    public async Task NativeWalkingReconcilesQueuedMovementWithoutStoppingWorkOrInventingPosition()
    {
        await using var world = new NativeWorld();
        await world.Control(async cancellation =>
        {
            await world.Connection.SendAsync(new ClientPacket(0x06, [0, 0]), 1, cancellation,
                movementOrigin: new(10, 10), movementCapacity: 3);
            await world.Request();
            Assert.Equal(1, world.Connection.Movement.Pending);
            Assert.False(world.Connection.Movement.Recovery);

            var forwarded = await world.Native(0x06, [1, 99]);
            Assert.Equal(new byte[] { 1, 1 }, forwarded.Data);
            Assert.False(cancellation.IsCancellationRequested);
            Assert.Empty(world.Interruptions);
            Assert.True(world.Connection.Movement.Recovery);
            Assert.Equal(1, world.Connection.Movement.Pending);
            Assert.Equal(new Tile(10, 10), world.Connection.Tracker.Snapshot.Position);
        });
    }

    [Fact]
    public async Task ServerDisconnectStillInterruptsControlledWork()
    {
        await using var world = new NativeWorld();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Control(async cancellation =>
        {
            world.DisconnectServer();
            await Task.Delay(Timeout.Infinite, cancellation);
        }));
        Assert.Contains("Disconnected", error.Message);
    }

    sealed class NativeWorld : IAsyncDisposable
    {
        readonly SocketPair client = new(), remote = new();
        readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        readonly ClientPacketEncryptor nativeCrypto = new(), serverCrypto = new();
        readonly GameGateway gateway = new(new(), null, _ => throw new InvalidOperationException("No real credentials."), "");
        readonly CharacterConnection character;
        readonly Task transport;
        byte nativeSequence;
        public ProxyConnection Connection { get; }
        public ConcurrentQueue<string> Interruptions { get; } = new();
        public ConcurrentQueue<byte> DialogActions { get; } = new();
        public ConcurrentQueue<byte> NativeActions { get; } = new();

        public NativeWorld()
        {
            Connection = new(client.Connection, remote.Connection, 1, _ => Task.FromResult(0));
            Connection.Tracker.Identity("Fictional player", 1);
            typeof(StateTracker).GetProperty(nameof(StateTracker.Snapshot))!.SetValue(Connection.Tracker,
                Connection.Tracker.Snapshot with { Map = 502, Position = new(10, 10) });
            character = new(Connection, new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json")), 400);
            Connection.Interrupted += Interruptions.Enqueue;
            Connection.DialogAction += packet => DialogActions.Enqueue((byte)packet.Command);
            Connection.NativeGameAction += packet => NativeActions.Enqueue((byte)packet.Command);
            transport = Connection.RunAsync(deadline.Token);
        }

        public Task Control(Func<CancellationToken, Task> action) =>
            (Task)typeof(GameGateway).GetMethod("Control", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(gateway, [new[] { character }, action, deadline.Token])!;

        public async Task<ClientPacket> Native(byte opcode, byte[] payload)
        {
            var packet = nativeCrypto.Encrypt(new ClientPacket(opcode, payload), nativeSequence++);
            await client.Peer.GetStream().WriteAsync(packet.ToArray(), deadline.Token);
            return await Request();
        }

        public async Task<ClientPacket> Request()
        {
            byte[] header = new byte[3];
            await remote.Peer.GetStream().ReadExactlyAsync(header, deadline.Token);
            byte[] frame = new byte[(header[1] << 8) | header[2]];
            await remote.Peer.GetStream().ReadExactlyAsync(frame, deadline.Token);
            return (ClientPacket)serverCrypto.Decrypt(new ClientPacket(frame[0], frame[1..]));
        }

        public void DisconnectServer() => remote.Peer.Dispose();

        public async ValueTask DisposeAsync()
        {
            deadline.Cancel();
            await transport;
            await Connection.DisposeAsync();
            client.Dispose();
            remote.Dispose();
            deadline.Dispose();
        }
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
