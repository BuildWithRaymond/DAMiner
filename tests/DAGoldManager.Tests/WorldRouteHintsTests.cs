using System.Text.Json;
using Arbiter.Net.Server.Types;
using DAQuester.Core;
using DAQuester.DarkAges.Navigation;
using Xunit;

namespace DAGoldManager.Tests;

public class WorldRouteHintsTests
{
    [Fact]
    public void UnknownLandingPlansOnlyTheNextExit()
    {
        using var world = new TestWorld();
        world.Hints(new { SourceMap = 1, DestinationMap = 2, From = new Tile(4, 2) });
        var plan = world.Maps().Plan(1, new(1, 2), 3, new(2, 2));
        var step = Assert.Single(plan.Steps);
        Assert.Equal(2, step.Map);
        Assert.Equal(new Tile(4, 2), step.From);
        // A missing landing must not be represented by an invented coordinate.
        Assert.Null(step.To);
    }

    [Fact]
    public void ObservedLandingIsUsedForTheNextPlan()
    {
        using var world = new TestWorld();
        world.Hints(new { SourceMap = 1, DestinationMap = 2, From = new Tile(4, 2) });
        var maps = world.Maps();
        Assert.Single(maps.Plan(1, new(1, 2), 3).Steps);
        var next = Assert.Single(maps.Plan(2, new(3, 3), 3).Steps);
        Assert.Equal(2, next.SourceMap);
        Assert.Equal(3, next.Map);
    }

    [Fact]
    public void OccupiedHintDoesNotBecomeAWalkableExit()
    {
        using var world = new TestWorld();
        world.Hints(new { SourceMap = 1, DestinationMap = 2, From = new Tile(4, 2) });
        Assert.Throws<InvalidOperationException>(() => world.Maps().Plan(1, new(1, 2), 3,
            obstacles: new HashSet<Tile> { new(4, 2) }));
    }

    [Theory]
    [InlineData(99, 4, 2)]
    [InlineData(2, 6, 2)]
    [InlineData(1, 4, 2)]
    public void InvalidHintsCannotCreateARoute(int destination, int x, int y)
    {
        using var world = new TestWorld();
        world.Hints(new { SourceMap = 1, DestinationMap = destination, From = new Tile(x, y) });
        Assert.Throws<InvalidOperationException>(() => world.Maps().Plan(1, new(1, 2), 3));
    }

    [Fact]
    public void CompleteRouteIsPreferredToUnknownLanding()
    {
        using var world = new TestWorld(complete: true);
        world.Hints(new { SourceMap = 1, DestinationMap = 2, From = new Tile(2, 2) });
        var step = Assert.Single(world.Maps().Plan(1, new(1, 2), 3).Steps);
        Assert.Equal(3, step.Map);
        Assert.NotNull(step.To);
    }

    [Fact]
    public void PackagedCoverageMatchesEveryReferencedMapsRuntimeTopology()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "content");
        var maps = new MapRepository(Path.GetTempPath(), Path.Combine(directory, "routes.json"));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "navigation-coverage.json")));
        int bank = report.RootElement.GetProperty("bankMap").GetInt32();
        int reachable = 0;
        foreach (var entry in report.RootElement.GetProperty("maps").EnumerateArray())
        {
            int id = entry.GetProperty("id").GetInt32();
            if (entry.GetProperty("combinedBankTopology").GetBoolean())
            {
                maps.Route(id, bank, includeNpcTransfers: false);
                reachable++;
            }
            else Assert.Throws<InvalidOperationException>(() => maps.Route(id, bank, includeNpcTransfers: false));
        }
        Assert.Equal(report.RootElement.GetProperty("summary").GetProperty("combinedBankTopology").GetInt32(), reachable);
    }

    [Fact]
    public void HintCannotOverrideAnExistingPortalToAnotherMap()
    {
        using var world = new TestWorld(complete: true);
        world.Hints(new { SourceMap = 1, DestinationMap = 2, From = new Tile(5, 2) });
        var maps = world.Maps();
        Assert.DoesNotContain(maps.Record(1).Portals, p => p.To is null);
        Assert.Equal(3, Assert.Single(maps.Plan(1, new(1, 2), 3).Steps).Map);
    }

    [Fact]
    public void SharedUnidentifiedTriggerCannotChooseAnArbitraryDestination()
    {
        using var world = new TestWorld();
        world.Hints(new { SourceMap = 1, DestinationMap = 2, From = new Tile(4, 2) },
            new { SourceMap = 1, DestinationMap = 3, From = new Tile(4, 2) });
        Assert.Throws<InvalidOperationException>(() => world.Maps().Plan(1, new(1, 2), 3));
    }

    [Fact]
    public void WorldMapPrefersACompleteRouteOverAShortPartialHint()
    {
        using var world = new TestWorld();
        world.Hints(new { SourceMap = 1, DestinationMap = 2, From = new Tile(4, 2) });
        var partial = new ServerWorldMapNode { MapId = 1, Name = "Incomplete" };
        var complete = new ServerWorldMapNode { MapId = 3, Name = "Bank" };
        Assert.Same(complete, world.Maps().WorldDestination(3, [partial, complete], 4, new(5, 2), new(5, 5)));
    }

    sealed class TestWorld : IDisposable
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), "DAMinerRouteTests", Guid.NewGuid().ToString("N"));
        public TestWorld(bool complete = false)
        {
            Directory.CreateDirectory(Path.Combine(directory, "maps"));
            MapRecord[] records = [
                new(1, "Start", 6, 6, complete ? [new(new(5, 2), 3, new(1, 2))] : []),
                new(2, "Bridge", 6, 6, [new(new(5, 2), 3, new(1, 2))]),
                new(3, "Bank", 6, 6, []),
                new(4, "World entrance", 6, 6, [new(new(5, 2), 1, new(1, 2), true), new(new(5, 2), 3, new(1, 2), true)])
            ];
            File.WriteAllText(Path.Combine(directory, "routes.json"), JsonSerializer.Serialize(records));
            var collision = new
            {
                SchemaVersion = 1, ServerProfile = "datester-7.41", SotpSha256 = new string('0', 64),
                Maps = records.Select(r => new { r.Id, r.Width, r.Height,
                    BlockedBits = Convert.ToBase64String(new byte[5]), SourceSha256 = new string('0', 64) }),
                MissingMapIds = Array.Empty<int>()
            };
            File.WriteAllText(Path.Combine(directory, "maps", "datester-7.41.walkability.json"), JsonSerializer.Serialize(collision));
        }
        public void Hints(params object[] hints) =>
            File.WriteAllText(Path.Combine(directory, "route-hints.json"), JsonSerializer.Serialize(hints));
        public MapRepository Maps() => new(directory, Path.Combine(directory, "routes.json"));
        public void Dispose() => Directory.Delete(directory, true);
    }
}
