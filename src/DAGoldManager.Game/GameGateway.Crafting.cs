using DAGoldManager.Core;
using DAQuester.Core;
using DAQuester.DarkAges.Protocol;

namespace DAGoldManager.Game;

// Crafting replays a demonstrated recipe, checking the NPC and expected text at each step.
// Purchase verification is separate from popup cleanup so cleanup never repeats spending.
public sealed partial class GameGateway
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> lastCraftRequest = new(StringComparer.OrdinalIgnoreCase);
    public Task CraftBarAsync(string name, CancellationToken cancellation)
    {
        var character = GetCharacter(name);
        var learned = CraftRecipeFor(character);
        return Control([character], async token =>
        {
            if (character.Snapshot.Registered == false)
                throw new CraftRegistrationRequiredException("This account must be registered to make gold bars.");
            var wait = cooldowns.Remaining(name, DateTimeOffset.UtcNow, learned.CooldownSeconds);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
            character = await NavigateManagedAsync(name, BankNavigation.Arrival(learned), token);
            bool NpcVisible(CharacterSnapshot state) => state.Map == learned.Map && state.Entities.Values.Any(e =>
                !e.IsPlayer && e.Name.Equals(learned.NpcName, StringComparison.OrdinalIgnoreCase));
            if (!NpcVisible(character.Snapshot))
                await character.Session.WaitAsync(NpcVisible, character.Snapshot.Revision, TimeSpan.FromSeconds(8), token);
            await character.Session.ExecuteAsync(BankNavigation.OpenNpc(learned), token);
            var before = character.Snapshot;
            if (before.Gold < learned.GoldCost || !before.InventoryKnown)
                throw new InvalidOperationException("Gold or inventory changed before crafting.");

            long finalRequestRevision = before.Revision;
            for (int index = 0; index < learned.Actions.Length; index++)
            {
                var action = learned.Actions[index];
                var dialog = character.Snapshot.Dialog ?? throw new InvalidOperationException("Recorded bank dialog did not appear.");
                if (!dialog.Name.Equals(learned.NpcName, StringComparison.OrdinalIgnoreCase) ||
                    DialogText.ForCharacter(dialog.Text, before.Name ?? "") != action.ExpectedText)
                    throw new InvalidOperationException("Bank dialog differs from recorded setup. Record the process again.");
                long revision = character.Snapshot.Revision;
                finalRequestRevision = revision;
                var packet = action.Kind switch
                {
                    DialogActionKind.Choice => DialogPackets.Choice(character.Snapshot, dialog, action.Value),
                    DialogActionKind.Input => DialogPackets.Continue(character.Snapshot, dialog, action.Value),
                    DialogActionKind.Next => DialogPackets.Continue(character.Snapshot, dialog),
                    _ => throw new InvalidOperationException("Unknown recorded dialog action.")
                };
                await character.Connection.SendAsync(packet, before.Generation, token);
                if (index < learned.Actions.Length - 1)
                {
                    var response = await character.Session.WaitAsync(s => RegistrationEvidence.IsFreshRefusal(s, revision) ||
                        s.Dialog is { } next && next.EntityId == dialog.EntityId && next.Revision > revision,
                        revision, TimeSpan.FromSeconds(8), token);
                    ThrowIfRegistrationRefused(response, revision);
                }
            }

            var outcome = await character.Session.WaitAsync(s => s.Generation == before.Generation &&
                (RegistrationEvidence.IsFreshRefusal(s, before.Revision) ||
                 (long)before.Gold!.Value - (s.Gold ?? before.Gold.Value) == learned.GoldCost &&
                 s.Count(learned.BarName) - before.Count(learned.BarName) == 1),
                before.Revision, TimeSpan.FromSeconds(12), token);
            ThrowIfRegistrationRefused(outcome, before.Revision);
            cooldowns.RecordCraft(name, DateTimeOffset.UtcNow);
            lastCraftRequest[name] = finalRequestRevision;
        }, cancellation);
    }

    public Task FinishCraftDialogsAsync(string name, CancellationToken cancellation)
    {
        var character = GetCharacter(name);
        return Control([character], async token =>
        {
            if (lastCraftRequest.TryGetValue(name, out long requestRevision) &&
                character.Snapshot.Dialog is { } old && old.Revision <= requestRevision)
            {
                // Gold/inventory packets can arrive just before the final confirmation popup.
                try
                {
                    await character.Session.WaitAsync(s => s.Dialog is null || s.Dialog.Revision > requestRevision,
                        requestRevision, TimeSpan.FromSeconds(5), token);
                }
                catch (TimeoutException)
                {
                    // Some NPCs leave the purchase menu open. Dismiss it; never repeat its choice.
                }
            }
            await CraftDialogCleanup.FinishAsync(CraftRecipeFor(character), () => character.Snapshot,
                (command, ct) => character.Session.ExecuteAsync(command, ct), token);
            lastCraftRequest.TryRemove(name, out _);
        }, cancellation);
    }

    static void ThrowIfRegistrationRefused(CharacterSnapshot state, long afterRevision)
    {
        if (RegistrationEvidence.IsFreshRefusal(state, afterRevision))
            throw new CraftRegistrationRequiredException(state.Notice!.Text);
    }
}
