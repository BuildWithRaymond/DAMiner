using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using DAGoldManager.Core;
using DAGoldManager.Game;
using DAQuester.Core;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Protocol;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

public class RegionalCraftingTests
{
    static CraftRecipe AbelRecipe => new("Lamont", 167, 3, 4, "Gold Bar", 50_500_000,
        [new(DialogActionKind.Choice, "Synthetic bank menu", "Gold Bar"),
         new(DialogActionKind.Choice, "Synthetic purchase confirmation", "Yes")], DateTimeOffset.UtcNow)
        { OfferName = "Gold Bar 100%" };
    static MapRepository Maps() => new("", Path.Combine(AppContext.BaseDirectory, "content", "routes.json"));

    [Fact]
    public void NoamUsesKelanWithoutChangingRecordedTermsOrActions()
    {
        var saved = AbelRecipe;
        var selected = RegionalCrafting.ForBank(saved, 10061);
        Assert.Equal("Kelan", selected.NpcName);
        Assert.Equal(10061, selected.Map);
        Assert.Equal(saved.GoldCost, selected.GoldCost);
        Assert.Equal(saved.BarName, selected.BarName);
        Assert.Equal(saved.OfferName, selected.OfferName);
        Assert.Equal(saved.CooldownSeconds, selected.CooldownSeconds);
        Assert.Equal(saved.Actions, selected.Actions);
        Assert.Equal("Lamont", saved.NpcName);
        Assert.Equal(167, saved.Map);
    }

    [Fact]
    public void ExistingRecordedBankRecipeIsPreserved()
    {
        var saved = AbelRecipe;
        Assert.Same(saved, RegionalCrafting.ForBank(saved, saved.Map));
    }

    [Fact]
    public void NoamArrivalUsesReachableInteriorFloorAwayFromExit()
    {
        var command = BankNavigation.Arrival(RegionalCrafting.ForBank(AbelRecipe, 10061));
        Assert.Equal(10061, command.Map);
        Assert.Equal(new Tile(8, 8), command.Target);
        var maps = Maps();
        Assert.DoesNotContain(command.Target!, maps.Record(10061).ExitTiles);
        Assert.NotEmpty(maps.Load(10061).Path(new(12, 6), command.Target!));
    }

    [Fact]
    public void RecordingCanReadHomeBankAndCourierKeepsItAfterCrossingContinents()
    {
        using var client = new TcpClient();
        using var remote = new TcpClient();
        var connection = new ProxyConnection(client, remote, 1, _ => Task.FromResult(0));
        connection.Tracker.Identity("Fictional", 1);
        var character = new CharacterConnection(connection, Maps(), 400);
        void Publish(int map) => typeof(StateTracker).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(connection.Tracker, [character.Snapshot with { Map = map, Position = new(8, 8),
                Gold = 100_000, InventoryKnown = true, Weight = 0, MaxWeight = 100 }]);
        var gateway = new GameGateway(new(), null, _ => throw new InvalidOperationException("No real credentials."), "");
        var characters = (ConcurrentDictionary<string, CharacterConnection>)typeof(GameGateway)
            .GetField("characters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gateway)!;
        characters["Fictional"] = character;
        Publish(10055);
        Assert.Equal(10061, Assert.Single(gateway.KnownStates()).CraftBankMap);
        Publish(167);
        Assert.Equal(10061, Assert.Single(gateway.KnownStates()).CraftBankMap);
    }

    [Theory]
    [InlineData(10061, true)]
    [InlineData(10000, true)]
    [InlineData(6997, true)]
    [InlineData(8995, true)]
    [InlineData(3950, true)]
    [InlineData(9350, true)]
    [InlineData(167, false)]
    [InlineData(500, false)]
    [InlineData(1960, false)]
    [InlineData(6926, false)]
    public void RegionalSelectionFollowsLocalTopologyRatherThanMapNumber(int map, bool medenia)
        => Assert.Equal(medenia, Maps().IsMedenia(map));
}
