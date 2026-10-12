using System.Text.Json;
using DAGoldManager.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class UnregisteredCharacterTests
{
    // Deserialize the setting just as an existing saved roster does.
    static SavedCharacter Donor(string name) => JsonSerializer.Deserialize<SavedCharacter>(
        $$"""{"Name":"{{name}}","CanCraftBars":false}""")!;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CappedUnregisteredCharacterFundsRegisteredCrafter(bool poolLeftovers)
    {
        var game = new TestGame(2, 0, 59);
        game.States["Character1"] = game.States["Character1"] with { Gold = 99_999_999 };
        var ledger = new TestLedger();
        var result = await new RunCoordinator(game, ledger, new TestClock()).RunAsync(
            [Donor("Character1"), new("Character2")],
            TestGame.DeliverySettings with { PoolLeftoverGold = poolLeftovers }, default);

        Assert.Equal(1, result.CreatedBars);
        Assert.Equal(1, result.DeliveredBars);
        Assert.Equal(49_499_999, result.EndingGold);
        Assert.All(ledger.Operations.Where(o => o.Kind == "craft"), o => Assert.Equal("Character2", o.Sender));
        Assert.Contains(ledger.Operations, o => o.Kind == "gold trade" && o.Sender == "Character1" &&
            o.Receiver == "Character2" && o.Amount == 50_500_000 && o.Outcome == OperationOutcome.Verified);
    }

    [Fact]
    public async Task SeveralDonorsKeepFundingCrafterUntilAllPossibleBarsAreMade()
    {
        var game = new TestGame(4, 99_999_999, 59);
        var ledger = new TestLedger();
        var result = await new RunCoordinator(game, ledger, new TestClock()).RunAsync(
            [new("Character1"), Donor("Character2"), Donor("Character3"), Donor("Character4")],
            TestGame.DeliverySettings with { BarsPerTrade = 3 }, default);

        Assert.Equal(7, result.CreatedBars);
        Assert.Equal(7, result.DeliveredBars);
        Assert.Equal(46_499_996, result.EndingGold);
        Assert.All(ledger.Operations.Where(o => o.Kind == "craft"), o => Assert.Equal("Character1", o.Sender));
        Assert.All(game.States.Values, state => Assert.InRange(state.Gold, 0, GoldPlanner.CarryingLimit));
    }

    [Fact]
    public async Task NoRegisteredCrafterStopsWithoutSpendingOrMovingGold()
    {
        var game = new TestGame(1, 99_999_999, 59);
        var ledger = new TestLedger();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RunCoordinator(game, ledger, new TestClock()).RunAsync(
                [Donor("Character1")], TestGame.DeliverySettings, default));
        Assert.Contains("registered", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(99_999_999, game.States["Character1"].Gold);
        Assert.Empty(ledger.Operations);
    }

    [Fact]
    public async Task DonorWithExistingBarsCanStillDeliverWithoutRegisteredCrafter()
    {
        var game = new TestGame(1, 0, 59);
        game.States["Character1"] = game.States["Character1"] with { Bars = 2, BarCapacity = 57 };
        var result = await new RunCoordinator(game, new TestLedger(), new TestClock()).RunAsync(
            [Donor("Character1")], TestGame.DeliverySettings with { BarsPerTrade = 3 }, default);
        Assert.Equal(0, result.CreatedBars);
        Assert.Equal(2, result.DeliveredBars);
    }
}
