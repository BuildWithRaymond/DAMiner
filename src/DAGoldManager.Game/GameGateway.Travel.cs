using System.Collections.Concurrent;
using DAGoldManager.Core;
using DAQuester.Core;

namespace DAGoldManager.Game;

public sealed partial class GameGateway
{
    const string NpcRedirectReason = "Server redirect. Resume on replacement connection.";
    readonly ConcurrentDictionary<string, NpcTravelRecovery> travelRecovery = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<CharacterConnection, byte> observedTravelConnections = new();
    readonly ConcurrentDictionary<CharacterConnection, byte> activeTravelConnections = new();
    readonly ConcurrentDictionary<CharacterConnection, NpcTravelCheckpoint> acceptedTravelRedirects = new();
    readonly SemaphoreSlim travelLedgerGate = new(1, 1);
    readonly Guid travelRunId = Guid.NewGuid();
    readonly HashSet<Guid> plannedTravel = [];
    readonly Dictionary<Guid, NpcTravelCheckpoint> startedTravel = [];
    readonly HashSet<Guid> verifiedTravel = [];
    event Action<CharacterConnection, string>? ManagedCharacterInterrupted;

    public long TravelGoldSpent(string name)
    {
        if (characters.TryGetValue(name, out var character)) CollectVerifiedTravelFares(character);
        return travelRecovery.TryGetValue(name, out var recovery) ? recovery.VerifiedGoldSpent : 0;
    }

    public async Task FlushVerifiedTravelAsync(CancellationToken cancellation)
    {
        if (travelLedger is null) return;
        // Old connections can finish a receipt just before their replacement is
        // installed. Collect them too; dictionary replacement must not lose it.
        foreach (var character in observedTravelConnections.Keys) CollectVerifiedTravelFares(character);
        foreach (var character in characters.Values)
            if (character.Snapshot.Name is { } name) Recovery(name).TryComplete(character.Snapshot);
        await travelLedgerGate.WaitAsync(cancellation);
        try
        {
            foreach (var checkpoint in travelRecovery.Values.SelectMany(recovery => recovery.VerifiedCheckpoints))
            {
                if (verifiedTravel.Contains(checkpoint.OperationId)) continue;
                await travelLedger.AppendAsync(TravelOperation(checkpoint, OperationOutcome.Verified,
                    "Own destination, exact gold change and unchanged inventory verified."), cancellation);
                verifiedTravel.Add(checkpoint.OperationId);
            }
        }
        finally { travelLedgerGate.Release(); }
    }

    async Task PersistTravelSubmissionAsync(NpcTravelCheckpoint checkpoint, CancellationToken cancellation)
    {
        if (travelLedger is null) return;
        await FlushVerifiedTravelAsync(cancellation);
        await travelLedgerGate.WaitAsync(cancellation);
        try
        {
            if (verifiedTravel.Contains(checkpoint.OperationId) || startedTravel.TryGetValue(checkpoint.OperationId, out var prior) &&
                prior.Revision >= checkpoint.Revision)
                throw new InvalidOperationException("NPC travel choice was already journaled. No choice was repeated.");
            if (!plannedTravel.Contains(checkpoint.OperationId))
            {
                await travelLedger.AppendAsync(TravelOperation(checkpoint, OperationOutcome.Planned,
                    "NPC travel intent saved before its first choice."), cancellation);
                plannedTravel.Add(checkpoint.OperationId);
            }
            // Update this same operation when a later fresh page first quotes its
            // fare. Even an unquoted service choice gets a durable ambiguity marker.
            await travelLedger.AppendAsync(TravelOperation(checkpoint, OperationOutcome.Started,
                "NPC choice may be submitted. Do not repeat without a verified arrival."), cancellation);
            startedTravel[checkpoint.OperationId] = checkpoint;
        }
        finally { travelLedgerGate.Release(); }
    }

    OperationRecord TravelOperation(NpcTravelCheckpoint checkpoint, OperationOutcome outcome, string detail) =>
        new(travelRunId, checkpoint.OperationId, "travel", checkpoint.Character, checkpoint.Npc,
            checkpoint.QuotedFare ?? 0, outcome, DateTimeOffset.UtcNow,
            $"{checkpoint.Npc}: map {checkpoint.SourceMap} -> {checkpoint.Map}; generation {checkpoint.Generation}; " +
            $"dialog revision {checkpoint.Revision}; fare {(checkpoint.QuotedFare is { } fare ? fare.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unquoted")}. {detail}");

    NpcTravelRecovery Recovery(string name) => travelRecovery.GetOrAdd(name, static value => new(value));

    // Keep this observer after Go stops. A fare already submitted can redirect later;
    // the next Go must reconcile that receipt instead of buying the passage again.
    void ObserveTravelConnection(CharacterConnection character)
    {
        if (!observedTravelConnections.TryAdd(character, 0)) return;
        character.Session.BeforeNpcTravelSubmission = PersistTravelSubmissionAsync;
        character.Connection.Interrupted += reason =>
        {
            if (reason == NpcRedirectReason) CaptureTravel(character, redirected: true);
            ManagedCharacterInterrupted?.Invoke(character, reason);
        };
        character.Session.Changed += state =>
        {
            CollectVerifiedTravelFares(character);
            if (state.Name is { } name && characters.TryGetValue(name, out var owned) && ReferenceEquals(owned, character))
                Recovery(name).TryComplete(state);
        };
        CollectVerifiedTravelFares(character);
        var current = character.Snapshot;
        if (current.Name is { } owner && characters.TryGetValue(owner, out var managed) && ReferenceEquals(managed, character))
            Recovery(owner).TryComplete(current);
    }

    void CollectVerifiedTravelFares(CharacterConnection character)
    {
        if (character.Snapshot.Name is not { } name) return;
        foreach (var receipt in character.Session.VerifiedTravelFares)
            Recovery(name).RecordVerified(receipt);
    }

    bool CaptureTravel(CharacterConnection character, bool redirected)
    {
        var state = character.Snapshot;
        if (state.Name is not { } name || character.Session.SubmittedNpcTravel is not { } submitted) return false;
        bool captured = Recovery(name).Capture(submitted, state, redirected);
        if (captured && redirected) acceptedTravelRedirects[character] = submitted;
        return captured;
    }

    // Control calls this for its specific participant. Only a known NPC redirect,
    // and that old connection's eventual disconnect, can bypass its interruption.
    bool TryAcceptTravelInterruption(CharacterConnection character, string reason)
    {
        if (reason == NpcRedirectReason && activeTravelConnections.ContainsKey(character))
            return CaptureTravel(character, redirected: true) || acceptedTravelRedirects.ContainsKey(character);
        return reason == "Disconnected" && acceptedTravelRedirects.ContainsKey(character);
    }

    async Task<bool> ResumePendingTravelAsync(string name, CancellationToken cancellation)
    {
        if (!travelRecovery.TryGetValue(name, out var recovery) || recovery.Pending is null) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            while (recovery.Pending is not null)
            {
                cancellation.ThrowIfCancellationRequested();
                if (characters.TryGetValue(name, out var character))
                {
                    ObserveTravelConnection(character);
                    recovery.TryComplete(character.Snapshot);
                }
                if (recovery.Pending is null) break;
                await Task.Delay(50, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException($"{name}'s submitted NPC travel has no verified arrival yet. No fare was repeated.");
        }
        await FlushVerifiedTravelAsync(CancellationToken.None);
        return true;
    }

    async Task<CharacterConnection> NavigateManagedAsync(string name, GameCommand command, CancellationToken cancellation)
    {
        if (command.Kind != "navigate") throw new ArgumentException("Managed travel requires a navigation command.", nameof(command));
        await ResumePendingTravelAsync(name, cancellation);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var character = GetCharacter(name);
            ObserveTravelConnection(character);
            using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            string? interruption = null;
            bool redirected = false;
            void Stop(string reason)
            {
                if (TryAcceptTravelInterruption(character, reason))
                {
                    redirected = true;
                    interrupted.Cancel();
                    return;
                }
                interruption = reason;
                interrupted.Cancel();
            }
            activeTravelConnections[character] = 0;
            character.Connection.Interrupted += Stop;
            try
            {
                try
                {
                    await character.Session.ExecuteAsync(command, interrupted.Token);
                    CollectVerifiedTravelFares(character);
                    if (CaptureTravel(character, redirected: false))
                    {
                        Recovery(name).TryComplete(character.Snapshot);
                        if (Recovery(name).Pending is not null)
                            throw new InvalidOperationException($"{name}'s NPC travel still needs its actual arrival receipt. No fare was repeated.");
                    }
                    return GetCharacter(name);
                }
                catch
                {
                    CollectVerifiedTravelFares(character);
                    CaptureTravel(character, redirected: false);
                    cancellation.ThrowIfCancellationRequested();
                    if (interruption is not null)
                        throw new InvalidOperationException(interruption);
                    if (!redirected && !acceptedTravelRedirects.ContainsKey(character)) throw;

                    await ResumePendingTravelAsync(name, cancellation);
                    if (!characters.TryGetValue(name, out var replacement) || ReferenceEquals(character, replacement) ||
                        replacement.Snapshot.Generation <= character.Snapshot.Generation)
                        throw new InvalidOperationException($"{name}'s NPC redirect has no fresh owned replacement connection. No fare was repeated.");
                    // The replacement's observed landing drives a new route to the
                    // original goal. The submitted NPC choice is never replayed.
                }
            }
            finally
            {
                character.Connection.Interrupted -= Stop;
                activeTravelConnections.TryRemove(character, out _);
                // Cancellation stops game actions; an already proved fare still
                // needs its durable receipt so the next Go does not request review.
                await FlushVerifiedTravelAsync(CancellationToken.None);
            }
        }
    }
}

/// <summary>Retains one ambiguous NPC submission and counts only exact arrival receipts.</summary>
public sealed class NpcTravelRecovery(string character)
{
    readonly object gate = new();
    readonly Dictionary<Guid, NpcTravelCheckpoint> verifiedFares = [];
    NpcTravelCheckpoint? pending;
    bool redirectAccepted;

    public NpcTravelCheckpoint? Pending { get { lock (gate) return pending; } }
    public long VerifiedGoldSpent { get { lock (gate) return verifiedFares.Values.Sum(checkpoint => (long)(checkpoint.QuotedFare ?? 0)); } }
    public IReadOnlyList<NpcTravelCheckpoint> VerifiedCheckpoints { get { lock (gate) return verifiedFares.Values.ToArray(); } }

    public bool Capture(NpcTravelCheckpoint checkpoint, CharacterSnapshot source, bool redirected)
    {
        lock (gate)
        {
            if (checkpoint.OperationId == Guid.Empty || !OwnName(checkpoint.Character) || !OwnName(source.Name) ||
                checkpoint.Generation != source.Generation || checkpoint.CharacterId is null || checkpoint.CharacterId != source.Id ||
                checkpoint.Revision <= 0 || checkpoint.Revision > source.Revision || verifiedFares.ContainsKey(checkpoint.OperationId))
                return false;
            if (pending is not null && pending.OperationId != checkpoint.OperationId) return false;
            pending ??= checkpoint;
            redirectAccepted |= redirected;
            return true;
        }
    }

    public bool TryComplete(CharacterSnapshot arrival)
    {
        lock (gate)
        {
            if (pending is not { } checkpoint || !Ready(arrival) || !OwnName(arrival.Name) || arrival.Map != checkpoint.Map ||
                arrival.Dialog is not null || (redirectAccepted ? arrival.Generation <= checkpoint.Generation :
                    arrival.Generation != checkpoint.Generation || arrival.Id != checkpoint.CharacterId || arrival.Revision <= checkpoint.Revision) ||
                !ExactFare(checkpoint, arrival.Gold) || !SameInventory(checkpoint, arrival))
                return false;
            Complete(checkpoint);
            return true;
        }
    }

    // GameSession creates these receipts only after checking the actual destination,
    // exact quoted gold delta and unchanged server-observed inventory.
    public void RecordVerified(NpcTravelFareReceipt receipt)
    {
        lock (gate)
        {
            var checkpoint = receipt.Checkpoint;
            if (checkpoint.OperationId == Guid.Empty || !OwnName(checkpoint.Character) || checkpoint.InventoryBefore is null ||
                !ExactFare(checkpoint, receipt.GoldAfter) || receipt.ArrivalGeneration != checkpoint.Generation ||
                receipt.ArrivalRevision <= checkpoint.Revision)
                return;
            Complete(checkpoint);
        }
    }

    void Complete(NpcTravelCheckpoint checkpoint)
    {
        verifiedFares.TryAdd(checkpoint.OperationId, checkpoint);
        if (pending?.OperationId == checkpoint.OperationId)
        {
            pending = null;
            redirectAccepted = false;
        }
    }

    bool OwnName(string? name) => string.Equals(character, name, StringComparison.OrdinalIgnoreCase);
    static bool Ready(CharacterSnapshot state) => state.Connected && state.Name is not null && state.Id is not null &&
        state.Map is not null && state.Position is not null && state.Gold is not null && state.InventoryKnown &&
        state.Weight is not null && state.MaxWeight is not null;
    static bool ExactFare(NpcTravelCheckpoint checkpoint, uint? after) => checkpoint.GoldBefore is { } before &&
        after is { } gold && before >= gold && before - gold == (checkpoint.QuotedFare ?? 0);
    static bool SameInventory(NpcTravelCheckpoint checkpoint, CharacterSnapshot arrival) =>
        checkpoint.InventoryBefore is { } inventory && inventory.Count == arrival.Inventory.Count &&
        inventory.All(item => arrival.Inventory.TryGetValue(item.Key, out var actual) && item.Value == actual);
}
