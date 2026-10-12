using System.Collections.Concurrent;
using DAGoldManager.Core;
using DAQuester.Core;

namespace DAGoldManager.Game;

// Local banking parks the receiver at Abel, deposits one carried bar per request,
// and waits for a fresh inventory change before allowing the next deposit.
public sealed partial class GameGateway
{
    readonly ConcurrentDictionary<string, (uint Npc, long Generation, DateTimeOffset Sent)> depositCleanup = new(StringComparer.OrdinalIgnoreCase);

    public Task PrepareBankerAsync(string name, RunSettings run, CancellationToken cancellation)
    {
        if (run.BankerMode != BankerMode.Local || !name.Equals(run.Recipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only the configured local banker can be moved to Abel bank.");
        var banker = GetCharacter(name);
        return Control([banker], async token =>
        {
            await BankerPreparation.PrepareAsync(() => banker.Snapshot,
                (command, ct) => banker.Session.ExecuteAsync(command, ct), async ct =>
            {
                try
                {
                    await banker.Session.WaitAsync(BankerPreparation.NpcVisible, banker.Snapshot.Revision,
                        TimeSpan.FromSeconds(8), ct);
                }
                catch (TimeoutException)
                {
                    throw new InvalidOperationException("Lamont did not appear after the banker walked to Abel bank. No deposit was sent.");
                }
            }, token);
        }, cancellation);
    }

    public Task DepositBarAsync(string name, RunSettings run, CancellationToken cancellation)
    {
        if (run.BankerMode != BankerMode.Local || !name.Equals(run.Recipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only the configured local banker can deposit bars.");
        var banker = GetCharacter(name);
        return Control([banker], async token =>
        {
            // Retain pacing across Stop after a verified deposit.
            if (depositCleanup.TryGetValue(name, out var previous))
            {
                var remaining = previous.Sent.AddMilliseconds(1100) - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
            }
            var before = banker.Snapshot;
            var npc = BankerPreparation.RequireNpc(before);
            string barName = RequireRecipe().BarName;
            var bar = before.Inventory.Values.Where(i => i.Quantity > 0 && i.Name.Equals(barName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Slot).FirstOrDefault() ?? throw new InvalidOperationException("Banker has no Gold Bars to deposit.");
            var sent = DateTimeOffset.UtcNow;
            await banker.Connection.SendAsync(BankDepositPackets.Create(npc.Id, bar.Slot, bar.Quantity), before.Generation, token);
            var after = await banker.Session.WaitAsync(s => s.Count(barName) == before.Count(barName) - 1,
                before.Revision, TimeSpan.FromSeconds(settings.TradeTimeoutSeconds), token);
            if (!BankDepositEvidence.IsConfirmed(before, after, barName))
                throw new InvalidOperationException("Deposit changed unexpected inventory or balances. Inspect the banker before continuing.");
            depositCleanup[name] = (npc.Id, before.Generation, sent);
        }, cancellation);
    }

    public Task FinishDepositDialogsAsync(string name, CancellationToken cancellation)
    {
        var banker = GetCharacter(name);
        return Control([banker], async token =>
        {
            if (!depositCleanup.TryGetValue(name, out var cleanup)) return;
            var current = banker.Snapshot;
            if (current.Generation != cleanup.Generation) throw new InvalidOperationException("Banker reconnected after its verified deposit.");
            // Close only this deposit's NPC popup; leave unrelated dialogs untouched.
            if (current.Dialog is { } dialog && dialog.EntityId == cleanup.Npc)
                await banker.Connection.SendAsync(BankDepositPackets.Close(cleanup.Npc), cleanup.Generation, token);
            var spacing = cleanup.Sent.AddMilliseconds(1100) - DateTimeOffset.UtcNow;
            if (spacing > TimeSpan.Zero) await Task.Delay(spacing, token);
            depositCleanup.TryRemove(name, out _);
        }, cancellation);
    }

}
