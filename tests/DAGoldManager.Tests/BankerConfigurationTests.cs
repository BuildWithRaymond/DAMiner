using DAGoldManager.Core;
using DAGoldManager.Storage;
using DAGoldManager.UI;
using Xunit;

namespace DAGoldManager.Tests;

public class BankerConfigurationTests
{
    [Fact]
    public void LocalSetupNeedsOnlyAnEnabledSavedBankerAndCourier()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAGoldManagerTests", Guid.NewGuid().ToString("N"));
        new LocalStore(directory).Save(new([new("Courier"), new("Banker")],
            new() { Courier = "Courier", Recipient = "Banker", BankerMode = BankerMode.Local }, new()));
        var model = new DashboardModel(false, directory, "");
        Assert.True(model.BankerConfigured);
        Assert.Contains("Abel bank", model.BankerDestination);
    }

    [Fact]
    public void FreshInstallationDoesNotChooseAnotherPlayersBanker()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAGoldManagerTests", Guid.NewGuid().ToString("N"));
        var settings = new LocalStore(directory).Load().Run;
        Assert.Equal("", settings.Recipient);
        Assert.Equal(0, settings.RecipientMap);
        Assert.Equal(BankerMode.Local, settings.BankerMode);
    }

    [Fact]
    public void ProfileSavedBeforeBankerModesKeepsExternalVpsBehavior()
    {
        var configuration = System.Text.Json.JsonSerializer.Deserialize<AppConfiguration>(
            """{"Characters":[],"Run":{"Recipient":"Vaultkeeper","RecipientMap":901,"RecipientX":19,"RecipientY":12},"Game":{}}""");
        Assert.Equal(BankerMode.External, configuration!.Run.BankerMode);
        Assert.Equal("Vaultkeeper", configuration.Run.Recipient);
    }

    [Fact]
    public void SavedVpsBankerRetainsItsNameAndLocation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAGoldManagerTests", Guid.NewGuid().ToString("N"));
        var store = new LocalStore(directory);
        store.Save(new([], new() { Recipient = "Vaultkeeper", RecipientMap = 901, RecipientX = 19, RecipientY = 12 }, new()));
        var restored = new LocalStore(directory).Load().Run;
        Assert.Equal("Vaultkeeper", restored.Recipient);
        Assert.Equal(901, restored.RecipientMap);
        Assert.Equal(19, restored.RecipientX);
        Assert.Equal(12, restored.RecipientY);
    }

    [Fact]
    public async Task MissingBankerStopsBeforeLaunchingOrSpending()
    {
        var game = new TestGame(1, 50_500_000, 59);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RunCoordinator(game, new TestLedger(), new TestClock()).RunAsync(game.Roster,
                new() { Courier = "Character1", Recipient = "" }, default));
        Assert.Empty(game.Actions);
    }

    [Fact]
    public async Task CustomExternalBankerReceivesDeliveryAtConfiguredLocation()
    {
        SavedCharacter[] roster = [new("Courier")];
        var game = new SimulatedGame(roster);
        var ledger = new TestLedger();
        var result = await new RunCoordinator(game, ledger, new TestClock()).RunAsync(roster,
            new() { Courier = "Courier", Recipient = "Vaultkeeper", RecipientMap = 901, RecipientX = 19, RecipientY = 12 }, default);
        var courier = await game.ReadAsync("Courier", default);
        Assert.Equal(1, result.DeliveredBars);
        Assert.Equal(901, courier.Map);
        Assert.Equal(12, courier.Y);
        Assert.Contains(ledger.Operations, operation => operation.Receiver == "Vaultkeeper" && operation.Outcome == OperationOutcome.Verified);
        Assert.Single(game.States);
    }
}
