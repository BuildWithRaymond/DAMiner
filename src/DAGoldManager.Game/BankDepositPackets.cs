using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Serialization;
using Arbiter.Net.Types;
using System.Buffers.Binary;

namespace DAGoldManager.Game;

/// <summary>Builds slot-based deposit requests; the gateway must independently verify their outcome.</summary>
public static class BankDepositPackets
{
    public static ClientPacket Create(uint npc, byte slot, uint stackQuantity)
    {
        if (npc == 0 || slot is < 1 or > 59 || stackQuantity == 0) throw new ArgumentOutOfRangeException(nameof(slot));
        // DAOrganizer's verified merchant format: 0x53 for a single item;
        // 0x54 carries an inventory slot followed by a string quantity.
        var payload = new byte[stackQuantity == 1 ? 8 : 10];
        payload[0] = (byte)EntityTypeFlags.Creature;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(1), npc);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(5), (ushort)(stackQuantity == 1 ? 0x53 : 0x54));
        payload[7] = slot;
        if (stackQuantity > 1) { payload[8] = 1; payload[9] = (byte)'1'; }
        return new((byte)ClientCommand.Merchant, payload);
    }

    public static ClientPacket Close(uint npc)
    {
        var message = new ClientPursuitMessage
        {
            EntityType = EntityTypeFlags.Creature, EntityId = npc,
            PursuitId = 0, StepId = 1, ArgsType = DialogArgsType.None
        };
        var builder = new NetworkPacketBuilder(ClientCommand.Pursuit);
        try { message.Serialize(ref builder); return (ClientPacket)builder.ToPacket(); }
        finally { builder.Dispose(); }
    }
}
