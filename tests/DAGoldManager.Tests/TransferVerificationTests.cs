using DAGoldManager.Core;
using Xunit;

namespace DAGoldManager.Tests;

public class TransferVerificationTests
{
    [Theory]
    [InlineData(true, false, false, 3, 5, 2, false)]
    [InlineData(true, true, false, 3, 5, 5, false)]
    [InlineData(true, true, false, 3, 5, 3, false)]
    [InlineData(true, true, false, 3, 5, 2, true)]
    [InlineData(false, false, true, 3, 5, 2, true)]
    public void AcceptanceMustMatchExactInventoryChange(bool local, bool partner, bool completed,
        int expected, int before, int after, bool valid)
    {
        Assert.Equal(valid, TransferVerification.IsComplete(local, partner, completed, expected, before, after));
    }
}
