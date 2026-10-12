using System.Collections.Concurrent;

namespace DAGoldManager.Core;

/// <summary>Coordinates one roster run through an interchangeable game gateway and durable ledger.</summary>
/// <remarks>Parallel work is limited to independent producers. Collection and delivery use one courier.</remarks>
public sealed class RunCoordinator(IGameGateway game, IOperationLedger ledger, IRunClock clock)
{
    public event Action<RunProgress>? Changed;
    readonly object pauseLock = new();
    TaskCompletionSource? resumeSignal;
    Guid runId;
    int created, delivered;
    readonly ConcurrentDictionary<string, byte> unregistered = new(StringComparer.OrdinalIgnoreCase);

    public void Pause()
    {
        // Pause is cooperative: Checkpoint waits between operations, never halfway through a trade.
        lock (pauseLock) resumeSignal ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        lock (pauseLock)
        {
            resumeSignal?.TrySetResult();
            resumeSignal = null;
        }
    }

    public async Task<RunResult> RunAsync(IReadOnlyList<SavedCharacter> characters, RunSettings settings, CancellationToken cancellation)
    {
        // Reject invalid roles before any client is launched or any money can move.
        Validate(characters, settings);
        runId = Guid.NewGuid();
        created = delivered = 0;
        unregistered.Clear();
        // Banker inventory is separate from production in both local and external modes.
        var producers = characters.Where(c => c.Enabled &&
            !c.Name.Equals(settings.Recipient, StringComparison.OrdinalIgnoreCase)).ToArray();
        bool localBanker = settings.BankerMode == BankerMode.Local;
        var launchRoster = localBanker ? producers.Append(characters.Single(c => c.Name.Equals(settings.Recipient, StringComparison.OrdinalIgnoreCase))).ToArray() : producers;
        // Keep each pre-craft scan for final reconciliation even though producers work concurrently.
        var scanned = await RunTogether(launchRoster, async (character, token) =>
        {
            await Checkpoint(token);
            Report(RunPhase.Launching, character.Name, "Launching and signing in");
            await game.LaunchAsync(character, token);
            Report(RunPhase.Scanning, character.Name, "Reading gold and inventory");
            var before = await game.ReadAsync(character.Name, token);
            if (localBanker && character.Name.Equals(settings.Recipient, StringComparison.OrdinalIgnoreCase))
            {
                Report(RunPhase.Scanning, character.Name, "Walking to Abel bank for deposits");
                await game.PrepareBankerAsync(character.Name, settings, token);
                Report(RunPhase.WaitingForDeposit, character.Name, "Banker ready at Abel bank");
                return before;
            }
            if (before.Registered == false) unregistered.TryAdd(character.Name, 0);
            if (CanCraft(character)) await CraftFundedCharacter(character.Name, settings, token);
            else Report(RunPhase.WaitingForCollection, character.Name, "Unregistered; waiting to fund a registered crafter");
            return before;
        }, cancellation);
        var starting = scanned.Where(c => !c.Name.Equals(settings.Recipient, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (localBanker) await DepositBankerBars(settings, cancellation);
        await CraftAll(producers, settings, cancellation);
        await DeliverAll(producers, settings, cancellation);
        var ending = await Scan(producers, cancellation);
        var result = new RunResult(starting.Sum(c => c.Gold), ending.Sum(c => c.Gold),
            starting.Sum(c => c.Bars), created, delivered);
        // Gold purchases and bar delivery must reconcile across the whole production roster.
        // The banker is excluded because it may already carry unrelated bars before this run.
        if (result.StartingGold - result.EndingGold != created * settings.BarCost ||
            result.StartingBars + created != delivered + ending.Sum(c => c.Bars))
            throw new InvalidOperationException("Final totals do not reconcile. Inspect the ledger before starting another run.");
        Report(RunPhase.Completed, "", $"Delivered {delivered} bars; {result.EndingGold:N0} gold remains");
        return result;
    }

    static void Validate(IReadOnlyList<SavedCharacter> characters, RunSettings settings)
    {
        var roster = characters.Where(c => c.Enabled).ToArray();
        if (roster.Length == 0 || roster.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != roster.Length)
            throw new InvalidOperationException("Select a nonempty roster with unique character names.");
        if (!roster.Any(c => c.Name.Equals(settings.Courier, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Select an enabled character as courier.");
        if (settings.Courier.Equals(settings.Recipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a courier other than the recipient.");
        if (settings.BankerMode == BankerMode.Local &&
            !roster.Any(c => c.Name.Equals(settings.Recipient, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Select an enabled local banker.");
        if (!Enum.IsDefined(settings.BankerMode) || string.IsNullOrWhiteSpace(settings.Recipient) ||
            (settings.BankerMode == BankerMode.External && settings.RecipientMap is < 1 or > 65535) ||
            settings.RecipientX is < 0 or > 65535 || settings.RecipientY is < 0 or > 65535 || settings.BarsPerTrade is < 1 or > 59 ||
            settings.DepositWaitSeconds is < 0 or > 3600 || settings.BarCost <= 0 || settings.BarCost > GoldPlanner.CarryingLimit)
            throw new InvalidOperationException("Check bar cost, batch size, recipient, and deposit wait settings.");
    }

    async Task CraftAll(SavedCharacter[] roster, RunSettings settings, CancellationToken cancellation)
    {
        // Repeat scan -> fund -> craft until the remaining eligible gold cannot buy another bar.
        // Re-scan after every handoff rather than planning several transfers from stale balances.
        while (true)
        {
            await Checkpoint(cancellation);
            var states = await Scan(roster, cancellation);
            foreach (var state in states.Where(c => c.Registered == false)) unregistered.TryAdd(state.Name, 0);
            // Fund several producers before their next craft round. Never move already-funded gold.
            if (!roster.Any(CanCraft) && states.Sum(c => c.Gold) >= settings.BarCost)
                throw new InvalidOperationException("Enable a registered character that can craft bars before handing off gold.");

            while (true)
            {
                await Checkpoint(cancellation);
                var balances = states.Select(state => new GoldBalance(state.Name, state.Gold,
                    CanCraft(roster.Single(c => c.Name.Equals(state.Name, StringComparison.OrdinalIgnoreCase)))))
                    .Where(c => !c.CanCraftBars || c.Gold < settings.BarCost).ToArray();
                var transfer = GoldPlanner.NextTransfer(balances, settings.BarCost, settings.PoolLeftoverGold);
                if (transfer is null) break;
                Report(RunPhase.PoolingGold, transfer.Sender, $"Funding {transfer.Receiver} with {transfer.Amount:N0} gold");
                await game.ApproachCharacterAsync(transfer.Sender, transfer.Receiver, cancellation);
                await VerifyGoldTransfer(transfer, cancellation);
                states = await Scan(roster, cancellation);
            }
            var fundedNames = states.Where(c => c.Gold >= settings.BarCost).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fundedCrafters = roster.Where(c => CanCraft(c) && fundedNames.Contains(c.Name)).ToArray();
            if (fundedCrafters.Length == 0) break;
            await RunTogether(fundedCrafters, async (character, token) =>
            {
                await CraftFundedCharacter(character.Name, settings, token);
                return true;
            }, cancellation);
        }
    }

    async Task CraftFundedCharacter(string name, RunSettings settings, CancellationToken cancellation)
    {
        while (true)
        {
            await Checkpoint(cancellation);
            var state = await game.ReadAsync(name, cancellation);
            if (state.Registered == false || unregistered.ContainsKey(name))
            {
                unregistered.TryAdd(name, 0);
                Report(RunPhase.WaitingForCollection, name, "Unregistered; waiting to fund a registered crafter");
                return;
            }
            if (state.Gold < settings.BarCost)
            {
                Report(RunPhase.WaitingForCollection, name, "Funded crafting complete; waiting for courier");
                return;
            }
            if (state.BarCapacity < 1)
                throw new InvalidOperationException($"{name} needs inventory room before all crafting can finish.");
            Report(RunPhase.Crafting, name, "Walking to bank and making a bar");
            await game.WalkToBankAsync(name, cancellation);
            try
            {
                await VerifyOperation("craft", name, "", 1, async () =>
                {
                    var before = await game.ReadAsync(name, cancellation);
                    try { await game.CraftBarAsync(name, cancellation); }
                    catch (CraftRegistrationRequiredException refusal)
                    {
                        var unchanged = await game.ReadAsync(name, cancellation);
                        if (before.Gold != unchanged.Gold || before.Bars != unchanged.Bars)
                            throw new InvalidOperationException("Registration refusal arrived with changed balances. Inspect this craft before continuing.");
                        // A server refusal alone is not sufficient: unchanged balances distinguish
                        // a safe rejection from a purchase followed by an unrelated refusal message.
                        throw new VerifiedCraftRejection(refusal.Message);
                    }
                    var after = await game.ReadAsync(name, cancellation);
                    return before.Gold - after.Gold == settings.BarCost && after.Bars - before.Bars == 1;
                }, cancellation);
            }
            catch (VerifiedCraftRejection)
            {
                unregistered.TryAdd(name, 0);
                Report(RunPhase.WaitingForCollection, name, "Server rejected unregistered craft; gold will fund a registered character");
                return;
            }
            Interlocked.Increment(ref created);
            // The craft is already verified. A popup failure must not erase that outcome.
            await game.FinishCraftDialogsAsync(name, cancellation);
        }
    }

    bool CanCraft(SavedCharacter character) => character.CanCraftBars && !unregistered.ContainsKey(character.Name);
    sealed class VerifiedCraftRejection(string message) : InvalidOperationException(message);

    static async Task<T[]> RunTogether<T>(IEnumerable<SavedCharacter> roster,
        Func<SavedCharacter, CancellationToken, Task<T>> work, CancellationToken cancellation)
    {
        using var group = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        async Task<T> RunOne(SavedCharacter character)
        {
            try { return await work(character, group.Token); }
            catch { group.Cancel(); throw; }
        }
        // Await every worker even after failure, so no background craft escapes reconciliation/review.
        return await Task.WhenAll(roster.Select(RunOne));
    }

    async Task VerifyGoldTransfer(GoldTransfer transfer, CancellationToken cancellation)
    {
        await VerifyOperation("gold trade", transfer.Sender, transfer.Receiver, transfer.Amount, async () =>
        {
            var donorBefore = await game.ReadAsync(transfer.Sender, cancellation);
            var receiverBefore = await game.ReadAsync(transfer.Receiver, cancellation);
            await game.TransferGoldAsync(transfer.Sender, transfer.Receiver, transfer.Amount, cancellation);
            var donorAfter = await game.ReadAsync(transfer.Sender, cancellation);
            var receiverAfter = await game.ReadAsync(transfer.Receiver, cancellation);
            return donorBefore.Gold - donorAfter.Gold == transfer.Amount &&
                receiverAfter.Gold - receiverBefore.Gold == transfer.Amount;
        }, cancellation);
        await game.FinishTradeDialogsAsync(transfer.Sender, transfer.Receiver, cancellation);
    }

    async Task DeliverAll(SavedCharacter[] roster, RunSettings settings, CancellationToken cancellation)
    {
        // Overflow takes another collection trip. Keep the external banker's deadline across trips.
        DateTimeOffset nextBankerTrade = clock.Now;
        while ((await Scan(roster, cancellation)).Sum(c => c.Bars) > 0)
        {
            await Checkpoint(cancellation);
            await game.WalkToBankAsync(settings.Courier, cancellation);
            await CollectBars(roster, settings, cancellation);
            var courier = await game.ReadAsync(settings.Courier, cancellation);
            if (courier.Bars == 0)
                throw new InvalidOperationException("Courier cannot hold any bars. Free inventory room and start a fresh scan.");

            while ((courier = await game.ReadAsync(settings.Courier, cancellation)).Bars > 0)
            {
                await Checkpoint(cancellation);
                TimeSpan wait = nextBankerTrade - clock.Now;
                if (wait > TimeSpan.Zero)
                {
                    Report(RunPhase.WaitingForDeposit, settings.Courier, $"Giving {settings.Recipient} time to deposit bars");
                    await clock.DelayAsync(wait, cancellation);
                    await Checkpoint(cancellation);
                }
                await game.WalkToRecipientAsync(settings.Courier, settings, cancellation);
                int batch = Math.Min(courier.Bars, settings.BarsPerTrade);
                CharacterState? bankerBefore = null;
                if (settings.BankerMode == BankerMode.Local)
                {
                    bankerBefore = await game.ReadAsync(settings.Recipient, cancellation);
                    batch = Math.Min(batch, bankerBefore.BarCapacity);
                    if (batch < 1) throw new InvalidOperationException("The local banker needs free inventory slots and weight before accepting bars.");
                }
                Report(RunPhase.Delivering, settings.Courier, $"Trading {batch} bars to {settings.Recipient}");
                await VerifyOperation("banker delivery", settings.Courier, settings.Recipient, batch, async () =>
                {
                    var before = await game.ReadAsync(settings.Courier, cancellation);
                    await game.TransferBarsAsync(settings.Courier, settings.Recipient, batch, cancellation);
                    var after = await game.ReadAsync(settings.Courier, cancellation);
                    // External confirmation happens in the gateway because we cannot scan a remote
                    // banker. Locally we also prove the exact gain on the receiving character.
                    bool receiverConfirmed = bankerBefore is null;
                    if (bankerBefore is not null)
                    {
                        var bankerAfter = await game.ReadAsync(settings.Recipient, cancellation);
                        receiverConfirmed = bankerAfter.Bars - bankerBefore.Bars == batch && bankerAfter.Gold == bankerBefore.Gold;
                    }
                    return receiverConfirmed && before.Bars - after.Bars == batch && before.Gold == after.Gold;
                }, cancellation);
                delivered += batch;
                await game.FinishTradeDialogsAsync(settings.Courier, settings.Recipient, cancellation);
                // This deadline survives a return trip for overflow stock.
                if (settings.BankerMode == BankerMode.Local) await DepositBankerBars(settings, cancellation);
                else nextBankerTrade = clock.Now.AddSeconds(settings.DepositWaitSeconds);
            }
        }
    }

    async Task DepositBankerBars(RunSettings settings, CancellationToken cancellation)
    {
        while ((await game.ReadAsync(settings.Recipient, cancellation)).Bars > 0)
        {
            await Checkpoint(cancellation);
            Report(RunPhase.WaitingForDeposit, settings.Recipient, "Depositing Gold Bars; waiting for server confirmation");
            await VerifyOperation("bar deposit", settings.Recipient, "Bank", 1, async () =>
            {
                var before = await game.ReadAsync(settings.Recipient, cancellation);
                await game.DepositBarAsync(settings.Recipient, settings, cancellation);
                var after = await game.ReadAsync(settings.Recipient, cancellation);
                return before.Bars - after.Bars == 1 && before.Gold == after.Gold;
            }, cancellation);
            // Keep the verified deposit if closing its popup or pacing is interrupted.
            await game.FinishDepositDialogsAsync(settings.Recipient, cancellation);
        }
    }

    async Task CollectBars(SavedCharacter[] roster, RunSettings settings, CancellationToken cancellation)
    {
        // Serial collection gives the courier one trade partner at a time and respects its capacity.
        foreach (var producer in roster.Where(c => !c.Name.Equals(settings.Courier, StringComparison.OrdinalIgnoreCase)))
        {
            while (true)
            {
                await Checkpoint(cancellation);
                var donor = await game.ReadAsync(producer.Name, cancellation);
                var courier = await game.ReadAsync(settings.Courier, cancellation);
                int batch = Math.Min(settings.BarsPerTrade, Math.Min(donor.Bars, courier.BarCapacity));
                if (batch == 0) break;
                Report(RunPhase.Collecting, producer.Name, $"Giving {batch} bars to {settings.Courier}");
                await game.ApproachCharacterAsync(producer.Name, settings.Courier, cancellation);
                await VerifyBarCollection(producer.Name, settings.Courier, batch, cancellation);
            }
        }
    }

    async Task VerifyBarCollection(string donor, string courier, int amount, CancellationToken cancellation)
    {
        await VerifyOperation("courier collection", donor, courier, amount, async () =>
        {
            var donorBefore = await game.ReadAsync(donor, cancellation);
            var courierBefore = await game.ReadAsync(courier, cancellation);
            await game.TransferBarsAsync(donor, courier, amount, cancellation);
            var donorAfter = await game.ReadAsync(donor, cancellation);
            var courierAfter = await game.ReadAsync(courier, cancellation);
            return donorBefore.Bars - donorAfter.Bars == amount && courierAfter.Bars - courierBefore.Bars == amount &&
                donorBefore.Gold == donorAfter.Gold && courierBefore.Gold == courierAfter.Gold;
        }, cancellation);
        await game.FinishTradeDialogsAsync(donor, courier, cancellation);
    }

    async Task VerifyOperation(string kind, string sender, string receiver, long amount,
        Func<Task<bool>> performAndVerify, CancellationToken cancellation)
    {
        // Both intent and start must be durable before issuing a command. If the app exits after
        // the server commits, Started remains unresolved rather than inviting a duplicate payment.
        var operation = new OperationRecord(runId, Guid.NewGuid(), kind, sender, receiver, amount, OperationOutcome.Planned, clock.Now);
        await ledger.AppendAsync(operation, cancellation);
        await ledger.AppendAsync(operation with { Outcome = OperationOutcome.Started, Time = clock.Now }, cancellation);
        try
        {
            if (!await performAndVerify()) throw new InvalidOperationException($"{kind} balance changes could not be verified.");
            // User Stop must not cancel persistence of a result that has already been established.
            await ledger.AppendAsync(operation with { Outcome = OperationOutcome.Verified, Time = clock.Now }, CancellationToken.None);
        }
        catch (VerifiedCraftRejection refusal)
        {
            await ledger.AppendAsync(operation with { Outcome = OperationOutcome.Rejected, Time = clock.Now,
                Detail = refusal.Message + " Gold and bars unchanged; character will donate gold." }, CancellationToken.None);
            throw;
        }
        catch (Exception error)
        {
            // Stop can arrive after a server commit. Record ambiguity even if cancellation was requested.
            await ledger.AppendAsync(operation with { Outcome = OperationOutcome.NeedsReview, Time = clock.Now, Detail = error.Message }, CancellationToken.None);
            throw;
        }
    }

    async Task<CharacterState[]> Scan(SavedCharacter[] roster, CancellationToken cancellation)
    {
        var states = new List<CharacterState>();
        foreach (var character in roster) states.Add(await game.ReadAsync(character.Name, cancellation));
        return states.ToArray();
    }

    async Task Checkpoint(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Task? wait;
        lock (pauseLock) wait = resumeSignal?.Task;
        if (wait is not null) await wait.WaitAsync(cancellation);
    }

    void Report(RunPhase phase, string character, string message) => Changed?.Invoke(new(phase, character, message, Volatile.Read(ref created), delivered));
}
