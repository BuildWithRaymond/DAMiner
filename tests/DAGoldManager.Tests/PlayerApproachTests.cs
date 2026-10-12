using DAQuester.Core;
using DAQuester.DarkAges.Navigation;
using Xunit;

namespace DAGoldManager.Tests;

public class PlayerApproachTests
{
    [Fact]
    public void CourierAlreadyWithinTradeRangeKeepsItsOwnFreeTile()
    {
        var grid = new Grid(60, 60, (_, _) => false);
        var banker = new Tile(19, 12);
        var courier = new Tile(17, 12);
        Assert.Equal(courier, NpcApproach.Resolve(grid, courier, banker,
            new HashSet<Tile> { banker }, 2, stopWithinTolerance: true));
    }

    [Fact]
    public void ReachedTradeRangeStopsEvenWhenCloserTilesAreOccupied()
    {
        var grid = new Grid(60, 60, (_, _) => false);
        var occupied = new HashSet<Tile> { new(19, 12), new(18, 12), new(20, 12), new(19, 11), new(19, 13) };
        var courier = new Tile(17, 12);
        Assert.Equal(courier, NpcApproach.Resolve(grid, courier, new(19, 12), occupied, 2, stopWithinTolerance: true));
    }

    [Fact]
    public void OccupiedGoalCanBeReplacedWithAnotherReachableTradeTile()
    {
        var grid = new Grid(60, 60, (_, _) => false);
        var occupied = new HashSet<Tile> { new(19, 12), new(18, 12), new(19, 11), new(19, 13) };
        var target = NpcApproach.Resolve(grid, new(11, 12), new(19, 12), occupied, 2, stopWithinTolerance: true);
        Assert.DoesNotContain(target, occupied);
        Assert.InRange(Math.Abs(target.X - 19) + Math.Abs(target.Y - 12), 1, 2);
        Assert.NotEmpty(grid.Path(new(11, 12), target, occupied));
    }

    [Fact]
    public void ArrivalTriggerIsNotUsedAsTradeStandingTile()
    {
        var grid = new Grid(60, 60, (_, _) => false);
        var courier = new Tile(17, 12);
        var avoided = new HashSet<Tile> { courier, new(19, 12) };
        var target = NpcApproach.Resolve(grid, courier, new(19, 12), avoided, 2, stopWithinTolerance: true);
        Assert.NotEqual(courier, target);
        Assert.DoesNotContain(target, avoided);
    }
}
