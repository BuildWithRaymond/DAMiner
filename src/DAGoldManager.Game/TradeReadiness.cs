using DAQuester.Core;

namespace DAGoldManager.Game;

/// <summary>Checks authoritative identities and locations independently of packets or Windows.</summary>
public static class TradeReadiness
{
    public static void ValidateIdentities(CharacterSnapshot sender, CharacterSnapshot? receiver, Entity target,
        CharacterSnapshot senderBefore, CharacterSnapshot? receiverBefore)
    {
        RequireSameIdentity(sender, senderBefore, "Sender");
        VisiblePlayer(sender, target.Id, target.Name);
        if (receiver is null) return;
        if (receiverBefore is null) throw new InvalidOperationException("Managed receiver identity is missing.");
        RequireSameIdentity(receiver, receiverBefore, "Managed receiver");
        if (receiver.Id != target.Id || !target.Name.Equals(receiver.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Managed receiver changed identity.");
        VisiblePlayer(receiver, senderBefore.Id!.Value, senderBefore.Name!);
    }

    // Own movement acknowledgments can precede the other player's draw or movement
    // packet. Wait for both server views without inventing either player's position.
    public static bool ManagedParticipantsReady(CharacterSnapshot sender, CharacterSnapshot receiver)
    {
        if (!sender.Connected || !receiver.Connected || sender.Id is null || receiver.Id is null || sender.Name is null || receiver.Name is null ||
            sender.Map is null || sender.Map != receiver.Map || sender.Position is null || receiver.Position is null ||
            Distance(sender.Position, receiver.Position) > 2) return false;
        var receiverVisible = VisiblePlayer(sender, receiver.Id.Value, receiver.Name);
        var senderVisible = VisiblePlayer(receiver, sender.Id.Value, sender.Name);
        return receiverVisible is not null && senderVisible is not null &&
            Distance(sender.Position, receiverVisible.Position) <= 2 && Distance(receiver.Position, senderVisible.Position) <= 2;
    }

    public static void Validate(CharacterSnapshot sender, CharacterSnapshot? receiver, Entity target,
        long senderGeneration, long? receiverGeneration)
    {
        if (!sender.Connected || sender.Generation != senderGeneration || sender.Id is null || sender.Position is null || sender.Map is null)
            throw new InvalidOperationException("Sender disconnected or changed before the trade response.");
        var visible = VisiblePlayer(sender, target.Id, target.Name);
        if (visible is null)
            throw new InvalidOperationException("Trade partner is no longer uniquely visible.");
        if (Distance(sender.Position, visible.Position) > 2)
            throw new InvalidOperationException("Trade partner moved out of range. No further trade command was sent.");
        if (receiver is null) return;
        if (!receiver.Connected || receiver.Generation != receiverGeneration || receiver.Id != target.Id ||
            !target.Name.Equals(receiver.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Managed receiver disconnected or changed identity.");
        if (receiver.Map != sender.Map || receiver.Position is null || Distance(sender.Position, receiver.Position) > 2)
            throw new InvalidOperationException("Managed participants are no longer together within trade range.");
        if (!ManagedParticipantsReady(sender, receiver))
            throw new InvalidOperationException("Managed players are no longer mutually visible within trade range.");
    }

    public static bool MutuallyVisible(CharacterSnapshot sender, CharacterSnapshot? receiver) => receiver is null ||
        sender.Id is { } id && receiver.Entities.TryGetValue(id, out var visible) && visible.IsPlayer &&
        visible.Name.Equals(sender.Name, StringComparison.OrdinalIgnoreCase);

    public static void RequireClosedWindows(bool senderOpen, bool receiverOpen)
    {
        if (senderOpen || receiverOpen)
            throw new InvalidOperationException("A managed character has an existing or unconfirmed exchange window. Inspect and close any open exchange before a fresh run; no new request was sent.");
    }

    static void RequireSameIdentity(CharacterSnapshot current, CharacterSnapshot before, string role)
    {
        if (!current.Connected || current.Generation != before.Generation || before.Id is null || current.Id != before.Id ||
            before.Name is null || !before.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{role} disconnected or changed identity before the trade response.");
    }

    static Entity? VisiblePlayer(CharacterSnapshot state, uint id, string name)
    {
        var matches = state.Entities.Values.Where(entity => entity.IsPlayer && entity.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1 || matches.Length == 1 && matches[0].Id != id ||
            state.Entities.TryGetValue(id, out var sameId) && (!sameId.IsPlayer || !sameId.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Trade partner is not uniquely visible with the expected identity.");
        return matches.SingleOrDefault();
    }

    static int Distance(Tile a, Tile b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
