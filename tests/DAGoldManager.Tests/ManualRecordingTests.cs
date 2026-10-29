using System.Net.Sockets;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAGoldManager.Game;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

public class ManualRecordingTests
{
    [Theory]
    [InlineData(WorldMessageType.BarMessage, 3)]
    [InlineData(WorldMessageType.Popup, 3)]
    [InlineData(WorldMessageType.Whisper, 0)]
    public void SampleTradeRequiresServerCompletionAfterExactInventoryLoss(WorldMessageType type, int expectedBatch)
    {
        using var client = new TcpClient();
        using var remote = new TcpClient();
        var connection = new ProxyConnection(client, remote, 1, _ => Task.FromResult(0));
        connection.Tracker.Identity("Fictional", 1);
        var character = new CharacterConnection(connection,
            new MapRepository(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json")), 400);
        Observe(connection.Tracker, new ServerAddInventoryMessage { Slot = 1, Name = "Gold Bar", Quantity = 3 });
        var recorder = new SetupRecorder();
        using var recording = new ManualRecording(character, recorder, () => { });
        Observe(connection.Tracker, new ServerExchangeMessage
        {
            Event = ExchangeServerEventType.Started, TargetId = 2, TargetName = "Receiver"
        });
        Observe(connection.Tracker, new ServerExchangeMessage
        {
            Event = ExchangeServerEventType.ItemAdded, Party = ExchangeParty.You,
            ItemIndex = 1, ItemSprite = 1, ItemName = "Gold Bar(3)"
        });
        Observe(connection.Tracker, new ServerRemoveInventoryMessage { Slot = 1 });
        Assert.Equal(0, recorder.VerifiedBatchSize);
        Observe(connection.Tracker, new ServerSystemMessage { MessageType = type, Message = "You have exchanged." });
        Assert.Null(recording.Error);
        Assert.Equal(expectedBatch, recorder.VerifiedBatchSize);
    }

    static void Observe(StateTracker tracker, ServerMessage message)
    {
        var command = ServerMessageFactory.Default.GetMessageCommand(message.GetType())!.Value;
        var builder = new NetworkPacketBuilder(command);
        try
        {
            message.Serialize(ref builder);
            var packet = (ServerPacket)builder.ToPacket();
            tracker.Observe((byte)command, packet.Data);
        }
        finally { builder.Dispose(); }
    }
}
