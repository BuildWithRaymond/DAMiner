using Arbiter.Net.Client;
using Arbiter.Net.Types;
using System.Buffers.Binary;

namespace DAGoldManager.Game;

/// <summary>Encodes supported exchange commands. Packet submission is never transaction confirmation.</summary>
public static class ExchangePackets
{
    public static ClientPacket Create(ExchangeClientActionType action, uint target, byte slot = 0, byte quantity = 1, uint gold = 0)
    {
        if (target == 0) throw new ArgumentOutOfRangeException(nameof(target));
        int length = action switch
        {
            ExchangeClientActionType.AddItem => 6,
            ExchangeClientActionType.AddStackableItem => 7,
            ExchangeClientActionType.SetGold => 9,
            ExchangeClientActionType.BeginExchange or ExchangeClientActionType.Accept or ExchangeClientActionType.Cancel => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        if (length is 6 or 7 && (slot is < 1 or > 59 || quantity == 0))
            throw new ArgumentOutOfRangeException(nameof(slot));
        var payload = new byte[length];
        // Wire layout: one action byte, four-byte player ID, then optional slot/quantity or gold.
        // The server protocol uses big-endian integers, unlike the local Windows machine.
        payload[0] = (byte)action;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(1), target);
        if (length is 6 or 7) payload[5] = slot;
        if (length == 7) payload[6] = quantity;
        if (length == 9) BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(5), gold);
        return new((byte)ClientCommand.Exchange, payload);
    }
}
