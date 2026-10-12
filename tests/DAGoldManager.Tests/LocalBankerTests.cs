using DAGoldManager.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class LocalBankerTests
{
    [Fact]
    public async Task LocalBankerNeedsNoDepositNpcSetting()
    {
        var game = new BankerGame();
        var result = await new RunCoordinator(game, new TestLedger(), new TestClock()).RunAsync(
            game.Roster, Settings with { DepositNpcName = "" }, default);
        Assert.Equal(3, result.DeliveredBars);
        Assert.Equal(3, game.Deposited);
    }

    [Fact]
    public async Task LocalBankerReceivesAndDepositsEveryBatchWithoutCraftingOrFixedDelay()
    {
        var game = new BankerGame();
        var ledger = new TestLedger();
        var clock = new TestClock();
        var result = await new RunCoordinator(game, ledger, clock).RunAsync(game.Roster, Settings, default);
        Assert.Equal(3, result.DeliveredBars);
        Assert.Equal(3, game.Deposited);
        Assert.Equal(0, game.Banker.Bars);
        Assert.Contains("Banker", game.Launched);
        Assert.DoesNotContain("Banker", game.Crafted);
        Assert.Empty(clock.Delays);
        Assert.Equal(3, ledger.Operations.Count(o => o.Kind == "bar deposit" && o.Outcome == OperationOutcome.Verified));
    }

    [Fact]
    public async Task UnconfirmedDepositStopsBeforeAnotherBatchAndRequiresReview()
    {
        var game = new BankerGame { ConfirmDeposit = false };
        var ledger = new TestLedger();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RunCoordinator(game, ledger, new TestClock()).RunAsync(game.Roster, Settings, default));
        Assert.Equal(1, game.Trades);
        Assert.Equal(1, game.DepositAttempts);
        Assert.Contains(ledger.Operations, o => o.Kind == "bar deposit" && o.Outcome == OperationOutcome.NeedsReview);
    }

    [Fact]
    public async Task DisabledLocalBankerIsRejectedBeforeAnyLaunch()
    {
        var game = new BankerGame();
        var roster = game.Roster.Select(c => c.Name == "Banker" ? c with { Enabled = false } : c).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RunCoordinator(game, new TestLedger(), new TestClock()).RunAsync(roster, Settings, default));
        Assert.Empty(game.Launched);
    }

    static RunSettings Settings => new()
    {
        Courier = "Character1", Recipient = "Banker", BankerMode = BankerMode.Local,
        DepositNpcName = "Fictional NPC", BarsPerTrade = 1
    };
}

internal sealed class BankerGame : IGameGateway
{
    readonly TestGame inner = new(3, 50_500_000, 59);
    public bool ConfirmDeposit { get; init; } = true;
    public int Deposited, Trades, DepositAttempts;
    public List<string> Launched { get; } = [];
    public List<string> Crafted { get; } = [];
    public SavedCharacter[] Roster => inner.Roster;
    public CharacterState Banker => inner.States["Banker"];

    public BankerGame() => inner.States["Banker"] = new("Banker", 0, 0, 1, 901, 19, 12);
    public Task LaunchAsync(SavedCharacter character, CancellationToken token) { Launched.Add(character.Name); return Task.CompletedTask; }
    public Task<CharacterState> ReadAsync(string name, CancellationToken token) => inner.ReadAsync(name, token);
    public Task WalkToBankAsync(string name, CancellationToken token) => Task.CompletedTask;
    public Task ApproachCharacterAsync(string sender, string receiver, CancellationToken token) => Task.CompletedTask;
    public Task WalkToRecipientAsync(string courier, RunSettings settings, CancellationToken token)
    {
        inner.States[courier] = inner.States[courier] with { Map = Banker.Map, X = Banker.X - 1, Y = Banker.Y };
        return Task.CompletedTask;
    }
    public Task CraftBarAsync(string name, CancellationToken token) { Crafted.Add(name); return inner.CraftBarAsync(name, token); }
    public Task TransferGoldAsync(string sender, string receiver, long amount, CancellationToken token) => inner.TransferGoldAsync(sender, receiver, amount, token);
    public Task TransferBarsAsync(string sender, string receiver, int amount, CancellationToken token)
    { if (receiver == "Banker") Trades++; return inner.TransferBarsAsync(sender, receiver, amount, token); }
    public Task PrepareBankerAsync(string name, RunSettings settings, CancellationToken token) => Task.CompletedTask;
    public Task DepositBarAsync(string name, RunSettings settings, CancellationToken token)
    {
        DepositAttempts++;
        if (ConfirmDeposit) { inner.States[name] = Banker with { Bars = Banker.Bars - 1, BarCapacity = Banker.BarCapacity + 1 }; Deposited++; }
        return Task.CompletedTask;
    }
}
