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
            await PrepareVerifiedTradeDialogs(participants, token);
            TradeReadiness.RequireClosedWindows(donor.TradeWindow.BlocksNewTrade, recipient?.TradeWindow.BlocksNewTrade == true);
            var before = donor.Snapshot;
            var recipientBefore = recipient?.Snapshot;
            // Managed identities come from their own authenticated sessions. Their
            // draw in the sender's view may still be catching up after arrival.
            var target = recipientBefore is null ? RequirePlayer(donor, receiverName) :
                new Entity(recipientBefore.Id ?? throw new InvalidOperationException("Managed receiver identity is missing."),
                    recipientBefore.Name ?? throw new InvalidOperationException("Managed receiver name is missing."),
                    recipientBefore.Position ?? throw new InvalidOperationException("Managed receiver position is missing."), true) { IsPlayer = true };
            if (!target.Name.Equals(receiverName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Visible player does not match managed recipient identity.");
            string barName = RequireRecipe().BarName;
            string offerName = RequireRecipe().OfferName ?? barName;
            using var observed = new ObservedTrade(donor, offerName);
            using var recipientObserved = recipient is null ? null : new ObservedTrade(recipient, offerName);
            bool openingSent = false;
            bool acceptanceStarted = false;
            string stage = "opening the sender's trade window";
            void ValidateIdentities()
            {
                TradeReadiness.ValidateIdentities(donor.Snapshot, recipient?.Snapshot, target, before, recipientBefore);
                if (observed.Error is { } senderError) throw new InvalidOperationException(senderError);
                if (recipientObserved?.Error is { } receiverError) throw new InvalidOperationException(receiverError);
            }
            void ValidateParticipants()
            {
                ValidateIdentities();
                TradeReadiness.Validate(donor.Snapshot, recipient?.Snapshot, target, before.Generation, recipientBefore?.Generation);
            }
            string OpeningContext() => $"Sender window started: {observed.Monitor.View.Started}; " +
                (recipientObserved is null ? "external receiver." : $"receiver window started: {recipientObserved.Monitor.View.Started}." +
                    (recipientObserved.LastNotice is { } notice ? " Receiver's last server notice: " + notice : ""));
            bool ReceiverConfirmsGold() => recipientObserved is not null &&
                GoldOfferEvidence.IsConfirmed(observed.Monitor.View, recipientObserved.Monitor.View, gold);
            bool EmptyOpeningIsUnchanged()
            {
                ValidateParticipants();
                if (observed.Error is { } senderError) throw new InvalidOperationException(senderError);
                // No offer or acceptance has been sent yet. Any server exchange
                // evidence ends recovery permanently, even if it lacks Started.
                return recipient is not null && recipientObserved is not null &&
                    !observed.HasExchangeEvidence && !recipientObserved.HasExchangeEvidence &&
                    !donor.TradeWindow.BlocksNewTrade && !recipient.TradeWindow.BlocksNewTrade &&
                    TradeReadiness.MutuallyVisible(donor.Snapshot, recipient.Snapshot) &&
                    UnchangedOpeningBalance(before, donor.Snapshot) && UnchangedOpeningBalance(recipientBefore!, recipient.Snapshot);
            }
            int openingRequests = 1;
            TimeSpan nextOpening = TimeSpan.FromSeconds(2);
            var openingClock = new System.Diagnostics.Stopwatch();
            async Task RecoverEmptyOpening(TimeSpan _, CancellationToken token)
            {
                if (openingRequests >= 3 || openingClock.Elapsed < nextOpening || !EmptyOpeningIsUnchanged()) return;
                openingRequests++;
                // Repeat only an empty local request. The wire guard rechecks after
                // acquiring its lock so a late window is used rather than replaced.
                await donor.Connection.SendAsync(ExchangePackets.Create(ExchangeClientActionType.BeginExchange, target.Id),
                    before.Generation, token, sendAllowed: EmptyOpeningIsUnchanged);
                // The wire lock can delay submission. Pace from its actual return,
                // rather than letting a queued request make the next one overdue.
                nextOpening = openingClock.Elapsed + TimeSpan.FromSeconds(2);
            }
            try
            {
                if (recipient is not null)
                {
                    stage = "waiting for managed players to settle into trade range";
                    var settleTimeout = TimeSpan.FromSeconds(Math.Min(5, TradeResponseTimeout.TotalSeconds));
                    using var settling = CancellationTokenSource.CreateLinkedTokenSource(token);
                    settling.CancelAfter(settleTimeout);
                    string ArrivalContext() => OpeningContext() + " " + TradeLocationContext(donor.Snapshot, recipient.Snapshot);
                    void ValidateArrival()
                    {
                        ValidateIdentities();
                        TradeReadiness.RequireClosedWindows(donor.TradeWindow.BlocksNewTrade, recipient.TradeWindow.BlocksNewTrade);
                        if (!UnchangedOpeningBalance(before, donor.Snapshot) || !UnchangedOpeningBalance(recipientBefore!, recipient.Snapshot))
                            throw new InvalidOperationException("Managed balances changed while waiting for arrival. No trade request or offer was sent.");
                    }
                    try
                    {
                        while (!openingSent)
                        {
                            await observed.WaitAsync(_ => TradeReadiness.ManagedParticipantsReady(donor.Snapshot, recipient.Snapshot),
                                settleTimeout, settling.Token, ArrivalContext, ValidateArrival);
                            // The sender's wire lock can outlast the observed arrival.
                            // A skipped opening returns to readiness waiting, never to a
                            // value-bearing command or an unbounded response wait.
                            await donor.Connection.SendAsync(ExchangePackets.Create(ExchangeClientActionType.BeginExchange, target.Id),
                                before.Generation, settling.Token, sendAllowed: () =>
                                {
                                    ValidateArrival();
                                    if (!TradeReadiness.ManagedParticipantsReady(donor.Snapshot, recipient.Snapshot)) return false;
                                    ValidateParticipants();
                                    stage = "opening the sender's trade window";
                                    openingSent = true;
                                    return true;
                                });
                        }
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        if (openingSent)
                            throw new TimeoutException("Trade opening write did not finish within the arrival deadline. " +
                                ArrivalContext() + " No offer was sent; inspect any exchange window before another run.");
                        throw new TimeoutException($"Managed players did not settle into trade range within {settleTimeout.TotalSeconds:0.###} seconds. " +
                            ArrivalContext() + " No trade request or offer was sent.");
                    }
                }
                else
                {
                    ValidateParticipants();
                    openingSent = true;
                    await SendExchange(donor, ExchangeClientActionType.BeginExchange, target.Id, token);
                }
                stage = "opening the sender's trade window";
                openingClock.Start();
                await observed.WaitAsync(v => v.Started, TradeResponseTimeout, token, OpeningContext, ValidateParticipants,
                    recipient is null ? null : RecoverEmptyOpening);
                CheckPartner(observed.Monitor.View, target);
                if (recipientObserved is not null)
                {
                    stage = "opening the receiver's trade window";
                    await WaitTrade(recipientObserved, v => v.Started, token, OpeningContext, ValidateParticipants);
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
                ValidateParticipants();
                if (RequirePlayer(donor, receiverName).Id != target.Id || donor.Snapshot.Generation != before.Generation)
                    throw new InvalidOperationException("Trade partner changed before acceptance.");
                stage = "sending acceptance";
                _ = CheckedTrade();
                // A write can fail after reaching the server. From this point,
                // the outcome may be committed and cancellation must not guess.
                acceptanceStarted = true;
                await SendExchange(donor, ExchangeClientActionType.Accept, target.Id, token);
                if (recipient is not null)
                {
                    _ = CheckedTrade();
                    await SendExchange(recipient, ExchangeClientActionType.Accept, before.Id!.Value, token);
                }

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
                    before, recipientBefore, bars, gold, barName, TradeResponseTimeout, token);
                completedTradeBaselines[before.Name!] = (before.Generation, before.Revision);
                donor.TradeWindow.ConfirmVerifiedCompletion();
                if (recipientBefore is not null)
                {
                    completedTradeBaselines[recipientBefore.Name!] = (recipientBefore.Generation, recipientBefore.Revision);
                    recipient!.TradeWindow.ConfirmVerifiedCompletion();
                }
            }
            catch (Exception error)
            {
                // Before acceptance, cancelling an open offer is safe. After acceptance, rescan instead of guessing.
                if (!acceptanceStarted && openingSent)
                {
                    // Cancel only the intended exchange observed on this attempt.
                    // A missing sender echo can still leave the managed receiver open.
                    try
                    {
                        if (StartedWith(observed.Monitor.View, target) && donor.Snapshot.Connected && donor.Snapshot.Generation == before.Generation)
                            await SendExchange(donor, ExchangeClientActionType.Cancel, target.Id, CancellationToken.None);
                        else if (recipient is not null && recipientObserved is not null && before.Id is { } senderId &&
                            StartedWith(recipientObserved.Monitor.View, new(senderId, before.Name!, before.Position!, true) { IsPlayer = true }) &&
                            recipient.Snapshot.Connected && recipient.Snapshot.Generation == recipientBefore!.Generation)
                            await SendExchange(recipient, ExchangeClientActionType.Cancel, senderId, CancellationToken.None);
                    }
                    catch (Exception) { Diagnostic?.Invoke("Trade cancellation could not be confirmed; inspect the game window."); }
                }
                if (error is OperationCanceledException interrupted)
                    throw new TradeCancellationException(stage, acceptanceStarted, cancellation.IsCancellationRequested, interrupted);
                throw new InvalidOperationException($"Trade stopped while {stage}: {error.Message}", error);
            }
        }, cancellation);
    }

    static string TradeLocationContext(CharacterSnapshot sender, CharacterSnapshot receiver)
    {
        static string Position(Tile? position) => position is null ? "unknown" : $"({position.X},{position.Y})";
        static string Seen(CharacterSnapshot observer, CharacterSnapshot partner) => partner.Id is { } id &&
            observer.Entities.TryGetValue(id, out var visible) ? Position(visible.Position) : "not observed";
        return $"{sender.Name} own: map {sender.Map} {Position(sender.Position)}; " +
            $"{receiver.Name} own: map {receiver.Map} {Position(receiver.Position)}; " +
            $"{sender.Name} sees {receiver.Name}: {Seen(sender, receiver)}; " +
            $"{receiver.Name} sees {sender.Name}: {Seen(receiver, sender)}.";
    }

    static bool StartedWith(TradeView view, Entity target) => view.Started && view.TargetId == target.Id &&
        view.TargetName.Equals(target.Name, StringComparison.OrdinalIgnoreCase);

    static bool UnchangedOpeningBalance(CharacterSnapshot before, CharacterSnapshot after) =>
        before.Connected && after.Connected && before.Generation == after.Generation && before.Id == after.Id &&
        string.Equals(before.Name, after.Name, StringComparison.OrdinalIgnoreCase) &&
        before.Gold is not null && before.Gold == after.Gold && before.InventoryKnown && after.InventoryKnown &&
        before.Inventory.Count == after.Inventory.Count &&
        before.Inventory.All(item => after.Inventory.TryGetValue(item.Key, out var current) && current == item.Value);

    async Task PrepareVerifiedTradeDialogs(CharacterConnection[] participants, CancellationToken cancellation)
    {
        foreach (var character in participants)
        {
            string name = character.Snapshot.Name!;
            if (lastCraftRequest.ContainsKey(name))
            {
                await CraftDialogCleanup.FinishAsync(CraftRecipeFor(character), () => character.Snapshot,
                    (command, ct) => character.Session.ExecuteAsync(command, ct), cancellation);
                lastCraftRequest.TryRemove(name, out _);
            }
            if (completedTradeBaselines.TryGetValue(name, out var baseline) &&
                TradePopupCleanup.CompletionPopup(character.Snapshot, baseline.Generation, baseline.Revision) is { } completion &&
                await character.Connection.PresentPopupCloseAsync(completion, cancellation))
            {
                completedTradeBaselines.TryRemove(name, out _);
            }
        }
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
                if (character.Snapshot.Generation != baseline.Generation)
                {
                    completedTradeBaselines.TryRemove(name, out _);
                    continue;
                }
                bool FreshPopup(CharacterSnapshot state) => state.Popup is { } popup &&
                    popup.Generation == baseline.Generation && popup.Revision > baseline.Revision;
                if (!FreshPopup(character.Snapshot))
                {
                    // Balance updates may precede the optional completion popup by a packet.
                    try { await character.Session.WaitAsync(FreshPopup, baseline.Revision, TimeSpan.FromSeconds(2), token); }
                    catch (TimeoutException) { }
                }
                if (TradePopupCleanup.CompletionPopup(character.Snapshot, baseline.Generation, baseline.Revision) is { } completion &&
                    await character.Connection.PresentPopupCloseAsync(completion, token))
                    completedTradeBaselines.TryRemove(name, out _);
                // The popup is optional and may arrive after this short wait.
                // Keep its verified baseline for cleanup before the next exchange.
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

    TimeSpan TradeResponseTimeout => TimeSpan.FromSeconds(Math.Clamp(settings.TradeTimeoutSeconds, 1, 600));

    Task WaitTrade(ObservedTrade trade, Func<TradeView, bool> ready, CancellationToken cancellation,
        Func<string>? context = null, Action? validate = null) =>
        trade.WaitAsync(ready, TradeResponseTimeout, cancellation, context, validate);

}
