using System.Text;
using DAGoldManager.Core;
using DAGoldManager.Storage;
using DAGoldManager.UI;
using DAQuester.DarkAges.Protocol;
using DAQuester.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class RegistrationRefusalTests
{
    const string Refusal = "((Please register your character to use this feature.))";

    [Fact]
    public void ExplicitRegistrationNoticeRemainsAvailableForCraftVerification()
    {
        var tracker = new StateTracker(7);
        tracker.Identity("Fictional", 1);
        byte[] text = Encoding.ASCII.GetBytes(Refusal);
        tracker.Observe(0x0A, [(byte)3, (byte)(text.Length >> 8), (byte)text.Length, .. text]);
        Assert.Equal(Refusal, tracker.Snapshot.Notice?.Text);
        Assert.Equal(tracker.Snapshot.Revision, tracker.Snapshot.Notice?.Revision);
        Assert.Equal(7, tracker.Snapshot.Notice?.Generation);
        Assert.False(tracker.Snapshot.Registered);
    }

    [Fact]
    public async Task ExplicitRefusalWithUnchangedBalancesBecomesDonorWithoutRetry()
    {
        var game = new HookedGame(2);
        game.Inner.States["Character2"] = game.Inner.States["Character2"] with { Gold = 0 };
        int attempts = 0;
        game.Craft = (name, _) =>
        {
            if (name != "Character1") return Task.CompletedTask;
            attempts++;
            throw new CraftRegistrationRequiredException(Refusal);
        };
        var ledger = new TestLedger();
        var result = await new RunCoordinator(game, ledger, new TestClock()).RunAsync(game.Inner.Roster,
            TestGame.DeliverySettings with { PoolLeftoverGold = false }, default);
        Assert.Equal(1, attempts);
        Assert.Equal(1, result.CreatedBars);
        Assert.Equal(1, result.DeliveredBars);
        Assert.Contains(ledger.Operations, o => o.Sender == "Character1" && o.Outcome.ToString() == "Rejected");
        Assert.DoesNotContain(ledger.Operations, o => o.Outcome == OperationOutcome.NeedsReview);
    }

    [Fact]
    public async Task RefusalAccompaniedByChangedBalanceStillRequiresReview()
    {
        var game = new HookedGame(1);
        game.Craft = (name, _) =>
        {
            game.Inner.States[name] = game.Inner.States[name] with { Gold = 1 };
            throw new CraftRegistrationRequiredException(Refusal);
        };
        var ledger = new TestLedger();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RunCoordinator(game, ledger, new TestClock()).RunAsync(game.Inner.Roster,
                TestGame.DeliverySettings, default));
        Assert.Contains(ledger.Operations, o => o.Outcome == OperationOutcome.NeedsReview);
        Assert.Empty(game.Inner.Deliveries);
    }

    [Fact]
    public void UpdatingPasswordPreservesSavedDonorAndDisabledFlags()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAGoldManagerTests", Guid.NewGuid().ToString("N"));
        var store = new LocalStore(directory);
        store.Save(new([new("Fictional", Enabled: false, CanCraftBars: false)], new() { Courier = "Fictional" }, new()));
        new DashboardModel(false, directory, "").SaveCharacter("fictional", "");
        var character = Assert.Single(store.Load().Characters);
        Assert.False(character.Enabled);
        Assert.False(character.CanCraftBars);
    }

    [Fact]
    public async Task RegistrationRefusalSeenAtLoginSkipsCraftAttempt()
    {
        var game = new HookedGame(2);
        game.Inner.States["Character1"] = game.Inner.States["Character1"] with { Registered = false };
        game.Inner.States["Character2"] = game.Inner.States["Character2"] with { Gold = 0 };
        game.Craft = (name, _) => name == "Character1" ?
            throw new InvalidOperationException("Unregistered account should not attempt craft") : Task.CompletedTask;
        var ledger = new TestLedger();
        var result = await new RunCoordinator(game, ledger, new TestClock()).RunAsync(game.Inner.Roster,
            TestGame.DeliverySettings, default);
        Assert.Equal(1, result.CreatedBars);
        Assert.DoesNotContain(ledger.Operations, o => o.Kind == "craft" && o.Sender == "Character1");
    }

    [Fact]
    public void OldRefusalCannotRejectAnotherRequest()
    {
        var state = new CharacterSnapshot { Generation = 7, Notice = new(7, 10, 3, Refusal, DateTimeOffset.UtcNow) };
        Assert.True(RegistrationEvidence.IsFreshRefusal(state, 9));
        Assert.False(RegistrationEvidence.IsFreshRefusal(state, 10));
        Assert.False(RegistrationEvidence.IsFreshRefusal(state with { Generation = 8 }, 9));
        Assert.False(RegistrationEvidence.IsRefusal("Your registration expires on October 31."));
    }

    [Theory]
    [InlineData(0)] // Whisper
    [InlineData(5)] // World shout
    [InlineData(11)] // Group chat
    [InlineData(12)] // Guild chat
    public void ChatTextCannotChangeCraftRegistration(byte messageType)
    {
        var tracker = new StateTracker(7);
        tracker.Identity("Fictional", 1);
        byte[] text = Encoding.ASCII.GetBytes(Refusal);
        tracker.Observe(0x0A, [messageType, (byte)(text.Length >> 8), (byte)text.Length, .. text]);
        Assert.Null(tracker.Snapshot.Registered);
    }
}
