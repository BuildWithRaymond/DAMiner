using DAGoldManager.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class GoldPlannerTests
{
    [Fact]
    public void FourCappedCharactersProduceSevenBarsAndPreserveEveryRemainingCoin()
    {
        var balances = Enumerable.Range(1, 4).Select(i => new GoldBalance($"Character{i}", 99_999_999)).ToList();
        int bars = 0;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int funded = balances.FindIndex(c => c.Gold >= GoldPlanner.DefaultBarCost);
            if (funded >= 0)
            {
                balances[funded] = balances[funded] with { Gold = balances[funded].Gold - GoldPlanner.DefaultBarCost };
                bars++;
                continue;
            }
            var transfer = GoldPlanner.NextTransfer(balances, GoldPlanner.DefaultBarCost);
            if (transfer is null) break;
            int sender = balances.FindIndex(c => c.Name == transfer.Sender);
            int receiver = balances.FindIndex(c => c.Name == transfer.Receiver);
            balances[sender] = balances[sender] with { Gold = balances[sender].Gold - transfer.Amount };
            balances[receiver] = balances[receiver] with { Gold = balances[receiver].Gold + transfer.Amount };
            Assert.InRange(balances[receiver].Gold, 0, GoldPlanner.CarryingLimit);
        }
        Assert.Equal(7, bars);
        Assert.Equal(46_499_996, balances.Sum(c => c.Gold));
    }

    [Fact]
    public void InsufficientCombinedGoldDoesNotMoveMoney()
    {
        Assert.Null(GoldPlanner.NextTransfer([new("A", 25_000_000), new("B", 25_000_000)], 50_500_000));
    }

    [Fact]
    public void TransferFundsOnlyTheReceiversDeficit()
    {
        Assert.Equal(new GoldTransfer("B", "A", 1_000_001),
            GoldPlanner.NextTransfer([new("A", 49_499_999), new("B", 10_000_000)], 50_500_000));
    }

    [Fact]
    public void DisablingLeftoverPoolingDoesNotTakeGoldFromAnotherCrafter()
    {
        Assert.Null(GoldPlanner.NextTransfer([new("A", 49_000_000), new("B", 25_000_000),
            new("Donor", 1_000_000, CanCraftBars: false)], 50_500_000, poolCrafterLeftovers: false));
    }
}
