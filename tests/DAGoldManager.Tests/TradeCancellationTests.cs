using DAGoldManager.Core;
using DAGoldManager.Game;
using Xunit;

namespace DAGoldManager.Tests;

public class TradeCancellationTests
{
    [Fact]
    public void UserStopRetainsCancellationTokenAndTradeStage()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var original = new OperationCanceledException(stop.Token);
        var error = new TradeCancellationException("confirming the bar offer", false, true, original);

        Assert.Equal(stop.Token, error.CancellationToken);
        Assert.Same(original, error.InnerException);
        Assert.Contains("confirming the bar offer", error.Message);
        Assert.Contains("Stop requested", error.Message);
        Assert.Contains("Acceptance was not attempted", error.Message);
    }

    [Fact]
    public void InterruptedAcceptedTradeKeepsAmbiguityExplicit()
    {
        var error = new TradeCancellationException("verifying the completed transfer's balances", true, false,
            new TaskCanceledException());

        Assert.Contains("Session interruption", error.Message);
        Assert.Contains("verifying the completed transfer's balances", error.Message);
        Assert.Contains("may have reached the server", error.Message);
        Assert.Contains("Inspect the game", error.Message);
    }

    [Fact]
    public async Task CancelledTradeRemainsReviewableWithItsStageAndIsNeverRetried()
    {
        using var stop = new CancellationTokenSource();
        var simulated = new TestGame(1, 0, 59);
        simulated.States["Character1"] = simulated.States["Character1"] with { Bars = 2, BarCapacity = 57 };
        var game = new CancelledTradeGame(simulated, stop);
        var ledger = new TestLedger();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RunCoordinator(game, ledger, new TestClock())
            .RunAsync(simulated.Roster, TestGame.DeliverySettings, stop.Token));

        var review = Assert.Single(ledger.Operations, o => o.Outcome == OperationOutcome.NeedsReview);
        Assert.Contains("sending acceptance", review.Detail);
        Assert.Contains("Stop requested", review.Detail);
        Assert.Contains("may have reached the server", review.Detail);
        Assert.DoesNotContain(ledger.Operations, o => o.Outcome == OperationOutcome.Verified);
        Assert.Equal(1, game.TradeAttempts);
    }

    sealed class CancelledTradeGame(TestGame simulated, CancellationTokenSource stop) : IGameGateway
    {
        public int TradeAttempts { get; private set; }
        public Task LaunchAsync(SavedCharacter character, CancellationToken cancellation) => simulated.LaunchAsync(character, cancellation);
        public Task<CharacterState> ReadAsync(string name, CancellationToken cancellation) => simulated.ReadAsync(name, cancellation);
        public Task WalkToBankAsync(string name, CancellationToken cancellation) => simulated.WalkToBankAsync(name, cancellation);
        public Task ApproachCharacterAsync(string sender, string receiver, CancellationToken cancellation) => simulated.ApproachCharacterAsync(sender, receiver, cancellation);
        public Task WalkToRecipientAsync(string courier, RunSettings settings, CancellationToken cancellation) => simulated.WalkToRecipientAsync(courier, settings, cancellation);
        public Task CraftBarAsync(string name, CancellationToken cancellation) => simulated.CraftBarAsync(name, cancellation);
        public Task TransferGoldAsync(string sender, string receiver, long amount, CancellationToken cancellation) => simulated.TransferGoldAsync(sender, receiver, amount, cancellation);
        public Task TransferBarsAsync(string sender, string receiver, int amount, CancellationToken cancellation)
        {
            TradeAttempts++;
            stop.Cancel();
            return Task.FromException(new TradeCancellationException("sending acceptance", true, true,
                new OperationCanceledException(cancellation)));
        }
    }
}
