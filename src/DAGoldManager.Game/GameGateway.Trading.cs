using Arbiter.Net.Types;
using DAGoldManager.Core;
using DAQuester.Core;

namespace DAGoldManager.Game;

// Trading owns one exchange at a time for both local participants. Evidence helpers
// validate offers and exact balance changes before the coordinator marks a transfer Verified.
public sealed partial class GameGateway
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Generation, long Revision)> completedTradeBaselines = new(StringComparer.OrdinalIgnoreCase);
    public Task TransferGoldAsync(string sender, string receiver, long amount, CancellationToken cancellation)
    {
        var donor = GetCharacter(sender);
        var recipient = GetCharacter(receiver);
        if (amount <= 0 || donor.Snapshot.Gold < amount || recipient.Snapshot.Gold + amount > GoldPlanner.CarryingLimit)
            throw new InvalidOperationException("Gold trade would exceed available funds or carrying capacity.");
        return Trade(donor, receiver, recipient, 0, checked((uint)amount), cancellation);
    }

    public Task TransferBarsAsync(string sender, string receiver, int amount, CancellationToken cancellation)
    {
        var donor = GetCharacter(sender);
        characters.TryGetValue(receiver, out var recipient);
        // The external banker accepts independently. Local collection controls both sides.
        if (receiver.Equals(externalRecipient, StringComparison.OrdinalIgnoreCase) || recipient?.Snapshot.Connected != true)
            recipient = null;
        if (amount <= 0 || donor.Snapshot.Count(RequireRecipe().BarName) < amount)
            throw new InvalidOperationException("Not enough bars remain for this trade.");
        return Trade(donor, receiver, recipient, amount, 0, cancellation);
    }

    Task Trade(CharacterConnection donor, string receiverName, CharacterConnection? recipient, int bars, uint gold, CancellationToken cancellation)
    {
        var participants = recipient is null ? new[] { donor } : new[] { donor, recipient };
        return Control(participants, async token =>
        {
            var target = RequirePlayer(donor, receiverName);
            var before = donor.Snapshot;
            var recipientBefore = recipient?.Snapshot;
            if (recipient is not null && target.Id != recipientBefore!.Id)
                throw new InvalidOperationException("Visible player does not match managed recipient identity.");
            string barName = RequireRecipe().BarName;
            string offerName = RequireRecipe().OfferName ?? barName;
            using var observed = new ObservedTrade(donor, offerName);
            using var recipientObserved = recipient is null ? null : new ObservedTrade(recipient, offerName);
            bool accepted = false;
            string stage = "opening the sender's trade window";
            bool ReceiverConfirmsGold() => recipientObserved is not null &&
                GoldOfferEvidence.IsConfirmed(observed.Monitor.View, recipientObserved.Monitor.View, gold);
            try
            {
                await SendExchange(donor, ExchangeClientActionType.BeginExchange, target.Id, token);
                await WaitTrade(observed, v => v.Started, token);
                CheckPartner(observed.Monitor.View, target);
                if (recipientObserved is not null)
                {
                    stage = "opening the receiver's trade window";
                    await WaitTrade(recipientObserved, v => v.Started, token);
                    CheckPartner(recipientObserved.Monitor.View, new(before.Id!.Value, before.Name!, before.Position!, true) { IsPlayer = true });
                }

                if (gold > 0)
                {
                    stage = "confirming the gold offer";
                    await SendExchange(donor, ExchangeClientActionType.SetGold, target.Id, token, gold: gold);
                    if (recipientObserved is not null)
                        await WaitTrade(recipientObserved, v => v.OtherGold == gold, token);
                    else await WaitTrade(observed, v => v.OwnGold == gold, token);
                }
                else
                {
                    stage = "confirming the bar offer";
                    await OfferBars(donor, observed, target.Id, bars, token);
                }

                EnsureOffer(observed.Monitor.View, bars, gold, sending: true, allowMissingGoldEcho: ReceiverConfirmsGold());
                if (recipientObserved is not null)
                {
                    stage = "confirming the receiver's offer contents";
                    await WaitTrade(recipientObserved, v => v.OtherBars == bars && v.OtherGold == gold, token);
                    EnsureOffer(recipientObserved.Monitor.View, bars, gold, sending: false);
                }
                // Last fresh checks before committing either side's acceptance.
                if (RequirePlayer(donor, receiverName).Id != target.Id || donor.Snapshot.Generation != before.Generation)
                    throw new InvalidOperationException("Trade partner changed before acceptance.");
                stage = "sending acceptance";
                await SendExchange(donor, ExchangeClientActionType.Accept, target.Id, token);
                accepted = true;
                if (recipient is not null)
                    await SendExchange(recipient, ExchangeClientActionType.Accept, before.Id!.Value, token);

                TradeView CheckedTrade()
                {
                    if (observed.Error is not null) throw new InvalidOperationException(observed.Error);
                    var view = observed.Monitor.View;
                    CheckPartner(view, target);
                    EnsureOffer(view, bars, gold, sending: true, allowMissingGoldEcho: ReceiverConfirmsGold());
                    if (recipientObserved is not null)
                    {
                        if (recipientObserved.Error is not null) throw new InvalidOperationException(recipientObserved.Error);
                        CheckPartner(recipientObserved.Monitor.View,
                            new(before.Id!.Value, before.Name!, before.Position!, true) { IsPlayer = true });
                        EnsureOffer(recipientObserved.Monitor.View, bars, gold, sending: false);
                    }
                    return view;
                }
                Func<CharacterSnapshot>? readRecipient = recipient is null ? null : () => recipient.Snapshot;
                stage = "verifying the completed transfer's balances";
                await TradeConfirmation.WaitAsync(CheckedTrade, () => donor.Snapshot, readRecipient,
                    before, recipientBefore, bars, gold, barName, TimeSpan.FromSeconds(settings.TradeTimeoutSeconds), token);
                completedTradeBaselines[before.Name!] = (before.Generation, before.Revision);
                if (recipientBefore is not null)
                    completedTradeBaselines[recipientBefore.Name!] = (recipientBefore.Generation, recipientBefore.Revision);
            }
            catch (Exception error)
            {
                // Before acceptance, cancelling an open offer is safe. After acceptance, rescan instead of guessing.
                if (!accepted && observed.Monitor.View.Started && donor.Snapshot.Connected)
                {
                    try { await SendExchange(donor, ExchangeClientActionType.Cancel, target.Id, CancellationToken.None); }
                    catch (Exception) { Diagnostic?.Invoke("Trade cancellation could not be confirmed; inspect the game window."); }
                }
                if (error is OperationCanceledException) throw;
                throw new InvalidOperationException($"Trade stopped while {stage}: {error.Message}", error);
            }
        }, cancellation);
    }

    public Task FinishTradeDialogsAsync(string sender, string receiver, CancellationToken cancellation)
    {
        var donor = GetCharacter(sender);
        characters.TryGetValue(receiver, out var recipient);
        if (receiver.Equals(externalRecipient, StringComparison.OrdinalIgnoreCase) || recipient?.Snapshot.Connected != true)
            recipient = null;
        var participants = recipient is null ? new[] { donor } : new[] { donor, recipient };
        return Control(participants, async token =>
        {
            foreach (var character in participants)
            {
                string name = character.Snapshot.Name!;
                if (!completedTradeBaselines.TryGetValue(name, out var baseline)) continue;
                bool FreshPopup(CharacterSnapshot state) => state.Popup is { } popup &&
                    popup.Generation == baseline.Generation && popup.Revision > baseline.Revision;
                if (!FreshPopup(character.Snapshot))
                {
                    // Balance updates may precede the optional completion popup by a packet.
                    try { await character.Session.WaitAsync(FreshPopup, baseline.Revision, TimeSpan.FromSeconds(2), token); }
                    catch (TimeoutException) { }
                }
                if (TradePopupCleanup.CompletionPopup(character.Snapshot, baseline.Generation, baseline.Revision) is { } completion)
                    await character.Connection.PresentPopupCloseAsync(completion, token);
                completedTradeBaselines.TryRemove(name, out _);
            }
        }, cancellation);
    }

    async Task OfferBars(CharacterConnection donor, ObservedTrade trade, uint target, int amount, CancellationToken cancellation)
    {
        int offered = 0;
        foreach (var item in donor.Snapshot.Inventory.Values.Where(i => i.Name.Equals(RequireRecipe().BarName, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Slot))
        {
            if (offered == amount) break;
            int quantity = Math.Min(amount - offered, (int)Math.Min(item.Quantity, byte.MaxValue));
            await SendExchange(donor, ExchangeClientActionType.AddItem, target, cancellation, item.Slot);
            if (item.Quantity > 1)
            {
                await WaitTrade(trade, v => v.PromptSlot == item.Slot, cancellation);
                await SendExchange(donor, ExchangeClientActionType.AddStackableItem, target, cancellation, item.Slot, (byte)quantity);
            }
            offered += quantity;
            int expected = offered;
            await WaitTrade(trade, v => v.OwnBars == expected, cancellation);
        }
        if (offered != amount) throw new InvalidOperationException("Inventory could not supply the exact requested offer.");
    }

    static void CheckPartner(TradeView view, Entity expected)
    {
        if (view.TargetId != expected.Id || !view.TargetName.Equals(expected.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Trade window opened with an unexpected player.");
    }

    static void EnsureOffer(TradeView view, int bars, uint gold, bool sending, bool allowMissingGoldEcho = false)
    {
        // The managed recipient may receive the authoritative amount before an optional sender echo.
        bool ownGoldCorrect = view.OwnGold == gold || allowMissingGoldEcho && gold > 0 && view.OwnGold == 0;
        bool correct = sending ? view.OwnBars == bars && ownGoldCorrect && view.OtherBars == 0 && view.OtherGold == 0 :
            view.OtherBars == bars && view.OtherGold == gold && view.OwnBars == 0 && view.OwnGold == 0;
        if (!correct || view.UnexpectedItem || view.Cancelled)
            throw new InvalidOperationException("Trade contents differ from the expected one-way transfer.");
    }

    static Task SendExchange(CharacterConnection character, ExchangeClientActionType action, uint target,
        CancellationToken cancellation, byte slot = 0, byte quantity = 1, uint gold = 0)
        => character.Connection.SendAsync(ExchangePackets.Create(action, target, slot, quantity, gold), character.Snapshot.Generation, cancellation);

    static async Task WaitTrade(ObservedTrade trade, Func<TradeView, bool> ready, CancellationToken cancellation, int seconds = 8)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (trade.Error is not null) throw new InvalidOperationException(trade.Error);
                var view = trade.Monitor.View;
                if (view.Cancelled || view.UnexpectedItem) throw new InvalidOperationException("Trade was cancelled or contains unexpected items.");
                if (ready(view)) return;
                await Task.Delay(25, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("Trade response was not confirmed. Inspect the game before another run."); }
    }

}

/// <summary>A disposable server-response listener scoped to one trade attempt.</summary>
public sealed class ObservedTrade : IDisposable
{
    readonly CharacterConnection character;
    public TradeMonitor Monitor { get; }
    public string? Error { get; private set; }
    public ObservedTrade(CharacterConnection character, string barName)
    {
        this.character = character;
        Monitor = new(barName);
        character.Connection.Tracker.Observed += Observe;
    }
    void Observe(byte opcode, byte[] payload)
    {
        if (opcode != 0x42) return;
        try { Monitor.Observe(ExchangeReader.Read(payload)); }
        catch (Exception error) { Error = "Exchange packet could not be decoded: " + error.Message; }
    }
    public void Dispose() => character.Connection.Tracker.Observed -= Observe;
}
