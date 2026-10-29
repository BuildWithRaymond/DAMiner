using System.Collections.Immutable;

namespace DAQuester.Core;

// A submitted destination permits reconciliation only; it never permits another payment.
public sealed record NpcTravelCheckpoint(long Generation, int Map, string Character, string Npc, DateTimeOffset ObservedAt)
{
    public Guid OperationId { get; init; }
    public int SourceMap { get; init; }
    public uint? CharacterId { get; init; }
    public long Revision { get; init; }
    public uint? GoldBefore { get; init; }
    public uint? QuotedFare { get; init; }
    public ImmutableDictionary<byte, Item>? InventoryBefore { get; init; }
}

public sealed record NpcTravelFareReceipt(NpcTravelCheckpoint Checkpoint, uint GoldAfter, long ArrivalGeneration, long ArrivalRevision);

public interface INpcConnectionHandoff
{
    NpcTravelCheckpoint? SubmittedNpcTravel { get; }
}
