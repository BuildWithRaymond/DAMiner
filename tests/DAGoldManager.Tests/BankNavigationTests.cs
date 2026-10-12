using Arbiter.Net.Server.Types;
using DAGoldManager.Core;
using DAGoldManager.Game;
using DAQuester.Core;
using DAQuester.DarkAges.Navigation;
using Xunit;

namespace DAGoldManager.Tests;

public class BankNavigationTests
{
    static CraftRecipe Recipe => new("Lamont", 167, 3, 4, "Gold Bar", 50_500_000, [], DateTimeOffset.UtcNow);
    static MapRepository Maps() => new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json"));

    [Fact]
    public void BankDestinationIsFloorAndOpeningNpcDoesNotApproachCounter()
    {
        var arrival = BankNavigation.Arrival(Recipe);
        Assert.Equal(new Tile(6, 8), arrival.Target);
        Assert.Equal(8, arrival.TargetRow);
        Assert.Equal("open", BankNavigation.OpenNpc(Recipe).Kind);
    }

    [Fact]
    public void ActualBankCounterIsUnreachableButRowEightIsReachable()
    {
        var maps = Maps();
        var grid = maps.Load(167);
        var exits = maps.Record(167).ExitTiles.ToHashSet();
        Assert.Throws<QuestBlockedException>(() => NpcApproach.Resolve(grid, new(6, 10), new(3, 4), exits, 3));
        var arrival = BankNavigation.Arrival(Recipe);
        var floor = NpcApproach.Resolve(grid, new(6, 10), arrival.Target!, exits, arrival.Tolerance, arrival.TargetRow);
        Assert.Equal(8, floor.Y);
        Assert.DoesNotContain(floor, exits);
        Assert.NotEmpty(grid.Path(new(6, 10), floor, exits));
    }

    [Fact]
    public void OccupiedPreferredTileChoosesAnotherTileOnRowEight()
    {
        var grid = new Grid(12, 12, (x, y) => x == 0 || x >= 12 || y < 7 || y >= 12);
        var occupied = new HashSet<Tile> { new(6, 8), new(5, 8), new(5, 11), new(6, 11) };
        var target = NpcApproach.Resolve(grid, new(6, 10), new(6, 8), occupied, 5, 8);
        Assert.Equal(new Tile(7, 8), target);
    }

    [Fact]
    public void AlreadySafeCharacterStaysOnItsOwnTile()
    {
        var grid = Maps().Load(167);
        Assert.Equal(new Tile(7, 8), NpcApproach.Resolve(grid, new(7, 8), new(6, 8), new HashSet<Tile>(), 5, 8));
    }

    [Fact]
    public void FullRowDoesNotFallBackTowardExit()
    {
        var grid = Maps().Load(167);
        var occupied = Enumerable.Range(1, 11).Select(x => new Tile(x, 8)).ToHashSet();
        Assert.Throws<QuestBlockedException>(() => NpcApproach.Resolve(grid, new(6, 10), new(6, 8), occupied, 5, 8));
    }

    [Fact]
    public void WorldMapSelectsAbelForSafeBankFloor()
    {
        var maps = Maps();
        var abel = new ServerWorldMapNode { MapId = 502, Name = "Abel" };
        var target = BankNavigation.Arrival(Recipe).Target;
        Assert.Equal(new Tile(6, 8), target);
        Assert.Same(abel, maps.WorldDestination(167, [abel], 3008, new(15, 16), target));
    }
}
