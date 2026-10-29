using DAQuester.Core;
using DAQuester.DarkAges.Navigation;
using Xunit;

namespace DAGoldManager.Tests;

public class ContinentTravelTests
{
    static MapRepository Maps() => new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json"));

    [Theory]
    [InlineData(10055, 43, 26)]
    [InlineData(10056, 4, 15)]
    [InlineData(10061, 8, 8)]
    public void MedeniaCourierCanReachAbelThroughTagor(int map, int x, int y)
    {
        var plan = Maps().Plan(map, new(x, y), 167, new(6, 8));
        Assert.Contains(plan.Steps, step => step.SourceMap == 10055 && step.Map == 1960);
    }

    [Fact]
    public void AbelCourierCanReturnThroughAsilonShipAndCaravan()
    {
        var maps = Maps();
        // Unknown legacy doorway landings deliberately produce one-hop plans;
        // topology still needs both observed-menu services for the whole trip.
        var route = maps.Route(167, 10061);
        Assert.Contains(route, edge => edge.Map == 10028 && edge.NpcTransfer?.Npc == "Quard");
        Assert.Contains(route, edge => edge.Map == 10999 && edge.NpcTransfer?.Npc == "Lenoa");
        var plan = maps.Plan(167, new(6, 8), 10061, new(8, 8));
        Assert.Equal(502, plan.Steps.First().Map);
    }

    [Fact]
    public void AsilonCharacterReachesNoamWithoutVisitingTemuair()
    {
        var plan = Maps().Plan(10000, new(17, 5), 10061, new(8, 8));
        Assert.Contains(plan.Steps, step => step.SourceMap == 10000 && step.Map == 10999 && step.NpcTransfer?.Npc == "Lenoa");
        Assert.DoesNotContain(plan.Steps, step => step.Map is 1960 or 6926 or 167);
    }

    [Fact]
    public void LocalNoamBankTripStaysInNoam()
    {
        var plan = Maps().Plan(10055, new(43, 26), 10061, new(8, 8));
        Assert.All(plan.Steps, step => Assert.Equal(10061, step.Map));
    }

    [Fact]
    public void LegacyNpcApproachesAreNotWalkingExits()
    {
        var maps = Maps();
        Assert.DoesNotContain(maps.Record(6926).Portals, portal => portal.Map == 10028);
        Assert.DoesNotContain(maps.Record(10000).Portals, portal => portal.Map == 10999);
    }

    [Theory]
    [InlineData(10055, true)]
    [InlineData(10061, true)]
    [InlineData(10000, true)]
    [InlineData(10004, true)]
    [InlineData(6997, true)]
    [InlineData(8995, true)]
    [InlineData(167, false)]
    [InlineData(500, false)]
    [InlineData(6926, false)]
    [InlineData(65534, false)]
    public void BankRegionUsesLocalRoutesIncludingMedeniaCaravans(int map, bool medenia)
    {
        Assert.Equal(medenia, Maps().IsMedenia(map));
    }
}
