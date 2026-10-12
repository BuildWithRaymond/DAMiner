using DAGoldManager.Core;
using DAGoldManager.Game;
using DAQuester.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class CraftDialogCleanupTests
{
    static CraftRecipe Recipe => new("Fictional banker", 167, 3, 4, "Gold Bar", 50_500_000, [], DateTimeOffset.UtcNow);
    static CharacterSnapshot State(bool choices = false) => new()
    {
        Generation = 1, Connected = true, Map = 167,
        Dialog = new(1, 4, 42, 0, "Fictional banker", "Your Gold Bar is ready.", true, 1, 2, 0,
            choices ? [new("Buy another bar", 1, 1)] : [], HasNext: true)
    };

    [Fact]
    public async Task FinishesNextPagesAndClosesFinalConfirmation()
    {
        var state = State();
        var commands = new List<string>();
        await CraftDialogCleanup.FinishAsync(Recipe, () => state, (command, _) =>
        {
            commands.Add(command.Kind);
            state = commands.Count switch
            {
                1 => state with { Dialog = state.Dialog! with { Revision = 5, Step = 3 } },
                2 => state with { Dialog = state.Dialog! with { Revision = 6, Step = 4, HasNext = false } },
                _ => state with { Dialog = null }
            };
            return Task.CompletedTask;
        }, default);
        Assert.Equal(new[] { "next", "next", "close" }, commands);
        Assert.Null(state.Dialog);
    }

    [Fact]
    public async Task PurchaseChoicesAreDismissedWithoutSelectingAnything()
    {
        var state = State(choices: true);
        var commands = new List<string>();
        await CraftDialogCleanup.FinishAsync(Recipe, () => state, (command, _) =>
        {
            commands.Add(command.Kind);
            state = state with { Dialog = null };
            return Task.CompletedTask;
        }, default);
        Assert.Equal(new[] { "close" }, commands);
    }

    [Fact]
    public async Task UnrelatedNpcDialogIsLeftAlone()
    {
        var state = State();
        state = state with { Dialog = state.Dialog! with { Name = "Other NPC" } };
        await CraftDialogCleanup.FinishAsync(Recipe, () => state, (_, _) =>
            throw new InvalidOperationException("Should not touch another NPC"), default);
    }

    [Fact]
    public async Task RepeatedUnchangedPageStopsRatherThanLoopingForever()
    {
        var state = State();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CraftDialogCleanup.FinishAsync(
            Recipe, () => state, (_, _) => Task.CompletedTask, default));
    }

    [Fact]
    public async Task CleanupFinishesBeforeNextCraftAndAfterVerifiedLedgerEntry()
    {
        var game = new HookedGame(1);
        game.Inner.States["Character1"] = game.Inner.States["Character1"] with { Gold = 99_999_999 };
        game.Inner.States["Character2"] = new("Character2", 1_000_001, 0, 59, 167, 6, 8);
        var ledger = new TestLedger();
        int cleanups = 0;
        game.Craft = (_, _) =>
        {
            Assert.Equal(game.Inner.Actions.Count(a => a == "craft"), cleanups);
            return Task.CompletedTask;
        };
        game.FinishDialogs = (name, _) =>
        {
            Assert.Contains(ledger.Operations, o => o.Sender == name && o.Outcome == OperationOutcome.Verified);
            cleanups++;
            return Task.CompletedTask;
        };
        var result = await new RunCoordinator(game, ledger, new TestClock()).RunAsync(game.Inner.Roster,
            TestGame.DeliverySettings, default);
        Assert.Equal(2, cleanups);
        Assert.Equal(2, result.CreatedBars);
    }

    [Fact]
    public async Task CleanupFailurePreservesVerifiedCraftAndStopsFurtherSpending()
    {
        var game = new HookedGame(1);
        game.FinishDialogs = (_, _) => throw new InvalidOperationException("Synthetic popup failure");
        var ledger = new TestLedger();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RunCoordinator(game, ledger, new TestClock())
            .RunAsync(game.Inner.Roster, TestGame.DeliverySettings, default));
        Assert.Single(ledger.Operations, o => o.Outcome == OperationOutcome.Verified);
        Assert.DoesNotContain(ledger.Operations, o => o.Outcome == OperationOutcome.NeedsReview);
        Assert.Empty(game.Inner.Deliveries);
    }
}
