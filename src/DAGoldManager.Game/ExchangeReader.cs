using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;

namespace DAGoldManager.Game;

/// <summary>Decodes exchange responses without treating an outgoing command as server acceptance.</summary>
public static class ExchangeReader
{
    public static ServerExchangeMessage Read(byte[] payload)
    {
        // DAOrganizer's recorded completion subtype has no message string.
        if (payload.Length == 2 && payload[0] == (byte)ExchangeServerEventType.Accepted && payload[1] == 2)
            return new() { Event = ExchangeServerEventType.Accepted, Party = (ExchangeParty)2 };
        return ServerMessageFactory.Default.Create(new ServerPacket((byte)ServerCommand.Exchange, payload)) as ServerExchangeMessage
            ?? throw new InvalidDataException("Expected an exchange response.");
    }
}
