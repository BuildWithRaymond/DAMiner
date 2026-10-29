using DAGoldManager.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class ConcurrentRunTests
{
    [Fact]
    public async Task AllProducersCanLaunchAndCraftTogether()
    {
        var game = new HookedGame(3);
        int launched = 0, crafting = 0;
        var allLaunched = Signal();
        var allCrafting = Signal();
        game.Launch = async (_, token) =>
        {
            if (Interlocked.Increment(ref launched) == 3) allLaunched.SetResult();
            await allLaunched.Task.WaitAsync(token);
        };
        game.Craft = async (_, token) =>
        {
            if (Interlocked.Increment(ref crafting) == 3) allCrafting.SetResult();
            await allCrafting.Task.WaitAsync(token);
        };
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new RunCoordinator(game, new TestLedger(), new TestClock())
            .RunAsync(game.Inner.Roster, TestGame.DeliverySettings with { BarsPerTrade = 16 }, limit.Token);
        Assert.Equal(3, result.CreatedBars);
        Assert.Equal(3, result.DeliveredBars);
    }

    [Fact]
    public async Task CraftingWaitsUntilEveryLoginLocationIsKnown()
    {
        var game = new HookedGame(2);
        var slowLoginStarted = Signal();
        var finishLogin = Signal();
        game.Launch = async (name, token) =>
        {
            if (name != "Character2") return;
            slowLoginStarted.SetResult();
            await finishLogin.Task.WaitAsync(token);
        };
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = new RunCoordinator(game, new TestLedger(), new TestClock())
            .RunAsync(game.Inner.Roster, TestGame.DeliverySettings, limit.Token);
        await slowLoginStarted.Task.WaitAsync(limit.Token);
        bool craftedBeforeScanFinished = game.Inner.Actions.Contains("craft");
        finishLogin.SetResult();
        var result = await run;
        Assert.False(craftedBeforeScanFinished);
        Assert.Equal(2, result.CreatedBars);
    }

    static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task WorkerFailureCancelsAndDrainsSiblingCraftBeforeReturning()
    {
        var game = new HookedGame(2);
        var siblingCrafting = Signal();
        bool siblingStopped = false;
        game.Walk = async (name, token) =>
        {
            if (name != "Character1") return;
            await siblingCrafting.Task.WaitAsync(token);
            throw new InvalidOperationException("Synthetic bank route blocked.");
        };
        game.Craft = async (name, token) =>
        {
            if (name != "Character2") return;
            siblingCrafting.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { siblingStopped = true; }
        };
        var ledger = new TestLedger();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RunCoordinator(game, ledger, new TestClock()).RunAsync(game.Inner.Roster,
                TestGame.DeliverySettings, limit.Token));
        Assert.Equal("Synthetic bank route blocked.", error.Message);
        Assert.True(siblingStopped);
        Assert.Empty(game.Inner.Deliveries);
        Assert.Contains(ledger.Operations, o => o.Kind == "craft" && o.Outcome == OperationOutcome.NeedsReview);
    }
}

internal sealed class HookedGame(int count) : IGameGateway
{
    public TestGame Inner { get; } = new(count, 50_500_000, 59);
    public Func<string, CancellationToken, Task> Launch { get; set; } = (_, _) => Task.CompletedTask;
    public Func<string, CancellationToken, Task> Craft { get; set; } = (_, _) => Task.CompletedTask;
    public Func<string, CancellationToken, Task> Walk { get; set; } = (_, _) => Task.CompletedTask;
    public Func<string, CancellationToken, Task> FinishDialogs { get; set; } = (_, _) => Task.CompletedTask;
    public async Task LaunchAsync(SavedCharacter character, CancellationToken token) { await Launch(character.Name, token); await Inner.LaunchAsync(character, token); }
    public async Task CraftBarAsync(string name, CancellationToken token) { await Craft(name, token); await Inner.CraftBarAsync(name, token); }
    public Task FinishCraftDialogsAsync(string name, CancellationToken token) => FinishDialogs(name, token);
    public Task<CharacterState> ReadAsync(string name, CancellationToken token) => Inner.ReadAsync(name, token);
    public async Task WalkToBankAsync(string name, CancellationToken token) { await Walk(name, token); await Inner.WalkToBankAsync(name, token); }
    public Task ApproachCharacterAsync(string sender, string receiver, CancellationToken token) => Inner.ApproachCharacterAsync(sender, receiver, token);
    public Task WalkToRecipientAsync(string name, RunSettings settings, CancellationToken token) => Inner.WalkToRecipientAsync(name, settings, token);
    public Task TransferGoldAsync(string sender, string receiver, long amount, CancellationToken token) => Inner.TransferGoldAsync(sender, receiver, amount, token);
    public Task TransferBarsAsync(string sender, string receiver, int amount, CancellationToken token) => Inner.TransferBarsAsync(sender, receiver, amount, token);
}
