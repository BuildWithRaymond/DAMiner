using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DAQuester.Core;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

public class MovementRecoveryTests
{
    [Fact]
    public void SoleRejectedMoveCanReanchorToTheAuthoritativePosition()
    {
        using var world = new MovementWorld();
        world.Queue(new(10, 10));
        world.PositionCorrection();
        Assert.True(world.Connection.Movement.Recovery);
        Assert.True(world.Connection.Movement.CanReanchor);
        Assert.Equal(new Tile(10, 10), world.Connection.Tracker.Snapshot.Position);
        world.Connection.AcceptServerPosition();
        Assert.False(world.Connection.Movement.Recovery);
        Assert.False(world.Connection.Movement.CanReanchor);
    }

    [Fact]
    public void SeveralInFlightMovesRetainTheRefreshBarrier()
    {
        using var world = new MovementWorld();
        world.Queue(new(10, 10));
        world.Queue(new(10, 9));
        world.PositionCorrection();
        Assert.True(world.Connection.Movement.Recovery);
        Assert.False(world.Connection.Movement.CanReanchor);
    }

    [Fact]
    public void AcceptingASettledPositionDoesNotClearPendingMoves()
    {
        using var world = new MovementWorld();
        world.Queue(new(10, 10));
        world.Connection.AcceptServerPosition();
        Assert.Equal(1, world.Connection.Movement.Pending);
    }

    sealed class MovementWorld : IDisposable
    {
        readonly SocketPair client = new(), remote = new();
        public ProxyConnection Connection { get; }
        public MovementWorld()
        {
            Connection = new(client.Connection, remote.Connection, 1, _ => Task.FromResult(0));
            Connection.Tracker.Identity("Fictional", 1);
            typeof(StateTracker).GetProperty(nameof(StateTracker.Snapshot))!.SetValue(Connection.Tracker,
                Connection.Tracker.Snapshot with { Map = 502, Position = new(10, 10) });
        }
        public void Queue(Tile origin) => typeof(ProxyConnection)
            .GetMethod("RegisterMovement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Connection, [(byte)0, 1L, origin, true, 3]);
        public void PositionCorrection() => typeof(ProxyConnection)
            .GetMethod("ObserveMovement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Connection, [Connection.Tracker.Snapshot, (byte)0x04, new byte[] { 0, 10, 0, 10 }]);
        public void Dispose() { client.Dispose(); remote.Dispose(); }
    }

    sealed class SocketPair : IDisposable
    {
        public TcpClient Connection { get; } = new();
        readonly TcpClient peer;
        public SocketPair()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                Connection.Connect((IPEndPoint)listener.LocalEndpoint);
                peer = listener.AcceptTcpClient();
            }
            finally { listener.Stop(); }
        }
        public void Dispose() { Connection.Dispose(); peer.Dispose(); }
    }
}
