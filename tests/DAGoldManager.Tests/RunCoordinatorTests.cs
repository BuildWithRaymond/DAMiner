using DAGoldManager.Core;
using System.Collections.Concurrent;
using Xunit;

namespace DAGoldManager.Tests;

public class RunCoordinatorTests
{
    [Fact]
    public async Task AllCraftsFinishBeforeDeliveryAndOnlyCourierTradesToExternalBanker()
    {
        var game = new TestGame(4, 99_999_999, 59);
        var clock = new TestClock();
        var ledger = new TestLedger();
        var result = await new RunCoordinator(game, ledger, clock).RunAsync(game.Roster,
            TestGame.DeliverySettings with { BarsPerTrade = 3 }, default);

        Assert.Equal(7, result.CreatedBars);
        Assert.Equal(7, result.DeliveredBars);
        Assert.Equal(46_499_996, result.EndingGold);
        Assert.Equal(new[] { 3, 3, 1 }, game.Deliveries.Select(d => d.Amount));
        Assert.All(game.Deliveries, d => Assert.Equal("Character1", d.Sender));
        Assert.True(game.Actions.FindLastIndex(a => a == "craft") < game.Actions.FindIndex(a => a == "deliver"));
        Assert.Equal(2, clock.Delays.Count);
        Assert.All(clock.Delays, d => Assert.Equal(TimeSpan.FromMinutes(3), d));
        Assert.Equal(ledger.Operations.Count(o => o.Outcome == OperationOutcome.Started),
            ledger.Operations.Count(o => o.Outcome == OperationOutcome.Verified));
    }

    [Fact]
    public async Task CourierReturnsForOverflowAndStillHonorsDepositWait()
    {
        var game = new TestGame(3, 50_500_000, 2);
        var clock = new TestClock();
        var result = await new RunCoordinator(game, new TestLedger(), clock).RunAsync(game.Roster,
            TestGame.DeliverySettings with { BarsPerTrade = 10 }, default);
        Assert.Equal(new[] { 2, 1 }, game.Deliveries.Select(d => d.Amount));
        Assert.Single(clock.Delays);
        Assert.Equal(3, result.DeliveredBars);
    }

    [Fact]
    public async Task IncorrectCraftOutcomeStopsWithoutTradingAnything()
    {
        var game = new TestGame(1, 50_500_000, 59) { BreakCraft = true };
        var ledger = new TestLedger();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RunCoordinator(game, ledger, new TestClock())
            .RunAsync(game.Roster, TestGame.DeliverySettings, default));
        Assert.Empty(game.Deliveries);
        Assert.Contains(ledger.Operations, o => o.Outcome == OperationOutcome.NeedsReview);
    }

    [Fact]
    public async Task ExistingBarsAreDeliveredWithoutBeingCountedAsCreated()
    {
        var game = new TestGame(1, 0, 59);
        game.States["Character1"] = game.States["Character1"] with { Bars = 2, BarCapacity = 57 };
        var result = await new RunCoordinator(game, new TestLedger(), new TestClock())
            .RunAsync(game.Roster, TestGame.DeliverySettings with { BarsPerTrade = 10 }, default);
        Assert.Equal(0, result.CreatedBars);
        Assert.Equal(2, result.StartingBars);
        Assert.Equal(2, result.DeliveredBars);
    }

    [Fact]
    public async Task CancelledRunDoesNotLaunchOrSpendGold()
    {
        var game = new TestGame(1, 50_500_000, 59);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RunCoordinator(game, new TestLedger(), new TestClock())
            .RunAsync(game.Roster, TestGame.DeliverySettings, cancellation.Token));
        Assert.Empty(game.Actions);
    }

    [Fact]
    public async Task VpsRecipientIsNeverLaunchedOrIncludedInProduction()
    {
        var game = new TestGame(1, 50_500_000, 59);
        game.States["Vaultkeeper"] = new("Vaultkeeper", 99_999_999, 0, 59, 901, 19, 12);
        var result = await new RunCoordinator(game, new TestLedger(), new TestClock())
            .RunAsync(game.Roster, TestGame.DeliverySettings, default);
        Assert.Single(game.Actions, a => a == "launch");
        Assert.Equal(99_999_999, game.States["Vaultkeeper"].Gold);
        Assert.Equal(50_500_000, result.StartingGold);
        Assert.Equal(1, result.CreatedBars);
        Assert.Equal(1, result.DeliveredBars);
    }
}

internal sealed class TestGame : IGameGateway
{
    public static RunSettings DeliverySettings => new()
    {
        Courier = "Character1", Recipient = "Vaultkeeper", RecipientMap = 901, RecipientX = 19, RecipientY = 12
    };
    public ConcurrentDictionary<string, CharacterState> States { get; } = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentQueue<string> actions = new();
    public List<string> Actions => actions.ToList();
    public List<(string Sender, int Amount)> Deliveries { get; } = [];
    public bool BreakCraft { get; init; }
    public SavedCharacter[] Roster => States.Keys.Select(n => new SavedCharacter(n)).ToArray();

    public TestGame(int count, long gold, int capacity)
    {
        for (int i = 1; i <= count; i++)
            States[$"Character{i}"] = new($"Character{i}", gold, 0, capacity, 167, 10, 10);
    }

    public Task LaunchAsync(SavedCharacter character, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); actions.Enqueue("launch"); return Task.CompletedTask; }
    public Task<CharacterState> ReadAsync(string name, CancellationToken cancellation) => Task.FromResult(States[name]);
    public Task WalkToBankAsync(string name, CancellationToken cancellation) => Task.CompletedTask;
    public Task ApproachCharacterAsync(string sender, string receiver, CancellationToken cancellation) => Task.CompletedTask;
    public Task WalkToRecipientAsync(string courier, RunSettings settings, CancellationToken cancellation) => Task.CompletedTask;
    public Task CraftBarAsync(string name, CancellationToken cancellation)
    {
        actions.Enqueue("craft");
        var state = States[name];
        States[name] = state with { Gold = state.Gold - 50_500_000, Bars = state.Bars + (BreakCraft ? 0 : 1), BarCapacity = state.BarCapacity - 1 };
        return Task.CompletedTask;
    }
    public Task TransferGoldAsync(string sender, string receiver, long amount, CancellationToken cancellation)
    {
        States[sender] = States[sender] with { Gold = States[sender].Gold - amount };
        States[receiver] = States[receiver] with { Gold = States[receiver].Gold + amount };
        return Task.CompletedTask;
    }
    public Task TransferBarsAsync(string sender, string receiver, int amount, CancellationToken cancellation)
    {
        var donor = States[sender];
        States[sender] = donor with { Bars = donor.Bars - amount, BarCapacity = donor.BarCapacity + amount };
        if (receiver == "Vaultkeeper") { Deliveries.Add((sender, amount)); actions.Enqueue("deliver"); }
        else
        {
            var to = States[receiver];
            Assert.True(to.BarCapacity >= amount);
            States[receiver] = to with { Bars = to.Bars + amount, BarCapacity = to.BarCapacity - amount };
        }
        return Task.CompletedTask;
    }
}

internal sealed class TestLedger : IOperationLedger
{
    public ConcurrentQueue<OperationRecord> Operations { get; } = new();
    public Task AppendAsync(OperationRecord operation, CancellationToken cancellation) { Operations.Enqueue(operation); return Task.CompletedTask; }
}

internal sealed class TestClock : IRunClock
{
    public DateTimeOffset Now { get; private set; } = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
    public List<TimeSpan> Delays { get; } = [];
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); Delays.Add(duration); Now += duration; return Task.CompletedTask; }
}
