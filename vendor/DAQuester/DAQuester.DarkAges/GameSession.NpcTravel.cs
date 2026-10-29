using System.Globalization;
using System.Text.RegularExpressions;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Types;
using DAQuester.Core;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Protocol;

namespace DAQuester.DarkAges;

public sealed partial class GameSession : INpcConnectionHandoff
{
    sealed record PendingNpcTravel(NpcTravelCheckpoint Checkpoint, uint EntityId, long Revision, NpcTransfer Transfer,
        bool Submitted = false, bool ServiceSelected = false, bool DestinationSelected = false);
    volatile PendingNpcTravel? npcTravel;
    readonly System.Collections.Concurrent.ConcurrentQueue<NpcTravelFareReceipt> verifiedTravelFares = new();
    public IReadOnlyList<NpcTravelFareReceipt> VerifiedTravelFares => verifiedTravelFares.ToArray();
    public Func<NpcTravelCheckpoint, CancellationToken, Task>? BeforeNpcTravelSubmission { get; set; }
    public NpcTravelCheckpoint? SubmittedNpcTravel => npcTravel is { Submitted: true, DestinationSelected: true } pending
        ? pending.Checkpoint : null;

    static bool NpcTravelOwner(PendingNpcTravel pending, CharacterSnapshot state) => state.Connected &&
        state.Generation == pending.Checkpoint.Generation && state.Id == pending.Checkpoint.CharacterId &&
        string.Equals(state.Name, pending.Checkpoint.Character, StringComparison.OrdinalIgnoreCase);
    static bool NpcArrival(PendingNpcTravel pending, CharacterSnapshot state) => NpcTravelOwner(pending, state) &&
        state.Revision > pending.Revision && state.Map == pending.Transfer.Map && state.Position is not null && state.Dialog is null;

    static bool SameTravelInventory(NpcTravelCheckpoint receipt, CharacterSnapshot state) =>
        state.InventoryKnown && receipt.InventoryBefore is not null && receipt.InventoryBefore.Count == state.Inventory.Count &&
        receipt.InventoryBefore.All(item => state.Inventory.TryGetValue(item.Key, out var current) && item.Value == current);

    static bool ExactTravelGold(NpcTravelCheckpoint receipt, CharacterSnapshot state) =>
        receipt.GoldBefore is { } before && state.Gold is { } after && before >= after && before - after == (receipt.QuotedFare ?? 0);

    async Task<bool> FinishNpcTravel(CancellationToken ct)
    {
        var state = Snapshot;
        if (npcTravel is not { } pending || !NpcArrival(pending, state)) return false;
        var receipt = pending.Checkpoint;
        if (!SameTravelInventory(receipt, state))
            throw new QuestBlockedException("Travel arrival has unexpected gold or inventory. Reconcile this fare; travel will not be repeated.");
        if (!ExactTravelGold(receipt, state))
            try
            {
                // Position and account status arrive separately. Waiting for the
                // quoted debit does not authorize another fare submission.
                await WaitAsync(candidate => NpcArrival(pending, candidate) && ExactTravelGold(receipt, candidate),
                    pending.Revision, TimeSpan.FromSeconds(5), ct);
                state = Snapshot;
            }
            catch (TimeoutException)
            {
                throw new QuestBlockedException("Travel gold receipt is missing or differs from its quote. Reconcile this fare; travel will not be repeated.");
            }
        if (!NpcArrival(pending, state) || !ExactTravelGold(receipt, state) || !SameTravelInventory(receipt, state))
            throw new QuestBlockedException("Travel arrival changed before fare verification. Travel will not be repeated.");
        verifiedTravelFares.Enqueue(new(receipt, state.Gold!.Value, state.Generation, state.Revision));
        npcTravel = null;
        return true;
    }

    async Task ReconcileNpcTravel(CancellationToken ct)
    {
        if (npcTravel is { } pending)
        {
            if (!await FinishNpcTravel(ct))
            {
                if (!pending.Submitted && NpcTravelOwner(pending, Snapshot) &&
                    Snapshot.Map == pending.Checkpoint.SourceMap && Snapshot.Dialog is null)
                    npcTravel = null;
                else throw new QuestBlockedException("NPC travel needs reconciliation. Review the saved dialogue and location; travel will not be repeated.");
            }
        }
        if (Snapshot.Dialog is { } dialog && (maps.IsTravelNpc(dialog.Name) ||
            Snapshot.Entities.TryGetValue(dialog.EntityId, out var entity) && !entity.IsPlayer && maps.IsTravelNpc(entity.Name)))
            throw new QuestBlockedException("Travel dialogue is already open. Review or finish it before resuming travel.");
    }

    static bool SameTravelChoice(string text, string expected) => text.Trim().TrimEnd('.', '!', '?')
        .Equals(expected, StringComparison.OrdinalIgnoreCase);
    static bool MentionsDestination(string text, NpcTransfer transfer) => transfer.Destinations.Any(destination =>
        Regex.IsMatch(text, @"\b" + Regex.Escape(destination) + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    static bool TravelDestinationChoice(string text, NpcTransfer transfer) => MentionsDestination(text, transfer) &&
        !Regex.IsMatch(text, @"\b(citizenship|renounce|join|birth|register|cancel|decline|not|no)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        (transfer.Destinations.Any(destination => SameTravelChoice(text, destination)) ||
         Regex.IsMatch(text, @"\b(go|travel|take|send|return|transport|depart|journey|ride|visit|sail|caravan)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    static bool TravelRefusal(string text) => Regex.IsMatch(text,
        @"\b(cannot|can't|unable|unavailable|insufficient|not enough|don't have|do not have|not allowed|not eligible)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static bool TravelConfirmation(string text, PendingNpcTravel pending) => !TravelRefusal(text) &&
        (pending.DestinationSelected || MentionsDestination(text, pending.Transfer) || pending.Transfer.FareBeforeDestination &&
            Regex.IsMatch(text, @"^It will cost you [\d,]+ coins to take the caravan\. Would you like to go\?$", RegexOptions.CultureInvariant)) &&
        Regex.IsMatch(text, @"\b(go|travel|take|transport|ride|sail|ship|carpet|caravan|fare)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static bool AffirmativeTravelChoice(string text) => new[] { "Yes", "Yes, please", "Yes please", "I will pay", "I'll pay", "Pay the fare", "Board the ship" }
        .Any(expected => SameTravelChoice(text, expected));

    static uint? QuotedTravelFare(string text)
    {
        var fare = Regex.Match(text, @"(?<amount>[\d,]+)\s+(?:gold|coins)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return fare.Success && uint.TryParse(fare.Groups["amount"].Value, NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out uint amount) ? amount : null;
    }
    static void CheckTravelGold(CharacterSnapshot state, NpcTransfer transfer, string? text = null, bool requireEntryMinimum = true)
    {
        uint required = Math.Max(requireEntryMinimum ? transfer.MinimumGold : 0, text is null ? 0 : QuotedTravelFare(text) ?? 0);
        if (required == 0) return;
        if (state.Gold is null) throw new QuestBlockedException("Waiting for observed gold before using " + transfer.Service + ".");
        if (state.Gold < required) throw new QuestBlockedException("Not enough gold for " + transfer.Service + ". Travel has been paused.");
    }

    async Task NpcTravelReply(long after, CancellationToken ct)
    {
        var pending = npcTravel!;
        await WaitAsync(state => NpcTravelOwner(pending, state) &&
            (state.Map != pending.Checkpoint.SourceMap && state.Position is not null ||
             state.Dialog is { } dialog && dialog.EntityId == pending.EntityId && dialog.Revision > after),
            after, TimeSpan.FromSeconds(5), ct);
    }

    bool TravelNpcVisible(int map, string name) => Snapshot is { Connected: true, Position: { } position } state &&
        state.Map == map && !maps.Record(map).ExitTiles.Contains(position) &&
        NpcVisibility.Find(state, map, name) is not null;

    async Task ExecuteNpcTravel(TravelStep step, CancellationToken ct)
    {
        var transfer = step.NpcTransfer!;
        var entry = Snapshot;
        CheckTravelGold(entry, transfer);
        var npc = await ApproachTravelNpc(step, entry, ct);
        var observed = Snapshot;
        if (observed.Generation != entry.Generation || observed.Id != entry.Id || observed.Name != entry.Name ||
            observed.Map != step.SourceMap || observed.Dialog is not null || FindTravelNpc(observed, transfer)?.Id != npc.Id ||
            observed.Position is null || !TravelNpcVisible(step.SourceMap, transfer.Npc) || !observed.InventoryKnown || observed.Gold is null)
            throw new QuestBlockedException("Travel NPC, inventory or approach changed. Review location and popup before resuming.");
        CheckTravelGold(observed, transfer);
        var checkpoint = new NpcTravelCheckpoint(observed.Generation, transfer.Map, observed.Name!, transfer.Npc, DateTimeOffset.UtcNow)
        {
            OperationId = Guid.NewGuid(), SourceMap = step.SourceMap, CharacterId = observed.Id,
            Revision = observed.Revision, GoldBefore = observed.Gold, InventoryBefore = observed.Inventory
        };
        npcTravel = new(checkpoint, npc.Id, observed.Revision, transfer);
        try
        {
            await connection.SendAsync(Packet(new ClientRequestObjectInfoMessage { InteractionType = InteractionType.Entity, TargetId = npc.Id }), observed.Generation, ct,
                sendAllowed: () => TravelApproachStable(entry, Snapshot, step.SourceMap) && Snapshot.Position == observed.Position &&
                    FindTravelNpc(Snapshot, transfer) is { } currentNpc && currentNpc.Id == npc.Id && currentNpc.Position == npc.Position);
            await NpcTravelReply(observed.Revision, ct);
            for (int page = 0; page < 12; page++)
            {
                var pending = npcTravel!;
                var state = Snapshot;
                if (await FinishNpcTravel(ct)) return;
                var dialog = state.Dialog;
                if (!NpcTravelOwner(pending, state) || state.Map != pending.Checkpoint.SourceMap || dialog is null ||
                    dialog.Generation != state.Generation || dialog.Revision <= pending.Revision || dialog.Revision > state.Revision ||
                    dialog.EntityId != pending.EntityId || dialog.EntityType != 1 || dialog.Prompt is not null ||
                    !(dialog.Name.Equals(transfer.Npc, StringComparison.OrdinalIgnoreCase) || pending.ServiceSelected &&
                      dialog.Name.Equals(state.Name, StringComparison.OrdinalIgnoreCase)) || TravelRefusal(dialog.Text))
                    throw new QuestBlockedException("Unexpected travel reply. Dialogue is preserved; travel will not be repeated.");

                string? choice = null;
                bool destinationSelected = pending.DestinationSelected;
                if (!pending.ServiceSelected)
                {
                    var services = dialog.Choices.Where(option => SameTravelChoice(option.Text, transfer.Service)).ToArray();
                    if (services.Length != 1) throw new QuestBlockedException("Travel menu has no unique " + transfer.Service + " choice. It is preserved for review.");
                    choice = services[0].Text;
                }
                else if (dialog.Choices.Length > 0)
                {
                    var destinations = dialog.Choices.Where(option => TravelDestinationChoice(option.Text, transfer)).ToArray();
                    if (destinations.Length == 1)
                    {
                        choice = destinations[0].Text;
                        destinationSelected = true;
                    }
                    else if (destinations.Length == 0 && TravelConfirmation(dialog.Text, pending))
                    {
                        var affirmatives = dialog.Choices.Where(option => AffirmativeTravelChoice(option.Text)).ToArray();
                        if (affirmatives.Length == 1)
                        {
                            choice = affirmatives[0].Text;
                            destinationSelected = true;
                        }
                    }
                    if (choice is null) throw new QuestBlockedException("Travel menu has no unique destination or fare confirmation. It is preserved for review.");
                    // The entry minimum is not charged again when an already
                    // paid caravan asks for its destination.
                    CheckTravelGold(state, transfer, dialog.Text, requireEntryMinimum: false);
                }
                else if (!dialog.IsPursuit || dialog.Type != 0 || !dialog.HasNext)
                    throw new QuestBlockedException("Travel reply has no safe Next action. It is preserved for review.");

                uint? quoted = QuotedTravelFare(dialog.Text) ?? pending.Checkpoint.QuotedFare;
                if (pending.Checkpoint.QuotedFare is { } previousQuote && quoted != previousQuote)
                    throw new QuestBlockedException("Travel fare changed after its first quote. Reconcile the preserved dialogue.");
                var packet = choice is not null ? DialogPackets.Choice(state, dialog, choice) : DialogPackets.Continue(state, dialog);
                // Save before sending: cancellation or a server redirect cannot authorize another fare.
                var submission = pending with
                {
                    Submitted = true, ServiceSelected = true, DestinationSelected = destinationSelected,
                    Revision = state.Revision,
                    Checkpoint = pending.Checkpoint with { Revision = state.Revision, QuotedFare = quoted }
                };
                npcTravel = submission;
                if (BeforeNpcTravelSubmission is { } persist)
                    await persist(submission.Checkpoint, ct);
                var current = Snapshot;
                if (!NpcTravelOwner(submission, current) || current.Map != state.Map || current.Dialog is not { } currentDialog ||
                    currentDialog.Revision != dialog.Revision || currentDialog.EntityId != dialog.EntityId ||
                    current.Gold != state.Gold || !SameTravelInventory(submission.Checkpoint, current))
                    throw new QuestBlockedException("Travel evidence changed before submission. Reconcile the saved checkpoint; no new fare was sent.");
                await connection.SendAsync(packet, state.Generation, ct);
                await NpcTravelReply(state.Revision, ct);
            }
            if (await FinishNpcTravel(ct)) return;
            throw new QuestBlockedException("NPC travel exceeded its page limit. Reconcile the preserved dialogue.");
        }
        catch (TimeoutException)
        {
            throw new QuestBlockedException("NPC travel reply missing. Reconcile the saved dialogue and location; travel will not be repeated.");
        }
    }
}
