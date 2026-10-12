using DAGoldManager.Game;
using Xunit;

namespace DAGoldManager.Tests;

public class BankDepositPacketsTests
{
    [Fact]
    public void SingleBarUsesExactInventorySlotWithoutAQuantityArgument()
        => Assert.Equal(new byte[] { 1, 0x11, 0x22, 0x33, 0x44, 0, 0x53, 7 },
            BankDepositPackets.Create(0x11223344, 7, 1).Data);

    [Fact]
    public void StackedBarsDepositExactlyOneUsingQuantitySlotFormat()
        => Assert.Equal(new byte[] { 1, 0x11, 0x22, 0x33, 0x44, 0, 0x54, 7, 1, (byte)'1' },
            BankDepositPackets.Create(0x11223344, 7, 10).Data);

    [Fact]
    public void CloseTargetsOnlyTheDepositNpc()
        => Assert.Equal(new byte[] { 1, 0x11, 0x22, 0x33, 0x44, 0, 0, 0, 1 },
            BankDepositPackets.Close(0x11223344).Data);
}
